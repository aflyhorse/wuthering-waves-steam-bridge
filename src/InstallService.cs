using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace WutheringWavesSteamBridge
{
    // Filesystem transaction is separate from Steam for fixture tests.
    internal sealed class InstallFiles
    {
        private readonly string home;
        public string StatePath { get { return Path.Combine(home, "installation.json"); } }
        public InstallFiles(string dataDirectory) { home = dataDirectory; }
        public Installation Prepare(string steam, string manifest, string exe, Metadata metadata, ulong owner, bool legacy)
        {
            if (File.Exists(StatePath)) throw new InvalidOperationException("已有载入记录，请先点击“撤回载入”。");
            if (String.IsNullOrEmpty(metadata.Executable)) throw new InvalidDataException("Steam 启动文件配置缺失，请更新工具后重试。");
            string library = Path.GetDirectoryName(manifest);
            string directory = Path.Combine(library, "common", "Wuthering Waves Steam Bridge");
            if (Directory.Exists(directory)) throw new IOException("桥接目录已存在，请先处理旧安装：" + directory);
            string backup = Path.Combine(home, "backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            var state = new Installation { Version = App.Version, SteamRoot = steam, ManifestPath = manifest,
                OriginalManifestBackup = Path.Combine(backup, "original.acf"), PreviousManifestBackup = Path.Combine(backup, "previous.acf"),
                ManagedManifestBackup = Path.Combine(backup, "managed.acf"), BridgeDirectory = directory,
                Token = Guid.NewGuid().ToString("N"), WasInstalled = File.Exists(manifest) && !legacy, Phase = "prepared", Executable = metadata.Executable };
            if (File.Exists(manifest)) File.Copy(manifest, state.PreviousManifestBackup);
            if (state.WasInstalled) File.Copy(manifest, state.OriginalManifestBackup);
            App.Save(StatePath, state);
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, ".wuthering-waves-steam-bridge"), state.Token);
                File.Copy(exe, Path.Combine(directory, state.Executable));
                var depots = new Dictionary<string, object>();
                foreach (var depot in metadata.Depots) depots[depot.Key] = new Dictionary<string, object> { { "manifest", depot.Value }, { "size", "0" } };
                var appState = new Dictionary<string, object> {
                    { "appid", App.AppId.ToString() }, { "Universe", "1" }, { "name", "Wuthering Waves" }, { "StateFlags", "4" },
                    { "installdir", Path.GetFileName(directory) }, { "LastUpdated", ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds).ToString() },
                    { "SizeOnDisk", new FileInfo(exe).Length.ToString() }, { "buildid", App.BuildPin }, { "LastOwner", owner.ToString() },
                    { "UpdateResult", "0" }, { "BytesToDownload", "0" }, { "BytesDownloaded", "0" }, { "BytesToStage", "0" }, { "BytesStaged", "0" },
                    { "AutoUpdateBehavior", "1" }, { "AllowOtherDownloadsWhileRunning", "0" }, { "ScheduledAutoUpdate", "0" },
                    { "InstalledDepots", depots }, { "UserConfig", new Dictionary<string, object> { { "language", "schinese" } } },
                    { "MountedConfig", new Dictionary<string, object> { { "language", "schinese" } } }
                };
                File.WriteAllText(state.ManagedManifestBackup, KeyValues.Render(new Dictionary<string, object> { { "AppState", appState } }), new UTF8Encoding(false));
                return state;
            }
            catch { Rollback(state); throw; }
        }
        public void Commit(Installation state)
        {
            AtomicCopy(state.ManagedManifestBackup, state.ManifestPath);
            state.Phase = "enabled"; App.Save(StatePath, state);
        }
        public void Rollback(Installation state)
        {
            if (File.Exists(state.PreviousManifestBackup)) AtomicCopy(state.PreviousManifestBackup, state.ManifestPath);
            else if (File.Exists(state.ManifestPath)) File.Delete(state.ManifestPath);
            RemoveBridge(state);
            if (File.Exists(StatePath)) File.Delete(StatePath);
        }
        public void Restore(Installation state)
        {
            ValidateState(state);
            if (File.Exists(state.ManifestPath) && state.Phase != "restoring")
            {
                var acf = KeyValues.Child(KeyValues.Parse(File.ReadAllText(state.ManifestPath)), "AppState");
                string install = KeyValues.String(acf, "installdir");
                if (state.Phase == "enabled" && !String.Equals(install, Path.GetFileName(state.BridgeDirectory), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Steam 安装记录已被其他程序更改，请保留备份并查看使用说明后处理。");
            }
            if (state.WasInstalled && !File.Exists(state.OriginalManifestBackup)) throw new IOException("找不到原始安装记录备份：" + state.OriginalManifestBackup);
            state.Phase = "restoring"; App.Save(StatePath, state);
            if (state.WasInstalled) AtomicCopy(state.OriginalManifestBackup, state.ManifestPath);
            else if (File.Exists(state.ManifestPath)) File.Delete(state.ManifestPath);
            RemoveBridge(state);
        }
        public void FinishRestore() { if (File.Exists(StatePath)) File.Delete(StatePath); }
        // Steam reads buildid from the manifest; keeping it above the latest real
        // build makes the client treat the app as up to date. Re-apply it when the
        // client (or another tool) rewrote the manifest.
        public void Reassert(Installation state)
        {
            if (state == null || state.Phase != "enabled" || !File.Exists(state.ManagedManifestBackup)) return;
            ValidateState(state);
            string managed = File.ReadAllText(state.ManagedManifestBackup);
            string pinned = PinBuild(managed);
            if (pinned != managed) { managed = pinned; File.WriteAllText(state.ManagedManifestBackup, managed, new UTF8Encoding(false)); }
            if (!File.Exists(state.ManifestPath) || File.ReadAllText(state.ManifestPath) != managed) AtomicCopy(state.ManagedManifestBackup, state.ManifestPath);
        }
        private static string PinBuild(string text)
        { return System.Text.RegularExpressions.Regex.Replace(text, "(\"buildid\"\\s*\")[^\"]*(\")", "${1}" + App.BuildPin + "${2}"); }
        private static void AtomicCopy(string source, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string temp = target + ".wwsb-tmp"; File.Copy(source, temp, true);
            if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
        }
        private static void ValidateState(Installation state)
        {
            string expected = Path.Combine(Path.GetDirectoryName(state.ManifestPath), "common", "Wuthering Waves Steam Bridge");
            if (!Detection.Same(state.BridgeDirectory, expected) || Path.GetFileName(state.ManifestPath) != "appmanifest_3513350.acf")
                throw new InvalidDataException("本地载入记录路径不正确，已停止修改。");
        }
        private static void RemoveBridge(Installation state)
        {
            ValidateState(state);
            if (!Directory.Exists(state.BridgeDirectory)) return;
            string marker = Path.Combine(state.BridgeDirectory, ".wuthering-waves-steam-bridge");
            if (!File.Exists(marker) || File.ReadAllText(marker) != state.Token) throw new IOException("桥接目录标记不匹配，已保留目录：" + state.BridgeDirectory);
            string exe = Path.Combine(state.BridgeDirectory, state.Executable);
            if (File.Exists(exe)) File.Delete(exe);
            File.Delete(marker);
            if (Directory.GetFileSystemEntries(state.BridgeDirectory).Length == 0) Directory.Delete(state.BridgeDirectory);
        }
    }
    internal static class InstallService
    {
        public static void Enable(DetectionResult scan, Action<string> progress)
        {
            Apply(scan, progress, null);
            progress("已载入。请等待 Steam 启动完成，再从游戏库开始游戏。");
        }
        // Withdraw and re-apply in a single Steam restart cycle.
        public static void Reload(DetectionResult scan, Action<string> progress)
        {
            var existing = File.Exists(App.StatePath) ? App.Load<Installation>(App.StatePath) : null;
            if (existing == null || existing.Phase != "enabled") { Enable(scan, progress); return; }
            Apply(scan, progress, existing);
            progress("已重新载入。请等待 Steam 启动完成，再从游戏库开始游戏。");
        }
        // Best effort: restore the pinned build id if Steam rewrote the manifest.
        public static void RepairManifest()
        {
            try
            {
                if (!File.Exists(App.StatePath)) return;
                new InstallFiles(App.Home).Reassert(App.Load<Installation>(App.StatePath));
            }
            catch { }
        }
        private static void Apply(DetectionResult scan, Action<string> progress, Installation existing)
        {
            var account = SteamAccount.ProbeIsolated(scan.SteamRoot);
            if (!account.Known || !account.Owned) throw new InvalidOperationException(account.Error ?? "当前 Steam 账号尚未拥有《鸣潮》，请先在商店免费添加入库。");
            bool ready;
            string clientStatus = Detection.ClientStatus(scan.Game, out ready);
            if (!ready) throw new InvalidOperationException(clientStatus);
            var metadata = KeyValues.ReadAppInfo(Path.Combine(scan.SteamRoot, "appcache", "appinfo.vdf"));
            string manifest = FindManifest(scan.SteamRoot);
            bool legacy = IsLegacy(manifest);
            EnsureGameClosed(scan.Game);
            progress("正在正常退出 Steam…"); StopSteam(scan.SteamRoot);
            var files = new InstallFiles(App.Home); Installation state = null;
            try
            {
                if (existing != null) { progress("正在撤回上次载入…"); files.Restore(existing); files.FinishRestore(); }
                progress(existing != null ? "正在应用新的载入…" : "备份原始状态并安装桥接…");
                Directory.CreateDirectory(Path.GetDirectoryName(App.RuntimeExe));
                if (!Detection.Same(System.Reflection.Assembly.GetExecutingAssembly().Location, App.RuntimeExe)) File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location, App.RuntimeExe, true);
                state = files.Prepare(scan.SteamRoot, manifest, App.RuntimeExe, metadata, account.SteamId, legacy);
                App.Save(App.ConfigPath, new RuntimeConfig { Game = Path.GetFullPath(scan.Game), Quality = scan.Quality, SteamRoot = scan.SteamRoot });
                files.Commit(state);
            }
            catch
            {
                if (state != null) files.Rollback(state);
                throw;
            }
            finally { StartSteam(scan.SteamRoot); }
        }
        public static void Restore(Action<string> progress)
        {
            var state = App.Load<Installation>(App.StatePath);
            EnsureGameClosed(null);
            progress("正在正常退出 Steam…"); StopSteam(state.SteamRoot);
            try
            {
                var files = new InstallFiles(App.Home);
                progress("正在还原 Steam 安装状态…"); files.Restore(state);
                files.FinishRestore();
                if (File.Exists(App.ConfigPath)) File.Delete(App.ConfigPath);
            }
            finally { StartSteam(state.SteamRoot); }
            progress(state.WasInstalled ? "已撤回载入，恢复原来的 Steam 客户端。" : "已撤回载入，Steam 将恢复为“安装”。");
        }
        public static string FindManifest(string steam)
        {
            var matches = Detection.Libraries(steam).Select(p => Path.Combine(p, "steamapps", "appmanifest_3513350.acf")).Where(File.Exists).ToArray();
            if (matches.Length > 1) throw new IOException("多个 Steam 库中都有鸣潮安装记录，请先在 Steam 中处理重复安装。");
            return matches.Length == 1 ? matches[0] : Path.Combine(steam, "steamapps", "appmanifest_3513350.acf");
        }
        // Leftover bridge directory that would block a fresh install.
        public static string StaleBridgeDirectory(DetectionResult scan)
        {
            if (scan == null || String.IsNullOrEmpty(scan.SteamRoot)) return null;
            try
            {
                string directory = Path.Combine(Path.GetDirectoryName(FindManifest(scan.SteamRoot)), "common", "Wuthering Waves Steam Bridge");
                return Directory.Exists(directory) ? directory : null;
            }
            catch { return null; }
        }
        public static void ClearBridgeDirectory(string directory)
        {
            string path = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            if (!String.Equals(Path.GetFileName(path), "Wuthering Waves Steam Bridge", StringComparison.OrdinalIgnoreCase)
                || !String.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "common", StringComparison.OrdinalIgnoreCase)
                || !String.Equals(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))), "steamapps", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("目录路径不正确，已停止清空：" + directory);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        public static bool IsLegacy(string manifest)
        {
            // No legacy bridge format exists for Wuthering Waves yet.
            return false;
        }
        private static void EnsureGameClosed(string game)
        {
            if (String.IsNullOrEmpty(game)) return;
            string gameDir = Path.GetDirectoryName(game);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string path = ImagePath(p.Id);
                    if (path != null && path.StartsWith(gameDir + "\\", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("请先完全退出鸣潮游戏，再操作。");
                }
                catch (InvalidOperationException) { throw; }
                catch { }
            }
        }
        private static bool SteamRunning()
        {
            bool found = false; foreach (var p in Process.GetProcessesByName("steam")) using (p) found = true; return found;
        }
        private static void StopSteam(string steam)
        {
            if (!SteamRunning()) return;
            using (var p = Process.Start(new ProcessStartInfo(Path.Combine(steam, "steam.exe"), "-shutdown") { UseShellExecute = false })) { }
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (SteamRunning() && DateTime.UtcNow < deadline) Thread.Sleep(400);
            if (SteamRunning()) throw new InvalidOperationException("Steam 尚未正常退出。请处理 Steam 的提示，完全退出后重试。");
            Thread.Sleep(700);
        }
        private static void StartSteam(string steam)
        { Process.Start(new ProcessStartInfo(Path.Combine(steam, "steam.exe"), "") { UseShellExecute = true }); }
        private static string ImagePath(int processId)
        {
            IntPtr handle = OpenProcess(0x1000, false, processId);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var path = new System.Text.StringBuilder(2048);
                int length = path.Capacity;
                return QueryFullProcessImageName(handle, 0, path, ref length) ? path.ToString() : null;
            }
            finally { CloseHandle(handle); }
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, int processId);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder path, ref int size);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}

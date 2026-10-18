using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace WutheringWavesSteamBridge
{
    internal static class Detection
    {
        public static string FindSteam()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                string path = key == null ? null : Convert.ToString(key.GetValue("SteamPath"));
                if (!String.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "steam.exe"))) return Path.GetFullPath(path);
            }
            throw new InvalidOperationException("未找到当前 Windows 用户的 Steam，请先安装并登录 Steam。");
        }
        public static List<string> Libraries(string steam)
        {
            var paths = new List<string> { steam };
            string file = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(file))
            {
                var folders = KeyValues.Child(KeyValues.Parse(File.ReadAllText(file)), "libraryfolders");
                if (folders != null) foreach (var folder in folders)
                {
                    uint id; if (!UInt32.TryParse(folder.Key, out id)) continue;
                    string path = KeyValues.String(folder.Value as Dictionary<string, object>, "path") ?? folder.Value as string;
                    if (!String.IsNullOrEmpty(path) && Directory.Exists(path) && !paths.Exists(p => Same(p, path))) paths.Add(Path.GetFullPath(path));
                }
            }
            return paths;
        }
        public static bool Same(string a, string b) { return a != null && b != null && String.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        public static bool ValidGame(string path)
        {
            try { return ValidGameFile(path); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        private static bool ValidGameFile(string path)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                if (r.BaseStream.Length < 64 || r.ReadUInt16() != 0x5a4d) return false;
                r.BaseStream.Position = 0x3c; int offset = r.ReadInt32();
                if (offset < 64 || offset + 6 > r.BaseStream.Length) return false;
                r.BaseStream.Position = offset; return r.ReadUInt32() == 0x4550 && r.ReadUInt16() == 0x8664;
            }
        }
        public static string FindInstalledGame()
        {
            var candidates = new[]
            {
                new { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry32, Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\KRInstall Wuthering Waves", Value = "LauncherPath" },
                new { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64, Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\KRInstall Wuthering Waves", Value = "LauncherPath" },
                new { Hive = RegistryHive.CurrentUser, View = RegistryView.Default, Key = @"Software\kurogame\KRLauncher\Aki_G152_default_10003", Value = "SingleLauncherInstallPath" },
            };
            foreach (var c in candidates)
            {
                string root = null;
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(c.Hive, c.View))
                    using (var key = baseKey.OpenSubKey(c.Key))
                        if (key != null) root = Convert.ToString(key.GetValue(c.Value));
                }
                catch { continue; }
                if (String.IsNullOrWhiteSpace(root)) continue;
                string exe = Path.Combine(root.Trim(), "Wuthering Waves Game", "Wuthering Waves.exe");
                if (ValidGame(exe)) return exe;
            }
            return null;
        }
        public static string ClientStatus(string game, out bool ready)
        {
            ready = false;
            if (!ValidGame(game)) return "请选择鸣潮游戏目录中的可执行文件（.exe）";
            ready = true; return "游戏程序已就绪";
        }
        public static DetectionResult Scan(string game, string quality = null)
        {
            var result = new DetectionResult();
            try
            {
                if (File.Exists(App.StatePath)) result.Installation = App.Load<Installation>(App.StatePath);
                result.SteamRoot = FindSteam(); result.Account = SteamAccount.ProbeIsolated(result.SteamRoot);
                RuntimeConfig config = File.Exists(App.ConfigPath) ? App.Load<RuntimeConfig>(App.ConfigPath) : null;
                result.Game = game ?? (config != null && ValidGame(config.Game) ? config.Game : null) ?? FindInstalledGame();
                result.Quality = quality ?? (config != null ? config.Quality : null) ?? QualityPackage.Hd.Id;
                result.GameStatus = ClientStatus(result.Game, out result.ClientReady);
            }
            catch (Exception e) { result.Error = e.Message; }
            return result;
        }
    }
}

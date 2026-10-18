using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Steam launches the bridge as the game's configured executable. The bridge
// starts the real Wuthering Waves client with the selected quality package and
// stays alive so Steam keeps counting play time while the game runs.
namespace WutheringWavesSteamBridge
{
internal static class BridgeRuntime
{
    private static readonly string Root = App.Home;
    private static RuntimeConfig Config;
    private static string Game { get { return Config.Game; } }
    private static string Quality { get { return Config.Quality ?? QualityPackage.Hd.Id; } }

    public static int Run()
    {
        Config = App.Load<RuntimeConfig>(App.ConfigPath);
        string appId = Environment.GetEnvironmentVariable("SteamAppId");
        string gameId = Environment.GetEnvironmentVariable("SteamGameId");
        bool installedBridge = IsInstalledBridge();
        Log("launch", "SteamAppId=" + (appId ?? "<missing>") + "; SteamGameId=" + (gameId ?? "<missing>") + "; installedBridge=" + installedBridge);
        if (!AcceptSteamLaunch(appId, gameId, installedBridge))
            throw new InvalidOperationException("尚未找到有效的载入记录。请打开 Wuthering Waves Steam Bridge 重新载入，再从 Steam 游戏库开始游戏。");
        bool created;
        using (var mutex = new Mutex(true, @"Local\SteamWutheringWavesCNBridge", out created))
        {
            if (!created) return 0;
            return Bridge();
        }
    }
    internal static bool AcceptSteamLaunch(string appId, string gameId, bool installedBridge)
    {
        string expected = App.AppId.ToString();
        return installedBridge || String.Equals((appId ?? "").Trim(), expected, StringComparison.Ordinal) ||
            String.Equals((gameId ?? "").Trim(), expected, StringComparison.Ordinal);
    }
    private static bool IsInstalledBridge()
    {
        if (!File.Exists(App.StatePath)) return false;
        var state = App.Load<Installation>(App.StatePath);
        return state.Phase == "enabled" && Detection.Same(state.SteamRoot, Config.SteamRoot) &&
            Detection.Same(Application.ExecutablePath, Path.Combine(state.BridgeDirectory, state.Executable));
    }
    private static int Bridge()
    {
        try
        {
            if (!Detection.ValidGame(Game)) throw new InvalidOperationException("找不到鸣潮游戏程序：" + Game);
            if (GameRunning()) throw new InvalidOperationException("鸣潮已在运行。请正常退出后再从 Steam 启动。");
            string arguments = "Client -krqlv=" + Quality + " -SkipSplash";
            Log("bridge", "启动鸣潮：" + Game + " " + arguments);
            var info = new ProcessStartInfo
            {
                FileName = Game, Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(Game), UseShellExecute = false
            };
            // Restore the app identifiers so Steam's overlay renderer attaches to the game.
            info.EnvironmentVariables["SteamAppId"] = App.AppId.ToString();
            info.EnvironmentVariables["SteamGameId"] = App.AppId.ToString();
            using (var game = Process.Start(info))
            {
                Log("bridge", "游戏进程 PID=" + game.Id);
                while (!game.HasExited)
                {
                    if (GameRunning()) break;
                    Thread.Sleep(500);
                }
                Log("bridge", "检测到鸣潮游戏，跟踪游玩时长。");
                while (!game.HasExited || GameRunning()) Thread.Sleep(1000);
            }
            Log("bridge", "鸣潮已退出，结束 Steam 本次运行。");
            return 0;
        }
        catch (Exception error)
        {
            Log("bridge", "错误：" + error.Message);
            MessageBox.Show(error.Message, "Wuthering Waves Steam Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
    private static bool GameRunning()
    {
        string gameDir = Path.GetDirectoryName(Game);
        int self = Process.GetCurrentProcess().Id;
        foreach (var process in Process.GetProcesses())
        {
            if (process.Id == self) continue;
            try
            {
                string path = ImagePath(process.Id);
                if (path != null && path.StartsWith(gameDir + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return false;
    }
    private static string ImagePath(int processId)
    {
        IntPtr handle = OpenProcess(0x1000, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var path = new StringBuilder(2048);
            int length = path.Capacity;
            return QueryFullProcessImageName(handle, 0, path, ref length) ? path.ToString() : null;
        }
        finally { CloseHandle(handle); }
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    private static void Log(string role, string message)
    {
        using (var mutex = new Mutex(false, @"Local\SteamWutheringWavesBridgeLog"))
        {
            try { mutex.WaitOne(); }
            catch (AbandonedMutexException) { }
            try { File.AppendAllText(Path.Combine(Root, "bridge.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + role + "] " + message + Environment.NewLine); }
            finally { mutex.ReleaseMutex(); }
        }
    }
}

}

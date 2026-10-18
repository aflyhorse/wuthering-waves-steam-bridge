using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WutheringWavesSteamBridge
{
    internal static class App
    {
        public const string Version = "0.2.1";
        public const uint AppId = 3513350;
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        public static readonly string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WutheringWavesSteamBridge");
        public static readonly string StatePath = Path.Combine(Home, "installation.json");
        public static readonly string ConfigPath = Path.Combine(Home, "runtime.json");
        public static readonly string RuntimeExe = Path.Combine(Home, "app", "WutheringWavesSteamBridge.exe");
        public static readonly string LogPath = Path.Combine(Home, "bridge.log");
        public static void Save(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, Json.Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static T Load<T>(string path) { return Json.Deserialize<T>(File.ReadAllText(path)); }
    }
    public sealed class RuntimeConfig
    {
        public string Game { get; set; }
        public string Quality { get; set; }
        public string SteamRoot { get; set; }
    }
    public sealed class Installation
    {
        public string Version { get; set; }
        public string SteamRoot { get; set; }
        public string ManifestPath { get; set; }
        public string OriginalManifestBackup { get; set; }
        public string PreviousManifestBackup { get; set; }
        public string BridgeDirectory { get; set; }
        public string Token { get; set; }
        public bool WasInstalled { get; set; }
        public string Phase { get; set; }
        public string ManagedManifestBackup { get; set; }
        public string Executable { get; set; }
    }
    public sealed class Ownership
    {
        public bool Known { get; set; }
        public bool Owned { get; set; }
        public bool Installed { get; set; }
        public ulong SteamId { get; set; }
        public string Name { get; set; }
        public string Error { get; set; }
    }
    internal sealed class DetectionResult
    {
        public bool ClientReady;
        public string SteamRoot;
        public Ownership Account;
        public string Game;
        public string Quality;
        public string GameStatus;
        public Installation Installation;
        public string Error;
    }
    internal sealed class Metadata
    {
        public string Build;
        public string Executable;
        public System.Collections.Generic.Dictionary<string, string> Depots = new System.Collections.Generic.Dictionary<string, string>();
    }
}

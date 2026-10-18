using System;

namespace WutheringWavesSteamBridge
{
    internal sealed class QualityPackage
    {
        public static readonly QualityPackage Sd = new QualityPackage("sd", "SD（标清）");
        public static readonly QualityPackage Hd = new QualityPackage("hd", "HD（高清）");
        public static readonly QualityPackage Uhd = new QualityPackage("uhd", "UHD（超清）");
        public static readonly QualityPackage[] All = new[] { Sd, Hd, Uhd };
        public readonly string Id, Name;
        private QualityPackage(string id, string name) { Id = id; Name = name; }
        public static QualityPackage Get(string id)
        {
            if (String.IsNullOrEmpty(id)) return Hd;
            string lower = id.ToLowerInvariant();
            if (lower == Hd.Id) return Hd;
            if (lower == Sd.Id) return Sd;
            if (lower == Uhd.Id) return Uhd;
            throw new InvalidOperationException("不支持的清晰度分包：" + id);
        }
        public override string ToString() { return Name; }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace AltarHelperV2
{
    /// <summary>
    /// Reads live poe.ninja prices (in chaos) from the cache that the Ninja Price plugin
    /// (Get-Chaos-Value) keeps in Plugins\Temp\Get-Chaos-Value\NinjaData\&lt;league&gt;\.
    /// Only Currency2.json and Threads.json are used. No network access happens here.
    /// </summary>
    public sealed class AltarPriceFeed
    {
        private static readonly string[] Files = { "Currency2.json", "Threads.json" };

        private readonly List<string> _roots = new();
        private DateTime _checkedAt = DateTime.MinValue;
        private string _leagueDir;
        private DateTime _stamp;

        /// <summary>Item name -> chaos value, from the newest league cache.</summary>
        public Dictionary<string, double> Prices { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        public string League { get; private set; } = "";
        public string Status { get; private set; } = "not initialised";
        public bool HasData => Prices.Count > 0;

        public void Initialise(string pluginDirectory)
        {
            _roots.Clear();
            try
            {
                var parent = Path.GetDirectoryName((pluginDirectory ?? "").TrimEnd('\\', '/'));
                if (!string.IsNullOrEmpty(parent))
                {
                    _roots.Add(Path.Combine(parent, "Get-Chaos-Value", "NinjaData"));
                    var plugins = Path.GetDirectoryName(parent);
                    if (!string.IsNullOrEmpty(plugins))
                        _roots.Add(Path.Combine(plugins, "Temp", "Get-Chaos-Value", "NinjaData"));
                    // Ninja Price is also installed under other folder names (e.g. "Ninja-Price" from its
                    // current repo): accept any plugin folder next to these that has a NinjaData cache.
                    foreach (var dir in new[] { parent, string.IsNullOrEmpty(plugins) ? null : Path.Combine(plugins, "Temp") })
                    {
                        if (dir == null || !Directory.Exists(dir)) continue;
                        foreach (var sub in Directory.GetDirectories(dir))
                        {
                            var ninja = Path.Combine(sub, "NinjaData");
                            if (!_roots.Exists(r => string.Equals(r, ninja, StringComparison.OrdinalIgnoreCase)) && Directory.Exists(ninja))
                                _roots.Add(ninja);
                        }
                    }
                }
            }
            catch { }
            _checkedAt = DateTime.MinValue;
            _leagueDir = null;
        }

        /// <summary>Re-reads the cache when it changed. Returns true when prices were updated.</summary>
        public bool Refresh(bool force = false)
        {
            if (!force && (DateTime.Now - _checkedAt).TotalSeconds < 30) return false;
            _checkedAt = DateTime.Now;
            try
            {
                string bestDir = null;
                var bestAt = DateTime.MinValue;
                foreach (var root in _roots)
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var dir in Directory.GetDirectories(root))
                    {
                        var f = Path.Combine(dir, "Currency2.json");
                        if (!File.Exists(f)) continue;
                        var t = File.GetLastWriteTime(f);
                        if (t > bestAt) { bestAt = t; bestDir = dir; }
                    }
                }

                if (bestDir == null)
                {
                    Status = "Ninja Price cache not found - using built-in prices";
                    return false;
                }
                if (!force && bestDir == _leagueDir && bestAt == _stamp) return false;

                var fresh = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Files)
                {
                    var path = Path.Combine(bestDir, file);
                    if (File.Exists(path)) ReadFile(path, fresh);
                }
                if (!fresh.ContainsKey("Divine Orb") || fresh["Divine Orb"] <= 0)
                {
                    Status = "Ninja Price cache has no Divine Orb price - using built-in prices";
                    return false;
                }

                Prices = fresh;
                _leagueDir = bestDir;
                _stamp = bestAt;
                League = Path.GetFileName(bestDir);
                Status = $"poe.ninja {League}, Divine = {fresh["Divine Orb"]:0.#}c, updated {bestAt:yyyy-MM-dd HH:mm}";
                return true;
            }
            catch (Exception ex)
            {
                Status = "Price read failed: " + ex.Message;
                return false;
            }
        }

        private static void ReadFile(string path, Dictionary<string, double> into)
        {
            var root = JObject.Parse(File.ReadAllText(path));
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in root["items"] as JArray ?? new JArray())
                names[(string)it["id"] ?? ""] = (string)it["name"] ?? "";

            foreach (var line in root["lines"] as JArray ?? new JArray())
            {
                var id = (string)line["id"] ?? "";
                if (!names.TryGetValue(id, out var name) || string.IsNullOrEmpty(name)) continue;
                var value = (double?)line["primaryValue"] ?? 0;
                if (value > 0) into[name] = value;
            }
        }
    }
}

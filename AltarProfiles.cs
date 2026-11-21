using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace AltarHelperV2
{
    /// <summary>
    /// One named set of weights + thresholds (a "character profile").
    /// A profile with <see cref="BaseProfile"/> set stores only the entries that differ from its base
    /// (mod weights / alerts); thresholds are always stored in full.
    /// Pure data/IO: no ExileCore or ImGui dependency, so it can be unit tested.
    /// </summary>
    public sealed class AltarProfile
    {
        public string Name { get; set; } = "Default";

        /// <summary>Name of the profile this one inherits from, or null for a standalone profile.</summary>
        public string BaseProfile { get; set; }

        public Dictionary<string, int> ModTiers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ManualWeights { get; set; } = new();
        public Dictionary<string, bool> ModAlerts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public int MinPickWeight { get; set; }
        public int NegativeVetoThreshold { get; set; }
        public int PositiveVetoThreshold { get; set; }
        public int AlertMinNetWeight { get; set; } = 1;
        public int MinionWeight { get; set; }
        public int BossWeight { get; set; }

        /// <summary>Positive weights in the downside section count as a small bonus (see AltarSettings).</summary>
        public bool SignedDownsideWeights { get; set; }

        /// <summary>Makes a file-loaded profile safe to use (null collections, comparer, blank base).</summary>
        public AltarProfile Normalize()
        {
            ModTiers = new Dictionary<string, int>(ModTiers ?? new(), StringComparer.OrdinalIgnoreCase);
            ModAlerts = new Dictionary<string, bool>(ModAlerts ?? new(), StringComparer.OrdinalIgnoreCase);
            ManualWeights = (ManualWeights ?? new()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (string.IsNullOrWhiteSpace(BaseProfile)) BaseProfile = null;
            if (string.IsNullOrWhiteSpace(Name)) Name = "Default";
            return this;
        }

        public AltarProfile Clone() => FromJson(ToJson(this));

        public static string ToJson(AltarProfile p) => JsonConvert.SerializeObject(p, Formatting.Indented);

        /// <summary>Parses profile JSON; returns null when the text is not a valid profile.</summary>
        public static AltarProfile FromJson(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonConvert.DeserializeObject<AltarProfile>(json)?.Normalize();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>Reads and writes profiles as one JSON file each inside a directory.</summary>
    public sealed class AltarProfileStore
    {
        private const int MaxInheritanceDepth = 8;

        public string Directory { get; }

        public AltarProfileStore(string directory)
        {
            Directory = directory;
        }

        /// <summary>File-name safe profile name, or null when nothing usable is left.</summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
            if (clean.Length > 60) clean = clean.Substring(0, 60).TrimEnd();
            return clean.Length == 0 ? null : clean;
        }

        private string PathFor(string name) => Path.Combine(Directory, SanitizeName(name) + ".json");

        public IReadOnlyList<string> List()
        {
            try
            {
                if (!System.IO.Directory.Exists(Directory)) return Array.Empty<string>();
                return System.IO.Directory.GetFiles(Directory, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        public bool Exists(string name) => SanitizeName(name) != null && File.Exists(PathFor(name));

        /// <summary>The profile exactly as stored (an inheriting profile holds only its overrides).</summary>
        public AltarProfile LoadRaw(string name)
        {
            try
            {
                if (!Exists(name)) return null;
                var p = AltarProfile.FromJson(File.ReadAllText(PathFor(name)));
                if (p != null) p.Name = SanitizeName(name);
                return p;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The profile with all inherited values merged in. A missing or cyclic base is ignored.</summary>
        public AltarProfile LoadResolved(string name)
        {
            var leaf = LoadRaw(name);
            if (leaf == null) return null;

            var chain = new List<AltarProfile> { leaf };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { leaf.Name };
            var current = leaf;
            while (current.BaseProfile != null && chain.Count < MaxInheritanceDepth)
            {
                var next = seen.Contains(SanitizeName(current.BaseProfile) ?? "") ? null : LoadRaw(current.BaseProfile);
                if (next == null) break;
                seen.Add(next.Name);
                chain.Add(next);
                current = next;
            }

            var result = leaf.Clone();
            result.ModTiers.Clear();
            result.ModAlerts.Clear();
            var manual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                foreach (var kv in chain[i].ModTiers) result.ModTiers[kv.Key] = kv.Value;
                foreach (var kv in chain[i].ModAlerts) result.ModAlerts[kv.Key] = kv.Value;
                foreach (var m in chain[i].ManualWeights) manual.Add(m);
            }
            result.ManualWeights = manual.ToList();
            return result;
        }

        /// <summary>
        /// Turns a full (resolved) profile into what is written to disk: when it has a base, only the
        /// entries that differ from that base's resolved values are kept.
        /// </summary>
        public AltarProfile ToStorable(AltarProfile full)
        {
            var baseName = full.BaseProfile;
            var baseResolved = baseName == null ? null : LoadResolved(baseName);
            if (baseResolved == null || string.Equals(SanitizeName(baseName), SanitizeName(full.Name), StringComparison.OrdinalIgnoreCase))
            {
                var standalone = full.Clone();
                standalone.BaseProfile = null;
                return standalone;
            }

            var diff = full.Clone();
            diff.ModTiers.Clear();
            diff.ModAlerts.Clear();
            foreach (var key in full.ModTiers.Keys.Union(baseResolved.ModTiers.Keys, StringComparer.OrdinalIgnoreCase))
            {
                int live = full.ModTiers.GetValueOrDefault(key, 0);
                int basis = baseResolved.ModTiers.GetValueOrDefault(key, 0);
                if (live != basis) diff.ModTiers[key] = live;
            }
            foreach (var key in full.ModAlerts.Keys.Union(baseResolved.ModAlerts.Keys, StringComparer.OrdinalIgnoreCase))
            {
                bool live = full.ModAlerts.GetValueOrDefault(key, false);
                bool basis = baseResolved.ModAlerts.GetValueOrDefault(key, false);
                if (live != basis) diff.ModAlerts[key] = live;
            }
            return diff;
        }

        /// <summary>Writes the profile (as overrides when it has a base). Returns false on IO failure.</summary>
        public bool Save(AltarProfile full)
        {
            try
            {
                var name = SanitizeName(full.Name);
                if (name == null) return false;
                System.IO.Directory.CreateDirectory(Directory);
                var storable = ToStorable(full);
                storable.Name = name;

                var target = PathFor(name);
                var temp = target + ".tmp";
                File.WriteAllText(temp, AltarProfile.ToJson(storable));
                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool Delete(string name)
        {
            try
            {
                if (!Exists(name)) return false;
                File.Delete(PathFor(name));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Renames a profile file and re-points children that inherit from it.</summary>
        public bool Rename(string oldName, string newName)
        {
            try
            {
                var target = SanitizeName(newName);
                if (target == null || !Exists(oldName) || Exists(target)) return false;
                var p = LoadRaw(oldName);
                if (p == null) return false;
                var oldClean = SanitizeName(oldName);

                // Write under the new name first, then remove the old file.
                p.Name = target;
                var temp = PathFor(target) + ".tmp";
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(temp, AltarProfile.ToJson(p));
                File.Move(temp, PathFor(target));
                File.Delete(PathFor(oldClean));

                foreach (var child in List())
                {
                    var c = LoadRaw(child);
                    if (c?.BaseProfile != null &&
                        string.Equals(SanitizeName(c.BaseProfile), oldClean, StringComparison.OrdinalIgnoreCase))
                    {
                        c.BaseProfile = target;
                        File.WriteAllText(PathFor(c.Name), AltarProfile.ToJson(c));
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Names of profiles that (directly) inherit from <paramref name="name"/>.</summary>
        public IReadOnlyList<string> Children(string name)
        {
            var clean = SanitizeName(name);
            return List()
                .Where(n => LoadRaw(n)?.BaseProfile is { } b &&
                            string.Equals(SanitizeName(b), clean, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>First free name: "Name", "Name 2", "Name 3", ...</summary>
        public string UniqueName(string wanted)
        {
            var clean = SanitizeName(wanted) ?? "Profile";
            if (!Exists(clean)) return clean;
            for (int i = 2; i < 1000; i++)
            {
                var candidate = $"{clean} {i}";
                if (!Exists(candidate)) return candidate;
            }
            return clean + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        /// <summary>Would making <paramref name="child"/> inherit from <paramref name="baseName"/> create a cycle?</summary>
        public bool WouldCreateCycle(string child, string baseName)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cur = SanitizeName(baseName);
            var target = SanitizeName(child);
            while (cur != null && seen.Add(cur) && seen.Count <= MaxInheritanceDepth + 1)
            {
                if (string.Equals(cur, target, StringComparison.OrdinalIgnoreCase)) return true;
                var raw = LoadRaw(cur);
                cur = raw?.BaseProfile == null ? null : SanitizeName(raw.BaseProfile);
            }
            return false;
        }
    }
}

using System;
using System.Text.RegularExpressions;

namespace AltarHelperV2
{
    /// <summary>
    /// "Final Boss drops N additional X" lines. The game prints the exact count and switches wording at 1
    /// ("... additional Ultimatum Thread", "an additional Divination Card ..."), so the same reward has
    /// several texts. This maps every form to one canonical (plural) mod id and the real count N.
    /// Pure text logic: no ExileCore dependency.
    /// </summary>
    public static class AltarBossDrops
    {
        public const string Prefix = "Final Boss drops # additional ";

        private static readonly Regex Pattern = new(
            @"^Final Boss drops (?<n>\d+|an|a) additional (?<item>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>[Key|Text] and [Text] markup used by the game's stat descriptions.</summary>
        public static string StripMarkup(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = Regex.Replace(s, @"\[([^\]|]*)\|([^\]]*)\]", "$2");
            return Regex.Replace(s, @"\[([^\]]*)\]", "$1");
        }

        /// <summary>Plural form of the item phrase as used in the plugin's mod ids.</summary>
        public static string Pluralize(string item)
        {
            item = item.Trim();
            if (item.StartsWith("Divination Card which rewards ", StringComparison.OrdinalIgnoreCase))
                return "Divination Cards which reward " + item.Substring("Divination Card which rewards ".Length);
            if (item.StartsWith("Divination Cards", StringComparison.OrdinalIgnoreCase)) return item;   // already plural
            if (item.Equals("Divination Card", StringComparison.OrdinalIgnoreCase)) return "Divination Cards";
            if (Regex.IsMatch(item, @"\bOrbs of\b")) return item;                  // already plural
            var orbOf = Regex.Match(item, @"^(?<pre>.*?)\bOrb of (?<rest>.+)$");   // Orb of Annulment
            if (orbOf.Success) return orbOf.Groups["pre"].Value + "Orbs of " + orbOf.Groups["rest"].Value;
            return item.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? item : item + "s";
        }

        public const string MinionPrefix = "#% chance to drop an additional ";

        private static readonly Regex MinionPattern = new(
            @"^(?<n>[\d.,]+)% chance to drop an additional (?<item>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Singular form of the item phrase (the minion wording always uses the singular).</summary>
        public static string Singularize(string item)
        {
            item = item.Trim();
            if (item.StartsWith("Divination Card which rewards ", StringComparison.OrdinalIgnoreCase)) return item; // already singular
            if (item.StartsWith("Divination Cards which reward ", StringComparison.OrdinalIgnoreCase))
                return "Divination Card which rewards " + item.Substring("Divination Cards which reward ".Length);
            if (item.Equals("Divination Cards", StringComparison.OrdinalIgnoreCase)) return "Divination Card";
            if (Regex.IsMatch(item, @"\bOrbs of\b")) return Regex.Replace(item, @"\bOrbs of\b", "Orb of");
            if (item.EndsWith("s", StringComparison.OrdinalIgnoreCase) && !item.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
                return item.Substring(0, item.Length - 1);
            return item;
        }

        /// <summary>
        /// "N% chance to drop an additional X" (minion drops). Returns the canonical mod id even when the game
        /// printed the item in another form (plural, markup), so a reward is never reported as unknown for wording.
        /// </summary>
        public static bool TryParseMinion(string rawText, out string canonicalId)
        {
            canonicalId = null;
            if (string.IsNullOrWhiteSpace(rawText)) return false;
            var m = MinionPattern.Match(StripMarkup(rawText).Trim());
            if (!m.Success) return false;
            canonicalId = MinionPrefix + Singularize(m.Groups["item"].Value);
            return true;
        }

        /// <summary>
        /// Parses the raw altar line (real numbers). Returns the drop count (1 for "an additional" and the
        /// singular forms) and the canonical plural mod id, e.g. "Final Boss drops # additional Ultimatum Threads".
        /// </summary>
        public static bool TryParse(string rawText, out int count, out string canonicalId)
        {
            count = 0;
            canonicalId = null;
            if (string.IsNullOrWhiteSpace(rawText)) return false;

            var m = Pattern.Match(StripMarkup(rawText).Trim());
            if (!m.Success) return false;

            var n = m.Groups["n"].Value;
            count = int.TryParse(n, out var parsed) ? parsed : 1;
            canonicalId = Prefix + Pluralize(m.Groups["item"].Value);
            return true;
        }
    }
}

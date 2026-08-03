using System.Text.RegularExpressions;

namespace AltarHelperV2
{
    /// <summary>
    /// Converts changing numeric values in altar text to stable configuration keys.
    /// Leading signs are significant: player penalties use -# while bonuses use +#.
    /// </summary>
    public static class AltarTextNormalizer
    {
        private static readonly Regex NumberPattern = new(
            @"(?<sign>[+-]?)\d+(?:[.,]\d+)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string NormalizeNumbers(string value) =>
            NumberPattern.Replace(value ?? string.Empty, "${sign}#");
    }
}

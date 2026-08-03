using AltarHelperV2;

var cases = new (string Input, string Expected)[]
{
    ("-40% to Cold Resistance", "-#% to Cold Resistance"),
    ("+80% to Cold Resistance", "+#% to Cold Resistance"),
    ("25% increased Quantity of Items", "#% increased Quantity of Items"),
    ("1.6% chance to drop an additional Divine Orb", "#% chance to drop an additional Divine Orb"),
    ("Take 600 Chaos Damage every 2 seconds", "Take # Chaos Damage every # seconds"),
    ("(-60–-40)% to Lightning Resistance", "(-#–-#)% to Lightning Resistance"),
};

foreach (var (input, expected) in cases)
{
    var actual = AltarTextNormalizer.NormalizeNumbers(input);
    if (!string.Equals(actual, expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}' for '{input}'.");
}

Console.WriteLine($"Passed {cases.Length} altar text normalization tests.");

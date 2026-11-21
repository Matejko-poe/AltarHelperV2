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

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + message);
}

// ---------------------------------------------------------------------------
// Profiles
// ---------------------------------------------------------------------------
var tmp = Path.Combine(Path.GetTempPath(), "altar-profile-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new AltarProfileStore(tmp);

    var baseProfile = new AltarProfile { Name = "Base", MinPickWeight = 500, NegativeVetoThreshold = 4000 };
    baseProfile.ModTiers["A"] = 100;
    baseProfile.ModTiers["B"] = -200;
    baseProfile.ModAlerts["A"] = true;
    baseProfile.ManualWeights.Add("A");
    Check(store.Save(baseProfile), "save base");
    Check(store.List().SequenceEqual(new[] { "Base" }), "list shows Base");

    var loaded = store.LoadResolved("Base")!;
    Check(loaded.ModTiers["A"] == 100 && loaded.ModTiers["B"] == -200, "round trip tiers");
    Check(loaded.ModAlerts["a"], "alerts are case-insensitive");
    Check(loaded.MinPickWeight == 500 && loaded.NegativeVetoThreshold == 4000, "round trip thresholds");

    // Child stores only overrides, resolves to base + overrides
    var child = store.LoadResolved("Base")!.Clone();
    child.Name = "Child";
    child.BaseProfile = "Base";
    child.ModTiers["B"] = -999;   // override
    child.ModTiers["C"] = -50;    // new
    child.MinPickWeight = 1200;
    Check(store.Save(child), "save child");
    var raw = store.LoadRaw("Child")!;
    Check(raw.ModTiers.Count == 2 && !raw.ModTiers.ContainsKey("A"), "child stores only overrides");
    var resolved = store.LoadResolved("Child")!;
    Check(resolved.ModTiers["A"] == 100 && resolved.ModTiers["B"] == -999 && resolved.ModTiers["C"] == -50, "child resolves base + overrides");
    Check(resolved.MinPickWeight == 1200, "child keeps own thresholds");

    // Editing the base flows into the child
    var editedBase = store.LoadRaw("Base")!;
    editedBase.ModTiers["A"] = 777;
    store.Save(editedBase);
    Check(store.LoadResolved("Child")!.ModTiers["A"] == 777, "base edit reaches child");

    // Cycle guard and corrupt files
    Check(store.WouldCreateCycle("Base", "Child"), "cycle detected");
    Check(!store.WouldCreateCycle("Child", "Base"), "no false cycle");
    File.WriteAllText(Path.Combine(tmp, "Broken.json"), "{ not json");
    Check(store.LoadRaw("Broken") == null && store.LoadResolved("Broken") == null, "corrupt file returns null");
    Check(AltarProfile.FromJson("") == null && AltarProfile.FromJson("[1,2]") == null, "invalid clipboard text rejected");

    // Rename re-points children, unique names, delete
    Check(store.Rename("Base", "Root"), "rename");
    Check(store.LoadRaw("Child")!.BaseProfile == "Root", "child re-pointed after rename");
    Check(store.LoadResolved("Child")!.ModTiers["A"] == 777, "child still resolves after rename");
    Check(store.UniqueName("Root") == "Root 2", "unique name");
    Check(store.Children("Root").SequenceEqual(new[] { "Child" }), "children listed");
    Check(store.Delete("Child") && !store.Exists("Child"), "delete");
    Check(AltarProfileStore.SanitizeName("a/b:c") == "a_b_c", "sanitize");
}
finally
{
    try { Directory.Delete(tmp, true); } catch { }
}
Console.WriteLine("Passed profile store tests.");

// ---------------------------------------------------------------------------
// Wizard (reads the real mod list from AltarModsConstants.cs)
// ---------------------------------------------------------------------------
string? dir = AppContext.BaseDirectory;
while (dir != null && !File.Exists(Path.Combine(dir, "AltarModsConstants.cs"))) dir = Path.GetDirectoryName(dir);
Check(dir != null, "find AltarModsConstants.cs");
var modRegex = new System.Text.RegularExpressions.Regex(
    "^\\s*\\(\"([^\"]+)\", \"([^\"]*)\", \"([A-Za-z]+)\"\\)", System.Text.RegularExpressions.RegexOptions.Multiline);
var mods = modRegex.Matches(File.ReadAllText(Path.Combine(dir!, "AltarModsConstants.cs")))
    .Select(m => (Id: m.Groups[1].Value, Name: m.Groups[2].Value, Type: m.Groups[3].Value)).ToList();
Check(mods.Count > 200, "parsed mod list");

foreach (var m in mods.Where(m => !AltarWizard.IsReward(m.Id)))
    Check(AltarWizard.Categorize(m.Id).Length > 0, "categorize " + m.Id);
Check(mods.Where(m => AltarWizard.IsReward(m.Id)).All(m => AltarWizard.Categorize(m.Id) == AltarWizard.GroupRewards), "rewards categorized");

var rewards = new Dictionary<string, int> { ["Final Boss drops # additional Divine Orbs"] = 10000 };

WizardAnswers Fragile(RiskAppetite risk) => new()
{
    Defence = DefenceKind.Armour, PhysMaxHit = Tier3.Low, EleRes = ResState.Below,
    ChaosRes = ChaosResState.Negative, Flasks = FlaskUse.Heavy, FrenzyCharges = true, Risk = risk,
};
var bal = AltarWizard.Build(Fragile(RiskAppetite.Balanced), mods, rewards);
var strict = AltarWizard.Build(Fragile(RiskAppetite.Strict), mods, rewards);
var greedy = AltarWizard.Build(Fragile(RiskAppetite.Greedy), mods, rewards);

Check(bal.Tiers["Final Boss drops # additional Divine Orbs"] == 10000, "rewards kept");
const string chaosFlask = "Take # Chaos Damage per second during any Flask Effect";
Check(bal.Tiers[chaosFlask] == -3000, "negative chaos res + flasks = lethal");
Check(strict.Tiers[chaosFlask] == -4500 && greedy.Tiers[chaosFlask] == -1500, "risk appetite scales weights");
Check(strict.MinPickWeight > bal.MinPickWeight && bal.MinPickWeight > greedy.MinPickWeight, "risk appetite sets thresholds");
Check(bal.Tiers["-# to Armour"] < 0, "armour build penalises armour loss");
Check(bal.Tiers["-# to Evasion Rating"] > 0, "armour build: evasion loss is harmless = small bonus");
Check(bal.Tiers["#% reduced Defences per Frenzy Charge"] <= -800, "frenzy charges + fragile is heavily penalised");

// Irrelevant things are ignored
var caster = new WizardAnswers
{
    Damage = DamageKind.Spell, DealsPhysical = false, DealsElemental = true,
    Defence = DefenceKind.EnergyShield, Flasks = FlaskUse.None, UsesProjectiles = false,
};
var c = AltarWizard.Build(caster, mods, rewards);
Check(c.Tiers[chaosFlask] > 0, "no flasks = flask chaos damage is harmless = small bonus");
Check(c.Tiers["Projectiles are fired in random directions"] > 0, "no projectiles = harmless bonus");
Check(c.Tiers["#% additional Physical Damage Reduction"] > 0, "non-physical build: enemy PDR harmless");
Check(c.Tiers.ContainsKey("+#% to Cold Resistance"), "elemental build cares about enemy resistances");

// Reward selection: multi-select + price threshold for threads / basic currency
var rw = new WizardAnswers { RewardDivine = true, RewardEldritch = false, RewardThreads = true,
    ThreadsOnlyValuable = true, ThreadsMinChaos = 1.5f, RewardBasic = false };
Check(AltarWizard.IncludeReward(rw, "Divine", 150), "divine ticked");
Check(!AltarWizard.IncludeReward(rw, "Eldritch", 39), "eldritch unticked");
Check(AltarWizard.IncludeReward(rw, "Thread", 2.04) && !AltarWizard.IncludeReward(rw, "Thread", 0.9), "thread threshold");
rw.ThreadsOnlyValuable = false;
Check(AltarWizard.IncludeReward(rw, "Thread", 0.11), "all threads when threshold off");
Check(!AltarWizard.IncludeReward(rw, "Basic", 8), "basic unticked");

// Boss drop wordings: singular / plural / "an additional" all map to one canonical id + real count
void BossCase(string raw, int count, string canonical)
{
    Check(AltarBossDrops.TryParse(raw, out var n, out var id), "parse " + raw);
    Check(n == count && id == canonical, $"'{raw}' -> {n} / {id}");
}
BossCase("Final Boss drops 1 additional Ultimatum Thread", 1, "Final Boss drops # additional Ultimatum Threads");
BossCase("Final Boss drops 4 additional Ultimatum Threads", 4, "Final Boss drops # additional Ultimatum Threads");
BossCase("Final Boss drops 4 additional [UltimatumThread|Ultimatum Threads]", 4, "Final Boss drops # additional Ultimatum Threads");
BossCase("Final Boss drops 4 additional Orbs of Scouring", 4, "Final Boss drops # additional Orbs of Scouring");
BossCase("Final Boss drops 1 additional Orb of Annulment", 1, "Final Boss drops # additional Orbs of Annulment");
BossCase("Final Boss drops 1 additional Eldritch Orb of Annulment", 1, "Final Boss drops # additional Eldritch Orbs of Annulment");
BossCase("Final Boss drops 2 additional Eldritch Orbs of Annulment", 2, "Final Boss drops # additional Eldritch Orbs of Annulment");
BossCase("Final Boss drops 1 additional Jeweller's Orb", 1, "Final Boss drops # additional Jeweller's Orbs");
BossCase("Final Boss drops 1 additional Lesser Eldritch Ichor", 1, "Final Boss drops # additional Lesser Eldritch Ichors");
BossCase("Final Boss drops 1 additional Gemcutter's Prism", 1, "Final Boss drops # additional Gemcutter's Prisms");
BossCase("Final Boss drops an additional Divination Card which rewards Currency", 1,
    "Final Boss drops # additional Divination Cards which reward Currency");
BossCase("Final Boss drops an additional Divination Card which rewards a Unique Item", 1,
    "Final Boss drops # additional Divination Cards which reward a Unique Item");
Check(!AltarBossDrops.TryParse("#% chance to drop an additional Divine Orb", out _, out _), "minion chance is not a boss drop");

var idSet = new HashSet<string>(mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
// Minion wording: always canonical singular, whatever form the game prints
void MinionCase(string raw, string canonical)
{
    Check(AltarBossDrops.TryParseMinion(raw, out var id), "parse minion " + raw);
    Check(id == canonical, $"'{raw}' -> {id}");
}
MinionCase("2.5% chance to drop an additional Ultimatum Thread", "#% chance to drop an additional Ultimatum Thread");
MinionCase("2.5% chance to drop an additional Ultimatum Threads", "#% chance to drop an additional Ultimatum Thread");
MinionCase("12% chance to drop an additional [UltimatumThread|Ultimatum Thread]", "#% chance to drop an additional Ultimatum Thread");
MinionCase("1,6% chance to drop an additional Orbs of Scouring", "#% chance to drop an additional Orb of Scouring");
MinionCase("8% chance to drop an additional Divination Card which rewards Currency",
    "#% chance to drop an additional Divination Card which rewards Currency");
MinionCase("8% chance to drop an additional Divination Cards which reward Currency",
    "#% chance to drop an additional Divination Card which rewards Currency");
Check(!AltarBossDrops.TryParseMinion("Final Boss drops 4 additional Orbs of Scouring", out _), "boss line is not a minion drop");
foreach (var m in mods.Where(m => m.Id.StartsWith(AltarBossDrops.MinionPrefix, StringComparison.Ordinal)))
{
    Check(AltarBossDrops.TryParseMinion(m.Id.Replace("#", "5"), out var mc) && idSet.Contains(mc) && mc == m.Id,
        "minion canonical id is stable for " + m.Id + " -> " + mc);
}
foreach (var m in mods.Where(m => m.Id.StartsWith(AltarBossDrops.Prefix, StringComparison.Ordinal)))
{
    Check(AltarBossDrops.TryParse(m.Id.Replace("#", "5"), out _, out var canon) && idSet.Contains(canon),
        "canonical id exists for " + m.Id + " -> " + canon);
}
Check(AltarWizard.RuleFor("#% reduced Rarity of Items found in this Area", new WizardAnswers()) == Danger.Severe, "reduced rarity is a cost");
Check(AltarWizard.IsReward("#% increased Rarity of Items found in this Area") &&
      !AltarWizard.IsReward("#% reduced Rarity of Items found in this Area"), "increased = reward, reduced = downside");

// Near editor -5..+5 scale
Check(AltarWizard.WeightForScale(-5) == -9000 && AltarWizard.WeightForScale(0) == 0 && AltarWizard.WeightForScale(5) > 0, "scale ends");
for (int sc = -5; sc <= 5; sc++)
    Check(AltarWizard.ScaleForWeight(AltarWizard.WeightForScale(sc)) == sc, "scale round trip " + sc);
Check(AltarWizard.ScaleForWeight(-1000) == -3 && AltarWizard.ScaleForWeight(-20000) == -5, "custom weights snap to nearest step");
Check(AltarWizard.NameForScale(-5) == "BRICK" && AltarWizard.NameForScale(0) == "Ignore", "scale names");

// Every downside weight is negative
Check(bal.Changes.All(ch => ch.Level == Danger.Ignore ? ch.Weight > 0 : ch.Weight < 0), "penalties negative, harmless bonuses positive");
Check(bal.SignedDownsideWeights && bal.HarmlessBonus == 50, "wizard profiles use signed downsides");

// Reward model: chaos is the unit, pricier = heavier, Divine top, threads in bulk
const double div = 153.9;
int W(double chaos, bool thread = false, bool isDivine = false) => AltarRewardWeights.Boss(chaos, div, thread, isDivine);
Check(W(div, false, true) == 10000, "divine is the top weight");
Check(W(39.04) < 10000 && W(39.04) > W(8.15) && W(8.15) > W(6.46), "weights follow price");
Check(W(2.4) > W(1.0) && W(1.99) > W(1.0), "Grand Ichor/Ember outweigh a Chaos Orb");
Check(W(1.0) > 250, "a Chaos Orb drop still counts for something");
Check(W(0.11, true) >= W(1.0), "even the cheapest thread is worth at least a chaos drop");
Check(W(2.04, true) > W(6.46), "good threads outweigh Ancient Orbs");
Check(W(2.04, true) < 10000, "no thread reaches the divine weight");
Check(AltarRewardWeights.Minion(1.0, div, false, false) == (int)Math.Round(W(1.0) * 0.4), "minion = 40% of boss");
Console.WriteLine($"Passed wizard tests ({mods.Count} mods parsed).");

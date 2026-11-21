using System;
using System.Collections.Generic;
using System.Linq;

namespace AltarHelperV2
{
    public enum DamageKind { Melee, Ranged, Spell, Minions }
    public enum DefenceKind { Armour, Evasion, EnergyShield, Mixed }
    public enum Tier3 { Low, Mid, High }
    public enum ResState { Overcapped, Capped, Below }
    public enum ChaosResState { Positive, Zero, Negative }
    public enum FlaskUse { None, Some, Heavy }
    public enum CurseStance { NoCurses, CursesImmune, Curses }
    public enum NodePreference { Balanced, Minions, Boss }
    public enum RiskAppetite { Strict, Balanced, Greedy }
    public enum Danger { Ignore = 0, Minor = 1, Moderate = 2, Severe = 3, Lethal = 4 }

    /// <summary>Answers of the setup survey. Public fields so ImGui can bind to them.</summary>
    public sealed class WizardAnswers
    {
        public DamageKind Damage = DamageKind.Melee;
        public bool DealsPhysical = true, DealsElemental, DealsChaos;
        public DefenceKind Defence = DefenceKind.Armour;
        public Tier3 LifePool = Tier3.Mid;      // Low <4k, Mid 4-6k, High 6k+
        public Tier3 PhysMaxHit = Tier3.Mid;    // Low <3k, Mid 3-6k, High 6k+
        public ResState EleRes = ResState.Overcapped;
        public ChaosResState ChaosRes = ChaosResState.Zero;
        public FlaskUse Flasks = FlaskUse.Some;
        public bool EnduranceCharges, FrenzyCharges, PowerCharges;
        public bool ImmuneChill, ImmuneShock, ImmuneIgnite;
        public CurseStance Curses = CurseStance.NoCurses;
        public bool InflictsNonDamagingAilments;
        public bool UsesProjectiles;
        // Rewards worth taking (tick any combination); *MinChaos = ignore cheap items of that kind (0 = take all)
        public bool RewardDivine = true, RewardEldritch = true, RewardThreads, RewardBasic, RewardUnpriced;
        public bool ThreadsOnlyValuable = true, BasicOnlyValuable = true;
        public float ThreadsMinChaos = 2f, BasicMinChaos = 2f;
        public NodePreference Preference = NodePreference.Balanced;
        public RiskAppetite Risk = RiskAppetite.Balanced;
    }

    public sealed record WizardChange(string Id, string Name, string Group, Danger Level, int Weight);

    public sealed class WizardResult
    {
        /// <summary>Full new mod weight table: rewards (positive) + chosen downsides (negative).</summary>
        public Dictionary<string, int> Tiers { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>The downside rules that fired, for the preview page (Ignore entries included).</summary>
        public List<WizardChange> Changes { get; } = new();
        public int MinPickWeight, NegativeVetoThreshold, PositiveVetoThreshold, AlertMinNetWeight = 1;
        public int MinionWeight, BossWeight;
        /// <summary>Wizard profiles use signed downside weights: harmless downsides carry a small positive bonus.</summary>
        public bool SignedDownsideWeights = true;
        /// <summary>Bonus given to every downside that does not matter for this build.</summary>
        public int HarmlessBonus;
    }

    /// <summary>
    /// Reward weights from prices. Chaos Orb is the reference unit and Divine Orb is always the top weight.
    /// Weight grows with price but flatter than linearly (exponent below 1), so cheap rewards such as a
    /// Chaos Orb still count while a more expensive item always ranks above a cheaper one.
    /// Threads drop in big stacks, so their price is multiplied before it is turned into a weight.
    /// </summary>
    public static class AltarRewardWeights
    {
        public const int Top = 10000;
        public const double Exponent = 0.65;
        public const double ThreadBulkFactor = 10.0;
        public const double MinionShare = 0.4;      // a minion drop chance is worth less than a guaranteed boss drop
        private const double NonDivineRatioCap = 0.5; // nothing but Divine may reach the top weight

        public static int Boss(double chaos, double divine, bool isThread, bool isDivine)
        {
            double eff = isThread ? chaos * ThreadBulkFactor : chaos;
            double ratio = divine > 0 ? eff / divine : 1.0;
            ratio = Math.Clamp(ratio, 0.0, isDivine ? 1.0 : NonDivineRatioCap);
            return Math.Clamp((int)Math.Round(Top * Math.Pow(ratio, Exponent)), 1, Top);
        }

        public static int Minion(double chaos, double divine, bool isThread, bool isDivine) =>
            Math.Clamp((int)Math.Round(Boss(chaos, divine, isThread, isDivine) * MinionShare), 1, Top);

        /// <summary>Boss wording "1 additional ..." (exactly one item instead of about three).</summary>
        public static int BossSingle(double chaos, double divine, bool isThread, bool isDivine) =>
            Math.Clamp((int)Math.Round(Boss(chaos, divine, isThread, isDivine) / 3.0), 1, Top);
    }

    public static class AltarWizard
    {
        public const string GroupRewards = "Rewards";
        public const string GroupResistances = "Resistances";
        public const string GroupDefences = "Defences";
        public const string GroupOffence = "Offence / enemy buffs";
        public const string GroupAilments = "Ailments & ground";
        public const string GroupFlasks = "Flasks & charges";
        public const string GroupOther = "Other";

        private static bool Has(string id, string part) =>
            id.Contains(part, StringComparison.OrdinalIgnoreCase);

        public static bool IsReward(string id) =>
            !Has(id, "reduced Quantity") && !Has(id, "reduced Rarity") &&
            (id.StartsWith("Final Boss drops", StringComparison.OrdinalIgnoreCase) ||
            Has(id, "chance to drop an additional") || Has(id, "Duplicated") ||
            Has(id, "Quantity of Items") || Has(id, "Rarity of Items") || Has(id, "Experience gain"));

        /// <summary>Coarse category of a mod id (used for the wizard preview and future group controls).</summary>
        public static string Categorize(string id)
        {
            if (IsReward(id)) return GroupRewards;
            if (Has(id, "Flask") || Has(id, "Charge")) return GroupFlasks;
            if (Has(id, "Energy Shield") || Has(id, "Armour") || Has(id, "Evasion") || Has(id, "Defences") ||
                Has(id, "Physical Damage Reduction") || Has(id, "Suppress")) return GroupDefences;
            if (Has(id, "Resistance") && !Has(id, "Penetrates")) return GroupResistances;
            if (Has(id, "Ground") || Has(id, "Ignite") || Has(id, "Shock") || Has(id, "Chill") || Has(id, "Poison") ||
                Has(id, "Sap") || Has(id, "Scorch") || Has(id, "Hinder") || Has(id, "Blind") || Has(id, "Frost") ||
                Has(id, "Ash") || Has(id, "Exposure") || Has(id, "Vine") || Has(id, "reflected") ||
                Has(id, "Malediction") || Has(id, "Curse") || Has(id, "Tentacles")) return GroupAilments;
            if (Has(id, "Extra") || Has(id, "Penetrat") || Has(id, "Speed") || Has(id, "Projectile") ||
                Has(id, "Area of Effect") || Has(id, "Critical Strike") || Has(id, "ignore Enemy")) return GroupOffence;
            return GroupOther;
        }

        /// <summary>
        /// Is a priced reward item worth a weight for this character? <paramref name="group"/> is
        /// "Divine", "Eldritch", "Thread" or "Basic"; <paramref name="chaos"/> is its current price.
        /// </summary>
        public static bool IncludeReward(WizardAnswers a, string group, double chaos) => group switch
        {
            "Divine" => a.RewardDivine,
            "Eldritch" => a.RewardEldritch,
            "Thread" => a.RewardThreads && (!a.ThreadsOnlyValuable || chaos >= a.ThreadsMinChaos),
            "Basic" => a.RewardBasic && (!a.BasicOnlyValuable || chaos >= a.BasicMinChaos),
            _ => false,
        };

        public static int WeightFor(Danger level, RiskAppetite risk)
        {
            int baseWeight = level switch
            {
                Danger.Minor => 60,
                Danger.Moderate => 250,
                Danger.Severe => 800,
                Danger.Lethal => 3000,
                _ => 0,
            };
            double mult = risk switch { RiskAppetite.Strict => 1.5, RiskAppetite.Greedy => 0.5, _ => 1.0 };
            return (int)Math.Round(baseWeight * mult / 10.0) * 10;
        }

        // -5 .. +5 danger scale used by the near editor: -5 bricks the character, 0 ignores the mod,
        // +1..+5 are growing "harmless, take it" bonuses (need signed downside weights).
        private static readonly int[] ScaleWeights = { -9000, -3000, -800, -250, -60, 0, 20, 50, 100, 200, 400 };
        private static readonly string[] ScaleNames =
            { "BRICK", "Lethal", "Severe", "Moderate", "Minor", "Ignore", "Harmless +1", "Harmless +2", "Harmless +3", "Harmless +4", "Harmless +5" };

        public static int WeightForScale(int scale) => ScaleWeights[Math.Clamp(scale, -5, 5) + 5];

        public static string NameForScale(int scale) => ScaleNames[Math.Clamp(scale, -5, 5) + 5];

        /// <summary>The scale step closest to an arbitrary weight (custom weights snap for display).</summary>
        public static int ScaleForWeight(int weight)
        {
            int best = 0, bestDiff = int.MaxValue;
            for (int i = 0; i < ScaleWeights.Length; i++)
            {
                int diff = Math.Abs(ScaleWeights[i] - weight);
                if (diff < bestDiff) { bestDiff = diff; best = i - 5; }
            }
            return best;
        }

        /// <summary>Small plus for a downside that does not hurt this build (more willingness to take the node).</summary>
        public static int HarmlessBonusFor(RiskAppetite risk) => risk switch
        {
            RiskAppetite.Strict => 20,
            RiskAppetite.Greedy => 100,
            _ => 50,
        };

        private static Danger Shift(Danger d, int by)
        {
            if (d == Danger.Ignore || by == 0) return d;
            return (Danger)Math.Clamp((int)d + by, (int)Danger.Minor, (int)Danger.Lethal);
        }

        /// <summary>
        /// Danger of one mod for the described character, or null when the mod is not a downside
        /// rule target (rewards and header lines).
        /// </summary>
        public static Danger? RuleFor(string id, WizardAnswers a)
        {
            if (IsReward(id) || Has(id, "Upside Eldritch Minions") || Has(id, "grasping vines to gain")) return null;

            // Fragile characters feel physical / defensive downsides more, tanky ones less.
            int minTier = Math.Min((int)a.LifePool, (int)a.PhysMaxHit);
            int frag = minTier == 0 ? 1 : minTier == 2 ? -1 : 0;

            bool attacks = a.Damage is DamageKind.Melee or DamageKind.Ranged;
            bool anyCharge = a.EnduranceCharges || a.FrenzyCharges || a.PowerCharges;

            Danger EleLevel() => a.EleRes switch
            {
                ResState.Overcapped => Danger.Minor,
                ResState.Capped => Danger.Moderate,
                _ => Danger.Severe,
            };
            Danger ChaosLevel() => a.ChaosRes switch
            {
                ChaosResState.Positive => Danger.Moderate,
                ChaosResState.Zero => Danger.Severe,
                _ => Danger.Lethal,
            };
            Danger FlaskLevel(Danger heavy, Danger some) =>
                a.Flasks == FlaskUse.Heavy ? heavy : a.Flasks == FlaskUse.Some ? some : Danger.Ignore;
            Danger ArmourDefence(Danger armour, Danger mixed, Danger other) =>
                a.Defence == DefenceKind.Armour ? armour : a.Defence == DefenceKind.Mixed ? mixed : other;

            // ---------------- Player downsides ("-#% ..." and player effects) ----------------
            if (Has(id, "reduced Quantity of Items") || Has(id, "reduced Rarity of Items"))
                return Danger.Severe;                                                  // item find is a real cost
            if (Has(id, "Take # Chaos Damage per second during any Flask Effect"))
                return a.Flasks == FlaskUse.None ? Danger.Ignore
                    : a.ChaosRes == ChaosResState.Positive ? Danger.Moderate : Danger.Lethal;
            if (Has(id, "targeted by a Meteor when you use a Flask"))
                return FlaskLevel(Danger.Severe, Danger.Moderate);
            if (Has(id, "Curses you inflict are reflected"))
                return a.Curses switch { CurseStance.NoCurses => Danger.Ignore, CurseStance.CursesImmune => Danger.Minor, _ => Danger.Severe };
            if (Has(id, "Non-Damaging Ailments you inflict are reflected"))
                return a.InflictsNonDamagingAilments ? Danger.Severe : Danger.Ignore;
            if (Has(id, "Projectiles are fired in random directions"))
                return a.UsesProjectiles ? Danger.Lethal : Danger.Ignore;

            if (id.StartsWith("-#% to Chaos Resistance", StringComparison.OrdinalIgnoreCase))
                return a.ChaosRes switch { ChaosResState.Positive => Danger.Minor, ChaosResState.Zero => Danger.Moderate, _ => Danger.Severe };
            if (id.StartsWith("-#% to ", StringComparison.OrdinalIgnoreCase) && Has(id, "Resistance"))
                return a.EleRes switch { ResState.Overcapped => Danger.Ignore, ResState.Capped => Danger.Moderate, _ => Danger.Severe };
            if (id.StartsWith("-#% additional Physical Damage Reduction", StringComparison.OrdinalIgnoreCase))
                return Shift(ArmourDefence(Danger.Severe, Danger.Moderate, Danger.Minor), frag);
            if (id.StartsWith("-# to Armour", StringComparison.OrdinalIgnoreCase))
                return ArmourDefence(Danger.Severe, Danger.Moderate, Danger.Ignore);
            if (id.StartsWith("-# to Evasion Rating", StringComparison.OrdinalIgnoreCase))
                return a.Defence == DefenceKind.Evasion ? Danger.Severe : a.Defence == DefenceKind.Mixed ? Danger.Moderate : Danger.Ignore;

            if (Has(id, "reduced Defences per Frenzy Charge"))
                return a.FrenzyCharges ? Shift(Danger.Severe, frag) : Danger.Ignore;
            if (Has(id, "Recovery Rate of Life, Mana and Energy Shield per Endurance Charge"))
                return a.EnduranceCharges ? Danger.Moderate : Danger.Ignore;
            if (Has(id, "Critical Strike Multiplier per Power Charge"))
                return a.PowerCharges && a.Damage != DamageKind.Minions ? Danger.Moderate : Danger.Ignore;

            if (Has(id, "Chilled Ground when Hitting you")) return a.ImmuneChill ? Danger.Ignore : Danger.Minor;
            if (Has(id, "Shocked Ground when Hitting you")) return a.ImmuneShock ? Danger.Ignore : Danger.Moderate;
            if (Has(id, "Burning Ground when Hitting you")) return a.ImmuneIgnite ? Danger.Ignore : Danger.Moderate;
            if (Has(id, "Sapped") || Has(id, "Sap you") || Has(id, "Scorch")) return Shift(Danger.Minor, frag);
            if (Has(id, "Hinder you")) return a.Damage == DamageKind.Melee ? Danger.Moderate : Danger.Minor;

            if (Has(id, "Enemies within # metres Gain #% of their Physical Damage as Extra Chaos"))
                return Shift(ChaosLevel(), frag);
            if (Has(id, "Enemies within # metres Gain #% of their Physical Damage as Extra"))
                return Shift(EleLevel(), frag);

            if (Has(id, "increased Flask Charges used") || Has(id, "reduced Flask Effect Duration"))
                return FlaskLevel(Danger.Moderate, Danger.Minor);

            // ---------------- Enemy (boss / minion) buffs ----------------
            if (Has(id, "to maximum Chaos Resistance")) return a.DealsChaos ? Danger.Minor : Danger.Ignore;
            if (Has(id, "to Chaos Resistance")) return a.DealsChaos ? Danger.Moderate : Danger.Ignore;
            if (Has(id, "to maximum") && Has(id, "Resistance")) return a.DealsElemental ? Danger.Minor : Danger.Ignore;
            if (Has(id, "Resistance")) return a.DealsElemental ? Danger.Moderate : Danger.Ignore;

            if (Has(id, "additional Physical Damage Reduction")) return a.DealsPhysical ? Danger.Moderate : Danger.Ignore;
            if (Has(id, "Suppress")) return a.Damage == DamageKind.Spell ? Danger.Moderate : Danger.Ignore;
            if (Has(id, "ignore Enemy Physical Damage Reduction"))
                return Shift(ArmourDefence(Danger.Severe, Danger.Moderate, Danger.Minor), frag);

            if (Has(id, "Maximum Life as Extra Maximum Energy Shield")) return Danger.Minor;
            if (Has(id, "as Extra Chaos Damage")) return Shift(ChaosLevel(), frag);
            if (Has(id, "as Extra Damage of a random Element")) return Shift(Danger.Moderate, frag);
            if (Has(id, "as Extra")) return Shift(EleLevel(), frag);
            if (Has(id, "Damage Penetrates"))
                return a.EleRes switch { ResState.Overcapped => Danger.Minor, ResState.Capped => Danger.Moderate, _ => Danger.Severe };
            if (Has(id, "Exposure on Hit")) return a.EleRes == ResState.Overcapped ? Danger.Minor : Danger.Moderate;

            if (Has(id, "Blind")) return attacks ? Danger.Moderate : Danger.Minor;
            if (Has(id, "Hits always Shock") || Has(id, "All Damage can Shock")) return a.ImmuneShock ? Danger.Ignore : Danger.Moderate;
            if (Has(id, "Hits always Ignite") || Has(id, "All Damage can Ignite")) return a.ImmuneIgnite ? Danger.Ignore : Danger.Moderate;
            if (Has(id, "can Chill") || Has(id, "Cover Enemies in Frost")) return a.ImmuneChill ? Danger.Ignore : Danger.Minor;
            if (Has(id, "Cover Enemies in Ash")) return Danger.Minor;
            if (Has(id, "Poison")) return a.ChaosRes == ChaosResState.Positive ? Danger.Minor : Danger.Moderate;
            if (Has(id, "Malediction")) return Danger.Moderate;
            if (Has(id, "Punishment") || Has(id, "Vulnerability") || Has(id, "Grasping Vine") ||
                Has(id, "Eldritch Tentacles") || Has(id, "Consecrated Ground")) return Danger.Minor;
            if (Has(id, "are Hindered")) return a.Damage == DamageKind.Melee ? Danger.Moderate : Danger.Minor;

            if (Has(id, "Drops Chilled Ground on Death")) return a.ImmuneChill ? Danger.Ignore : Danger.Minor;
            if (Has(id, "Shocked Ground on Death")) return a.ImmuneShock ? Danger.Ignore : Danger.Moderate;
            if (Has(id, "Burning Ground on Death")) return a.ImmuneIgnite ? Danger.Ignore : Danger.Moderate;

            if (Has(id, "Enemies lose # Flask Charges")) return FlaskLevel(Danger.Severe, Danger.Moderate);
            if (Has(id, "remove a random Charge from Enemy")) return anyCharge ? Danger.Severe : Danger.Ignore;

            if (Has(id, "+# to Armour")) return a.DealsPhysical ? Danger.Moderate : Danger.Minor;
            if (Has(id, "increased Armour")) return a.DealsPhysical ? Danger.Minor : Danger.Ignore;
            if (Has(id, "increased Evasion Rating")) return attacks ? Danger.Minor : Danger.Ignore;
            if (Has(id, "additional Projectiles")) return Danger.Moderate;
            if (Has(id, "increased Attack Speed")) return Danger.Moderate;
            if (Has(id, "increased Cast Speed")) return Danger.Minor;
            if (Has(id, "increased Movement Speed")) return Danger.Moderate;
            if (Has(id, "increased Area of Effect")) return Danger.Minor;

            return null;
        }

        /// <summary>
        /// Builds the full weight table for a new profile.
        /// <paramref name="rewardTiers"/> holds the positive reward weights (already filtered by reward focus).
        /// </summary>
        public static WizardResult Build(WizardAnswers a,
            IEnumerable<(string Id, string Name, string Type)> mods,
            IReadOnlyDictionary<string, int> rewardTiers)
        {
            var result = new WizardResult();
            foreach (var kv in rewardTiers) result.Tiers[kv.Key] = kv.Value;

            result.HarmlessBonus = HarmlessBonusFor(a.Risk);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, name, _) in mods)
            {
                if (!seen.Add(id)) continue;
                var level = RuleFor(id, a);
                if (level == null) continue;

                // Harmless downsides get a small POSITIVE weight (needs signed downside weights in the plugin).
                int weight = level.Value == Danger.Ignore ? result.HarmlessBonus : -WeightFor(level.Value, a.Risk);
                if (weight != 0) result.Tiers[id] = weight;
                result.Changes.Add(new WizardChange(id, name, Categorize(id), level.Value, weight));
            }

            switch (a.Risk)
            {
                case RiskAppetite.Strict:
                    result.MinPickWeight = 600; result.NegativeVetoThreshold = 2500; result.PositiveVetoThreshold = 5000; break;
                case RiskAppetite.Greedy:
                    result.MinPickWeight = 1; result.NegativeVetoThreshold = 8000; result.PositiveVetoThreshold = 3000; break;
                default:
                    result.MinPickWeight = 200; result.NegativeVetoThreshold = 3000; result.PositiveVetoThreshold = 6000; break;
            }
            result.AlertMinNetWeight = Math.Max(1, result.MinPickWeight);

            result.MinionWeight = a.Preference == NodePreference.Minions ? 25 : 0;
            result.BossWeight = a.Preference == NodePreference.Boss ? 25 : 0;
            return result;
        }
    }
}

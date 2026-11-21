using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using Newtonsoft.Json;
using SharpDX;
using SN = System.Numerics;

namespace AltarHelperV2
{
    [SupportedOSPlatform("windows")]
    public partial class Settings : ISettings
    {
        public ToggleNode Enable { get; set; } = new ToggleNode(true);
        public AltarSettings AltarSettings { get; set; } = new AltarSettings();
        public DisplaySettings DisplaySettings { get; set; } = new DisplaySettings();
        public DebugSettings DebugSettings { get; set; } = new DebugSettings();

        // Plain fields (not properties) to avoid ExileCore settings reflection warnings
        [JsonIgnore]
        public Action DrawUnknownModsDelegate = () => { };

        // Called by Core's DrawUnknownModsSection to move focus+filter to a mod in the main table
        [JsonIgnore]
        public Action<string> SetModFilter = _ => { };

        [JsonIgnore]
        public CustomNode ModsTable { get; }

        // [Test] Profiles + setup wizard panel (see SettingsProfiles.cs); the old panel above stays as it is
        [JsonIgnore]
        public CustomNode ProfilesPanel { get; }

        [JsonIgnore]
        public CustomNode UnknownModsDisplay { get; }

        [JsonIgnore]
        public CustomNode FAQ { get; }

        // Plain fields — ExileCore reflection doesn't warn on fields, only properties
        public Dictionary<string, int> ModTiers = BuildDefaultTiers();

        // Weights the user moved by hand: automatic price updates never overwrite these.
        public HashSet<string> ManualWeights = new(StringComparer.OrdinalIgnoreCase);

        // Automatic reward weights from live poe.ninja prices (Ninja Price plugin cache)
        public bool AutoUpdatePrices = true;

        [JsonIgnore]
        public string PriceStatus = "";

        [JsonIgnore]
        public Action RefreshPricesNow = () => { };

        public Dictionary<string, bool> ModAlerts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Final Boss drops # additional Eldritch Chaos Orbs"] = true,
            ["#% chance to drop an additional Eldritch Chaos Orb"] = true,
            ["#% chance to drop an additional Chaos Orb"] = true,
            ["Final Boss drops # additional Divine Orbs"] = true,
            ["#% chance to drop an additional Divine Orb"] = true,
            ["Take # Chaos Damage per second during any Flask Effect"] = true,
            ["#% chance to be targeted by a Meteor when you use a Flask"] = true,
            ["#% reduced Recovery Rate of Life, Mana and Energy Shield per Endurance Charge"] = false,
            ["#% to Critical Strike Multiplier per Power Charge"] = true,
            ["#% increased Rarity of Items found in this Area"] = false,
            ["Projectiles are fired in random directions"] = false,
            ["Final Boss drops # additional Eldritch Orbs of Annulment"] = true,
            ["#% chance to drop an additional Orb of Annulment"] = true,
        };

        public Settings()
        {
            var unitFilter = "";
            SetModFilter = (keyword) => { unitFilter = keyword; };

#pragma warning disable CA1416
            ModsTable = new CustomNode
            {
                DrawDelegate = () =>
                {
                    if (!ImGui.TreeNode("Mods & Weights (V2)")) return;

                    DrawProfileQuickSwitch();

                    ImGui.TextColored(new SN.Vector4(1f, 0.65f, 0.2f, 1f),
                        "IMPORTANT: bundled weights are the maintainer's personal defaults.");
                    ImGui.TextColored(new SN.Vector4(1f, 0.8f, 0.45f, 1f),
                        "Review every configured value for your build and the current economy.");
                    ImGui.Separator();

                    ImGui.TextColored(new SN.Vector4(0.9f, 0.8f, 0.2f, 1f),
                        "Slider > 0 = good mod | < 0 or in downside section = penalty to net weight");
                    ImGui.TextColored(new SN.Vector4(0.7f, 1f, 0.7f, 1f),
                        "Positive Veto: mod >= threshold → always take this node (wins over negative veto)");
                    ImGui.Separator();

                    // Presets
                    ImGui.Text("Quick presets:");
                    ImGui.SameLine();
                    if (ImGui.Button("Divine Orb Focus"))
                        ApplyPreset_DivineFocus();
                    ImGui.SameLine();
                    if (ImGui.Button("Thread Focus"))
                        ApplyPreset_ThreadFocus();
                    ImGui.SameLine();
                    if (ImGui.Button("Eldritch Currency"))
                        ApplyPreset_EldritchFocus();
                    ImGui.SameLine();
                    if (ImGui.Button("Armour Build"))
                        ApplyPreset_ArmourBuild();
                    ImGui.SameLine();
                    if (ImGui.Button("[Test] Champion (Bannerman)"))
                        ApplyPreset_ChampionBuild();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Preset testowy: profil dostosowany pod build Bannerman Champion (melee physical, armour).");
                    ImGui.SameLine();
                    if (ImGui.Button("Reset All Weights"))
                    {
                        ModTiers.Clear();
                        ManualWeights.Clear();
                    }

                    ImGui.Checkbox("[Test] Auto-update reward weights from poe.ninja prices", ref AutoUpdatePrices);
                    ImGui.SameLine();
                    if (ImGui.Button("Refresh prices now"))
                        RefreshPricesNow();
                    ImGui.TextColored(new SN.Vector4(0.6f, 0.85f, 1f, 1f),
                        "Prices: " + (PriceStatus.Length > 0 ? PriceStatus : "built-in (Covens 2026-11-17)"));
                    ImGui.TextColored(new SN.Vector4(0.7f, 0.7f, 0.7f, 1f),
                        "Divine Orb is always the top weight. Weights you move by hand are not overwritten.");

                    ImGui.Separator();
                    ImGui.InputTextWithHint("##V2UnitFilter", "Search mods...", ref unitFilter, 200);

                    // Configured mod counter
                    int configuredCount = ModTiers.Count(kvp => kvp.Value != 0);
                    ImGui.SameLine();
                    ImGui.TextColored(new SN.Vector4(0.7f, 0.9f, 0.7f, 1f), $"  ({configuredCount} configured)");

                    float tableHeight = 420f;
                    if (!ImGui.BeginTable("V2UnitConfigV2", 5,
                        ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Borders |
                        ImGuiTableFlags.ScrollY |
                        ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings,
                        new SN.Vector2(-1, tableHeight)))
                        return;

                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableSetupColumn("Weight", ImGuiTableColumnFlags.WidthFixed, 220);
                    ImGui.TableSetupColumn("Mod", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 60);
                    ImGui.TableSetupColumn("Alert", ImGuiTableColumnFlags.WidthFixed, 70);
                    ImGui.TableSetupColumn("Clear", ImGuiTableColumnFlags.WidthFixed, 60);

                    // Draw a plain header row. ImGui.TableHeadersRow adds per-column
                    // menu buttons which render as unreadable glyphs with ExileCore's font.
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextUnformatted("Weight");
                    ImGui.TableNextColumn(); ImGui.TextUnformatted("Mod");
                    ImGui.TableNextColumn(); ImGui.TextUnformatted("Type");
                    ImGui.TableNextColumn(); ImGui.TextUnformatted("Alert");
                    ImGui.TableNextColumn(); ImGui.TextUnformatted("Clear");

                    var filtered = AltarModsConstants.AltarTypes
                        .Where(t => t.Name.Contains(unitFilter, StringComparison.InvariantCultureIgnoreCase));

                    foreach (var (id, name, type) in filtered)
                    {
                        ImGui.PushID($"v2mod_{id}");
                        ImGui.TableNextRow();

                        var currentValue = GetModTier(id);
                        bool isConfigured = currentValue != 0;

                        // Weight column (slider + bar)
                        ImGui.TableNextColumn();
                        if (isConfigured)
                            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0,
                                ImGui.ColorConvertFloat4ToU32(new SN.Vector4(0.2f, 0.3f, 0.1f, 0.4f)));

                        ImGui.SetNextItemWidth(185);
                        if (ImGui.SliderInt("##w", ref currentValue, -1000, 1000))
                        {
                            ModTiers[id] = currentValue;
                            ManualWeights.Add(id);
                        }

                        // Visual weight bar
                        float barWidth = 185f;
                        float barHeight = 4f;
                        var drawList = ImGui.GetWindowDrawList();
                        var cursorPos = ImGui.GetCursorScreenPos();
                        float normalized = (currentValue + 1000f) / 2000f; // 0..1
                        uint barBg = ImGui.ColorConvertFloat4ToU32(new SN.Vector4(0.2f, 0.2f, 0.2f, 0.8f));
                        uint barFg = currentValue >= 0
                            ? ImGui.ColorConvertFloat4ToU32(new SN.Vector4(0.2f, 0.8f, 0.2f, 0.9f))
                            : ImGui.ColorConvertFloat4ToU32(new SN.Vector4(0.8f, 0.2f, 0.2f, 0.9f));
                        drawList.AddRectFilled(cursorPos, new SN.Vector2(cursorPos.X + barWidth, cursorPos.Y + barHeight), barBg);
                        drawList.AddRectFilled(cursorPos, new SN.Vector2(cursorPos.X + normalized * barWidth, cursorPos.Y + barHeight), barFg);
                        ImGui.Dummy(new SN.Vector2(barWidth, barHeight + 2));

                        // Mod name column (colored by type)
                        ImGui.TableNextColumn();
                        var col = type switch
                        {
                            "Boss" => new SN.Vector4(0.5f, 0.7f, 1f, 1f),
                            "Minion" => new SN.Vector4(0.5f, 1f, 0.5f, 1f),
                            "Player" => new SN.Vector4(1f, 0.8f, 0.5f, 1f),
                            _ => new SN.Vector4(1f, 1f, 1f, 1f)
                        };
                        // TextColored treats the string as a printf format — escape % to avoid garbage output
                        ImGui.TextColored(col, name.Replace("%", "%%"));
                        var priceText = PriceLabelFor(id);
                        if (priceText != null)
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(PriceColor, priceText);
                        }
                        DrawUnsavedMarker(id);

                        // Type column
                        ImGui.TableNextColumn();
                        ImGui.Text(type);

                        // Alert column
                        ImGui.TableNextColumn();
                        var alertVal = GetModAlert(id);
                        if (ImGui.Checkbox("##a", ref alertVal))
                            ModAlerts[id] = alertVal;

                        // Reset column
                        ImGui.TableNextColumn();
                        if (isConfigured && ImGui.SmallButton("X"))
                        {
                            ModTiers.Remove(id);
                            ManualWeights.Remove(id);
                        }

                        ImGui.PopID();
                    }
                    ImGui.EndTable();
                    ImGui.TreePop();
                }
            };

            ProfilesPanel = new CustomNode
            {
                DrawDelegate = DrawProfilesPanel
            };

            UnknownModsDisplay = new CustomNode
            {
                DrawDelegate = () => DrawUnknownModsDelegate?.Invoke()
            };

            FAQ = new CustomNode
            {
                DrawDelegate = DrawFaq
            };
#pragma warning restore CA1416
        }

        private static void DrawFaq()
        {
#pragma warning disable CA1416
            if (!ImGui.TreeNode("FAQ / How it works")) return;

            ImGui.TextColored(new SN.Vector4(1f, 0.75f, 0.25f, 1f),
                "Review the bundled weights before playing.");
            ImGui.TextWrapped(
                "They are the maintainer's personal defaults, not live prices or universal build recommendations.");
            ImGui.Separator();

            DrawFaqItem("How is the score calculated?",
                "Score = total upside weight - total downside weight + target bonus. " +
                "The highest valid positive score is highlighted; equal scores highlight both choices.");
            DrawFaqItem("What do weights mean?",
                "Positive values reward a modifier, negative values penalize danger, and 0 ignores it.");
            DrawFaqItem("What are veto thresholds?",
                "Positive Veto prioritizes one very valuable reward. Negative Veto marks one dangerous downside. " +
                "A threshold of 0 disables that veto; Positive Veto wins when both apply.");
            DrawFaqItem("What do the modes do?",
                "Any scores all targets. Minions + Player focuses on Eldritch minions and player effects. " +
                "Boss + Player focuses on the final map boss and player effects. F7 switches modes by default.");
            DrawFaqItem("What do the colors mean?",
                "Gold = positive-veto priority, yellow = recommended, orange = mixed reward and danger, red = avoid.");
            DrawFaqItem("What do the dots mean?",
                "Green = Eldritch minions, cyan = player, blue = final map boss. Set Dot size to 0 to hide them.");
            DrawFaqItem("What are Alert and Clear?",
                "Alert enables a sound rule for that modifier. Clear (X) removes its custom weight and returns it to 0.");
            DrawFaqItem("How do presets work?",
                "Presets assign groups of starting weights without clearing unrelated values. Reward weights come from poe.ninja prices (Divine = top weight). With auto-update on, the plugin re-prices them from the Ninja Price plugin cache whenever it changes; weights you move by hand are kept. Armour Build also adds small negative weights to defensive downsides, so only clearly worthwhile nodes are taken. [Test] Champion (Bannerman) is tailored to a melee physical armour character.");
            DrawFaqItem("What are Unknown Mods?",
                "They are altar lines that were not matched to the bundled database. Use Find to locate a related entry.");
            DrawFaqItem("Are existing settings overwritten?",
                "No. ExileCore loads your saved local configuration. Defaults apply only to new or reset settings.");
            DrawFaqItem("Does the plugin click an altar?",
                "No. It only reads the visible choices, calculates a recommendation, and draws the overlay.");

            ImGui.TreePop();
#pragma warning restore CA1416
        }

        private static void DrawFaqItem(string question, string answer)
        {
#pragma warning disable CA1416
            ImGui.TextColored(new SN.Vector4(0.45f, 0.8f, 1f, 1f), question);
            ImGui.Indent();
            ImGui.TextWrapped(answer);
            ImGui.Unindent();
            ImGui.Spacing();
#pragma warning restore CA1416
        }

        // ============================================================
        // Price-based weights - poe.ninja, league "Covens", 2026-11-17
        // (read from the Ninja Price / Get-Chaos-Value cache: NinjaData\Covens\*.json)
        //
        // Scale (see AltarRewardWeights): Divine Orb is ALWAYS the top reward (10000). Everything else is
        //   10000 x (price / divine) ^ 0.65, so cheap rewards like a Chaos Orb still count while a pricier
        //   item always ranks above a cheaper one. Threads use price x 10 (they drop in big stacks).
        //   minion drop weight = 40% of the boss weight        (low chance per kill)
        //
        // These numbers are only the built-in fallback; live prices from the Ninja Price cache replace them.
        // ============================================================
        private const double DivineChaos = 153.9;   // built-in fallback, used when no live prices exist
        private const int TopWeight = 10000;

        // Live prices (item name -> chaos) from the Ninja Price cache; empty = use the built-in table
        private Dictionary<string, double> _livePrices = new(StringComparer.OrdinalIgnoreCase);

        private double CurrentDivine =>
            _livePrices.TryGetValue("Divine Orb", out var d) && d > 0 ? d : DivineChaos;

        private double PriceOf(string name, double fallback) =>
            _livePrices.TryGetValue(name, out var v) && v > 0 ? v : fallback;

        private enum RewardGroup { Basic, Divine, Eldritch, Thread }

        // (singular name used by minion mods, plural used by boss mods, chaos price, group)
        private static readonly (string One, string Many, double Chaos, RewardGroup Group)[] PricedRewards =
        {
            ("Divine Orb", "Divine Orbs", DivineChaos, RewardGroup.Divine),
            ("Eldritch Chaos Orb", "Eldritch Chaos Orbs", 39.04, RewardGroup.Eldritch),
            ("Eldritch Orb of Annulment", "Eldritch Orbs of Annulment", 26.3, RewardGroup.Eldritch),
            ("Eldritch Exalted Orb", "Eldritch Exalted Orbs", 2.97, RewardGroup.Eldritch),
            ("Grand Eldritch Ichor", "Grand Eldritch Ichors", 2.4, RewardGroup.Eldritch),
            ("Grand Eldritch Ember", "Grand Eldritch Embers", 1.99, RewardGroup.Eldritch),
            ("Greater Eldritch Ichor", "Greater Eldritch Ichors", 0.97, RewardGroup.Eldritch),
            ("Greater Eldritch Ember", "Greater Eldritch Embers", 0.6, RewardGroup.Eldritch),
            ("Lesser Eldritch Ichor", "Lesser Eldritch Ichors", 0.48, RewardGroup.Eldritch),
            ("Lesser Eldritch Ember", "Lesser Eldritch Embers", 0.49, RewardGroup.Eldritch),
            ("Orb of Unmaking", "Orbs of Unmaking", 0.57, RewardGroup.Eldritch),
            ("Orb of Annulment", "Orbs of Annulment", 8.15, RewardGroup.Basic),
            ("Ancient Orb", "Ancient Orbs", 6.46, RewardGroup.Basic),
            ("Gemcutter's Prism", "Gemcutter's Prisms", 2.41, RewardGroup.Basic),
            ("Glassblower's Bauble", "Glassblower's Baubles", 1.95, RewardGroup.Basic),
            ("Orb of Regret", "Orbs of Regret", 1.25, RewardGroup.Basic),
            ("Exalted Orb", "Exalted Orbs", 0.89, RewardGroup.Basic),
            ("Chromatic Orb", "Chromatic Orbs", 0.81, RewardGroup.Basic),
            ("Orb of Scouring", "Orbs of Scouring", 0.57, RewardGroup.Basic),
            ("Vaal Orb", "Vaal Orbs", 0.52, RewardGroup.Basic),
            ("Instilling Orb", "Instilling Orbs", 0.5, RewardGroup.Basic),
            ("Enkindling Orb", "Enkindling Orbs", 0.36, RewardGroup.Basic),
            ("Regal Orb", "Regal Orbs", 0.2, RewardGroup.Basic),
            ("Blessed Orb", "Blessed Orbs", 0.17, RewardGroup.Basic),
            ("Orb of Fusing", "Orbs of Fusing", 0.14, RewardGroup.Basic),
            ("Orb of Alteration", "Orbs of Alteration", 0.08, RewardGroup.Basic),
            ("Jeweller's Orb", "Jeweller's Orbs", 0.03, RewardGroup.Basic),
            ("Chaos Orb", "Chaos Orbs", 1.0, RewardGroup.Basic),
            ("Legion Thread", "Legion Threads", 2.04, RewardGroup.Thread),
            ("Ultimatum Thread", "Ultimatum Threads", 1.91, RewardGroup.Thread),
            ("Abyss Thread", "Abyss Threads", 1.86, RewardGroup.Thread),
            ("Ritual Thread", "Ritual Threads", 0.93, RewardGroup.Thread),
            ("Blight Thread", "Blight Threads", 0.9, RewardGroup.Thread),
            ("Bestiary Thread", "Bestiary Threads", 0.68, RewardGroup.Thread),
            ("Breach Thread", "Breach Threads", 0.46, RewardGroup.Thread),
            ("Betrayal Thread", "Betrayal Threads", 0.28, RewardGroup.Thread),
            ("Incursion Thread", "Incursion Threads", 0.17, RewardGroup.Thread),
            ("Expedition Thread", "Expedition Threads", 0.11, RewardGroup.Thread),
        };

        private static string BossKey(string many) => $"Final Boss drops # additional {many}";
        private static string MinionKey(string one) => $"#% chance to drop an additional {one}";

        // Weight model lives in AltarRewardWeights (AltarWizard.cs): chaos price is the unit, Divine is the top,
        // weights grow flatter than the price, and Threads get a bulk factor because they drop in big stacks.
        private static void AddPriced(IDictionary<string, int> dict, (string One, string Many, double Chaos, RewardGroup Group) r,
            double chaos, double divine, Func<string, bool> skip = null)
        {
            void Set(string key, int weight)
            {
                if (skip != null && skip(key)) return;
                dict[key] = weight;
            }

            bool thread = r.Group == RewardGroup.Thread;
            bool isDivine = r.Group == RewardGroup.Divine;
            Set(BossKey(r.Many), AltarRewardWeights.Boss(chaos, divine, thread, isDivine));
            Set(MinionKey(r.One), AltarRewardWeights.Minion(chaos, divine, thread, isDivine));
            // Boss "1 additional" Ichor/Ember variant (singular wording, exactly 1 item instead of avg 3)
            if (r.One.Contains("Eldritch Ichor") || r.One.Contains("Eldritch Ember"))
                Set(BossKey(r.One), AltarRewardWeights.BossSingle(chaos, divine, thread, isDivine));
        }

        // Rewards that have no market price: kept as manual values (duplication, quantity, Divination Cards).
        private static void AddManual(IDictionary<string, int> dict)
        {
            // Duplicated drops multiply EVERYTHING that drops, so they are weighted high.
            dict["Threads dropped by slain Enemies have #% chance to be Duplicated"] = 2000;
            dict["Basic Currency Items dropped by slain Enemies have #% chance to be Duplicated"] = 1500;
            dict["Divination Cards dropped by slain Enemies have #% chance to be Duplicated"] = 2000;
            dict["Unique Items dropped by slain Enemies have #% chance to be Duplicated"] = 700;
            dict["Maps dropped by slain Enemies have #% chance to be Duplicated"] = 500;
            dict["Gems dropped by slain Enemies have #% chance to be Duplicated"] = 300;
            dict["#% increased Quantity of Items found in this Area"] = 1000;
            dict["#% increased Rarity of Items found in this Area"] = 1500;

            // Extra Divination Cards (boss: guaranteed extra cards, minion: chance per kill)
            void Cards(string kind, int boss, int minion)
            {
                dict[$"Final Boss drops # additional Divination Cards which reward {kind}"] = boss;
                dict[$"#% chance to drop an additional Divination Card which rewards {kind}"] = minion;
            }
            Cards("Currency", 1200, 500);
            Cards("Basic Currency", 800, 330);
            Cards("League Currency", 900, 360);
            Cards("other Divination Cards", 400, 160);

            dict["Projectiles are fired in random directions"] = 181;

            // Reduced item quantity / rarity are costs (stored negative)
            dict["#% reduced Quantity of Items found in this Area"] = -1000;
            dict["#% reduced Rarity of Items found in this Area"] = -1500;

            // Severe downsides (stored negative; the plugin subtracts the absolute value)
            dict["Take # Chaos Damage per second during any Flask Effect"] = -9000;
            dict["#% chance to be targeted by a Meteor when you use a Flask"] = -5000;
            dict["Curses you inflict are reflected back to you"] = -3000;
            dict["#% reduced Recovery Rate of Life, Mana and Energy Shield per Endurance Charge"] = -18;
            dict["-#% to Critical Strike Multiplier per Power Charge"] = -287;
        }

        private static Dictionary<string, int> BuildDefaultTiers()
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in PricedRewards) AddPriced(d, r, r.Chaos, DivineChaos);
            AddManual(d);
            return d;
        }

        // Explicit preset click: uses live prices when available and takes over those weights again.
        private void ApplyPriced(Func<(string One, string Many, double Chaos, RewardGroup Group), bool> filter)
        {
            double divine = CurrentDivine;
            foreach (var r in PricedRewards)
            {
                if (!filter(r)) continue;
                AddPriced(ModTiers, r, PriceOf(r.One, r.Chaos), divine);
                ManualWeights.Remove(BossKey(r.Many));
                ManualWeights.Remove(MinionKey(r.One));
                ManualWeights.Remove(BossKey(r.One));
            }
        }

        /// <summary>
        /// Called by the plugin when the Ninja Price cache changed. Re-prices every reward weight that is
        /// currently configured, unless the user set it by hand. Does nothing when auto-update is off.
        /// </summary>
        public void ApplyLivePrices(Dictionary<string, double> prices, string status)
        {
            _livePrices = new Dictionary<string, double>(prices, StringComparer.OrdinalIgnoreCase);
            _priceIndex = null; // rebuilt lazily with the new prices
            PriceStatus = status ?? "";
            if (!AutoUpdatePrices) return;
            bool hadPendingEdits = _dirty;

            double divine = CurrentDivine;
            bool Skip(string key) => ManualWeights.Contains(key) || !ModTiers.ContainsKey(key);
            foreach (var r in PricedRewards)
                AddPriced(ModTiers, r, PriceOf(r.One, r.Chaos), divine, Skip);

            // Duplication / quantity / rarity weights have no market price and stay manual.

            // A price refresh is not a user edit: persist it quietly unless the user has unsaved changes.
            if (!hadPendingEdits && _store != null) SaveActiveProfile();
        }

        // Presets only SET their own mods - they do NOT clear existing weights.
        private void ApplyPreset_DivineFocus()
        {
            ApplyPriced(r => r.Group is RewardGroup.Divine or RewardGroup.Eldritch);
            SetDangerousDownsides();
            ModTiers["Non-Damaging Ailments you inflict are reflected back to you"] = -2000;
        }

        private void ApplyPreset_ThreadFocus()
        {
            ApplyPriced(r => r.Group is RewardGroup.Divine or RewardGroup.Thread);
            ModTiers["Threads dropped by slain Enemies have #% chance to be Duplicated"] = 900;
            SetDangerousDownsides();
        }

        private void ApplyPreset_EldritchFocus()
        {
            ApplyPriced(r => r.Group is RewardGroup.Divine or RewardGroup.Eldritch);
            SetDangerousDownsides();
        }

        /// <summary>
        /// Armour-based character: only take a node when the reward clearly outweighs the loss.
        /// Defensive downsides get a modest negative weight (about 5-15 chaos of value each), so a
        /// cheap reward (Threads, basic currency) loses against a safe option, while a Divine or
        /// Eldritch Chaos reward still wins. Boss/minion buffs are not penalised here.
        /// Starting values - tune them while running altars.
        /// </summary>
        private void ApplyPreset_ArmourBuild()
        {
            ApplyPriced(_ => true);
            SetDangerousDownsides();

            // Resistances / physical damage reduction
            ModTiers["-#% to Cold Resistance"] = -150;
            ModTiers["-#% to Lightning Resistance"] = -150;
            ModTiers["-#% to Fire Resistance"] = -150;
            ModTiers["-#% to Chaos Resistance"] = -150;
            ModTiers["-#% additional Physical Damage Reduction"] = -250;

            // Armour / evasion / defences
            ModTiers["-# to Armour"] = -300;
            ModTiers["-# to Evasion Rating"] = -100;
            ModTiers["#% reduced Defences per Frenzy Charge"] = -150;
            ModTiers["#% reduced Recovery Rate of Life, Mana and Energy Shield per Endurance Charge"] = -100;
            ModTiers["-#% to Critical Strike Multiplier per Power Charge"] = -60;

            // Extra damage taken from nearby enemies
            ModTiers["Enemies within # metres Gain #% of their Physical Damage as Extra Cold Damage"] = -300;
            ModTiers["Enemies within # metres Gain #% of their Physical Damage as Extra Lightning Damage"] = -300;
            ModTiers["Enemies within # metres Gain #% of their Physical Damage as Extra Fire Damage"] = -300;
            ModTiers["Enemies within # metres Gain #% of their Physical Damage as Extra Chaos Damage"] = -300;

            // Flasks, ailments, ground effects
            ModTiers["#% increased Flask Charges used"] = -100;
            ModTiers["#% reduced Flask Effect Duration"] = -100;
            ModTiers["All Damage taken from Hits can Sap you"] = -60;
            ModTiers["#% chance to be Sapped when Hit"] = -60;
            ModTiers["All Damage taken from Hits can Scorch you"] = -60;
            ModTiers["#% chance to be Scorched when Hit"] = -60;
            ModTiers["Spell Hits have #% chance to Hinder you"] = -80;
            ModTiers["#% chance for Enemies to drop Chilled Ground when Hitting you, no more than once every # seconds"] = -60;
            ModTiers["#% chance for Enemies to drop Shocked Ground when Hitting you, no more than once every # seconds"] = -60;
            ModTiers["#% chance for Enemies to drop Burning Ground when Hitting you, no more than once every 2 seconds"] = -60;
        }

        /// <summary>
        /// [Test / Experimental] Tailored to the Bannerman Champion build:
        /// melee physical (Lacerate of Butchering / Cyclone), armour 18k, life 2.8k, physical max hit ~5k
        /// (the weakest point), elemental overcap 116-146% but chaos resistance only 80% (+1 overcap),
        /// Unwavering Stance (no evasion), permanent 3 endurance/frenzy/power charges, 5 flasks,
        /// Uul-Netol's Kiss (its Vulnerability curse reflected on you is weak and tanked fine),
        /// Maim/Intimidate on hit. Also sets the thresholds so weak altars are simply skipped.
        /// Rough scale: 1000 weight = ~46 chaos of boss-drop value.
        /// </summary>
        private void ApplyPreset_ChampionBuild()
        {
            ApplyPriced(_ => true);
            SetDangerousDownsides();

            void W(string id, int w) { ModTiers[id] = w; ManualWeights.Add(id); }
            void Zero(string id) { ModTiers.Remove(id); ManualWeights.Add(id); }

            // --- Lethal for this build (physical max hit is only ~5k) ---
            W("#% reduced Defences per Frenzy Charge", -9000);                     // 3 frenzy charges = 90-150% reduced defences
            W("-#% additional Physical Damage Reduction", -6000);
            W("Hits have #% chance to ignore Enemy Physical Damage Reduction", -6000); // minions ignore your armour
            W("Curses you inflict are reflected back to you", -200);               // Vulnerability from Uul-Netol's Kiss is weak; tanked fine
            W("Take # Chaos Damage per second during any Flask Effect", -500);     // 80% chaos res + 518 regen: mostly harmless

            // --- Painful: offence / sustain / uptime ---
            W("Enemies lose # Flask Charges every # seconds and cannot gain Flask Charges for # seconds after being Hit", -2000);
            W("#% chance to remove a random Charge from Enemy on Hit", -2000);     // removes YOUR charges
            W("Non-Damaging Ailments you inflict are reflected back to you", -1200); // Maim / Intimidate / Impale-type effects on you
            W("-#% to Critical Strike Multiplier per Power Charge", -700);         // 3 power charges = -60..-120%
            W("#% reduced Recovery Rate of Life, Mana and Energy Shield per Endurance Charge", -600);
            W("#% increased Flask Charges used", -400);
            W("#% reduced Flask Effect Duration", -400);
            W("#% chance to be targeted by a Meteor when you use a Flask", -600);
            W("-# to Armour", -1500);
            W("-#% to Chaos Resistance", -400);                                    // only +1% overcap
            W("#% chance for Enemies to drop Shocked Ground when Hitting you, no more than once every # seconds", -500);
            W("#% chance for Enemies to drop Burning Ground when Hitting you, no more than once every 2 seconds", -250);
            W("#% chance for Enemies to drop Chilled Ground when Hitting you, no more than once every # seconds", -60);
            W("All Damage taken from Hits can Sap you", -200);
            W("#% chance to be Sapped when Hit", -200);
            W("All Damage taken from Hits can Scorch you", -200);
            W("#% chance to be Scorched when Hit", -200);
            W("Spell Hits have #% chance to Hinder you", -60);
            foreach (var e in new[] { "Cold", "Lightning", "Fire" })
                W($"Enemies within # metres Gain #% of their Physical Damage as Extra {e} Damage", -700);
            W("Enemies within # metres Gain #% of their Physical Damage as Extra Chaos Damage", -500);

            // --- Irrelevant for this build ---
            Zero("-#% to Cold Resistance");                                        // 116-146% elemental overcap
            Zero("-#% to Lightning Resistance");
            Zero("-#% to Fire Resistance");
            Zero("-# to Evasion Rating");                                          // Unwavering Stance: no evasion
            Zero("Projectiles are fired in random directions");                    // melee build

            // --- Boss / minion tankiness: only mildly penalised (PoB: ~6.4M DPS, 100% hit chance) ---
            W("#% additional Physical Damage Reduction", -800);                    // shared by boss and minions
            W("+# to Armour", -600);
            W("#% increased Armour", -300);
            W("#% increased Evasion Rating", -100);
            W("#% Global chance to Blind Enemies on hit", -200);
            W("#% increased Blind Effect", -200);
            W("Gain #% of Maximum Life as Extra Maximum Energy Shield", -150);
            W("Damage Penetrates #% of Enemy Elemental Resistances", -500);
            W("Hits always Shock", -400);
            W("All Damage can Shock", -400);
            W("Skills fire # additional Projectiles", -500);
            W("#% increased Attack Speed", -400);
            W("#% increased Cast Speed", -200);
            W("#% increased Movement Speed", -300);
            W("Gain #% of Physical Damage as Extra Cold Damage", -400);
            W("Gain #% of Physical Damage as Extra Lightning Damage", -400);
            W("Gain #% of Physical Damage as Extra Fire Damage", -400);
            W("Gain #% of Physical Damage as Extra Chaos Damage", -400);
            W("Gain #% of Physical Damage as Extra Damage of a random Element", -400);
            W("#% chance to create Shocked Ground on Death, lasting # seconds", -300);
            W("Create Consecrated Ground on Hit, lasting # seconds", -300);
            W("#% increased Area of Effect", -200);

            // --- Thresholds: take an altar only when it is clearly worth it ---
            AltarSettings.MinPickWeight.Value = 1000;          // ~46c of expected value after downsides
            AltarSettings.NegativeVetoThreshold.Value = 3000;  // any single downside >= 3000 = red, never recommended
            AltarSettings.PositiveVetoThreshold.Value = 4000;  // only Divine Orb rewards override the vetoes
        }

        private void SetDangerousDownsides()
        {
            ModTiers["Take # Chaos Damage per second during any Flask Effect"] = -9000;
            ModTiers["Projectiles are fired in random directions"] = -9000;
            ModTiers["#% chance to be targeted by a Meteor when you use a Flask"] = -5000;
            ModTiers["Curses you inflict are reflected back to you"] = -3000;
        }

        private void SetMod(string partialName, int weight)
        {
            foreach (var (id, name, type) in AltarModsConstants.AltarTypes)
            {
                if (id.Contains(partialName, StringComparison.OrdinalIgnoreCase) ||
                    name.Contains(partialName, StringComparison.OrdinalIgnoreCase))
                    ModTiers[id] = weight;
            }
        }

        // ============================================================
        public int GetModTier(string mod) => ModTiers.GetValueOrDefault(mod ?? "", 0);
        public bool GetModAlert(string mod) => ModAlerts.GetValueOrDefault(mod ?? "", false);
    }

    // ============================================================
    [Submenu]
    [SupportedOSPlatform("windows")]
    public class AltarSettings
    {
        public RangeNode<int> FrameThickness { get; set; } = new RangeNode<int>(3, 1, 8);

        [Menu("Sound - Positive")]
        public TextNode PositiveSoundFile { get; set; } = new TextNode("alert.wav");

        [Menu("Sound - Negative")]
        public TextNode NegativeSoundFile { get; set; } = new TextNode("warning.wav");

        [Menu("Alert delay (ms)")]
        public RangeNode<int> DelayBetweenAlerts { get; set; } = new RangeNode<int>(3000, 1000, 10000);

        // Frame colors
        [Menu("Color: Pick this node")]
        public ColorNode PickColor { get; set; } = new ColorNode(SharpDX.Color.Yellow);

        [Menu("Color: Avoid")]
        public ColorNode BadColor { get; set; } = new ColorNode(SharpDX.Color.Red);

        [Menu("Color: Mixed (good + bad mods)")]
        public ColorNode MixedColor { get; set; } = new ColorNode(SharpDX.Color.Orange);

        [Menu("Color: Positive Veto (e.g. Divine Orb)")]
        public ColorNode PositiveVetoColor { get; set; } = new ColorNode(SharpDX.Color.Gold);

        // Dots
        [Menu("Dot size (node type)", "0 = disabled")]
        public RangeNode<int> DotSize { get; set; } = new RangeNode<int>(14, 0, 30);

        public ColorNode MinionDotColor { get; set; } = new ColorNode(SharpDX.Color.LightGreen);
        public ColorNode PlayerDotColor { get; set; } = new ColorNode(SharpDX.Color.LightCyan);
        public ColorNode BosssDotColor { get; set; } = new ColorNode(SharpDX.Color.LightBlue);

        // Veto thresholds
        [Menu("Negative Veto Threshold",
            "If any single BAD mod >= this value → always mark red.\n0 = disabled")]
        public RangeNode<int> NegativeVetoThreshold { get; set; } = new RangeNode<int>(0, 0, 10000);

        [Menu("Positive Veto Threshold",
            "If any single GOOD mod >= this value → always take (wins over negative veto).\n0 = disabled")]
        public RangeNode<int> PositiveVetoThreshold { get; set; } = new RangeNode<int>(0, 0, 10000);

        [Menu("Min. net weight to recommend an option",
            "An option is highlighted only when its net weight (rewards - downsides) reaches this value.\nBelow it the altar is simply not highlighted = skip it.\n0 = any positive net weight")]
        public RangeNode<int> MinPickWeight { get; set; } = new RangeNode<int>(0, 0, 10000);

        [Menu("Min. net weight for positive alert")]
        public RangeNode<int> AlertMinNetWeight { get; set; } = new RangeNode<int>(1, 1, 10000);

        [Menu("Scale boss drops by count",
            "\"Final Boss drops N additional X\": the weight of X is meant for 3 drops and is scaled to the real N on the altar\n" +
            "(1 drop = 1/3, 6 drops = 2x). Singular wording (\"1 additional Ultimatum Thread\") is recognised either way.")]
        public ToggleNode ScaleBossDropByCount { get; set; } = new ToggleNode(true);

        [Menu("Signed downside weights",
            "ON: in the downside section a NEGATIVE weight is a penalty and a POSITIVE weight is a small bonus\n" +
            "(counted only when the option also has a reward). Profiles from the wizard use this.\n" +
            "OFF (old behaviour): any non-zero weight in the downside section is a penalty.")]
        public ToggleNode SignedDownsideWeights { get; set; } = new ToggleNode(false);

        [Menu("Mode (1=Any | 2=Minions+Player | 3=Boss+Player)")]
        public RangeNode<int> SwitchMode { get; set; } = new RangeNode<int>(1, 1, 3);

        [Menu("Minion bonus weight")]
        public RangeNode<int> MinionWeight { get; set; } = new RangeNode<int>(0, 0, 100);

        [Menu("Boss bonus weight")]
        public RangeNode<int> BossWeight { get; set; } = new RangeNode<int>(0, 0, 100);

        public HotkeyNodeV2 HotkeyMode { get; set; } = new HotkeyNodeV2(Keys.F7);
    }

    // ============================================================
    [Submenu]
    [SupportedOSPlatform("windows")]
    public class DisplaySettings
    {
        [Menu("Show score overlay",
            "Displays +/- net weight numbers next to each altar option")]
        public ToggleNode ShowScoreOverlay { get; set; } = new ToggleNode(true);

        [Menu("Show top mod name",
            "Displays the name of the most important mod next to each option (why it's good/bad)")]
        public ToggleNode ShowTopModName { get; set; } = new ToggleNode(true);

        [Menu("Show 'TAKE' arrow",
            "Displays an arrow pointing to the option to take")]
        public ToggleNode ShowPickArrow { get; set; } = new ToggleNode(true);

        [Menu("Show background highlight",
            "Subtle background fill on the option to take")]
        public ToggleNode ShowBackgroundHighlight { get; set; } = new ToggleNode(true);

        [Menu("Background highlight opacity", "0=transparent 255=fully opaque")]
        public RangeNode<int> BackgroundAlpha { get; set; } = new RangeNode<int>(35, 0, 120);

        [Menu("[Test] Near editor window",
            "Shows a window with sliders for exactly the mods of the altar you are standing at.\nEdits go to the active profile and auto-save.")]
        public ToggleNode NearEditor { get; set; } = new ToggleNode(false);

        [Menu("Near editor hotkey")]
        public HotkeyNodeV2 NearEditorHotkey { get; set; } = new HotkeyNodeV2(Keys.NumPad7);

        [Menu("Near editor range", "Only show the window when the altar is this close (grid units)")]
        public RangeNode<int> NearEditorRange { get; set; } = new RangeNode<int>(60, 10, 300);
    }

    // ============================================================
    [Submenu]
    [SupportedOSPlatform("windows")]
    public class DebugSettings
    {
        public ToggleNode DebugRawText { get; set; } = new ToggleNode(false);
        public ToggleNode DebugBuffs { get; set; } = new ToggleNode(false);
        public ToggleNode DebugDebuffs { get; set; } = new ToggleNode(false);

        [Menu("Debug Weight", "Shows detailed net weight breakdown for each altar")]
        public ToggleNode DebugWeight { get; set; } = new ToggleNode(false);
    }
}

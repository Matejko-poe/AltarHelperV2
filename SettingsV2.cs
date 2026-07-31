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
    public class Settings : ISettings
    {
        public ToggleNode Enable { get; set; } = new ToggleNode(false);
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

        [JsonIgnore]
        public CustomNode UnknownModsDisplay { get; }

        // Plain fields — ExileCore reflection doesn't warn on fields, only properties
        public Dictionary<string, int> ModTiers = new();
        public Dictionary<string, bool> ModAlerts = new();

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
                    if (ImGui.Button("Scarab Focus"))
                        ApplyPreset_ScarabFocus();
                    ImGui.SameLine();
                    if (ImGui.Button("Eldritch Currency"))
                        ApplyPreset_EldritchFocus();
                    ImGui.SameLine();
                    if (ImGui.Button("Reset All Weights"))
                        ModTiers.Clear();

                    ImGui.Separator();
                    ImGui.InputTextWithHint("##V2UnitFilter", "Search mods...", ref unitFilter, 200);

                    // Configured mod counter
                    int configuredCount = ModTiers.Count(kvp => kvp.Value != 0);
                    ImGui.SameLine();
                    ImGui.TextColored(new SN.Vector4(0.7f, 0.9f, 0.7f, 1f), $"  ({configuredCount} configured)");

                    float tableHeight = 420f;
                    if (!ImGui.BeginTable("V2UnitConfig", 5,
                        ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Borders |
                        ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg,
                        new SN.Vector2(0, tableHeight)))
                        return;

                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableSetupColumn("Weight", ImGuiTableColumnFlags.WidthFixed, 220);
                    ImGui.TableSetupColumn("Mod", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 60);
                    ImGui.TableSetupColumn("Alert", ImGuiTableColumnFlags.WidthFixed, 45);
                    ImGui.TableSetupColumn("Reset", ImGuiTableColumnFlags.WidthFixed, 40);
                    ImGui.TableHeadersRow();

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
                            ModTiers[id] = currentValue;

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
                            ModTiers.Remove(id);

                        ImGui.PopID();
                    }
                    ImGui.EndTable();
                    ImGui.TreePop();
                }
            };

            UnknownModsDisplay = new CustomNode
            {
                DrawDelegate = () => DrawUnknownModsDelegate?.Invoke()
            };
#pragma warning restore CA1416
        }

        // ============================================================
        // Presets — weights based on poe.ninja prices (Mirage, 2026-03-15)
        // Scale: Divine Orb = 242c → weight 10000 (cap)
        //        => 1 chaos ≈ 41 weight units
        //
        // Boss drops avg 3 items:
        //   Divine Orb:            242c × 3 = 726c → 10000 (capped)
        //   Eldritch Chaos Orb:   19.4c × 3 = 58c  → 2400
        //   Eldritch Annulment:   15.0c × 3 = 45c  → 1850
        //   Eldritch Exalted:     10.0c × 3 = 30c  → 1235
        //   Grand Ichor:           3.0c × 3 =  9c  →  370
        //   Grand Ember:           4.0c × 3 = 12c  →  495
        //   Greater Ichor/Ember:   1.0c × 3 =  3c  →  125
        //   Orb of Unmaking:       0.4c × 3 =  1c  →   50
        //
        // % chance drops ≈ 40% of boss drop value (per-kill accumulated)
        //
        // Presets only SET their own mods — they do NOT clear existing weights.
        // ============================================================

        private void ApplyPreset_DivineFocus()
        {
            // === Divine Orb (242c) ===
            SetMod("Final Boss drops # additional Divine Orbs", 10000);
            SetMod("#% chance to drop an additional Divine Orb", 4000);

            // === Eldritch Currency (updated prices) ===
            // Eldritch Chaos Orb: 19.4c
            SetMod("Final Boss drops # additional Eldritch Chaos Orbs", 2400);
            SetMod("#% chance to drop an additional Eldritch Chaos Orb", 960);
            // Eldritch Orb of Annulment: 15c
            SetMod("Final Boss drops # additional Eldritch Orbs of Annulment", 1850);
            SetMod("#% chance to drop an additional Eldritch Orb of Annulment", 740);
            // Eldritch Exalted Orb: ~10c
            SetMod("Final Boss drops # additional Eldritch Exalted Orbs", 1235);
            SetMod("#% chance to drop an additional Eldritch Exalted Orb", 490);
            // Grand Ichor: 3c
            SetMod("Final Boss drops # additional Grand Eldritch Ichors", 370);
            SetMod("#% chance to drop an additional Grand Eldritch Ichor", 148);
            // Grand Ember: 4c
            SetMod("Final Boss drops # additional Grand Eldritch Embers", 495);
            SetMod("#% chance to drop an additional Grand Eldritch Ember", 198);
            // Greater Ichor/Ember: 1c each
            SetMod("Final Boss drops # additional Greater Eldritch Ichors", 125);
            SetMod("Final Boss drops # additional Greater Eldritch Embers", 125);
            SetMod("#% chance to drop an additional Greater Eldritch Ichor", 50);
            SetMod("#% chance to drop an additional Greater Eldritch Ember", 50);

            // === Dangerous downsides ===
            SetMod("Take # Chaos Damage per second during any Flask Effect", -9000);
            SetMod("Projectiles are fired in random directions", -9000);
            SetMod("#% chance to be targeted by a Meteor when you use a Flask", -5000);
            SetMod("Curses you inflict are reflected back to you", -3000);
            SetMod("Non-Damaging Ailments you inflict are reflected back to you", -2000);
        }

        private void ApplyPreset_ScarabFocus()
        {
            // === Divine Orb — always worth taking ===
            SetMod("Final Boss drops # additional Divine Orbs", 10000);
            SetMod("#% chance to drop an additional Divine Orb", 4000);

            // === Scarabs (prices ~5-11c each, boss drops 2-4) ===
            // Ultimatum: ~10c
            SetMod("Final Boss drops # additional Ultimatum Scarabs", 1230);
            SetMod("#% chance to drop an additional Ultimatum Scarab", 490);
            // Breach: ~5c
            SetMod("Final Boss drops # additional Breach Scarabs", 615);
            SetMod("#% chance to drop an additional Breach Scarab", 246);
            // Delirium: ~5c
            SetMod("Final Boss drops # additional Delirium Scarabs", 615);
            SetMod("#% chance to drop an additional Delirium Scarab", 246);
            // Legion: ~4c
            SetMod("Final Boss drops # additional Legion Scarabs", 490);
            SetMod("#% chance to drop an additional Legion Scarab", 196);
            // Essence: ~5c
            SetMod("Final Boss drops # additional Essence Scarabs", 615);
            SetMod("#% chance to drop an additional Essence Scarab", 246);
            // Incursion: ~5c
            SetMod("Final Boss drops # additional Incursion Scarabs", 615);
            SetMod("#% chance to drop an additional Incursion Scarab", 246);
            // Beyond: ~3c
            SetMod("Final Boss drops # additional Beyond Scarabs", 370);
            SetMod("#% chance to drop an additional Beyond Scarab", 148);
            // Harvest/Ritual/Ambush: ~5c
            SetMod("Final Boss drops # additional Harvest Scarabs", 615);
            SetMod("Final Boss drops # additional Ritual Scarabs", 615);
            SetMod("Final Boss drops # additional Ambush Scarabs", 615);
            // Scarab duplication (Player type) — bonus
            SetMod("Scarabs dropped by slain Enemies have #% chance to be Duplicated", 900);

            // === Dangerous downsides ===
            SetMod("Take # Chaos Damage per second during any Flask Effect", -9000);
            SetMod("Projectiles are fired in random directions", -9000);
            SetMod("#% chance to be targeted by a Meteor when you use a Flask", -5000);
        }

        private void ApplyPreset_EldritchFocus()
        {
            // === Divine + Eldritch as priority ===
            SetMod("Final Boss drops # additional Divine Orbs", 10000);
            SetMod("#% chance to drop an additional Divine Orb", 4000);
            // Eldritch Chaos Orb: 19.4c
            SetMod("Final Boss drops # additional Eldritch Chaos Orbs", 2400);
            SetMod("#% chance to drop an additional Eldritch Chaos Orb", 960);
            // Eldritch Annulment: 15c
            SetMod("Final Boss drops # additional Eldritch Orbs of Annulment", 1850);
            SetMod("#% chance to drop an additional Eldritch Orb of Annulment", 740);
            // Eldritch Exalted: ~10c
            SetMod("Final Boss drops # additional Eldritch Exalted Orbs", 1235);
            SetMod("#% chance to drop an additional Eldritch Exalted Orb", 490);
            // Grand Ichor: 3c
            SetMod("Final Boss drops # additional Grand Eldritch Ichors", 370);
            SetMod("#% chance to drop an additional Grand Eldritch Ichor", 148);
            // Grand Ember: 4c
            SetMod("Final Boss drops # additional Grand Eldritch Embers", 495);
            SetMod("#% chance to drop an additional Grand Eldritch Ember", 198);
            // Greater Ichor/Ember: 1c each
            SetMod("Final Boss drops # additional Greater Eldritch Ichors", 125);
            SetMod("Final Boss drops # additional Greater Eldritch Embers", 125);
            SetMod("#% chance to drop an additional Greater Eldritch Ichor", 50);
            SetMod("#% chance to drop an additional Greater Eldritch Ember", 50);
            // Orb of Unmaking: 0.4c
            SetMod("Final Boss drops # additional Orbs of Unmaking", 50);
            SetMod("#% chance to drop an additional Orb of Unmaking", 20);

            // === Dangerous downsides ===
            SetMod("Take # Chaos Damage per second during any Flask Effect", -9000);
            SetMod("Projectiles are fired in random directions", -9000);
            SetMod("#% chance to be targeted by a Meteor when you use a Flask", -5000);
            SetMod("Curses you inflict are reflected back to you", -3000);
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
        public TextNode NegativeSoundFile { get; set; } = new TextNode("alert.wav");

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

        [Menu("Min. net weight for positive alert")]
        public RangeNode<int> AlertMinNetWeight { get; set; } = new RangeNode<int>(1, 1, 10000);

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

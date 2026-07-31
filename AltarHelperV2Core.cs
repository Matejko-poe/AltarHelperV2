using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared.Cache;
using ImGuiNET;
using SharpDX;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using NumericsVector2 = System.Numerics.Vector2;

namespace AltarHelperV2
{
    [SupportedOSPlatform("windows")]
    public class AltarHelperV2Core : BaseSettingsPlugin<Settings>
    {
        // Frames: (rect, color, thickness)
        private List<(RectangleF rect, Color color, int thick)> _frames = new();
        // Filled boxes: (rect, color) — option backgrounds and dots
        private List<(RectangleF rect, Color color)> _boxes = new();
        // On-screen text: (text, pos, color)
        private List<(string text, NumericsVector2 pos, Color color)> _texts = new();

        private readonly object _positiveSoundLocker = new();
        private readonly object _negativeSoundLocker = new();
        private DateTime _lastPlayedPositive = DateTime.MinValue;
        private DateTime _lastPlayedNegative = DateTime.MinValue;

        private FrameCache<List<LabelOnGround>> LabelCache { get; set; }

        // Unknown mods (key=normalized, value=raw) — shown in settings UI
        public ConcurrentDictionary<string, string> UnknownMods { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public override bool Initialise()
        {
            Name = "AltarHelperV2";
            LabelCache = new FrameCache<List<LabelOnGround>>(UpdateAltarLabelList);
            Settings.DrawUnknownModsDelegate = DrawUnknownModsSection;
            return true;
        }

        // ============================================================
        // ImGui: unknown mods section in settings
        // ============================================================
        private void DrawUnknownModsSection()
        {
#pragma warning disable CA1416
            if (!ImGui.TreeNode($"Unknown Mods ({UnknownMods.Count}) — new, not in database"))
                return;

            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f),
                "These mods appeared on an altar but are not in AltarModsConstants.cs");
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f),
                "Add them to the file and set weights, or ignore.");
            ImGui.Separator();

            if (ImGui.Button("Clear list")) UnknownMods.Clear();
            ImGui.Separator();

            foreach (var kvp in UnknownMods.OrderBy(x => x.Key))
            {
                ImGui.TextUnformatted(kvp.Value);
                ImGui.SameLine();
                ImGui.PushID($"find_{kvp.Key}");
                if (ImGui.SmallButton("Find"))
                {
                    // Strip leading numeric/% prefix so the keyword matches the Name column in the main table
                    var keyword = System.Text.RegularExpressions.Regex
                        .Replace(kvp.Key, @"^[-+]?#%\s*(to\s+)?", "").Trim();
                    if (string.IsNullOrWhiteSpace(keyword)) keyword = kvp.Key;
                    Settings.SetModFilter?.Invoke(keyword);
                }
                ImGui.PopID();
            }
            ImGui.TreePop();
#pragma warning restore CA1416
        }

        // ============================================================
        // ExileCore lifecycle hooks
        // ============================================================
        private List<LabelOnGround> UpdateAltarLabelList()
        {
            var labels = GameController.IngameState.IngameUi.ItemsOnGroundLabelsVisible;
            if (labels == null || labels.Count == 0) return new List<LabelOnGround>();
            return labels
                .Where(x =>
                    x.ItemOnGround.Metadata == "Metadata/MiscellaneousObjects/PrimordialBosses/TangleAltar" ||
                    x.ItemOnGround.Metadata == "Metadata/MiscellaneousObjects/PrimordialBosses/CleansingFireAltar")
                .ToList();
        }

        public override void Render()
        {
            // Backgrounds (under frames)
            foreach (var (rect, color) in _boxes)
                Graphics.DrawBox(rect, color);

            // Frames
            foreach (var (rect, color, thick) in _frames)
                Graphics.DrawFrame(rect, color, thick);

            // Texts
            foreach (var (text, pos, color) in _texts)
                Graphics.DrawText(text, pos, color);
        }

        public override Job Tick()
        {
            _frames.Clear();
            _boxes.Clear();
            _texts.Clear();

            // Mode switch hotkey
            if (Settings.AltarSettings.HotkeyMode.PressedOnce() == true)
            {
                Settings.AltarSettings.SwitchMode.Value += 1;
                if (Settings.AltarSettings.SwitchMode.Value == 4)
                    Settings.AltarSettings.SwitchMode.Value = 1;
                DebugWindow.LogMsg($"[AltarHelperV2] Mode: {ModeName(Settings.AltarSettings.SwitchMode.Value)}");
            }

            if (!CanRun()) return null;
            CompareWeights();
            return null;
        }

        // ============================================================
        // Main logic
        // ============================================================
        private void CompareWeights()
        {
            var altars = LabelCache.Value;
            if (altars == null || altars.Count == 0) return;

            int posVetoThresh = Settings.AltarSettings.PositiveVetoThreshold.Value;
            int negVetoThresh = Settings.AltarSettings.NegativeVetoThreshold.Value;
            int alertMinWeight = Settings.AltarSettings.AlertMinNetWeight.Value;
            int dotSize = Settings.AltarSettings.DotSize.Value;
            int frameThick = Settings.AltarSettings.FrameThickness.Value;

            foreach (var altarLabel in altars)
            {
                var topLabel = altarLabel.Label.GetChildAtIndex(0);
                var bottomLabel = altarLabel.Label.GetChildAtIndex(1);
                string topText = topLabel?.GetChildAtIndex(1)?.GetText(512);
                string bottomText = bottomLabel?.GetChildAtIndex(1)?.GetText(512);

                if (Settings.DebugSettings.DebugRawText)
                {
                    DebugWindow.LogError($"[V2 Raw Top]: {topText}");
                    DebugWindow.LogError($"[V2 Raw Bottom]: {bottomText}");
                }
                if (topText == null || bottomText == null) continue;

                var altar = new Altar(GetSelectionData(topText), GetSelectionData(bottomText));

                if (altar.Top.UpsideWeight == 0 && altar.Bottom.UpsideWeight == 0 &&
                    altar.Top.DownsideWeight == 0 && altar.Bottom.DownsideWeight == 0)
                    continue;

                int topModeWeight = GetModeWeight(altar.Top);
                int bottomModeWeight = GetModeWeight(altar.Bottom);

                // Veto checks
                bool topPosVeto = posVetoThresh > 0 && altar.Top.MaxSingleUpside >= posVetoThresh;
                bool bottomPosVeto = posVetoThresh > 0 && altar.Bottom.MaxSingleUpside >= posVetoThresh;
                bool topNegVeto = negVetoThresh > 0 && altar.Top.MaxSingleDownside >= negVetoThresh;
                bool bottomNegVeto = negVetoThresh > 0 && altar.Bottom.MaxSingleDownside >= negVetoThresh;

                // Positive veto overrides negative veto
                bool topEffBad = topNegVeto && !topPosVeto;
                bool bottomEffBad = bottomNegVeto && !bottomPosVeto;

                // Effective weight for comparison (positive veto = max weight)
                int topEffWeight = topPosVeto ? Math.Max(topModeWeight, posVetoThresh) : topModeWeight;
                int bottomEffWeight = bottomPosVeto ? Math.Max(bottomModeWeight, posVetoThresh) : bottomModeWeight;

                // Sounds
                bool positiveAlert = (altar.Top.PlayPositiveAlert && !topEffBad && topEffWeight >= alertMinWeight)
                                  || (altar.Bottom.PlayPositiveAlert && !bottomEffBad && bottomEffWeight >= alertMinWeight);
                bool negativeAlert = altar.Top.PlayNegativeAlert || altar.Bottom.PlayNegativeAlert
                                  || topEffBad || bottomEffBad
                                  || topModeWeight < 0 || bottomModeWeight < 0;
                // Avoid overlapping positive and negative sounds for mixed altar choices.
                // A danger warning is more actionable and therefore takes precedence.
                if (negativeAlert) PlayNegativeSound();
                else if (positiveAlert) PlayPositiveSound();

                // Debug text
                if (Settings.DebugSettings.DebugWeight)
                {
                    var tr = topLabel.GetClientRectCache;
                    var br = bottomLabel.GetClientRectCache;
                    _texts.Add(($"Net:{topModeWeight} [+{altar.Top.UpsideWeight}/-{altar.Top.DownsideWeight}]{(topPosVeto ? " +VETO" : "")}{(topEffBad ? " -VETO" : "")}",
                        new NumericsVector2(tr.Center.X - 100, tr.Top - 28), Color.Cyan));
                    _texts.Add(($"Net:{bottomModeWeight} [+{altar.Bottom.UpsideWeight}/-{altar.Bottom.DownsideWeight}]{(bottomPosVeto ? " +VETO" : "")}{(bottomEffBad ? " -VETO" : "")}",
                        new NumericsVector2(br.Center.X - 100, br.Bottom + 12), Color.Cyan));
                }

                // Determine best option
                bool topCanBeGood = !topEffBad && topEffWeight > 0;
                bool bottomCanBeGood = !bottomEffBad && bottomEffWeight > 0;
                bool pickTop = topCanBeGood && (!bottomCanBeGood || topEffWeight >= bottomEffWeight);
                bool pickBottom = bottomCanBeGood && (!topCanBeGood || bottomEffWeight > topEffWeight);
                bool tie = topCanBeGood && bottomCanBeGood && topEffWeight == bottomEffWeight;

                // === Option backgrounds (under frames) ===
                if (Settings.DisplaySettings.ShowBackgroundHighlight)
                {
                    int alpha = Settings.DisplaySettings.BackgroundAlpha.Value;
                    if ((pickTop || tie) && topCanBeGood)
                    {
                        _boxes.Add((topLabel.GetClientRectCache, WithAlpha(GetPickColor(altar.Top, topPosVeto), alpha)));
                    }
                    else if (topEffBad || topModeWeight < 0)
                    {
                        // Fill bad node red so it's obvious what not to take
                        _boxes.Add((topLabel.GetClientRectCache, WithAlpha(Settings.AltarSettings.BadColor, alpha)));
                    }

                    if ((pickBottom || tie) && bottomCanBeGood)
                    {
                        _boxes.Add((bottomLabel.GetClientRectCache, WithAlpha(GetPickColor(altar.Bottom, bottomPosVeto), alpha)));
                    }
                    else if (bottomEffBad || bottomModeWeight < 0)
                    {
                        _boxes.Add((bottomLabel.GetClientRectCache, WithAlpha(Settings.AltarSettings.BadColor, alpha)));
                    }
                }

                // === Frames ===
                // Bad options
                if (topEffBad || topModeWeight < 0)
                    _frames.Add((topLabel.GetClientRectCache, Settings.AltarSettings.BadColor, frameThick));
                if (bottomEffBad || bottomModeWeight < 0)
                    _frames.Add((bottomLabel.GetClientRectCache, Settings.AltarSettings.BadColor, frameThick));

                // Good options
                int bonusThick = 2; // thicker frame for positive veto
                if (pickTop || tie)
                    _frames.Add((topLabel.GetClientRectCache, GetPickColor(altar.Top, topPosVeto), frameThick + (topPosVeto ? bonusThick : 0)));
                if (pickBottom || tie)
                    _frames.Add((bottomLabel.GetClientRectCache, GetPickColor(altar.Bottom, bottomPosVeto), frameThick + (bottomPosVeto ? bonusThick : 0)));

                // === Dots (node type) ===
                if (dotSize > 0)
                {
                    DrawTargetDot(topLabel.GetClientRectCache, altar.Top.Target, dotSize);
                    DrawTargetDot(bottomLabel.GetClientRectCache, altar.Bottom.Target, dotSize);
                }

                // === Information overlay ===
                DrawOptionOverlay(topLabel, altar.Top, topModeWeight, topEffBad, topPosVeto, pickTop || tie);
                DrawOptionOverlay(bottomLabel, altar.Bottom, bottomModeWeight, bottomEffBad, bottomPosVeto, pickBottom || tie);
            }
        }

        // ============================================================
        // Information overlay next to each option
        // ============================================================
        private void DrawOptionOverlay(Element label, Selection sel, int modeWeight,
            bool isEffBad, bool isPosVeto, bool isBestPick)
        {
            var rect = label.GetClientRectCache;
            // Position: to the right of the option, vertically centered
            float x = rect.Right + 10;
            float y = rect.Center.Y - 22;

            // Score (+/- net weight)
            if (Settings.DisplaySettings.ShowScoreOverlay)
            {
                Color scoreColor;
                string scorePrefix;
                if (isPosVeto)           { scoreColor = Color.Gold;        scorePrefix = "★ "; }
                else if (isEffBad)       { scoreColor = Color.Red;         scorePrefix = "✗ "; }
                else if (modeWeight > 0) { scoreColor = Color.Yellow;      scorePrefix = "+ "; }
                else if (modeWeight < 0) { scoreColor = Color.OrangeRed;   scorePrefix = "- "; }
                else                     { scoreColor = Color.Gray;        scorePrefix = "  "; }

                string scoreText = $"{scorePrefix}{Math.Abs(modeWeight)}";
                _texts.Add((scoreText, new NumericsVector2(x, y), scoreColor));
                y += 18;
            }

            // "TAKE" arrow
            if (Settings.DisplaySettings.ShowPickArrow && isBestPick && !isEffBad)
            {
                Color arrowColor = isPosVeto ? Color.Gold : Color.Yellow;
                _texts.Add(("◄ TAKE", new NumericsVector2(x, y), arrowColor));
                y += 18;
            }

            // Top mod name
            if (Settings.DisplaySettings.ShowTopModName)
            {
                string topMod = GetTopModLabel(sel);
                if (!string.IsNullOrEmpty(topMod))
                {
                    bool isGoodMod = sel.UpsideWeight >= sel.DownsideWeight;
                    Color modColor = isGoodMod ? Color.LightGreen : Color.LightCoral;
                    _texts.Add((TruncateMod(topMod, 32), new NumericsVector2(x, y), modColor));
                }
            }
        }

        /// <summary>
        /// Returns a short description of the most important mod in the option (highest upside or worst downside).
        /// </summary>
        private string GetTopModLabel(Selection sel)
        {
            // Find mod with highest weight among upsides or downsides
            string best = null;
            int bestWeight = 0;

            foreach (var modText in sel.Upsides.Concat(sel.Downsides))
            {
                var norm = Regex.Replace(modText, @"((\d+)(?:.\d)|\d+)", "#");
                int w = Math.Abs(Settings.GetModTier(norm));
                if (w > bestWeight)
                {
                    bestWeight = w;
                    best = modText;
                }
            }
            return best;
        }

        private string TruncateMod(string s, int maxLen)
            => s.Length <= maxLen ? s : s[..maxLen] + "…";

        private static string ModeName(int mode) => mode switch
        {
            2 => "Minions + Player",
            3 => "Boss + Player",
            _ => "Any"
        };

        // ============================================================
        // Drawing helpers
        // ============================================================
        private Color GetPickColor(Selection sel, bool posVeto)
        {
            if (posVeto) return Settings.AltarSettings.PositiveVetoColor;
            if (sel.HasMixedMods) return Settings.AltarSettings.MixedColor;
            return Settings.AltarSettings.PickColor;
        }

        private static Color WithAlpha(Color c, int alpha)
            => new Color(c.R, c.G, c.B, (byte)Math.Clamp(alpha, 0, 255));

        private void DrawTargetDot(RectangleF nodeRect, AffectedTarget target, int size)
        {
            var dotColor = target switch
            {
                AffectedTarget.Minions  => (Color)Settings.AltarSettings.MinionDotColor,
                AffectedTarget.FinalBoss => (Color)Settings.AltarSettings.BosssDotColor,
                AffectedTarget.Player   => (Color)Settings.AltarSettings.PlayerDotColor,
                _ => Color.Transparent
            };
            if (dotColor == Color.Transparent) return;

            // Top-right corner of the node
            float x = nodeRect.Right - size - 3;
            float y = nodeRect.Top + 3;
            _boxes.Add((new RectangleF(x, y, size, size), dotColor));
        }

        // ============================================================
        // Weight and mode
        // ============================================================
        private int GetModeWeight(Selection s)
        {
            int mode = Settings.AltarSettings.SwitchMode.Value;
            bool targetMatches = mode switch
            {
                2 => s.Target == AffectedTarget.Minions || s.Target == AffectedTarget.Player,
                3 => s.Target == AffectedTarget.FinalBoss || s.Target == AffectedTarget.Player,
                _ => true
            };

            int weight = targetMatches ? s.NetWeight : 0;
            if (s.Target == AffectedTarget.Minions) weight += Settings.AltarSettings.MinionWeight.Value;
            if (s.Target == AffectedTarget.FinalBoss) weight += Settings.AltarSettings.BossWeight.Value;
            return weight;
        }

        // ============================================================
        public bool CanRun()
        {
            if (GameController.Area.CurrentArea.IsHideout ||
                GameController.Area.CurrentArea.IsTown ||
                GameController.IngameState.IngameUi == null ||
                GameController.IngameState.IngameUi.ItemsOnGroundLabelsVisible == null)
                return false;
            return true;
        }

        private void PlayPositiveSound()
        {
            lock (_positiveSoundLocker)
            {
                double ms = (DateTime.Now - _lastPlayedPositive).TotalMilliseconds;
                if (ms > Settings.AltarSettings.DelayBetweenAlerts && ms > 500)
                {
                    GameController.SoundController.PlaySound(
                        Path.Combine(@"..\Sounds\", Settings.AltarSettings.PositiveSoundFile.Value.Replace(".wav", ""))
                            .Replace('\\', '/'));
                    _lastPlayedPositive = DateTime.Now;
                }
            }
        }

        private void PlayNegativeSound()
        {
            lock (_negativeSoundLocker)
            {
                double ms = (DateTime.Now - _lastPlayedNegative).TotalMilliseconds;
                if (ms > Settings.AltarSettings.DelayBetweenAlerts && ms > 500)
                {
                    GameController.SoundController.PlaySound(
                        Path.Combine(@"..\Sounds\", Settings.AltarSettings.NegativeSoundFile.Value.Replace(".wav", ""))
                            .Replace('\\', '/'));
                    _lastPlayedNegative = DateTime.Now;
                }
            }
        }

        // ============================================================
        // Altar text parsing
        // ============================================================
        public Selection GetSelectionData(string altarLabelText)
        {
            AffectedTarget target;
            var downsides = new List<string>();
            var upsides = new List<string>();

            if (string.IsNullOrWhiteSpace(altarLabelText))
                return new Selection();

            using (var reader = new StringReader(altarLabelText))
            {
                string targetLine = reader.ReadLine();
                const string targetPrefix = "<valuedefault>{";
                string targetKey = targetLine != null &&
                                   targetLine.StartsWith(targetPrefix, StringComparison.Ordinal) &&
                                   targetLine.EndsWith("}", StringComparison.Ordinal) &&
                                   targetLine.Length > targetPrefix.Length
                    ? targetLine[targetPrefix.Length..^1]
                    : string.Empty;
                target = AltarModsConstants.AltarTargetDict.GetValueOrDefault(targetKey, AffectedTarget.Any);

                string line;
                bool upsideSection = false;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("<enchanted>")) upsideSection = true;
                    if (upsideSection)
                    {
                        line = line.StartsWith("<enchanted>")
                            ? line.Replace("<enchanted>{", "") : line.Replace("}", "");
                        if (line.Contains('}')) line = line.Replace("}", "");
                        if (line.StartsWith("<rgb")) line = line[(line.IndexOf('{') + 1)..^1];
                        if (!string.IsNullOrWhiteSpace(line)) upsides.Add(line);
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(line)) downsides.Add(line);
                }
            }

            var upsideEntries = new List<FilterEntry>();
            var downsideEntries = new List<FilterEntry>();

            foreach (string entry in upsides)
            {
                var norm = NormalizeNumbers(entry);
                if (Settings.DebugSettings.DebugBuffs) DebugWindow.LogMsg($"[V2 Upside] {norm}");
                var fe = GetEntry(norm, isUpside: true);
                upsideEntries.Add(fe);
            }
            foreach (string entry in downsides)
            {
                var norm = NormalizeNumbers(entry);
                if (Settings.DebugSettings.DebugDebuffs) DebugWindow.LogMsg($"[V2 Downside] {norm}");
                var fe = GetEntry(norm, isUpside: false);
                downsideEntries.Add(fe);
            }

            int upsideWeight   = upsideEntries.Where(e => e.Weight > 0).Sum(e => e.Weight);
            int downsideWeight = downsideEntries.Where(e => e.Weight != 0).Sum(e => Math.Abs(e.Weight));
            int maxSingleUp    = upsideEntries.Count > 0 ? upsideEntries.Max(e => Math.Max(e.Weight, 0)) : 0;
            int maxSingleDown  = downsideEntries.Count > 0 ? downsideEntries.Max(e => Math.Abs(e.Weight)) : 0;

            return new Selection
            {
                Upsides         = upsides,
                Downsides       = downsides,
                Target          = target,
                UpsideWeight    = upsideWeight,
                DownsideWeight  = downsideWeight,
                MaxSingleUpside = maxSingleUp,
                MaxSingleDownside = maxSingleDown,
                PlayPositiveAlert = upsideEntries.Any(e => e.Alert == true && e.Weight > 0),
                PlayNegativeAlert = downsideEntries.Any(e => e.Alert == true),
            };
        }

        public FilterEntry GetEntry(string mod, bool isUpside)
        {
            int modWeight  = Settings.GetModTier(mod);
            bool modAlert  = Settings.GetModAlert(mod);

            var modNorm = mod.Contains('(') && mod.Contains(')')
                ? Regex.Replace(mod, @"\([^()]*\)", "#")
                : NormalizeNumbers(mod);

            var altarEntry = AltarModsConstants.AltarTypes
                .FirstOrDefault(t => t.Id.Contains(mod, StringComparison.InvariantCultureIgnoreCase));
            string modType = altarEntry.Type;

            if (modType == null && !string.IsNullOrWhiteSpace(mod) && mod.Length > 3 && modWeight == 0)
                UnknownMods.TryAdd(modNorm, mod);

            return new FilterEntry
            {
                Mod    = modNorm,
                Weight = modWeight,
                IsUpside = isUpside,
                Target = modType != null && AltarModsConstants.FilterTargetDict.TryGetValue(modType, out var t)
                    ? t : AffectedTarget.Any,
                Alert  = modAlert
            };
        }

        private static string NormalizeNumbers(string value) =>
            Regex.Replace(value ?? string.Empty, @"[-+]?\d+(?:[.,]\d+)?", "#");

        // ============================================================
        // Data models
        // ============================================================
        public class FilterEntry
        {
            public string Mod { get; set; }
            public int Weight { get; set; }
            public AffectedTarget Target { get; set; }
            public bool IsUpside { get; set; }
            public bool? Alert { get; set; } = false;
        }

        public class Altar
        {
            public Selection Top { get; set; }
            public Selection Bottom { get; set; }
            public Altar(Selection top, Selection bottom) { Top = top; Bottom = bottom; }
        }

        public class Selection
        {
            public AffectedTarget Target { get; set; }
            public List<string> Downsides { get; set; } = new();
            public List<string> Upsides { get; set; } = new();

            /// <summary>Sum of good mod weights (slider > 0)</summary>
            public int UpsideWeight { get; set; }

            /// <summary>Sum of absolute bad mod weights — subtracted in NetWeight</summary>
            public int DownsideWeight { get; set; }

            /// <summary>Max weight of a single good mod — for positive veto check</summary>
            public int MaxSingleUpside { get; set; }

            /// <summary>Max absolute weight of a single bad mod — for negative veto check</summary>
            public int MaxSingleDownside { get; set; }

            /// <summary>NetWeight = UpsideWeight - DownsideWeight</summary>
            public int NetWeight => UpsideWeight - DownsideWeight;

            /// <summary>Option has both good and bad mods simultaneously</summary>
            public bool HasMixedMods => UpsideWeight > 0 && DownsideWeight > 0;

            public bool PlayPositiveAlert { get; set; }
            public bool PlayNegativeAlert { get; set; }
        }
    }
}

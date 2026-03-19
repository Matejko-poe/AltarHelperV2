using ExileCore;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Cache;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace AltarHelper
{
    [SupportedOSPlatform("windows")]
    public class AltarHelperCore : BaseSettingsPlugin<Settings>
    {
        private static readonly Regex NumberRegex = new(@"\d+(?:[.,]\d+)?", RegexOptions.Compiled);
        private static readonly HashSet<string> AltarMetadata = new(StringComparer.Ordinal)
        {
            "Metadata/MiscellaneousObjects/PrimordialBosses/TangleAltar",
            "Metadata/MiscellaneousObjects/PrimordialBosses/CleansingFireAltar"
        };

        public List<Tuple<RectangleF, Color, int>> RectangleDrawingList { get; } = new();
        public List<Tuple<string, Vector2, Color>> TextDrawingList { get; } = new();

        private TimeCache<List<LabelOnGround>> _altarLabels;
        private readonly Dictionary<string, ParsedSelection> _selectionCache = new(StringComparer.Ordinal);
        private readonly Dictionary<long, string> _alertedLabelKeys = new();
        private readonly HashSet<long> _currentLabelIds = new();

        private readonly object _soundLock = new();
        private DateTime _lastPlayed;

        public override bool Initialise()
        {
            Name = "AltarHelper";
            _altarLabels = new TimeCache<List<LabelOnGround>>(UpdateAltarLabelList, 100);
            return true;
        }

        public override void AreaChange(AreaInstance area)
        {
            _selectionCache.Clear();
            _alertedLabelKeys.Clear();
        }

        public override void Render()
        {
            foreach (var frame in RectangleDrawingList)
            {
                Graphics.DrawFrame(frame.Item1, frame.Item2, frame.Item3);
            }
            foreach (var text in TextDrawingList)
            {
                Graphics.DrawText(text.Item1, text.Item2, text.Item3);
            }
        }

        public override Job Tick()
        {
            RectangleDrawingList.Clear();
            TextDrawingList.Clear();

            if (!CanRun()) return null;

            CompareWeights();

            return null;
        }

        private bool CanRun()
        {
            var ui = GameController?.IngameState?.IngameUi;
            if (ui == null) return false;
            var labels = ui.ItemsOnGroundLabelsVisible;
            if (labels == null) return false;

            if (GameController.Area.CurrentArea.IsHideout ||
                GameController.Area.CurrentArea.IsTown)
                return false;

            return _altarLabels?.Value?.Count > 0;
        }

        private List<LabelOnGround> UpdateAltarLabelList()
        {
            var labels = GameController?.IngameState?.IngameUi?.ItemsOnGroundLabelsVisible;
            if (labels == null || labels.Count == 0) return new List<LabelOnGround>();

            var result = new List<LabelOnGround>();
            foreach (var label in labels)
            {
                if (label?.ItemOnGround == null) continue;
                if (!AltarMetadata.Contains(label.ItemOnGround.Metadata)) continue;
                if (label.Label is not { IsValid: true, Address: > 0, IsVisible: true }) continue;
                result.Add(label);
            }

            return result;
        }

        private void CompareWeights()
        {
            var altars = _altarLabels.Value;
            if (altars.Count == 0)
            {
                _alertedLabelKeys.Clear();
                return;
            }

            _currentLabelIds.Clear();

            foreach (var altarLabel in altars)
            {
                var topOptionLabel = altarLabel.Label.GetChildAtIndex(0);
                var bottomOptionLabel = altarLabel.Label.GetChildAtIndex(1);

                string? topOptionText = topOptionLabel?.GetChildAtIndex(1)?.GetText(512);
                string? bottomOptionText = bottomOptionLabel?.GetChildAtIndex(1)?.GetText(512);

                if (Settings.DebugSettings.DebugRawText)
                {
                    DebugWindow.LogError($"AltarBottom Length 512 : {bottomOptionText}");
                    DebugWindow.LogError($"AltarTop Length 512 : {topOptionText}");
                }

                if (string.IsNullOrEmpty(topOptionText) || string.IsNullOrEmpty(bottomOptionText)) continue;

                var topParsed = ParseSelection(topOptionText);
                var bottomParsed = ParseSelection(bottomOptionText);

                var topEval = EvaluateSelection(topParsed);
                var bottomEval = EvaluateSelection(bottomParsed);

                if (topEval.UpsideWeight == 0 &&
                    bottomEval.UpsideWeight == 0 &&
                    topEval.DownsideWeight == 0 &&
                    bottomEval.DownsideWeight == 0)
                    continue;

                var topWeight = topEval.UpsideWeight + topEval.DownsideWeight;
                var bottomWeight = bottomEval.UpsideWeight + bottomEval.DownsideWeight;

                var labelId = altarLabel.Label.Address;
                _currentLabelIds.Add(labelId);

                var alertKey = $"{topOptionText}\n{bottomOptionText}";
                HandleAlert(labelId, topEval.PlayAlert || bottomEval.PlayAlert, topEval.AlertSound ?? bottomEval.AlertSound, alertKey);

                if (Settings.DebugSettings.DebugWeight)
                {
                    TextDrawingList.Add(new(topWeight.ToString(), new Vector2(topOptionLabel.GetClientRectCache.Center.X - 10, topOptionLabel.GetClientRectCache.Top - 25), Color.Cyan));
                    TextDrawingList.Add(new(bottomWeight.ToString(), new Vector2(bottomOptionLabel.GetClientRectCache.Center.X - 10, bottomOptionLabel.GetClientRectCache.Bottom + 15), Color.Cyan));
                }

                if (topWeight < 0 || bottomWeight < 0)
                {
                    if (topWeight < 0) RectangleDrawingList.Add(new(topOptionLabel.GetClientRectCache, Settings.AltarSettings.BadColor, Settings.AltarSettings.FrameThickness));
                    if (bottomWeight < 0) RectangleDrawingList.Add(new(bottomOptionLabel.GetClientRectCache, Settings.AltarSettings.BadColor, Settings.AltarSettings.FrameThickness));
                }

                if (topWeight >= 0 || bottomWeight >= 0)
                {
                    if (topWeight >= bottomWeight && topWeight > 0) RectangleDrawingList.Add(new(topOptionLabel.GetClientRectCache, GetColor(topEval.Target), Settings.AltarSettings.FrameThickness));
                    if (bottomWeight > topWeight && bottomWeight > 0) RectangleDrawingList.Add(new(bottomOptionLabel.GetClientRectCache, GetColor(bottomEval.Target), Settings.AltarSettings.FrameThickness));
                }
            }

            if (_alertedLabelKeys.Count > 0)
            {
                var toRemove = _alertedLabelKeys.Keys.Where(id => !_currentLabelIds.Contains(id)).ToList();
                foreach (var id in toRemove)
                    _alertedLabelKeys.Remove(id);
            }
        }

        private ParsedSelection ParseSelection(string altarLabelText)
        {
            if (_selectionCache.TryGetValue(altarLabelText, out var cached))
                return cached;

            using var stringReader = new StringReader(altarLabelText);

            var targetLine = stringReader.ReadLine() ?? string.Empty;
            var targetKey = ExtractTagValue(targetLine);
            if (!AltarModsConstants.AltarTargetDict.TryGetValue(targetKey, out var target))
                target = AffectedTarget.Any;

            var downsides = new List<string>();
            var upsides = new List<string>();

            string? line;
            var upsideSectionReached = false;
            while ((line = stringReader.ReadLine()) != null)
            {
                if (line.StartsWith("<enchanted>", StringComparison.Ordinal))
                {
                    upsideSectionReached = true;
                }

                var cleaned = StripTags(line);
                if (string.IsNullOrWhiteSpace(cleaned))
                    continue;

                var normalized = NormalizeMod(cleaned);
                if (string.IsNullOrWhiteSpace(normalized))
                    continue;

                if (upsideSectionReached)
                    upsides.Add(normalized);
                else
                    downsides.Add(normalized);
            }

            cached = new ParsedSelection(target, upsides, downsides);
            _selectionCache[altarLabelText] = cached;
            return cached;
        }

        private SelectionEval EvaluateSelection(ParsedSelection selection)
        {
            var upsideWeight = 0;
            var downsideWeight = 0;
            var playAlert = false;
            string alertSound = null;

            foreach (var mod in selection.Upsides)
            {
                var weight = Settings.GetModTier(mod);
                upsideWeight += weight;
                if (!playAlert && Settings.GetModAlert(mod))
                {
                    playAlert = true;
                    alertSound = Settings.GetModAlertSound(mod);
                }
            }

            foreach (var mod in selection.Downsides)
            {
                var weight = Settings.GetModTier(mod);
                downsideWeight += weight;
                if (!playAlert && Settings.GetModAlert(mod))
                {
                    playAlert = true;
                    alertSound = Settings.GetModAlertSound(mod);
                }
            }

            return new SelectionEval(selection.Target, upsideWeight, downsideWeight, playAlert, alertSound);
        }

        private void HandleAlert(long labelId, bool shouldPlay, string alertSound, string alertKey)
        {
            if (!shouldPlay)
            {
                _alertedLabelKeys.Remove(labelId);
                return;
            }

            if (!Settings.AltarSettings.RepeatAlerts.Value &&
                _alertedLabelKeys.TryGetValue(labelId, out var existingKey) &&
                string.Equals(existingKey, alertKey, StringComparison.Ordinal))
                return;

            if (TryPlaySound(alertSound))
                _alertedLabelKeys[labelId] = alertKey;
        }

        private bool TryPlaySound(string alertSound)
        {
            lock (_soundLock)
            {
                var minDelay = Math.Max(500, Settings.AltarSettings.DelayBetweenAlerts.Value);
                if ((DateTime.Now - _lastPlayed).TotalMilliseconds < minDelay)
                    return false;

                var file = string.IsNullOrWhiteSpace(alertSound)
                    ? Settings.AltarSettings.SoundFile.Value?.Trim()
                    : alertSound.Trim();
                if (string.IsNullOrWhiteSpace(file))
                    return false;

                var nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(nameWithoutExt))
                    return false;

                var soundPath = Path.Combine("..", "Sounds", nameWithoutExt).Replace('\\', '/');
                GameController.SoundController.PlaySound(soundPath);
                _lastPlayed = DateTime.Now;
                return true;
            }
        }

        private static string ExtractTagValue(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return string.Empty;
            var start = line.IndexOf('{');
            var end = line.LastIndexOf('}');
            if (start >= 0 && end > start)
                return line.Substring(start + 1, end - start - 1);
            return line.Trim();
        }

        private static string StripTags(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return string.Empty;

            var result = line;
            if (result.StartsWith("<enchanted>", StringComparison.Ordinal))
                result = result.Replace("<enchanted>{", string.Empty).Replace("<enchanted>", string.Empty);

            if (result.StartsWith("<rgb", StringComparison.Ordinal))
            {
                var braceIndex = result.IndexOf('{');
                if (braceIndex >= 0)
                    result = result[(braceIndex + 1)..];
            }

            result = result.Replace("}", string.Empty);
            return result.Trim();
        }

        private static string NormalizeMod(string line)
        {
            var normalized = line.Replace('–', '-').Replace('—', '-').Replace("%%", "%").Trim();
            normalized = NumberRegex.Replace(normalized, "#");

            while (normalized.Contains("  ", StringComparison.Ordinal))
                normalized = normalized.Replace("  ", " ");

            return normalized;
        }

        public Color GetColor(AffectedTarget choice)
        {
            if (choice == AffectedTarget.Minions) return Settings.AltarSettings.MinionColor;
            if (choice == AffectedTarget.FinalBoss) return Settings.AltarSettings.BossColor;
            if (choice == AffectedTarget.Player) return Settings.AltarSettings.PlayerColor;

            return Color.Transparent;
        }

        private sealed class ParsedSelection
        {
            public ParsedSelection(AffectedTarget target, List<string> upsides, List<string> downsides)
            {
                Target = target;
                Upsides = upsides;
                Downsides = downsides;
            }

            public AffectedTarget Target { get; }
            public List<string> Upsides { get; }
            public List<string> Downsides { get; }
        }

        private sealed class SelectionEval
        {
            public SelectionEval(AffectedTarget target, int upsideWeight, int downsideWeight, bool playAlert, string alertSound)
            {
                Target = target;
                UpsideWeight = upsideWeight;
                DownsideWeight = downsideWeight;
                PlayAlert = playAlert;
                AlertSound = alertSound;
            }

            public AffectedTarget Target { get; }
            public int UpsideWeight { get; }
            public int DownsideWeight { get; }
            public bool PlayAlert { get; }
            public string AlertSound { get; }
        }
    }
}

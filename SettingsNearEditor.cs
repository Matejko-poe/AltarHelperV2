using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using ImGuiNET;
using SN = System.Numerics;

namespace AltarHelperV2
{
    /// <summary>One altar the player stood at: its mod lines, kept so the weights can be edited afterwards.</summary>
    public sealed class AltarRecord
    {
        public DateTime Seen;
        public List<string> TopUp = new(), TopDown = new(), BottomUp = new(), BottomDown = new();
        public string Signature = "";
    }

    // [Test] "near editor": a small window with sliders for exactly the mods of the altar you stand at.
    // It edits the live weights (the same ones as the other panels), so the active profile auto-saves them.
    [SupportedOSPlatform("windows")]
    public partial class Settings
    {
        public void DrawNearEditor(List<string> topUp, List<string> topDown,
            List<string> bottomUp, List<string> bottomDown, string info, Action backToNearest = null)
        {
#pragma warning disable CA1416
            ImGui.SetNextWindowPos(new SN.Vector2(40, 120), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new SN.Vector2(560, 520), ImGuiCond.FirstUseEver);
            bool open = ImGui.Begin("Altar mods nearby (near editor)");
            if (open)
            {
                DrawSaveBar();
                ImGui.TextColored(new SN.Vector4(0.6f, 0.85f, 1f, 1f), $"Profile: {CurrentProfile}  |  {info}");
                if (backToNearest != null && ImGui.Button("Back to the nearest altar")) backToNearest();
                ImGui.TextColored(new SN.Vector4(0.7f, 0.7f, 0.7f, 1f),
                    "Penalties are negative, rewards positive. Moving a slider protects it from automatic price updates.");
                if (!AltarSettings.SignedDownsideWeights.Value)
                {
                    ImGui.TextColored(new SN.Vector4(1f, 0.8f, 0.4f, 1f), "Positive (green) values need signed downside weights.");
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Turn on"))
                        AltarSettings.SignedDownsideWeights.Value = true;
                }
                ImGui.Separator();

                DrawNearOption("Top option", topDown, topUp);
                DrawNearOption("Bottom option", bottomDown, bottomUp);
            }
            ImGui.End();
#pragma warning restore CA1416
        }

        private string Summarize(List<string> ups, List<string> downs)
        {
            string Short(string t) => t.Length > 48 ? t[..48] + "..." : t;
            int W(string t) => GetModTier(AltarTextNormalizer.NormalizeNumbers(t));

            var best = ups.OrderByDescending(W).FirstOrDefault();
            var worst = downs.OrderBy(W).FirstOrDefault();
            var parts = new List<string>();
            if (best != null) parts.Add($"{Short(best)} [{W(best)}]");
            if (worst != null) parts.Add($"worst: {Short(worst)} [{W(worst)}]");
            return parts.Count == 0 ? "(no mods)" : string.Join("  |  ", parts);
        }

        /// <summary>Window with the last altars you stood at; "Edit mods" opens one in the near editor.</summary>
        public void DrawRecentAltars(List<AltarRecord> history, AltarRecord pinned, Action<AltarRecord> pin)
        {
#pragma warning disable CA1416
            ImGui.SetNextWindowPos(new SN.Vector2(620, 120), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new SN.Vector2(640, 360), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Recent altars (last 10)"))
            {
                ImGui.TextColored(new SN.Vector4(0.7f, 0.7f, 0.7f, 1f),
                    "Took a mod and it was too strong? Open that altar again and change its weights. Numbers in [] are current weights.");
                ImGui.Separator();
                if (history.Count == 0)
                    ImGui.TextUnformatted("No altars recorded yet - walk up to one.");

                for (int i = 0; i < history.Count; i++)
                {
                    var rec = history[i];
                    ImGui.PushID(i);
                    bool isPinned = ReferenceEquals(rec, pinned);
                    ImGui.TextColored(isPinned ? new SN.Vector4(1f, 0.85f, 0.3f, 1f) : new SN.Vector4(0.6f, 0.85f, 1f, 1f),
                        rec.Seen.ToString("HH:mm:ss") + (isPinned ? "  (editing)" : ""));
                    ImGui.SameLine();
                    if (ImGui.SmallButton(isPinned ? "Editing" : "Edit mods")) pin(rec);
                    ImGui.TextUnformatted("Top:    " + Summarize(rec.TopUp, rec.TopDown));
                    ImGui.TextUnformatted("Bottom: " + Summarize(rec.BottomUp, rec.BottomDown));
                    ImGui.Separator();
                    ImGui.PopID();
                }
            }
            ImGui.End();
#pragma warning restore CA1416
        }

        private void DrawNearOption(string title, List<string> downsides, List<string> upsides)
        {
#pragma warning disable CA1416
            if (!ImGui.CollapsingHeader(title + "##near", ImGuiTreeNodeFlags.DefaultOpen)) return;

            ImGui.PushID(title);
            if (downsides.Count > 0)
            {
                Heading("Downsides (what you / the enemies get)");
                foreach (var text in downsides.Distinct()) _ = DrawNearMod(text, isReward: false);
            }
            if (upsides.Count > 0)
            {
                Heading("Rewards");
                double total = 0;
                foreach (var text in upsides.Distinct()) total += DrawNearMod(text, isReward: true);
                if (total > 0)
                    ImGui.TextColored(PriceColor, "Sum: " + total.ToString("0.#", CultureInfo.InvariantCulture) +
                                                  "c  (priced drops of this option, in chaos)");
            }
            ImGui.PopID();
#pragma warning restore CA1416
        }

        /// <summary>Draws one mod row; returns the chaos value of the drop when it is an exact, priced boss drop.</summary>
        private double DrawNearMod(string text, bool isReward)
        {
#pragma warning disable CA1416
            // Boss drops: every wording ("1 additional X", "4 additional Xs") edits the same canonical weight.
            bool bossDrop = isReward && AltarSettings.ScaleBossDropByCount.Value &&
                            AltarBossDrops.TryParse(text, out _, out _);
            int dropCount = 0;
            string canonical = null;
            if (bossDrop) AltarBossDrops.TryParse(text, out dropCount, out canonical);

            string minionId = null;
            bool minionDrop = isReward && !bossDrop && AltarBossDrops.TryParseMinion(text, out minionId);
            var id = bossDrop ? canonical
                : minionDrop ? minionId
                : AltarTextNormalizer.NormalizeNumbers(AltarBossDrops.StripMarkup(text));
            ImGui.PushID(id);

            int value = GetModTier(id);
            bool signed = AltarSettings.SignedDownsideWeights.Value;
            double sum = 0;

            // Name, price and (for unknown lines) a hint
            ImGui.TextColored(isReward ? new SN.Vector4(0.6f, 1f, 0.6f, 1f) : new SN.Vector4(1f, 0.8f, 0.5f, 1f),
                text.Replace("%", "%%"));
            var price = PriceLabelFor(id);
            if (price != null)
            {
                ImGui.SameLine();
                ImGui.TextColored(PriceColor, price);
            }
            // Exact count + known price: value of this drop in chaos
            if (bossDrop && TryPriceOf(id, out var unitChaos))
            {
                sum = dropCount * unitChaos;
                ImGui.SameLine();
                ImGui.TextColored(PriceColor, "Sum: " + dropCount + " x " +
                                              unitChaos.ToString("0.##", CultureInfo.InvariantCulture) + "c = " +
                                              sum.ToString("0.#", CultureInfo.InvariantCulture) + "c");
            }
            DrawUnsavedMarker(id);
            if (!AltarModsConstants.AltarTypes.Any(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                ImGui.SameLine();
                ImGui.TextColored(new SN.Vector4(1f, 0.4f, 0.4f, 1f), "(unknown mod)");
            }

            if (isReward)
            {
                int slider = Math.Clamp(value, 0, 10000);
                ImGui.SetNextItemWidth(300);
                if (ImGui.SliderInt("##w", ref slider, 0, 10000)) SetNearWeight(id, slider);
                ImGui.SameLine();
                var rewardAlert = GetModAlert(id);
                if (ImGui.Checkbox("Alert", ref rewardAlert)) ModAlerts[id] = rewardAlert;
                if (bossDrop)
                    ImGui.TextColored(new SN.Vector4(0.65f, 0.65f, 0.65f, 1f),
                        $"weight is for 3 drops; this altar drops {dropCount} -> counts as {(int)Math.Round(value * dropCount / 3.0)}");
            }
            else
            {
                DrawDangerScale(id, value, signed);
            }

            ImGui.Separator();
            ImGui.PopID();
            return sum;
#pragma warning restore CA1416
        }

        private bool TryPriceOf(string id, out double chaos)
        {
            _priceIndex ??= BuildPriceIndex();
            return _priceIndex.TryGetValue(id, out chaos);
        }

        private static SN.Vector4 ScaleColor(int s)
        {
            if (s == 0) return new SN.Vector4(0.6f, 0.6f, 0.6f, 1f);
            float t = Math.Abs(s) / 5f;
            return s < 0
                ? new SN.Vector4(1f - 0.1f * t, 0.65f - 0.55f * t, 0.2f - 0.1f * t, 1f)   // orange -> deep red
                : new SN.Vector4(0.55f - 0.45f * t, 0.9f, 0.5f - 0.3f * t, 1f);           // light -> vivid green
        }

        // -5..+5 danger slider (red = dangerous, green = harmless / take it) plus quick buttons
        private void DrawDangerScale(string id, int weight, bool signed)
        {
#pragma warning disable CA1416
            int scale = AltarWizard.ScaleForWeight(weight);
            var col = ScaleColor(scale);
            int max = signed ? 5 : 0;
            int shown = Math.Min(scale, max);

            ImGui.PushStyleColor(ImGuiCol.SliderGrab, col);
            ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, col);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new SN.Vector4(col.X * 0.35f, col.Y * 0.35f, col.Z * 0.35f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new SN.Vector4(col.X * 0.5f, col.Y * 0.5f, col.Z * 0.5f, 0.9f));
            ImGui.SetNextItemWidth(300);
            // The format string is just the label text (no %d), so the slider shows e.g. "Severe"
            if (ImGui.SliderInt("##scale", ref shown, -5, max, AltarWizard.NameForScale(shown)))
                SetNearWeight(id, AltarWizard.WeightForScale(shown));
            ImGui.PopStyleColor(4);

            ImGui.SameLine();
            ImGui.TextColored(col, weight.ToString());
            ImGui.SameLine();
            var alert = GetModAlert(id);
            if (ImGui.Checkbox("Alert", ref alert)) ModAlerts[id] = alert;

            if (ImGui.SmallButton("Ignore (doesn't affect me)")) SetNearWeight(id, 0);
            if (signed)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Harmless +"))
                    SetNearWeight(id, AltarWizard.HarmlessBonusFor(RiskAppetite.Balanced));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Does not hurt my build: small bonus (only counted when the option has a reward).");
            }
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, ScaleColor(-5) with { W = 0.55f });
            if (ImGui.SmallButton("BRICK")) SetNearWeight(id, AltarWizard.WeightForScale(-5));
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("This would ruin my character: very high penalty (-9000).");
#pragma warning restore CA1416
        }

        private void SetNearWeight(string id, int weight)
        {
            if (weight == 0) ModTiers.Remove(id);
            else ModTiers[id] = weight;
            ManualWeights.Add(id);
        }
    }
}

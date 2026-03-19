using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using Newtonsoft.Json;
using SharpDX;

namespace AltarHelper
{
    [SupportedOSPlatform("windows")]
    public class Settings : ISettings
    {
        public ToggleNode Enable { get; set; } = new ToggleNode(false);
        public AltarSettings AltarSettings { get; set; } = new AltarSettings();
        public DebugSettings DebugSettings { get; set; } = new DebugSettings();

        [JsonIgnore]
        public CustomNode Tribes { get; }

        public Settings()
        {
            var unitFilter = "";
#pragma warning disable CA1416 // Validar a compatibilidade da plataforma
            Tribes = new CustomNode
            {
                DrawDelegate = () =>
                {
                    if (ImGui.TreeNode("Mods & Weight"))
                    {
                        ImGui.InputTextWithHint("##UnitFilter", "Filter", ref unitFilter, 100);

                        if (ImGui.BeginTable("UnitConfig", 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Borders))
                        {
                            ImGui.TableSetupColumn("Audio Alert", ImGuiTableColumnFlags.WidthFixed, 190);
                            ImGui.TableSetupColumn("Weight", ImGuiTableColumnFlags.WidthFixed, 200);
                            ImGui.TableSetupColumn("Mod");
                            ImGui.TableSetupColumn("Type");
                            ImGui.TableHeadersRow();
                            foreach (var (id, name, type) in AltarModsConstants.AltarTypesDistinct.Where(t => t.Name.Contains(unitFilter, StringComparison.InvariantCultureIgnoreCase)))
                            {
                                ImGui.PushID($"unit{id}");
                                ImGui.TableNextRow(ImGuiTableRowFlags.None);
                                ImGui.TableNextColumn();
                                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6);
                                var currentAlertValue = GetModAlert(id);
                                if (ImGui.Checkbox("##Alert", ref currentAlertValue))
                                {
                                    ModAlerts[id] = currentAlertValue;
                                }
                                ImGui.SameLine();
                                ImGui.SetNextItemWidth(150);
                                var currentAlertSound = GetModAlertSound(id);
                                if (ImGui.InputTextWithHint("##AlertSound", this.AltarSettings.SoundFile.Value, ref currentAlertSound, 100))
                                {
                                    SetModAlertSound(id, currentAlertSound);
                                }
                                ImGui.TableNextColumn();
                                ImGui.SetNextItemWidth(200);
                                var currentValue = GetModTier(id);
                                if (ImGui.SliderInt("", ref currentValue, -1000, 1000))
                                {
                                    ModTiers[id] = currentValue;
                                }
                                ImGui.TableNextColumn();
                                ImGui.Text(name);
                                ImGui.TableNextColumn();
                                ImGui.Text(type);

                                ImGui.PopID();
                            }

                            ImGui.EndTable();
                        }

                        ImGui.TreePop();
                    }
                }
            };
#pragma warning restore CA1416 // Validar a compatibilidade da plataforma
        }

        public int GetModTier(string mod)
        {
            return ModTiers.GetValueOrDefault(mod ?? "", 0);
        }

        public Dictionary<string, int> ModTiers = new()
        {
        };

        public bool GetModAlert(string mod)
        {
            return ModAlerts.GetValueOrDefault(mod ?? "", false);
        }

        public Dictionary<string, bool> ModAlerts = new()
        {
        };

        public string GetModAlertSound(string mod)
        {
            return ModAlertSounds.GetValueOrDefault(mod ?? "", string.Empty);
        }

        public void SetModAlertSound(string mod, string sound)
        {
            mod ??= string.Empty;
            sound = sound?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(sound))
                ModAlertSounds.Remove(mod);
            else
                ModAlertSounds[mod] = sound;
        }

        public Dictionary<string, string> ModAlertSounds = new()
        {
        };
    }

    [Submenu]
    [SupportedOSPlatform("windows")]
    public class AltarSettings
    {
        public RangeNode<int> FrameThickness { get; set; } = new RangeNode<int>(2, 1, 5);
        [Menu("Alert sound", "Wav file, localized in PoeHelper/Sounds/")]
        public TextNode SoundFile { get; set; } = new TextNode("attention.wav");
        [Menu("Repeat alert sound", "If enabled, the same altar can alert again using Delay between Sounds")]
        public ToggleNode RepeatAlerts { get; set; } = new ToggleNode(false);
        [Menu("Delay between Sounds", "1 Second = 1000")]
        public RangeNode<int> DelayBetweenAlerts { get; set; } = new RangeNode<int>(3000, 1000, 10000);
        public ColorNode MinionColor { get; set; } = new ColorNode(Color.LightGreen);
        public ColorNode PlayerColor { get; set; } = new ColorNode(Color.LightCyan);
        public ColorNode BossColor { get; set; } = new ColorNode(Color.LightBlue);
        public ColorNode BadColor { get; set; } = new ColorNode(Color.Red);
    }

    [Submenu]
    [SupportedOSPlatform("windows")]
    public class DebugSettings
    {
        public ToggleNode DebugRawText { get; set; } = new ToggleNode(false);
        public ToggleNode DebugBuffs { get; set; } = new ToggleNode(false);
        public ToggleNode DebugDebuffs { get; set; } = new ToggleNode(false);
        public ToggleNode DebugWeight { get; set; } = new ToggleNode(false);
    }
}

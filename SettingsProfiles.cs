using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using ImGuiNET;
using SN = System.Numerics;

namespace AltarHelperV2
{
    // [Test] panel: profiles (one JSON file each), setup wizard, and chaos prices next to reward mods.
    // It edits the same live fields as the old "Mods & Weights (V2)" panel, which is left untouched.
    [SupportedOSPlatform("windows")]
    public partial class Settings
    {
        // Persisted by ExileCore with the rest of the settings: which profile is active.
        public string CurrentProfile = "Default";

        // false = changes are kept until you press Save (blinking button); true = saved ~1s after each change
        public bool AutoSaveProfile = false;

        private AltarProfileStore _store;
        private string _currentBase;            // profile the active one inherits from (null = standalone)
        private string _lastSavedJson = "";
        private DateTime _nextProfileCheck = DateTime.MinValue;
        private int _overrideCount;
        private string _profileMessage = "";
        private string _newProfileName = "";
        private bool _confirmDelete;
        private AltarProfile _savedSnapshot;    // what is on disk (resolved), to mark individual mods as not saved
        private bool _dirty;
        private DateTime _dirtySince = DateTime.MinValue;

        // ------------------------------------------------------------
        // Chaos prices shown next to reward mods
        // ------------------------------------------------------------
        private static readonly SN.Vector4 PriceColor = new(0.2f, 0.95f, 0.95f, 1f);
        private Dictionary<string, double> _priceIndex;

        private Dictionary<string, double> BuildPriceIndex()
        {
            var idx = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in PricedRewards)
            {
                double chaos = PriceOf(r.One, r.Chaos);
                idx[BossKey(r.Many)] = chaos;
                idx[MinionKey(r.One)] = chaos;
                idx[BossKey(r.One)] = chaos; // boss "1 additional" Ichor/Ember wording
            }
            return idx;
        }

        /// <summary>"(2.4c)" for mods that drop a priced item; null for everything else.</summary>
        private string PriceLabelFor(string id)
        {
            _priceIndex ??= BuildPriceIndex();
            if (!_priceIndex.TryGetValue(id, out var chaos)) return null;
            var text = chaos < 1
                ? chaos.ToString("0.##", CultureInfo.InvariantCulture)
                : chaos.ToString("0.#", CultureInfo.InvariantCulture);
            return $"({text}c)";
        }

        // Reward weights for a new profile, limited to the reward groups the user cares about.
        private Dictionary<string, int> BuildRewardTiers(WizardAnswers answers)
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            double divine = CurrentDivine;
            foreach (var r in PricedRewards)
            {
                double chaos = PriceOf(r.One, r.Chaos);
                if (AltarWizard.IncludeReward(answers, r.Group.ToString(), chaos))
                    AddPriced(d, r, chaos, divine);
            }

            if (answers.RewardUnpriced)
            {
                // Unpriced rewards (duplication / quantity / rarity): keep only the positive manual values.
                var manual = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                AddManual(manual);
                foreach (var kv in manual.Where(kv => kv.Value > 0)) d[kv.Key] = kv.Value;
            }
            return d;
        }

        // ------------------------------------------------------------
        // Profile storage glue
        // ------------------------------------------------------------
        private AltarProfile CaptureLive(string name, string baseName)
        {
            var p = new AltarProfile
            {
                Name = name,
                BaseProfile = baseName,
                MinPickWeight = AltarSettings.MinPickWeight.Value,
                NegativeVetoThreshold = AltarSettings.NegativeVetoThreshold.Value,
                PositiveVetoThreshold = AltarSettings.PositiveVetoThreshold.Value,
                AlertMinNetWeight = AltarSettings.AlertMinNetWeight.Value,
                MinionWeight = AltarSettings.MinionWeight.Value,
                BossWeight = AltarSettings.BossWeight.Value,
                SignedDownsideWeights = AltarSettings.SignedDownsideWeights.Value,
            };
            foreach (var kv in ModTiers.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)) p.ModTiers[kv.Key] = kv.Value;
            foreach (var kv in ModAlerts.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)) p.ModAlerts[kv.Key] = kv.Value;
            p.ManualWeights = ManualWeights.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            return p;
        }

        private void ApplyModsToLive(AltarProfile p)
        {
            ModTiers.Clear();
            foreach (var kv in p.ModTiers) ModTiers[kv.Key] = kv.Value;
            ModAlerts.Clear();
            foreach (var kv in p.ModAlerts) ModAlerts[kv.Key] = kv.Value;
            ManualWeights.Clear();
            foreach (var m in p.ManualWeights) ManualWeights.Add(m);
        }

        private void ApplyToLive(AltarProfile p)
        {
            ApplyModsToLive(p);
            AltarSettings.MinPickWeight.Value = p.MinPickWeight;
            AltarSettings.NegativeVetoThreshold.Value = p.NegativeVetoThreshold;
            AltarSettings.PositiveVetoThreshold.Value = p.PositiveVetoThreshold;
            AltarSettings.AlertMinNetWeight.Value = p.AlertMinNetWeight;
            AltarSettings.MinionWeight.Value = p.MinionWeight;
            AltarSettings.BossWeight.Value = p.BossWeight;
            AltarSettings.SignedDownsideWeights.Value = p.SignedDownsideWeights;
        }

        private void MarkSaved()
        {
            _savedSnapshot = CaptureLive(CurrentProfile, _currentBase);
            _lastSavedJson = AltarProfile.ToJson(_savedSnapshot);
            _dirty = false;
        }

        /// <summary>True when this mod's weight or alert differs from the saved profile.</summary>
        public bool IsModUnsaved(string id)
        {
            if (_store == null || _savedSnapshot == null) return false;
            return ModTiers.GetValueOrDefault(id, 0) != _savedSnapshot.ModTiers.GetValueOrDefault(id, 0) ||
                   ModAlerts.GetValueOrDefault(id, false) != _savedSnapshot.ModAlerts.GetValueOrDefault(id, false);
        }

        private static readonly SN.Vector4 UnsavedRed = new(1f, 0.25f, 0.25f, 1f);

        /// <summary>Small red "(not saved)" marker, drawn on the same line after a mod name.</summary>
        private void DrawUnsavedMarker(string id)
        {
#pragma warning disable CA1416
            if (!IsModUnsaved(id)) return;
            ImGui.SameLine();
            ImGui.TextColored(UnsavedRed, "(not saved)");
#pragma warning restore CA1416
        }

        /// <summary>
        /// Top bar: a blinking SAVE button while there are unsaved changes, a quiet "saved" otherwise.
        /// </summary>
        public void DrawSaveBar()
        {
#pragma warning disable CA1416
            if (_store == null) return;
            if (_dirty)
            {
                float pulse = (float)(Math.Sin(ImGui.GetTime() * 7.0) * 0.5 + 0.5);
                var bright = new SN.Vector4(0.95f, 0.45f + 0.4f * pulse, 0.1f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Button, bright);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new SN.Vector4(1f, 0.85f, 0.3f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new SN.Vector4(1f, 0.95f, 0.5f, 1f));
                ImGui.PushStyleColor(ImGuiCol.Text, new SN.Vector4(0.05f, 0.05f, 0.05f, 1f));
                bool save = ImGui.Button("  SAVE PROFILE \"" + CurrentProfile + "\" - unsaved changes  ");
                ImGui.PopStyleColor(4);
                if (save && SaveActiveProfile()) _profileMessage = "Saved \"" + CurrentProfile + "\".";
                ImGui.SameLine();
                if (ImGui.SmallButton("Discard changes"))
                {
                    if (LoadProfile(CurrentProfile)) _profileMessage = "Discarded unsaved changes.";
                }
            }
            else
            {
                ImGui.TextColored(new SN.Vector4(0.45f, 0.85f, 0.45f, 1f), "Profile \"" + CurrentProfile + "\": all changes saved");
            }
            ImGui.SameLine();
            ImGui.Checkbox("Auto-save##v2autosave", ref AutoSaveProfile);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("On: every change is saved about a second later.\nOff: press the blinking Save button.");
#pragma warning restore CA1416
        }

        private bool SaveActiveProfile()
        {
            if (_store == null) return false;
            var ok = _store.Save(CaptureLive(CurrentProfile, _currentBase));
            if (ok) MarkSaved();
            else _profileMessage = "Could not save profile (check folder permissions).";
            return ok;
        }

        /// <summary>
        /// Called once by the plugin. Loads the active profile into the live settings; on the first run
        /// the existing settings become profile "Default" so nothing is lost.
        /// </summary>
        public void InitProfiles(string directory, string legacyDirectory = null)
        {
            try
            {
                MigrateLegacyProfiles(directory, legacyDirectory);
                _store = new AltarProfileStore(directory);
                var names = _store.List();
                if (names.Count == 0)
                {
                    CurrentProfile = "Default";
                    _currentBase = null;
                    SaveActiveProfile();
                    _profileMessage = "Created profile \"Default\" from your current settings.";
                }
                else
                {
                    if (!_store.Exists(CurrentProfile)) CurrentProfile = names[0];
                    LoadProfile(CurrentProfile);
                }
            }
            catch (Exception ex)
            {
                _store = null;
                _profileMessage = "Profiles unavailable: " + ex.Message;
            }
        }

        // Profiles used to live next to the compiled plugin; copy them into the stable config folder once.
        private static void MigrateLegacyProfiles(string directory, string legacyDirectory)
        {
            try
            {
                if (string.IsNullOrEmpty(legacyDirectory) || !System.IO.Directory.Exists(legacyDirectory)) return;
                if (string.Equals(System.IO.Path.GetFullPath(directory), System.IO.Path.GetFullPath(legacyDirectory),
                        StringComparison.OrdinalIgnoreCase)) return;
                System.IO.Directory.CreateDirectory(directory);
                foreach (var file in System.IO.Directory.GetFiles(legacyDirectory, "*.json"))
                {
                    var target = System.IO.Path.Combine(directory, System.IO.Path.GetFileName(file));
                    if (!System.IO.File.Exists(target)) System.IO.File.Copy(file, target);
                }
            }
            catch (Exception)
            {
                // Migration is best effort; the plugin then starts with whatever is in the new folder.
            }
        }

        private bool LoadProfile(string name)
        {
            var resolved = _store?.LoadResolved(name);
            if (resolved == null)
            {
                _profileMessage = $"Profile \"{name}\" is missing or unreadable.";
                return false;
            }
            var raw = _store.LoadRaw(name);
            CurrentProfile = raw?.Name ?? name;
            _currentBase = raw?.BaseProfile;
            ApplyToLive(resolved);
            MarkSaved();
            RecountOverrides();
            _confirmDelete = false;
            return true;
        }

        private void SwitchProfile(string name)
        {
            if (_store == null || string.Equals(name, CurrentProfile, StringComparison.OrdinalIgnoreCase)) return;
            if (_dirty && !AutoSaveProfile)
            {
                _profileMessage = "Unsaved changes in \"" + CurrentProfile + "\": press Save (or Discard changes) before switching.";
                return;
            }
            SaveActiveProfile(); // never lose edits made to the profile we are leaving
            if (LoadProfile(name)) _profileMessage = $"Switched to \"{CurrentProfile}\".";
        }

        private void RecountOverrides()
        {
            _overrideCount = 0;
            if (_store == null || _currentBase == null) return;
            var stored = _store.ToStorable(CaptureLive(CurrentProfile, _currentBase));
            _overrideCount = stored.ModTiers.Count + stored.ModAlerts.Count;
        }

        /// <summary>
        /// Called every plugin tick: keeps the "unsaved changes" flag current and, when auto-save is on,
        /// saves about a second after the last change.
        /// </summary>
        public void TickProfiles()
        {
            if (_store == null) return;
            var now = DateTime.UtcNow;
            if (now < _nextProfileCheck) return;
            _nextProfileCheck = now.AddMilliseconds(300);

            try
            {
                var json = AltarProfile.ToJson(CaptureLive(CurrentProfile, _currentBase));
                bool dirty = json != _lastSavedJson;
                if (dirty && !_dirty) _dirtySince = now;
                _dirty = dirty;

                if (_dirty && AutoSaveProfile && (now - _dirtySince).TotalMilliseconds >= 1000)
                {
                    if (SaveActiveProfile()) RecountOverrides();
                }
            }
            catch (Exception)
            {
                // Saving must never break the overlay; the next tick retries.
            }
        }

        // ------------------------------------------------------------
        // Setup wizard state
        // ------------------------------------------------------------
        private readonly WizardAnswers _wizard = new();
        private int _wizardPage;
        private WizardResult _wizardResult;
        private string _wizardName = "My character";
        private const int WizardLastPage = 5;

        private static void Radio<T>(string label, ref T field, T value) where T : struct, Enum
        {
            int cur = Convert.ToInt32(field);
            if (ImGui.RadioButton(label, ref cur, Convert.ToInt32(value)))
                field = (T)Enum.ToObject(typeof(T), cur);
        }

        private static void Heading(string text) =>
            ImGui.TextColored(new SN.Vector4(0.45f, 0.8f, 1f, 1f), text);

        private void DrawProfilesPanel()
        {
#pragma warning disable CA1416
            if (!ImGui.TreeNode("[Test] Profiles & Setup Wizard")) return;

            ImGui.TextColored(new SN.Vector4(0.7f, 0.7f, 0.7f, 1f),
                "Edits here and in the old panel change the same weights. Press the blinking Save button (or turn on Auto-save).");

            if (_store == null)
            {
                ImGui.TextColored(new SN.Vector4(1f, 0.5f, 0.4f, 1f),
                    _profileMessage.Length > 0 ? _profileMessage : "Profiles are not available yet.");
                ImGui.TreePop();
                return;
            }

            DrawSaveBar();
            DrawProfileControls();
            ImGui.Separator();
            DrawWizard();

            if (_profileMessage.Length > 0)
                ImGui.TextColored(new SN.Vector4(0.6f, 0.85f, 1f, 1f), _profileMessage);
            ImGui.TreePop();
#pragma warning restore CA1416
        }

        /// <summary>Compact profile picker used at the top of the old "Mods & Weights" panel.</summary>
        private void DrawProfileQuickSwitch()
        {
#pragma warning disable CA1416
            if (_store == null) return;
            DrawSaveBar();
            ImGui.Text("Profile:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(240);
            if (ImGui.BeginCombo("##v2profilequick", CurrentProfile))
            {
                foreach (var name in _store.List())
                {
                    if (ImGui.Selectable(name, string.Equals(name, CurrentProfile, StringComparison.OrdinalIgnoreCase)))
                        SwitchProfile(name);
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            ImGui.TextColored(new SN.Vector4(0.7f, 0.7f, 0.7f, 1f),
                _currentBase != null ? $"(inherits {_currentBase}) auto-saved" : "auto-saved");
            if (_profileMessage.Length > 0)
                ImGui.TextColored(new SN.Vector4(0.6f, 0.85f, 1f, 1f), _profileMessage);
            ImGui.Separator();
#pragma warning restore CA1416
        }

        private void DrawProfileControls()
        {
#pragma warning disable CA1416
            Heading("Profile");
            ImGui.SetNextItemWidth(260);
            if (ImGui.BeginCombo("##v2profile", CurrentProfile))
            {
                foreach (var name in _store.List())
                {
                    if (ImGui.Selectable(name, string.Equals(name, CurrentProfile, StringComparison.OrdinalIgnoreCase)))
                        SwitchProfile(name);
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (ImGui.Button("Revert to saved"))
            {
                if (LoadProfile(CurrentProfile)) _profileMessage = "Reloaded the saved version of this profile.";
            }
            ImGui.SameLine();
            if (!_confirmDelete)
            {
                if (ImGui.Button("Delete")) _confirmDelete = true;
            }
            else
            {
                if (ImGui.Button("Really delete?"))
                {
                    DeleteCurrentProfile();
                    _confirmDelete = false;
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel")) _confirmDelete = false;
            }
            ImGui.TextColored(new SN.Vector4(0.6f, 0.6f, 0.6f, 1f), "Folder: " + _store.Directory);

            // Inheritance
            ImGui.Text("Inherits from:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(220);
            if (ImGui.BeginCombo("##v2base", _currentBase ?? "(none - standalone)"))
            {
                if (ImGui.Selectable("(none - standalone)", _currentBase == null))
                {
                    _currentBase = null;
                    SaveActiveProfile();
                    RecountOverrides();
                }
                foreach (var name in _store.List())
                {
                    if (string.Equals(name, CurrentProfile, StringComparison.OrdinalIgnoreCase)) continue;
                    if (_store.WouldCreateCycle(CurrentProfile, name)) continue;
                    if (ImGui.Selectable(name, string.Equals(name, _currentBase, StringComparison.OrdinalIgnoreCase)))
                    {
                        _currentBase = name;
                        SaveActiveProfile();
                        RecountOverrides();
                    }
                }
                ImGui.EndCombo();
            }
            if (_currentBase != null)
            {
                ImGui.SameLine();
                ImGui.TextColored(new SN.Vector4(0.7f, 0.9f, 0.7f, 1f), $"{_overrideCount} override(s)");
                ImGui.SameLine();
                if (ImGui.SmallButton("Reset overrides"))
                {
                    var baseResolved = _store.LoadResolved(_currentBase);
                    if (baseResolved != null)
                    {
                        ApplyModsToLive(baseResolved);
                        SaveActiveProfile();
                        RecountOverrides();
                        _profileMessage = "Mod weights reset to the base profile.";
                    }
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("A profile that inherits only stores what differs from its base.\nEditing the base changes every profile built on it.");

            // New / duplicate / rename / import / export
            ImGui.InputTextWithHint("##v2newname", "Name for new / renamed profile", ref _newProfileName, 60);
            ImGui.SameLine();
            if (ImGui.Button("New (copy of current)")) CreateProfileFromCurrent();
            ImGui.SameLine();
            if (ImGui.Button("Rename current")) RenameCurrent();

            if (ImGui.Button("Reset duplication / rarity / card rewards to defaults"))
            {
                var defaults = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                AddManual(defaults);
                int n = 0;
                foreach (var kv in defaults.Where(kv => kv.Value > 0 && !ManualWeights.Contains(kv.Key)))
                {
                    ModTiers[kv.Key] = kv.Value;
                    n++;
                }
                _profileMessage = $"Updated {n} unpriced reward weights (weights you moved by hand were kept).";
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Duplication, quantity, rarity and extra Divination Card rewards have no market price.\nThis applies the current default values to this profile.");

            if (ImGui.Button("Export to clipboard"))
            {
                var full = CaptureLive(CurrentProfile, null);
                ImGui.SetClipboardText(AltarProfile.ToJson(full));
                _profileMessage = "Profile copied to the clipboard (standalone, safe to share).";
            }
            ImGui.SameLine();
            if (ImGui.Button("Import from clipboard")) ImportFromClipboard();
#pragma warning restore CA1416
        }

        private void CreateProfileFromCurrent()
        {
            var name = _store.UniqueName(_newProfileName.Length > 0 ? _newProfileName : CurrentProfile + " copy");
            SaveActiveProfile();
            var copy = CaptureLive(name, null);
            if (_store.Save(copy)) { LoadProfile(name); _profileMessage = $"Created \"{name}\"."; _newProfileName = ""; }
            else _profileMessage = "Could not create the profile.";
        }

        private void RenameCurrent()
        {
            var target = AltarProfileStore.SanitizeName(_newProfileName);
            if (target == null) { _profileMessage = "Type a new name first."; return; }
            if (_store.Exists(target)) { _profileMessage = $"A profile named \"{target}\" already exists."; return; }
            SaveActiveProfile();
            if (_store.Rename(CurrentProfile, target))
            {
                LoadProfile(target);
                _profileMessage = $"Renamed to \"{target}\".";
                _newProfileName = "";
            }
            else _profileMessage = "Rename failed.";
        }

        private void DeleteCurrentProfile()
        {
            var children = _store.Children(CurrentProfile);
            if (children.Count > 0)
            {
                _profileMessage = "Cannot delete: used as base by " + string.Join(", ", children) + ".";
                return;
            }
            var deleted = CurrentProfile;
            if (!_store.Delete(deleted)) { _profileMessage = "Delete failed."; return; }

            var remaining = _store.List();
            if (remaining.Count == 0)
            {
                // Always keep at least one profile: keep the live weights as a fresh "Default".
                CurrentProfile = "Default";
                _currentBase = null;
                SaveActiveProfile();
            }
            else LoadProfile(remaining[0]);
            _profileMessage = $"Deleted \"{deleted}\".";
        }

        private void ImportFromClipboard()
        {
            var imported = AltarProfile.FromJson(ImGui.GetClipboardTextS());
            if (imported == null) { _profileMessage = "Clipboard does not contain a valid profile."; return; }
            SaveActiveProfile();
            imported.BaseProfile = null; // imported profiles are always standalone
            imported.Name = _store.UniqueName(imported.Name);
            if (_store.Save(imported)) { LoadProfile(imported.Name); _profileMessage = $"Imported \"{imported.Name}\"."; }
            else _profileMessage = "Import failed.";
        }

        // ------------------------------------------------------------
        // Wizard UI
        // ------------------------------------------------------------
        private void DrawWizard()
        {
#pragma warning disable CA1416
            if (!ImGui.TreeNode("Setup wizard (answer questions -> new profile)")) return;

            ImGui.TextColored(new SN.Vector4(0.7f, 0.7f, 0.7f, 1f),
                $"Page {_wizardPage + 1} of {WizardLastPage + 1}. The result is created as a NEW profile; nothing is overwritten.");
            ImGui.Separator();

            var a = _wizard;
            switch (_wizardPage)
            {
                case 0:
                    Heading("What does your character deal damage with?");
                    Radio("Melee attacks", ref a.Damage, DamageKind.Melee); ImGui.SameLine();
                    Radio("Ranged attacks", ref a.Damage, DamageKind.Ranged); ImGui.SameLine();
                    Radio("Spells", ref a.Damage, DamageKind.Spell); ImGui.SameLine();
                    Radio("Minions", ref a.Damage, DamageKind.Minions);
                    Heading("Damage types you deal (tick all that matter)");
                    ImGui.Checkbox("Physical", ref a.DealsPhysical); ImGui.SameLine();
                    ImGui.Checkbox("Elemental", ref a.DealsElemental); ImGui.SameLine();
                    ImGui.Checkbox("Chaos", ref a.DealsChaos);
                    ImGui.Checkbox("My skills use projectiles", ref a.UsesProjectiles);
                    ImGui.Checkbox("I inflict non-damaging ailments (Maim, Intimidate, Hinder, ...)", ref a.InflictsNonDamagingAilments);
                    Heading("Curses");
                    Radio("I don't use curses", ref a.Curses, CurseStance.NoCurses); ImGui.SameLine();
                    Radio("I curse, reflection is harmless to me", ref a.Curses, CurseStance.CursesImmune); ImGui.SameLine();
                    Radio("I curse and reflection hurts", ref a.Curses, CurseStance.Curses);
                    break;
                case 1:
                    Heading("Main defence layer");
                    Radio("Armour", ref a.Defence, DefenceKind.Armour); ImGui.SameLine();
                    Radio("Evasion", ref a.Defence, DefenceKind.Evasion); ImGui.SameLine();
                    Radio("Energy Shield", ref a.Defence, DefenceKind.EnergyShield); ImGui.SameLine();
                    Radio("Mixed / hybrid", ref a.Defence, DefenceKind.Mixed);
                    Heading("Life pool (life + ES)");
                    Radio("under 4k##life", ref a.LifePool, Tier3.Low); ImGui.SameLine();
                    Radio("4k - 6k##life", ref a.LifePool, Tier3.Mid); ImGui.SameLine();
                    Radio("6k+##life", ref a.LifePool, Tier3.High);
                    Heading("Worst physical hit you can survive");
                    Radio("under 3k##hit", ref a.PhysMaxHit, Tier3.Low); ImGui.SameLine();
                    Radio("3k - 6k##hit", ref a.PhysMaxHit, Tier3.Mid); ImGui.SameLine();
                    Radio("6k+##hit", ref a.PhysMaxHit, Tier3.High);
                    ImGui.TextColored(new SN.Vector4(0.7f, 0.7f, 0.7f, 1f),
                        "A low life pool or max hit makes physical / defensive downsides count more.");
                    break;
                case 2:
                    Heading("Elemental resistances");
                    Radio("Overcapped (safe)", ref a.EleRes, ResState.Overcapped); ImGui.SameLine();
                    Radio("Exactly capped", ref a.EleRes, ResState.Capped); ImGui.SameLine();
                    Radio("Below cap", ref a.EleRes, ResState.Below);
                    Heading("Chaos resistance");
                    Radio("Positive", ref a.ChaosRes, ChaosResState.Positive); ImGui.SameLine();
                    Radio("About zero", ref a.ChaosRes, ChaosResState.Zero); ImGui.SameLine();
                    Radio("Negative", ref a.ChaosRes, ChaosResState.Negative);
                    Heading("Immunities (ground effects and ailments from enemies)");
                    ImGui.Checkbox("Freeze / Chill", ref a.ImmuneChill); ImGui.SameLine();
                    ImGui.Checkbox("Shock", ref a.ImmuneShock); ImGui.SameLine();
                    ImGui.Checkbox("Ignite / Burning", ref a.ImmuneIgnite);
                    break;
                case 3:
                    Heading("Flasks");
                    Radio("I rely on them", ref a.Flasks, FlaskUse.Heavy); ImGui.SameLine();
                    Radio("A few", ref a.Flasks, FlaskUse.Some); ImGui.SameLine();
                    Radio("None / irrelevant", ref a.Flasks, FlaskUse.None);
                    Heading("Charges you keep up");
                    ImGui.Checkbox("Endurance", ref a.EnduranceCharges); ImGui.SameLine();
                    ImGui.Checkbox("Frenzy", ref a.FrenzyCharges); ImGui.SameLine();
                    ImGui.Checkbox("Power", ref a.PowerCharges);
                    break;
                case 4:
                    Heading("Which rewards are worth it?");
                    ImGui.TextColored(new SN.Vector4(0.7f, 0.7f, 0.7f, 1f), "Tick any combination. Unticked rewards get weight 0.");
                    ImGui.Checkbox("Divine Orbs", ref a.RewardDivine); ImGui.SameLine();
                    ImGui.Checkbox("Eldritch currency (Chaos / Annul / Exalted, Ichors, Embers)", ref a.RewardEldritch);
                    ImGui.Checkbox("Threads", ref a.RewardThreads);
                    if (a.RewardThreads)
                    {
                        ImGui.Indent();
                        ImGui.Checkbox("Only valuable Threads##thr", ref a.ThreadsOnlyValuable);
                        if (a.ThreadsOnlyValuable)
                        {
                            ImGui.SetNextItemWidth(220);
                            ImGui.SliderFloat("min price (chaos)##thr", ref a.ThreadsMinChaos, 0.1f, 20f, "%.1f");
                            ImGui.SameLine();
                            ImGui.TextColored(PriceColor, "Now: " + PriceHint(RewardGroup.Thread, a.ThreadsMinChaos));
                        }
                        ImGui.Unindent();
                    }
                    ImGui.Checkbox("Basic currency (Annulment, Ancient, Gemcutter's, Exalted, ...)", ref a.RewardBasic);
                    if (a.RewardBasic)
                    {
                        ImGui.Indent();
                        ImGui.Checkbox("Only valuable currency##bas", ref a.BasicOnlyValuable);
                        if (a.BasicOnlyValuable)
                        {
                            ImGui.SetNextItemWidth(220);
                            ImGui.SliderFloat("min price (chaos)##bas", ref a.BasicMinChaos, 0.1f, 20f, "%.1f");
                            ImGui.SameLine();
                            ImGui.TextColored(PriceColor, "Now: " + PriceHint(RewardGroup.Basic, a.BasicMinChaos));
                        }
                        ImGui.Unindent();
                    }
                    ImGui.Checkbox("Duplication / quantity / rarity bonuses and extra Divination Cards", ref a.RewardUnpriced);
                    Heading("Preference");
                    Radio("Balanced", ref a.Preference, NodePreference.Balanced); ImGui.SameLine();
                    Radio("Prefer minion drops", ref a.Preference, NodePreference.Minions); ImGui.SameLine();
                    Radio("Prefer boss drops", ref a.Preference, NodePreference.Boss);
                    Heading("How careful should the overlay be?");
                    Radio("Strict - only clearly good altars", ref a.Risk, RiskAppetite.Strict);
                    Radio("Balanced", ref a.Risk, RiskAppetite.Balanced);
                    Radio("Greedy - take most positive altars", ref a.Risk, RiskAppetite.Greedy);
                    break;
                default:
                    DrawWizardPreview();
                    break;
            }

            ImGui.Separator();
            if (_wizardPage > 0 && ImGui.Button("< Back")) _wizardPage--;
            if (_wizardPage > 0) ImGui.SameLine();
            if (_wizardPage < WizardLastPage)
            {
                if (ImGui.Button("Next >"))
                {
                    _wizardPage++;
                    if (_wizardPage == WizardLastPage) RebuildWizardResult();
                }
            }

            ImGui.TreePop();
#pragma warning restore CA1416
        }

        private void RebuildWizardResult() =>
            _wizardResult = AltarWizard.Build(_wizard, AltarModsConstants.AltarTypes, BuildRewardTiers(_wizard));

        // How many items of a group pass the price threshold right now ("5 of 10 above 2.0c")
        private string PriceHint(RewardGroup group, float minChaos)
        {
            int total = 0, pass = 0;
            foreach (var r in PricedRewards.Where(r => r.Group == group))
            {
                total++;
                if (PriceOf(r.One, r.Chaos) >= minChaos) pass++;
            }
            return $"{pass} of {total} items >= {minChaos.ToString("0.0", CultureInfo.InvariantCulture)}c";
        }

        private void DrawWizardPreview()
        {
#pragma warning disable CA1416
            if (_wizardResult == null) RebuildWizardResult();
            var r = _wizardResult;

            Heading("Preview");
            int active = r.Changes.Count(c => c.Level != Danger.Ignore);
            int ignored = r.Changes.Count - active;
            ImGui.TextUnformatted($"{r.Tiers.Count(kv => kv.Value > 0) - ignored} reward weights, {active} penalties.");
            ImGui.TextUnformatted($"{ignored} downsides that do not matter for your build get a small bonus of +{r.HarmlessBonus} " +
                                  "each (only counted on options that also have a reward).");
            ImGui.TextUnformatted($"Min pick weight {r.MinPickWeight}, negative veto {r.NegativeVetoThreshold}, positive veto {r.PositiveVetoThreshold}, " +
                                  $"minion bonus {r.MinionWeight}, boss bonus {r.BossWeight}.");

            if (ImGui.BeginTable("V2WizardPreview", 3,
                    ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings,
                    new SN.Vector2(-1, 260)))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("Group", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("Mod", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Level / weight", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted("Group");
                ImGui.TableNextColumn(); ImGui.TextUnformatted("Mod");
                ImGui.TableNextColumn(); ImGui.TextUnformatted("Level / weight");

                foreach (var c in r.Changes.Where(c => c.Level != Danger.Ignore)
                             .OrderBy(c => c.Group).ThenByDescending(c => c.Level).ThenBy(c => c.Name))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(c.Group);
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(c.Name);
                    ImGui.TableNextColumn();
                    var col = c.Level >= Danger.Severe ? new SN.Vector4(1f, 0.4f, 0.35f, 1f)
                        : c.Level == Danger.Moderate ? new SN.Vector4(1f, 0.7f, 0.3f, 1f)
                        : new SN.Vector4(0.8f, 0.8f, 0.5f, 1f);
                    ImGui.TextColored(col, $"{c.Level} ({c.Weight})");
                }
                ImGui.EndTable();
            }

            ImGui.InputTextWithHint("##v2wizname", "Profile name", ref _wizardName, 60);
            ImGui.SameLine();
            if (ImGui.Button("Create profile from answers")) CreateProfileFromWizard();
            ImGui.SameLine();
            if (ImGui.Button("Refresh preview")) RebuildWizardResult();
#pragma warning restore CA1416
        }

        private void CreateProfileFromWizard()
        {
            RebuildWizardResult();
            var r = _wizardResult;
            var name = _store.UniqueName(_wizardName);

            var p = new AltarProfile
            {
                Name = name,
                BaseProfile = null,
                MinPickWeight = r.MinPickWeight,
                NegativeVetoThreshold = r.NegativeVetoThreshold,
                PositiveVetoThreshold = r.PositiveVetoThreshold,
                AlertMinNetWeight = r.AlertMinNetWeight,
                MinionWeight = r.MinionWeight,
                BossWeight = r.BossWeight,
                SignedDownsideWeights = r.SignedDownsideWeights,
            };
            foreach (var kv in r.Tiers) p.ModTiers[kv.Key] = kv.Value;
            foreach (var kv in ModAlerts) p.ModAlerts[kv.Key] = kv.Value; // keep the user's sound rules

            SaveActiveProfile();
            if (_store.Save(p))
            {
                LoadProfile(name);
                _profileMessage = $"Created and activated profile \"{name}\" from the wizard.";
            }
            else _profileMessage = "Could not save the new profile.";
        }
    }
}

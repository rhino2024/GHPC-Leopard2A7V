using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC.Mission;
using GHPC.Weaponry;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Puts the imported round in the ammunition dropdown of the mission customizer.
    ///
    /// That panel builds its rows from two addressable assets: the unit's base ammo list
    /// (UnitPrefabLookupScriptable.GetBaseAmmoClipLoadout) and the shared logistics sets
    /// (AmmoLogisticsSetScriptable.AllAmmoReplacementOrders), and it offers the player every option of a set
    /// that matches - which is why the AP row for this vehicle reads DM23 / DM13 and nothing else. DM53 is a
    /// round this mod authors at runtime, so the panel cannot see it: it only reaches the game through
    /// LoadoutManager's per-unit copy of the ammo list, which is what the in-mission rack UI reads. Hence
    /// "selectable after the mission has started, not before".
    ///
    /// The fix is a copy of the AP set carrying the vanilla options plus ours, handed to the panel in place of
    /// the vanilla row. A copy rather than the original because that original is the asset every real LEO2A3
    /// in the game builds its menu from, and an option there would offer this vehicle's shell to them too. The
    /// copy is cached: the panel re-derives its rows on every refresh and prunes any saved override whose set
    /// instance it has not just produced, so a new instance each time would silently discard the player's pick.
    /// </summary>
    public static class LpAmmoMenu
    {
        private const string Tag = "[L2A7V] ";

        /// <summary>Our round leads the row, so an untouched customization loads the 2A7V's own shell.</summary>
        private const int DefaultSlot = 0;

        private static FieldInfo _unitsField;
        private static AmmoLogisticsScriptable _row;
        private static AmmoLogisticsScriptable _copiedFrom;
        private static bool _announced;
        private static bool _warnedMissing;

        public static void Install(HarmonyLib.Harmony harmony)
        {
            Type panel = AccessTools.TypeByName("MissionConfigMenuPanel");
            if (panel == null)
            {
                MelonLogger.Warning(Tag + "the mission customization panel is not in this build, so the " +
                                    "imported round stays selectable in-mission only");
                return;
            }

            MethodInfo getter = AccessTools.Method(panel, "GetAmmoSetsByUnit");
            if (getter == null)
            {
                MelonLogger.Warning(Tag + "could not find the ammunition rows the mission customizer builds");
                return;
            }

            _unitsField = AccessTools.Field(panel, "_allUnitsScriptable");
            harmony.Patch(getter, postfix: new HarmonyMethod(typeof(LpAmmoMenu), nameof(AddOurRound)));
            MelonLogger.Msg(Tag + " hooked the mission customizer's ammunition rows");
        }

        private static void AddOurRound(object __instance, string unitKey,
            ref List<AmmoLogisticsScriptable> __result, ref List<int> preferredIndices)
        {
            if (!LpConfig.Enabled.Value || __instance == null || unitKey != LpConfig.UnitKey) return;

            try
            {
                AmmoLogisticsScriptable vanilla = FindApRow(__result);
                AmmoClipCodexScriptable ours = OurRound(__instance, vanilla);
                if (ours == null) return;

                AmmoLogisticsScriptable row = BuildRow(vanilla, ours);
                if (row == null) return;

                if (__result == null) __result = new List<AmmoLogisticsScriptable>();
                if (preferredIndices == null) preferredIndices = new List<int>();

                int slot = vanilla == null ? -1 : __result.IndexOf(vanilla);
                if (slot >= 0)
                {
                    __result[slot] = row;
                    if (slot < preferredIndices.Count) preferredIndices[slot] = DefaultSlot;
                    else preferredIndices.Add(DefaultSlot);
                }
                else
                {
                    __result.Insert(0, row);
                    preferredIndices.Insert(0, DefaultSlot);
                }

                if (_announced) return;
                _announced = true;
                var names = new List<string>();
                foreach (AmmoReplacementOrder.AmmoTypeLogistics option in row.AmmoLogistics.AmmoOptions)
                    names.Add(option.AmmoType.ClipType.Name);
                MelonLogger.Msg(Tag + "the mission customizer's AP row for " + unitKey + " now offers " +
                                string.Join(", ", names) + ", defaulting to " +
                                row.AmmoLogistics.AmmoOptions[DefaultSlot].AmmoType.ClipType.Name);
            }
            catch (Exception ex)
            {
                MelonLogger.Error(Tag + "the pre-mission ammunition row failed: " + ex);
            }
        }

        /// <summary>
        /// The row whose rounds are kinetic penetrators - the one the panel labels "AP Round" and the only one
        /// this vehicle's gun is fed from. Our own copy is skipped so a refresh cannot copy itself and hand the
        /// panel a second instance, which would drop the player's earlier choice.
        /// </summary>
        private static AmmoLogisticsScriptable FindApRow(List<AmmoLogisticsScriptable> sets)
        {
            if (sets == null) return null;

            foreach (AmmoLogisticsScriptable set in sets)
            {
                if (set == null || ReferenceEquals(set, _row)) continue;
                AmmoReplacementOrder.AmmoTypeLogistics[] options =
                    set.AmmoLogistics == null ? null : set.AmmoLogistics.AmmoOptions;
                if (options == null) continue;

                foreach (AmmoReplacementOrder.AmmoTypeLogistics option in options)
                {
                    AmmoType type = FirstAmmoType(option == null ? null : option.AmmoType);
                    if (type != null && type.Category == AmmoType.AmmoCategory.Penetrator) return set;
                }
            }
            return null;
        }

        private static AmmoClipCodexScriptable OurRound(object panel, AmmoLogisticsScriptable apRow)
        {
            AmmoClipCodexScriptable built = LpWeapons.RoundOrNull;
            if (built != null) return built;

            AmmoClipCodexScriptable seed = DonorApClip(panel, apRow);
            if (seed == null)
            {
                if (!_warnedMissing)
                {
                    _warnedMissing = true;
                    MelonLogger.Warning(Tag + "the donor's APFSDS clip is not loaded yet, so the imported " +
                                        "round cannot join the pre-mission list this time");
                }
                return null;
            }
            return LpWeapons.EnsureClip(seed);
        }

        /// <summary>
        /// A clip the imported round can be copied field for field from. The unit's own base ammo list is the
        /// honest source and the panel keeps it loaded while it is open; failing that, the AP row's own options
        /// name the same kind of shell.
        /// </summary>
        private static AmmoClipCodexScriptable DonorApClip(object panel, AmmoLogisticsScriptable apRow)
        {
            var lookup = _unitsField == null ? null : _unitsField.GetValue(panel) as UnitPrefabLookupScriptable;
            if (lookup != null)
            {
                var list = lookup.GetBaseAmmoClipLoadout(LpConfig.UnitKey).Asset as VehicleAmmoListScriptable;
                if (list != null && list.AmmoClips != null)
                {
                    int index = LpWeapons.FindSabotClip(list.AmmoClips);
                    if (index >= 0) return list.AmmoClips[index];
                }
            }

            AmmoReplacementOrder.AmmoTypeLogistics[] options =
                apRow == null || apRow.AmmoLogistics == null ? null : apRow.AmmoLogistics.AmmoOptions;
            if (options != null)
            {
                foreach (AmmoReplacementOrder.AmmoTypeLogistics option in options)
                {
                    AmmoClipCodexScriptable clip = option == null ? null : option.AmmoType;
                    AmmoType type = FirstAmmoType(clip);
                    if (clip != null && type != null && type.Category == AmmoType.AmmoCategory.Penetrator &&
                        type.ShortName == AmmoType.AmmoShortName.Sabot) return clip;
                }
            }
            return null;
        }

        /// <summary>
        /// Our row, built once per vanilla row it is copied from. The vanilla option entries are reused as they
        /// are, so their prevalence, logistics curves and campaign gating keep coming from the game's data.
        /// </summary>
        private static AmmoLogisticsScriptable BuildRow(AmmoLogisticsScriptable vanilla, AmmoClipCodexScriptable ours)
        {
            if (_row != null && ReferenceEquals(_copiedFrom, vanilla)) return _row;
            if (vanilla == null && _row != null) return _row;

            var options = new List<AmmoReplacementOrder.AmmoTypeLogistics> { Entry(ours) };

            AmmoReplacementOrder.AmmoTypeLogistics[] vanillaOptions =
                vanilla == null || vanilla.AmmoLogistics == null ? null : vanilla.AmmoLogistics.AmmoOptions;
            if (vanillaOptions != null)
            {
                foreach (AmmoReplacementOrder.AmmoTypeLogistics option in vanillaOptions)
                {
                    if (option == null || option.AmmoType == null || option.AmmoType == ours) continue;
                    options.Add(option);
                }
            }
            else
            {
                // No AP row at all, so the donor's own shell is put next to ours rather than dropped: the row
                // replaces the panel's AP entry, and an entry that offers one round would take a choice away.
                AmmoClipCodexScriptable donor = LpWeapons.DonorApClip;
                if (donor != null && donor != ours) options.Add(Entry(donor));
            }

            var row = ScriptableObject.CreateInstance<AmmoLogisticsScriptable>();
            row.name = "L2A7V_120mm_AP_row";
            row.hideFlags = HideFlags.DontUnloadUnusedAsset;
            row.AmmoLogistics = new AmmoReplacementOrder
            {
                Name = "2A7V 120 mm AP",
                AmmoOptions = options.ToArray(),
            };

            _row = row;
            _copiedFrom = vanilla;
            _announced = false;
            return row;
        }

        private static AmmoReplacementOrder.AmmoTypeLogistics Entry(AmmoClipCodexScriptable clip)
        {
            return new AmmoReplacementOrder.AmmoTypeLogistics
            {
                AmmoType = clip,
                Prevalence = 1f,
                LogisticsModifierCurve = AnimationCurve.Constant(0f, 1f, 1f),
                AllowInCampaign = true,
            };
        }

        private static AmmoType FirstAmmoType(AmmoClipCodexScriptable clip)
        {
            if (clip == null || clip.ClipType == null || clip.ClipType.MinimalPattern == null ||
                clip.ClipType.MinimalPattern.Length == 0 || clip.ClipType.MinimalPattern[0] == null)
                return null;
            return clip.ClipType.MinimalPattern[0].AmmoType;
        }
    }
}

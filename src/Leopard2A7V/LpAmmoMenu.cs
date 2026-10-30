using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC.Mission;
using HarmonyLib;
using MelonLoader;

namespace Leopard2A7V
{
    /// <summary>
    /// Takes the ammunition choice out of the mission customiser for this vehicle.
    ///
    /// That panel does not know about this mod's rounds. It builds one row per shared logistics set by
    /// matching the set's clips against the unit's base ammo list asset, and that asset is the donor's -
    /// DM23 and DM13 for the Leopard, 3BM42 and 3BK29M for the T-80B. A round this mod authors at runtime
    /// cannot be in it, so the row can only ever offer the donor's shells next to ours, and for the 99A it
    /// offers nothing but Soviet stock under a Chinese vehicle's name.
    ///
    /// Emptying the unit's sets is enough: the panel writes an override only for sets it was handed, so
    /// this vehicle ends up with none and spawns on the ammunition list its own loadout composes - which
    /// for this vehicle opens on the DM53 LpWeapons prepends, with the donor's DM23 and DM12 behind it, and
    /// for the 99A the Chinese rounds it swaps in slot for slot. Rounds stay selectable in-mission through
    /// the rack's own transfer UI.
    /// </summary>
    public static class LpAmmoMenu
    {
        private const string Tag = "[L2A7V] ";

        private static bool _announced;

        public static void Install(HarmonyLib.Harmony harmony)
        {
            Type panel = AccessTools.TypeByName("MissionConfigMenuPanel");
            if (panel == null)
            {
                MelonLogger.Warning(Tag + "the mission customiser panel is not in this build, so only its own " +
                                    "ammunition rows, if any, will show for this vehicle");
                return;
            }

            MethodInfo getter = AccessTools.Method(panel, "GetAmmoSetsByUnit");
            if (getter == null)
            {
                MelonLogger.Warning(Tag + "could not find the ammunition rows the mission customiser builds");
                return;
            }

            harmony.Patch(getter, postfix: new HarmonyMethod(typeof(LpAmmoMenu), nameof(DropRows)));
            MelonLogger.Msg(Tag + "hooked the mission customiser's ammunition rows");
        }

        private static void DropRows(string unitKey, ref List<AmmoLogisticsScriptable> __result,
            ref List<int> preferredIndices)
        {
            if (!LpConfig.Enabled.Value || unitKey != LpConfig.UnitKey) return;
            if (__result == null || __result.Count == 0) return;

            int dropped = __result.Count;
            __result = new List<AmmoLogisticsScriptable>();
            preferredIndices = new List<int>();

            if (_announced) return;
            _announced = true;
            MelonLogger.Msg(Tag + "the mission customiser now offers no ammunition choice for " + unitKey +
                            " (" + dropped + " donor row(s) hidden); it loads its own list");
        }
    }
}

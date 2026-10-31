using System.Collections.Generic;
using System.Reflection;
using GHPC.Mission;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Adds a LEO2A7V entry to the vanilla unit prefab lookup by cloning the donor Leopard's record, so the
    /// new tank is selectable in the mission customiser while every gameplay-side reference (ammo clips,
    /// army, class, the LEO2A3 crew and its armour layout) is the donor's own.
    /// </summary>
    public static class LpUnitRegistry
    {
        private static readonly HashSet<int> _sourceMissingWarned = new HashSet<int>();

        public static void RegisterEverywhere()
        {
            var lookups = Resources.FindObjectsOfTypeAll<UnitPrefabLookupScriptable>();
            int count = 0;
            foreach (UnitPrefabLookupScriptable lookup in lookups)
                if (Register(lookup)) count++;
            if (count > 0)
            {
                MelonLogger.Msg($"[L2A7V] registered unit key '{LpConfig.UnitKey}' in {count} lookup asset(s)");
                ClearMissionConfigCaches();
            }
        }

        public static bool Register(UnitPrefabLookupScriptable lookup)
        {
            if (lookup == null || lookup.AllUnits == null) return false;

            string sourceKey = LpConfig.SourceUnitKey;
            UnitPrefabLookupScriptable.UnitPrefabMetadata source = null;
            bool alreadyRegistered = false;

            foreach (UnitPrefabLookupScriptable.UnitPrefabMetadata entry in lookup.AllUnits)
            {
                if (entry == null) continue;
                if (entry.Name == LpConfig.UnitKey) alreadyRegistered = true;
                else if (entry.Name == sourceKey) source = entry;
            }

            if (alreadyRegistered) return false;
            if (source == null)
            {
                if (_sourceMissingWarned.Add(lookup.GetInstanceID()))
                {
                    var names = new List<string>();
                    foreach (UnitPrefabLookupScriptable.UnitPrefabMetadata entry in lookup.AllUnits)
                        if (entry != null) names.Add(entry.Name);
                    MelonLogger.Warning("[L2A7V] source unit '" + sourceKey + "' is not in this lookup asset (" +
                                        names.Count + " units): " + string.Join(", ", names));
                }
                return false;
            }

            var clone = new UnitPrefabLookupScriptable.UnitPrefabMetadata
            {
                Name = LpConfig.UnitKey,
                FriendlyName = LpConfig.FriendlyName,
                PrefabReference = source.PrefabReference,
                BaseAmmoClipsReference = source.BaseAmmoClipsReference,
                // The donor's decal layout is UV-baked for its own hull, so the imported art gets none.
                UseDecalLayout = false,
                DecalLayout = source.DecalLayout,
                Army = source.Army,
                Class = source.Class,
                AlternativeClasses = source.AlternativeClasses,
                AllowInCustomizer = true
            };

            var units = new List<UnitPrefabLookupScriptable.UnitPrefabMetadata>(lookup.AllUnits) { clone };
            lookup.AllUnits = units.ToArray();

            // The spawner caches the lookup contents on first use, so any existing cache must go.
            ClearSpawnerCache(lookup);
            return true;
        }

        private static void ClearSpawnerCache(UnitPrefabLookupScriptable lookup)
        {
            foreach (UnitSpawner spawner in Object.FindObjectsOfType<UnitSpawner>())
            {
                if (spawner.PrefabLookup != lookup) continue;
                FieldInfo cache = AccessTools.Field(typeof(UnitSpawner), "_prefabLookupCached");
                if (cache != null) cache.SetValue(spawner, null);
            }
        }

        private static void ClearMissionConfigCaches()
        {
            System.Type panelType = AccessTools.TypeByName("MissionConfigMenuPanel");
            if (panelType == null) return;
            FieldInfo field = AccessTools.Field(panelType, "_unitKeysByClassCached");
            if (field == null) return;
            var dictionary = field.GetValue(null) as System.Collections.IDictionary;
            if (dictionary == null) return;
            dictionary.Clear();
            MelonLogger.Msg("[L2A7V] cleared the mission customization unit key cache");
        }
    }
}

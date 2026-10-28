using System;
using System.Reflection;
using GHPC;
using GHPC.Mission;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Hooks the point where a unit is instantiated so the imported look is put on it before the player
    /// first sees it, and the point where the mission customiser builds its unit list so LEO2A7V appears.
    /// </summary>
    public static class LpPatches
    {
        private static readonly FieldInfo UnitFriendlyName = AccessTools.Field(typeof(Unit), "_friendlyName");
        private static bool _installed;

        public static void Install(HarmonyLib.Harmony harmony)
        {
            if (_installed) return;
            _installed = true;

            MethodInfo spawnerAwake = AccessTools.Method(typeof(UnitSpawner), "Awake");
            if (spawnerAwake != null)
            {
                harmony.Patch(spawnerAwake,
                    postfix: new HarmonyMethod(typeof(LpPatches), nameof(SpawnerAwakePostfix)));
            }

            MethodInfo stringSpawn = FindStringSpawnMethod();
            if (stringSpawn != null)
            {
                harmony.Patch(stringSpawn,
                    prefix: new HarmonyMethod(typeof(LpPatches), nameof(SpawnUnitPrefix)),
                    postfix: new HarmonyMethod(typeof(LpPatches), nameof(SpawnUnitPostfix)));
            }
            else
            {
                MelonLogger.Error("[L2A7V] could not locate UnitSpawner.SpawnUnit(string, ...) to hook");
            }

            Type panelType = AccessTools.TypeByName("MissionConfigMenuPanel");
            MethodInfo panelOnEnable = panelType == null ? null : AccessTools.Method(panelType, "OnEnable");
            if (panelOnEnable != null)
            {
                harmony.Patch(panelOnEnable,
                    prefix: new HarmonyMethod(typeof(LpPatches), nameof(MissionPanelOnEnablePrefix)));
                MelonLogger.Msg("[L2A7V] hooked the mission customization panel");
            }
            else
            {
                MelonLogger.Warning("[L2A7V] mission customization panel hook target not found");
            }

            LpWeapons.Install(harmony);
            LpAmmoMenu.Install(harmony);
        }

        private static MethodInfo FindStringSpawnMethod()
        {
            MethodInfo[] methods = typeof(UnitSpawner).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (MethodInfo method in methods)
            {
                if (method.Name != "SpawnUnit") continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 4 && parameters[0].ParameterType == typeof(string)) return method;
            }
            return null;
        }

        public static void SpawnerAwakePostfix(UnitSpawner __instance)
        {
            if (__instance != null && LpUnitRegistry.Register(__instance.PrefabLookup))
                MelonLogger.Msg("[L2A7V] registered unit key '" + LpConfig.UnitKey + "' for the mission spawner");
        }

        public static void MissionPanelOnEnablePrefix()
        {
            LpUnitRegistry.RegisterEverywhere();
        }

        /// <summary>
        /// Serves our unit from the pre-converted template. Returning true hands the spawn back to the game,
        /// which then produces a plain donor unit that the postfix converts in place - the fallback path, kept
        /// for the first spawn of a session and for a template that could not be built at all.
        /// </summary>
        public static bool SpawnUnitPrefix(UnitSpawner __instance, string uniqueName,
            GHPC.Mission.Data.UnitMetaData metaData,
            GHPC.AI.WaypointHolder startingWaypoints, Transform parentTransform, ref IUnit __result)
        {
            if (!LpConfig.Enabled.Value || __instance == null) return true;

            bool isCustomUnit = uniqueName == LpConfig.UnitKey;
            bool isSourceUnit = LpConfig.ConvertSourceUnit.Value && uniqueName == LpConfig.SourceUnitKey.Value;
            if (!isCustomUnit && !isSourceUnit) return true;

            if (!LpTemplate.Ensure(__instance)) return true;

            __result = __instance.SpawnUnit(LpTemplate.Current, metaData, startingWaypoints, parentTransform);
            if (__result == null)
            {
                MelonLogger.Error("[L2A7V] the spawner refused the converted template");
                return true;
            }

            // The template is parked inactive so it cannot show up in the world, so its copy arrives inactive
            // and no Awake or OnEnable has run yet. Switching it on is what a vanilla spawn does anyway, and
            // Unit.OnEnable is the very thing that calls OnSpawn.
            var spawned = __result as Component;
            if (spawned != null && !spawned.gameObject.activeSelf) spawned.gameObject.SetActive(true);
            return false;
        }

        public static void SpawnUnitPostfix(string uniqueName, IUnit __result)
        {
            if (!LpConfig.Enabled.Value || __result == null) return;

            bool isCustomUnit = uniqueName == LpConfig.UnitKey;
            bool isSourceUnit = LpConfig.ConvertSourceUnit.Value && uniqueName == LpConfig.SourceUnitKey.Value;
            if (!isCustomUnit && !isSourceUnit) return;

            var component = __result as Component;
            if (component == null) return;
            GameObject root = component.gameObject;

            // The loadout fills its racks in Start, which has not run yet, so the clip swapped in here is
            // the one the racks are built from rather than a correction applied afterwards.
            LpWeapons.Apply(root);
            LpWeapons.ApplyGunArc(root);
            LpReticle.Apply(root.transform);

            if (root.GetComponent<LpConvertBehaviour>() != null) return;

            // The record cloned into the lookup carries the display name, but a scenario that spawns the
            // donor key directly, or an older save, shows the field on the Unit component instead.
            if (UnitFriendlyName != null && isCustomUnit)
                UnitFriendlyName.SetValue(__result, LpConfig.FriendlyName.Value);

            LpArmorLog.Apply(root);
            root.AddComponent<LpConvertBehaviour>();
        }
    }

    /// <summary>
    /// Retries the visual swap until the background model parse and the donor's own Awake pass are both
    /// finished; the rig bones exist as soon as the prefab is instantiated, but the template material is
    /// only reachable once its renderers are.
    /// </summary>
    public class LpConvertBehaviour : MonoBehaviour
    {
        private const float TimeoutSeconds = 60f;
        private float _elapsed;
        private bool _loggedWaiting;

        private void Update()
        {
            if (LpVisual.Convert(gameObject) != null)
            {
                Destroy(this);
                return;
            }

            if (ObjModel.Failed)
            {
                MelonLogger.Error("[L2A7V] giving up on the visual swap: " + ObjModel.Error);
                Destroy(this);
                return;
            }
            if (FindDeep(transform, LpConfig.VisualRootName) != null)
            {
                Destroy(this);
                return;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed > TimeoutSeconds)
            {
                MelonLogger.Error("[L2A7V] visual swap timed out, the donor look is still in place");
                Destroy(this);
                return;
            }
            if (!_loggedWaiting && _elapsed > 1f)
            {
                _loggedWaiting = true;
                MelonLogger.Msg("[L2A7V] waiting for the model to finish parsing");
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform match = FindDeep(child, name);
                if (match != null) return match;
            }
            return null;
        }
    }
}

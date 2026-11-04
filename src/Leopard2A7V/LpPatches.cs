using System;
using System.Reflection;
using GHPC;
using GHPC.Player;
using GHPC.Mission;
using GHPC.Mission.Data;
using GHPC.Weapons;
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

        /// <summary>
        /// Puts the 2A7V in place of the enemy's tanks, in every mission.
        ///
        /// <summary>Hooks the spawn path and the mission menu so a scenario can offer the 2A7V.</summary>
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
            LpArmor.Install(harmony);
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
            if (__instance == null) return true;

            bool isCustomUnit = uniqueName == LpConfig.UnitKey;
            bool isSourceUnit = LpConfig.ConvertSourceUnit && uniqueName == LpConfig.SourceUnitKey;
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
            if (__result == null) return;

            bool isCustomUnit = uniqueName == LpConfig.UnitKey;
            bool isSourceUnit = LpConfig.ConvertSourceUnit && uniqueName == LpConfig.SourceUnitKey;
            if (!isCustomUnit && !isSourceUnit) return;

            var component = __result as Component;
            if (component == null) return;
            GameObject root = component.gameObject;

            // This is a Harmony postfix on the game's own spawn call: anything thrown here unwinds into the
            // spawner's caller and takes the rest of the mission's set-up with it. One such throw - a game
            // getter called too early - was enough to produce "No units in platoon: 1st Plt", "no default
            // unit determined" and a mission that could not be entered. So each step is isolated: a broken
            // step costs that step, never the spawn.
            Guard("weapons", root, () => LpWeapons.Apply(root));
            Guard("gun arc", root, () => LpWeapons.ApplyGunArc(root));
            Guard("reticle", root, () => LpReticle.Apply(root.transform));
            Guard("commander's sight", root, () => LpCommanderSight.Apply(root));

            if (root.GetComponent<LpConvertBehaviour>() != null) return;

            // The record cloned into the lookup carries the display name, but a scenario that spawns the
            // donor key directly, or an older save, shows the field on the Unit component instead.
            if (UnitFriendlyName != null && isCustomUnit)
                UnitFriendlyName.SetValue(__result, LpConfig.FriendlyName);

            Guard("donor art watch", root, () => LpDonorArt.Request(root));
            Guard("donor protection log", root, () => LpArmorLog.Apply(root));
            Guard("modules", root, () => LpModulesOwn.Build(root));
            Guard("hatches", root, () => LpHatches.Bind(root));
            Guard("smoke", root, () => LpSmoke.Apply(root));
            // The pivots only have the artwork under them after the visual pass, so moving an axis here
            // would just offset a turret that is about to be parented. It runs from Convert instead.
            Guard("conversion marker", root, () => root.AddComponent<LpConvertBehaviour>());
        }

        private static void Guard(string step, GameObject root, System.Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception error)
            {
                MelonLogger.Warning("[L2A7V] " + step + " failed on '" + (root == null ? "?" : root.name) +
                                    "'; the rest of the spawn carries on: " + error);
            }
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
        private const float RetrySeconds = 0.25f;
        private float _elapsed;
        private float _nextAttempt;
        private bool _loggedWaiting;

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (Time.unscaledTime < _nextAttempt) return;
            _nextAttempt = Time.unscaledTime + RetrySeconds;

            // Convert is a few hundred mesh and material writes; asking it once a frame while the model is
            // still parsing stacks that work into every single frame of the loading screen, which is what a
            // frozen picture looks like even though nothing is waiting on anything.
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
            if (!ObjModel.Loaded) return;
            // The x-ray export is where the compartment and the added armour arrays come from, and it is the
            // bigger of the two parses. Converting before it lands would build a vehicle with no interior and
            // no arrays, and nothing retries that later - so the swap waits for it as well.
            if (!LpXrayParts.Loaded && !LpXrayParts.Failed) return;

            LpBaseRig.SnapshotDonor(transform);   // before our art is added, so the guard can tell them apart
            if (LpVisual.Convert(gameObject) != null)
            {
                Destroy(this);
                return;
            }

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


using GHPC;
using GHPC.Mission;
using HarmonyLib;
using MelonLoader;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// The unit this mod spawns is a private, already-converted clone of the donor prefab, so a Leopard 2A7V
    /// is its own object graph instead of a live LEO2A3 with art bolted on after the game finished with it.
    ///
    /// That distinction is what removes a whole class of faults. Per-spawn conversion meant the donor's own
    /// passes (LOD, crew stand-in, thermal, damage states) had already cached and switched on their renderers
    /// before we got there, so the mod had to fight them every frame - and a fight lost on one renderer shows
    /// as a panel of the wrong shade. Here the conversion runs once on a clone that no game system has ever
    /// seen: the donor's exterior geometry is taken away (static meshes lose their MeshFilter mesh, skinned
    /// meshes are swapped for a copy whose index buffers are empty, and neither can be restored by a pass that
    /// only toggles renderer.enabled), our art is part of the object graph, and the game's spawner
    /// then does for our unit exactly what it does for a vanilla one - Object.Instantiate plus OnSpawn.
    ///
    /// The chassis itself stays the donor's, and that is a hard limit rather than a shortcut: GHPC resolves a
    /// unit through an Addressables AssetReference in UnitPrefabLookupScriptable, so a unit that was never
    /// authored into the game's addressable content cannot be registered at all. What can be ours - all of the
    /// art, the running gear skin, the armour layers, the interior fittings and the sight data - is built here
    /// from the imported model; what stays is the movement, camera and hit-resolution plumbing the game needs
    /// to have authored against a prefab.
    /// </summary>
    public static class LpTemplate
    {
        /// <summary>The converted clone every LEO2A7V spawn is instantiated from.</summary>
        public static GameObject Current { get; private set; }

        private static readonly FieldInfo UniqueNameField = AccessTools.Field(typeof(Unit), "_uniqueName");
        private static readonly FieldInfo FriendlyNameField = AccessTools.Field(typeof(Unit), "_friendlyName");

        private static GameObject _host;
        private static bool _gaveUp;
        private static bool _loggedWaitingMaterial;

        /// <summary>
        /// Builds the template once. False means "not now" and the caller falls back to converting a unit in
        /// place, which still looks right but keeps the per-frame donor-visibility fight.
        /// </summary>
        public static bool Ensure(UnitSpawner spawner)
        {
            if (Current != null) return true;
            if (_gaveUp || spawner == null) return false;

            if (!ObjModel.Loaded)
            {
                if (ObjModel.Failed)
                {
                    MelonLogger.Error("[L2A7V] model parse failed: " + ObjModel.Error);
                    _gaveUp = true;
                }
                return false;
            }

            // The donor prefab on its own is not enough to convert: its painted vehicle material only becomes
            // reachable once the game has run its own Awake pass over a live instance, and that material is
            // what every imported mesh is cloned from. So the first spawn converts in place and learns it,
            // and from the next spawn on this template - which no game system has ever touched - is used.
            if (LpVisual.TemplateMaterial == null)
            {
                if (!_loggedWaitingMaterial)
                {
                    _loggedWaitingMaterial = true;
                    MelonLogger.Msg("[L2A7V] no shader template yet; this spawn converts in place, " +
                                    "later ones will use the converted clone");
                }
                return false;
            }

            GameObject donor = LoadDonorPrefab(spawner);
            if (donor == null)
            {
                MelonLogger.Error("[L2A7V] the donor prefab could not be loaded, so no template can be built");
                _gaveUp = true;
                return false;
            }

            _host = new GameObject("L2A7V_TemplateHost");
            Object.DontDestroyOnLoad(_host);

            GameObject clone = Object.Instantiate(donor, _host.transform);
            clone.name = LpConfig.UnitKey + " [template]";

            if (UniqueNameField != null) UniqueNameField.SetValue(clone.GetComponent<Unit>(), LpConfig.UnitKey);
            if (FriendlyNameField != null)
                FriendlyNameField.SetValue(clone.GetComponent<Unit>(), LpConfig.FriendlyName.Value);

            Stopwatch clock = Stopwatch.StartNew();
            LpVisual.Assembly art = LpVisual.Convert(clone);
            clock.Stop();
            if (art == null)
            {
                MelonLogger.Error("[L2A7V] template conversion failed, spawning stays on the fallback path");
                DestroyHost();
                _gaveUp = true;
                return false;
            }

            clone.SetActive(false);
            Current = clone;
            MelonLogger.Msg($"[L2A7V] unit template ready in {clock.ElapsedMilliseconds} ms: own object graph, " +
                            $"hull {art.Hull}, turret {art.Turret}, gun {art.Gun}");
            return true;
        }

        private static GameObject LoadDonorPrefab(UnitSpawner spawner)
        {
            string sourceKey = LpConfig.SourceUnitKey.Value;
            UnityEngine.AddressableAssets.AssetReference reference = spawner.GetPrefabByUniqueName(sourceKey);
            if (reference == null)
            {
                MelonLogger.Error("[L2A7V] the donor unit '" + sourceKey + "' has no prefab reference in this spawner");
                return null;
            }
            return reference.LoadAssetAsync<GameObject>().WaitForCompletion();
        }

        private static void DestroyHost()
        {
            if (_host == null) return;
            Object.Destroy(_host);
            _host = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using GHPC;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Loads the lids from their own asset bundle.
    ///
    /// The runtime OBJ pipeline merges every turret part into a single mesh, which is fine for the hull and the
    /// turret but fatal for a lid: a merged lump cannot hinge. The lids are therefore baked into prefabs by the
    /// Unity project (Tools → GHPC Armour → Build Hatch Bundle) and instantiated here, one object each, at the
    /// same anchor the turret art uses. Without the bundle the pipeline keeps merging them and the lids simply
    /// stay shut, which is what happened until now.
    /// </summary>
    public static class LpHatchBundle
    {
        private const string Tag = "[L2A7V][hatch-bundle]";
        private const string BundleFile = "l2a7v_hatches.bundle";
        private const string BundleName = "l2a7v_hatches";

        private static AssetBundle _bundle;
        private static bool _tried;

        internal static bool Ready
        {
            get
            {
                Load();
                return _bundle != null;
            }
        }

        private static void Load()
        {
            if (_tried) return;
            _tried = true;

            string path = Path.Combine(LpXrayArmor.ModFolder, BundleFile);
            if (!File.Exists(path))
            {
                MelonLogger.Msg(Tag + " no " + BundleFile + "; lids stay part of the turret mesh");
                return;
            }

            try
            {
                _bundle = AssetBundle.LoadFromFile(path);
            }
            catch (Exception error)
            {
                MelonLogger.Warning(Tag + " could not load " + BundleFile + ": " + error.Message);
                return;
            }

            if (_bundle == null)
            {
                MelonLogger.Warning(Tag + " " + BundleFile + " did not load as a bundle");
                return;
            }

            MelonLogger.Msg(Tag + " loaded " + BundleFile + " (" + _bundle.GetAllAssetNames().Length +
                            " asset(s))");
        }

        /// <summary>
        /// Instantiates every lid under the turret pivot, in the anchor frame the turret art uses.
        /// Returns the number of lids placed.
        /// </summary>
        internal static int Spawn(Transform turretPivot, Vector3 anchor, Material[] artMaterials)
        {
            if (!Ready || turretPivot == null) return 0;

            int placed = 0;
            foreach (string assetName in _bundle.GetAllAssetNames())
            {
                if (!assetName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                string lid = Path.GetFileNameWithoutExtension(assetName);
                if (!lid.StartsWith("hatch_", StringComparison.OrdinalIgnoreCase)) continue;
                if (turretPivot.Find(lid) != null) continue;

                GameObject prefab = null;
                try { prefab = _bundle.LoadAsset<GameObject>(assetName); }
                catch (Exception) { }
                if (prefab == null) continue;

                var instance = UnityEngine.Object.Instantiate(prefab, turretPivot, false);
                instance.name = lid;
                instance.transform.localPosition = anchor;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                int renderers = 0;
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    renderers++;
                    // The prefab carries no materials: it is geometry only, and an untextured renderer draws
                    // as a white block. The lid is cut from the same atlas as the turret, so the turret's own
                    // materials are the right ones.
                    if (artMaterials != null && artMaterials.Length > 0)
                    {
                        var owned = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                        for (int i = 0; i < owned.Length; i++)
                            owned[i] = artMaterials[Mathf.Min(i, artMaterials.Length - 1)];
                        renderer.sharedMaterials = owned;
                    }
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                if (renderers == 0) MelonLogger.Warning(Tag + " lid '" + lid + "' has no renderer");
                else if (placed >= 0) MelonLogger.Msg(Tag + " lid '" + lid + "' spawned (" + renderers + " renderer(s))");
                placed++;
            }

            MelonLogger.Msg(Tag + " placed " + placed + " lid(s) on the turret");
            return placed;
        }
    }
}

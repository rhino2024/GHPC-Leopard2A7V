using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Takes the donor's own compartment, crew figures and rack visuals out of the after-action picture while
    /// this mod's imported compartment is drawn - the Leopard 2A7HU mod's rule, ported.
    ///
    /// The donor interior is kept for normal play (crew stations, sight tubes, armour collider art). The AAR
    /// camera, however, draws it together with the imported War Thunder compartment, which reads as two
    /// interiors in one vehicle. The trigger is <c>AarController.InAar</c> rather than the x-ray toggle,
    /// because the imported compartment's AarVisual gate is the AAR camera being up, not the O key; gating on
    /// the x-ray toggle left the donor set visible exactly when ours was on screen.
    ///
    /// Only renderers are switched off. Colliders, hitzones, rack data and crew logic are untouched and come
    /// back the moment the report is left.
    /// </summary>
    public static class LpDonorInterior
    {
        public static void Attach(GameObject unitRoot)
        {
            if (unitRoot == null) return;
            if (unitRoot.GetComponent<LpDonorInteriorHider>() != null) return;
            unitRoot.AddComponent<LpDonorInteriorHider>();
        }
    }

    public class LpDonorInteriorHider : MonoBehaviour
    {
        private Renderer[] _renderers = new Renderer[0];
        private GHPC.AarVisual[] _visuals = new GHPC.AarVisual[0];
        private bool _hidden;

        private void Awake()
        {
            Collect();
            MelonLogger.Msg("[L2A7V] donor AAR renderers watched: " + _renderers.Length);
        }

        /// <summary>
        /// Re-asserted every frame while the report is up, not toggled once.
        ///
        /// AarController.HandleVehiclePose writes AarVisual.Hidden from the shot's own state every time it
        /// poses the vehicle, so a one-shot write is undone on the next frame and the donor compartment comes
        /// back beside the imported one - which is exactly "the x-ray shows the game's own interior". The list
        /// is rebuilt on entry as well, because the donor's interior art can arrive after the conversion.
        /// </summary>
        private void LateUpdate()
        {
            bool hide = GHPC.AarController.InAar;
            if (hide && !_hidden) Collect();
            _hidden = hide;
            if (!hide) return;

            for (int i = 0; i < _visuals.Length; i++)
                if (_visuals[i] != null && !_visuals[i].Hidden) _visuals[i].Hidden = true;
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null && _renderers[i].enabled) _renderers[i].enabled = false;
        }

        /// <summary>
        /// Everything the donor owns, which is the L2A7HU mod's rule: in the after-action picture the donor
        /// should contribute nothing at all, because every part of the vehicle the report shows is ours now.
        /// </summary>
        /// <remarks>
        /// An earlier revision narrowed this to the donor's interior paths and the "two interiors" report
        /// survived - the donor's own x-ray armour visuals and its rack decorations carry AarVisual components
        /// too and were not matched by those names. The narrow list also had to leave the racks alone so the
        /// rounds stayed visible; with the full list the imported compartment's own int_ammo is what draws
        /// them, which is the same division of labour the 2A7HU mod uses.
        /// </remarks>
        private void Collect()
        {
            var list = new List<Renderer>();
            var visuals = new List<GHPC.AarVisual>();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer is ParticleSystemRenderer) continue;
                if (LpVisual.IsImportedArt(renderer.transform)) continue;
                if (renderer.gameObject.layer == LpBaseRig.ImprintLayer) continue;
                list.Add(renderer);

                // The game's own switch for "not in this picture". Setting it matters more than the renderer
                // flag: AarVisual.UpdateVisibility re-derives the flag from Hidden, so a hidden visual cannot
                // be switched back on by the visibility pass.
                GHPC.AarVisual visual = renderer.GetComponentInParent<GHPC.AarVisual>();
                if (visual == null) visual = renderer.GetComponentInChildren<GHPC.AarVisual>();
                if (visual != null && !visuals.Contains(visual)) visuals.Add(visual);
            }
            _renderers = list.ToArray();
            _visuals = visuals.ToArray();
        }

        /// <summary>
        /// The donor's compartment shell and its soldiers, and nothing else.
        ///
        /// The ammunition racks are deliberately left alone: they are what draws the rounds inside the
        /// vehicle, and hiding them along with the compartment leaves the x-ray view with no shells in it at
        /// all unless the imported compartment's own int_ammo happens to cover every rack position. Two sets
        /// of rounds in the same racks is a far smaller fault than an empty vehicle.
        /// </summary>
        private static bool IsDonorCompartment(Renderer renderer)
        {
            string path = LpBaseRig.PathOf(renderer.transform).ToLowerInvariant();
            if (path.Contains("leo2_int") || path.Contains("m1ip_interior")) return true;
            if (path.Contains("soldier_")) return true;

            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null) continue;
                string name = material.name.ToLowerInvariant();
                if (name.Contains("leo2_int") || name.Contains("m1ip_interior")) return true;
            }
            return false;
        }
    }
}

using GHPC.Camera;
using GHPC.Equipment.Optics;
using Reticle;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Drives the imported plate's lit slot from the game's own illumination key.
    ///
    /// The game sets a reticle's NightIllumination light once, when the scene turns to night, and the I key in
    /// PlayerControl only flips the digital reticle's tint - a plate drawn with that light type has nothing
    /// pushing it during the day, so the 2A7V's graticule stays the black glass etching whatever the key does.
    /// This mirrors <see cref="PlayerControl.ReticleIlluminated"/> into the mesh's own light state instead,
    /// which is the same value the vanilla night handling writes, so night behaviour is untouched: it only
    /// acts when the key actually changes, never re-asserting on the frames in between.
    /// </summary>
    public class LpReticleLight : MonoBehaviour
    {
        private ReticleMesh _mesh;
        private ReticleTree.Light.Type _type = ReticleTree.Light.Type.NightIllumination;
        private bool _observed;
        private bool _last;

        public static void Attach(ReticleMesh mesh)
        {
            if (mesh == null) return;
            if (mesh.GetComponent<LpReticleLight>() != null) return;

            LpReticleLight light = mesh.gameObject.AddComponent<LpReticleLight>();
            light._mesh = mesh;
        }

        private void Update()
        {
            if (_mesh == null)
            {
                Destroy(this);
                return;
            }

            // The plate is one of the slot's own items: the game switches it off when it leaves the sight and
            // back on when it enters, and a re-entry that goes through a different path - after the report,
            // after a vehicle switch, after a channel change - can leave it off with the sight still up, which
            // reads as "the graticule sometimes disappears". Re-assert it while this sight is the active slot
            // and the channel is the optical one; on thermal the game swaps in its own plate and ours must stay
            // down.
            CameraSlot slot = CameraSlot.ActiveInstance;
            if (slot != null && slot.VisionType != NightVisionType.Thermal)
            {
                UsableOptic optic = GetComponentInParent<UsableOptic>();
                if (optic != null && optic.slot == slot) Show();
            }

            // PlayerControl is marked obsolete by the game but is still the component that holds the key.
            // The static property is the same value the digital reticles' tint reads.
#pragma warning disable CS0612
            bool on = PlayerControl.ReticleIlluminated;
#pragma warning restore CS0612

            // First frame only records the state: at night the game has already lit the plate and this must
            // not undo that just because the key is still off.
            if (!_observed)
            {
                _observed = true;
                _last = on;
                return;
            }
            if (on == _last) return;
            _last = on;

            _mesh.GetLight(_type, out float value);
            if (float.IsNaN(value)) return;
            _mesh.SetLight(_type, on ? 1f : 0f);
        }

        /// <summary>Puts the plate back on screen when the sight is up and the plate is switched off.</summary>
        private void Show()
        {
            if (!_mesh.gameObject.activeSelf) _mesh.gameObject.SetActive(true);
            Renderer renderer = _mesh.GetComponent<Renderer>();
            if (renderer != null && !renderer.enabled) renderer.enabled = true;
        }
    }
}

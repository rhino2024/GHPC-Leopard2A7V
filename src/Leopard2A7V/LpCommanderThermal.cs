using System;
using System.Reflection;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Thermals;
using HarmonyLib;
using Reticle;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// States the commander's channel as a thermal imager, or back as the glass sight it was built as.
    ///
    /// The imager picture is a property of the slot rather than of the camera: CameraSlot.ThisActiveChanged
    /// reads VisionType and, for a thermal channel, hands the resolution, the filtering and the blit material to
    /// FLIRCamera and switches that camera on - which is what renders the scene through the "Standard (FLIR)"
    /// shader swap and blits the result over the main view. So this states the slot the way an authored imager
    /// states itself and then runs the game's own wiring instead of poking the thermal camera by hand: that
    /// camera's OnEnable is what captures the render state its OnDisable puts back, and a channel switched
    /// around that step would leave the next daylight view drawing a thermal picture.
    ///
    /// The settings the imager takes over are the ones the gunner's channel needed for the same reasons
    /// (LpThermal): the slot's own resolution override, since a coarse grid stretched over the screen is what an
    /// imager's softness actually is, and both pre-blurs at zero, since BlurManager multiplies them by
    /// 60 / field and the tube's authored haze would be several blur units on top of that. The plate is drawn
    /// with the powered light, which UsableOptic holds at full while the slot is the view.
    /// </summary>
    internal static class LpCommanderThermal
    {
        /// <summary>
        /// The game's per-slot wiring. It is private and has no public route, and it is the only place the
        /// imager's size, filtering and blit material are read off a slot, so calling it is what keeps this
        /// channel's picture the same as one the game was authored with.
        /// </summary>
        private static readonly MethodInfo ThisActiveChanged =
            AccessTools.Method(typeof(CameraSlot), "ThisActiveChanged");

        internal static bool Available() => FLIRCamera.Instance != null && ThisActiveChanged != null;

        /// <summary>States the channel and returns what it became, for the log.</summary>
        internal static string Set(CameraSlot slot, ReticleMesh mesh, GameObject unitRoot, bool on)
        {
            FLIRCamera imager = FLIRCamera.Instance;
            if (slot == null || mesh == null) return "no slot or no reticle mesh to switch";
            if (imager == null) return "refused: this scene has no thermal camera";

            if (!on)
            {
                slot.VisionType = NightVisionType.None;
                slot.CanToggleFlirPolarity = false;
                slot.OverrideFLIRResolution = false;
                slot.BaseBlur = 0f;
                slot.VibrationBlurScale = LpCommanderSight.GlassVibrationBlur;
                slot.VibrationShakeMultiplier = LpCommanderSight.GlassVibrationShake;
                FitPlate(mesh, LpCommanderSight.OpticalPlate, ReticleTree.Light.Type.NightIllumination, false);

                // Switched off here rather than by the wiring above: with VisionType back to None the game's
                // own branch no longer runs, and an imager left enabled would blit its picture over whatever
                // view follows. OnDisable is what puts the main camera's render state back.
                imager.enabled = false;
                return "back to glass: " + mesh.reticleSO.name + " on the illumination light, the thermal " +
                       "camera is off, blur " + slot.VibrationBlurScale.ToString("0.##") +
                       "/" + slot.VibrationShakeMultiplier.ToString("0.##");
            }

            Vector2Int res = LpConfig.ThermalSize;
            slot.VisionType = NightVisionType.Thermal;
            slot.OverrideFLIRResolution = true;
            slot.FLIRWidth = res.x;
            slot.FLIRHeight = res.y;
            slot.FLIRFilterMode = FilterMode.Bilinear;
            slot.CanToggleFlirPolarity = true;
            slot.BaseBlur = 0f;
            slot.VibrationBlurScale = 0f;
            // The shake the gunner's imager is authored with, not the glass sight's: the tube has no eyepiece
            // assembly of its own to rattle.
            slot.VibrationShakeMultiplier = 0.1f;

            string surround = FitBlit(slot, unitRoot);
            FitPlate(mesh, LpCommanderSight.ThermalPlate, ReticleTree.Light.Type.Powered, true);

            string wired = "the game's slot wiring is missing, so the imager was not started";
            if (ThisActiveChanged != null)
            {
                ((Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), slot, ThisActiveChanged))(true);
                wired = "imager started by the game's own slot wiring";
            }

            return $"{res.x}x{res.y} imager on a {slot.DefaultFov:0.##} deg field, surround {surround}, " +
                   $"plate {mesh.reticleSO.name} in powered green, polarity key live, {wired}";
        }

        /// <summary>
        /// The eyepiece the imager's picture is drawn into. A slot with no blit override falls back to the
        /// imager's default material, which carries no scope edge, and that is what this sight wants: the
        /// PERI's field is rectangular, so the picture fills the window and the sight's own surround is drawn
        /// over it. The gunner's channel is framed by that vehicle's own round eyepiece instead, and its art
        /// would clip the corners off a commander's wide view - so an override is only ever kept, never borrowed.
        /// </summary>
        private static string FitBlit(CameraSlot slot, GameObject unitRoot)
        {
            if (slot.FLIRBlitMaterialOverride != null) return "kept " + slot.FLIRBlitMaterialOverride.name;
            return "none, the picture fills the window and the sight's own border frames it";
        }

        /// <summary>
        /// Rebuilds the mesh from another plate. Load() keeps the mesh it built first, so the old picture would
        /// be re-drawn from the new plate's lights if the cached mesh were kept, and it only hands the plate's
        /// own light list to the mesh at its very end - which is why the light this plate draws with has to be
        /// pushed after it rather than before.
        /// </summary>
        private static void FitPlate(ReticleMesh mesh, ReticleSO plate,
            ReticleTree.Light.Type light, bool atFull)
        {
            if (mesh.reticleSO == plate) return;
            mesh.reticleSO = plate;
            SkinnedMeshRenderer renderer = mesh.GetComponent<SkinnedMeshRenderer>();
            if (renderer != null) renderer.sharedMesh = null;
            mesh.Load();

            if (atFull)
            {
                mesh.SetLight(light, 1f);
                return;
            }
            mesh.GetLight(light, out float value);
            mesh.SetLight(light, float.IsNaN(value) || value <= 0f ? 0f : 1f);
        }
    }
}

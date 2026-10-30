using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Thermals;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Raises the gunner's thermal picture above the size the donor was authored at.
    ///
    /// GHPC does not render the thermal view with the main camera. FLIRCamera owns a camera of its own,
    /// draws it into a render target, and stretches that whole target over the screen, so the target's
    /// size is the picture's whole sharpness budget: the donor LEO2A3 asks for 480 x 240, which at a full
    /// screen is a coarse grid, and nothing downstream recovers detail that was never rendered. The only
    /// lever is the slot's own override - CameraSlot.ThisActiveChanged hands FLIRWidth/FLIRHeight to
    /// FLIRCamera.WidthOverride/HeightOverride, and FLIRCamera rebuilds the target when the pair changes.
    ///
    /// Those two numbers are not free. FLIRCamera derives the vertical field from the target's aspect
    /// (HorizontalToVerticalFieldOfView against _thermalCamera.aspect), so a target that is not in the
    /// ratio the slot was authored at does not sharpen the picture, it changes its geometry - the reticle
    /// would no longer sit where the gun points. The donor's 2:1 is therefore kept and only the pixel
    /// count grows, which is what LpConfig.ThermalResolution states.
    ///
    /// BaseBlur and VibrationBlurScale are zeroed for the same reason the 99A's imager does it: BlurManager
    /// multiplies BaseBlur by the pixel size of the slot's field against 60 degrees, so any haze authored
    /// for a wide eyepiece becomes several blur units at 9 degrees and turns the raised grid back into a
    /// smudge. Every vanilla imager in the game is authored at zero for both.
    ///
    /// Only slots whose VisionType is Thermal are touched. The image-intensifier night channels are not
    /// thermal pictures at all and their look is the donor's own.
    ///
    /// Called from the visual build, which runs once per unit spawn; the slot's fields are serialized on
    /// the donor prefab, so every spawn arrives with 480 x 240 and this states the wanted values again.
    /// </summary>
    public static class LpThermal
    {
        public static void Apply(GameObject unitRoot)
        {
            if (unitRoot == null) return;

            Vector2Int wanted = LpConfig.ThermalSize;
            int changed = 0;

            foreach (CameraSlot slot in unitRoot.GetComponentsInChildren<CameraSlot>(true))
            {
                if (slot == null || slot.VisionType != NightVisionType.Thermal) continue;
                if (LpVisual.IsImportedArt(slot.transform)) continue;

                string before = slot.OverrideFLIRResolution ? $"{slot.FLIRWidth}x{slot.FLIRHeight}" : "the screen";
                float blur = slot.BaseBlur;
                float vibration = slot.VibrationBlurScale;

                slot.OverrideFLIRResolution = true;
                slot.FLIRWidth = wanted.x;
                slot.FLIRHeight = wanted.y;
                slot.FLIRFilterMode = FilterMode.Bilinear;
                slot.BaseBlur = 0f;
                slot.VibrationBlurScale = 0f;
                changed++;

                // The slot hands these over in ThisActiveChanged, which only fires on activation. The
                // conversion can land after the player is already looking through the sight - the visual
                // swap waits for the model parse - so an already active channel is pushed here instead of
                // waiting for a slot switch.
                if (CameraSlot.ActiveInstance == slot && FLIRCamera.Instance != null)
                {
                    FLIRCamera.Instance.WidthOverride = wanted.x;
                    FLIRCamera.Instance.HeightOverride = wanted.y;
                    FLIRCamera.Instance.TextureFiltering = FilterMode.Bilinear;
                }

                MelonLogger.Msg($"[L2A7V] thermal channel {PathOf(slot.transform, unitRoot.transform)} " +
                                $"now renders at {wanted.x}x{wanted.y} instead of {before}" +
                                (blur != 0f || vibration != 0f
                                    ? $", and its {blur:0.##}/{vibration:0.##} of blur is off"
                                    : ", blur already at zero"));
            }

            if (changed == 0)
            {
                MelonLogger.Msg("[L2A7V] this unit carries no thermal channel, so there is no picture to sharpen");
                return;
            }

            float aspect = wanted.y == 0 ? 0f : (float)wanted.x / wanted.y;
            if (Mathf.Abs(aspect - 2f) > 0.01f)
            {
                MelonLogger.Warning($"[L2A7V] ThermalResolution \"{wanted.x},{wanted.y}\" is not 2:1 like the " +
                                    "donor's channel; the game derives the vertical field from this ratio, " +
                                    "so the picture's geometry - and the reticle's place on it - will shift");
            }
        }

        private static string PathOf(Transform node, Transform unitRoot)
        {
            var names = new System.Collections.Generic.List<string>();
            while (node != null && node != unitRoot)
            {
                names.Add(node.name);
                node = node.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }
    }
}

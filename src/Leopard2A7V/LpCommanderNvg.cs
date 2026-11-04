using System;
using System.Reflection;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Player;
using GHPC.Thermals;
using HarmonyLib;
using MelonLoader;
using Reticle;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Gives the game's own commander view a thermal channel, instead of the night sight it would switch to.
    ///
    /// The commander's head-out view is a CameraSlot in its own right - exterior, free look, no optic - and the
    /// donor gives it a LinkedNightSight, so the night-vision key leaves that view and attaches the camera to a
    /// different slot. On this vehicle the commander's picture should come from his own cupola sight, and he
    /// should keep working while he looks through it, so the key now switches the thermal sensor on the very
    /// view he is standing in:
    ///
    /// * nothing is reparented and no other slot is entered, which is what keeps the vanilla commander controls
    ///   (free look, laser ranging, target designation, turret override, the cupola weapon) wired exactly as
    ///   they were - they are the slot's own, and this never leaves the slot;
    /// * the picture is produced the way the game produces one: VisionType is stated on the slot and the game's
    ///   private slot wiring is run, which hands the resolution, filtering and blit material to FLIRCamera and
    ///   switches that camera on. Poking the camera by hand instead would leave its render state captured at the
    ///   wrong moment and the next daylight view would draw a thermal picture (see LpCommanderThermal);
    /// * the linked night sight is detached, because the game would otherwise attach to it at dusk and the
    ///   thermal would be switched out from under the player.
    /// </summary>
    public static class LpCommanderNvg
    {
        private const string Tag = "[L2A7V][cmdr-nv]";

        /// <summary>The commander's head-out slot, by the name the rig gives it.</summary>
        private const string SlotNameFragment = "commander head";

        internal static void Apply(GameObject unitRoot)
        {
            if (unitRoot == null) return;
            if (unitRoot.GetComponent<LpCommanderNvgToggle>() != null) return;

            CameraSlot slot = FindSlot(unitRoot);
            if (slot == null)
            {
                MelonLogger.Warning(Tag + " no commander head slot on " + unitRoot.name +
                                    "; the commander keeps whatever night sight the donor gave him");
                return;
            }

            var toggle = unitRoot.AddComponent<LpCommanderNvgToggle>();
            toggle.Slot = slot;
            toggle.Adopt();
        }

        /// <summary>
        /// The exterior, free-look slot under the commander's cupola. Matched on the rig's own name first and
        /// only then on the shape of the slot, so a renamed cupola cannot silently pick the driver's hatch.
        /// </summary>
        private static CameraSlot FindSlot(GameObject unitRoot)
        {
            CameraSlot shaped = null;
            foreach (CameraSlot slot in unitRoot.GetComponentsInChildren<CameraSlot>(true))
            {
                if (slot == null || !slot.IsExterior) continue;
                if (!slot.AllowFreeLook) continue;
                if (slot.transform.name.ToLowerInvariant().Contains(SlotNameFragment)) return slot;
                if (shaped == null) shaped = slot;
            }
            return shaped;
        }
    }

    /// <summary>
    /// Switches the commander's view between its glass and a thermal picture on the T key, and leaves every
    /// other input to the game.
    /// </summary>
    public class LpCommanderNvgToggle : MonoBehaviour
    {
        private const string Tag = "[L2A7V][cmdr-nv]";

        /// <summary>The game's per-slot wiring; private, and the only place a slot's imager settings are read.</summary>
        private static readonly MethodInfo ThisActiveChanged =
            AccessTools.Method(typeof(CameraSlot), "ThisActiveChanged");

        internal CameraSlot Slot;

        private bool _thermal;
        private bool _checked;
        private CameraSlot _detachedNightSight;
        private float _blur;
        private float _shake;
        private float _vibrationBlur;
        private bool _polarity;
        private bool _overrideResolution;

        /// <summary>
        /// Takes the donor's night sight out of the way and remembers what the slot looked like, so switching the
        /// thermal off puts the view back exactly as it shipped.
        /// </summary>
        internal void Adopt()
        {
            if (Slot == null) return;

            _detachedNightSight = Slot.LinkedNightSight;
            Slot.LinkedNightSight = null;
            Slot.NightSightAtNightOnly = false;

            _blur = Slot.BaseBlur;
            _shake = Slot.VibrationShakeMultiplier;
            _vibrationBlur = Slot.VibrationBlurScale;
            _polarity = Slot.CanToggleFlirPolarity;
            _overrideResolution = Slot.OverrideFLIRResolution;

            MelonLogger.Msg(Tag + " commander view '" + Path() + "': " +
                            (_detachedNightSight == null
                                ? "the donor linked no night sight"
                                : "detached the donor's night sight '" + _detachedNightSight.name + "'") +
                            ", T now switches this view to thermal" +
                            (FLIRCamera.Instance == null ? " (no thermal camera in this scene, the key will refuse)"
                                                         : string.Empty));
        }

        private void Update()
        {
            if (Slot == null) return;

            PlayerInput input = PlayerInput.Instance;
            if (input == null) return;
            GHPC.Unit player = LpSmoke.CurrentPlayerUnit(input);
            if (player == null || player.gameObject != gameObject) return;

            bool down = Input.GetKeyDown(KeyCode.T);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (CameraSlot.ActiveInstance != Slot)
            {
                // Once per session, and only when the key is actually ours to see: this is the line that says
                // the handler is alive but the commander view is not the slot it was pointed at.
                if (down && !_notedMismatch)
                {
                    _notedMismatch = true;
                    CameraSlot active = CameraSlot.ActiveInstance;
                    MelonLogger.Msg(Tag + " T seen, but the active view is '" +
                                    (active == null ? "<none>" : Path(active.transform)) +
                                    "' and not the commander view '" + Path() + "'");
                }
                return;
            }

            if (!down || ctrl) return;               // Ctrl+T stays the game's polarity key

            Toggle();
        }

        private bool _notedMismatch;

        private static string Path(Transform node)
        {
            if (node == null) return "<null>";
            string path = node.name;
            for (Transform parent = node.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        internal void Toggle()
        {
            if (!_thermal)
            {
                if (FLIRCamera.Instance == null)
                {
                    MelonLogger.Warning(Tag + " T ignored: this scene has no thermal camera");
                    return;
                }

                Vector2Int size = LpConfig.ThermalSize;
                Slot.VisionType = NightVisionType.Thermal;
                Slot.OverrideFLIRResolution = true;
                Slot.FLIRWidth = size.x;
                Slot.FLIRHeight = size.y;
                Slot.FLIRFilterMode = FilterMode.Bilinear;
                Slot.CanToggleFlirPolarity = true;
                Slot.BaseBlur = 0f;
                Slot.VibrationBlurScale = 0f;
                // The shake the gunner's imager carries rather than the glass view's: the picture is a sensor
                // readout, not an eyepiece assembly to rattle.
                Slot.VibrationShakeMultiplier = 0.1f;

                Fire(true);
                _thermal = true;
                MelonLogger.Msg(Tag + " commander view on the thermal camera (" + size.x + "x" + size.y +
                                ", free look and every other commander control untouched), T back to the glass");
                return;
            }

            Slot.VisionType = NightVisionType.None;
            Slot.OverrideFLIRResolution = _overrideResolution;
            Slot.CanToggleFlirPolarity = _polarity;
            Slot.BaseBlur = _blur;
            Slot.VibrationBlurScale = _vibrationBlur;
            Slot.VibrationShakeMultiplier = _shake;

            // With VisionType back to None the game's branch no longer runs, so an imager left on would blit over
            // whatever view comes next. Its OnDisable is what restores the main camera's render state.
            FLIRCamera imager = FLIRCamera.Instance;
            if (imager != null) imager.enabled = false;

            _thermal = false;
            MelonLogger.Msg(Tag + " commander view back on the glass");
        }

        /// <summary>Runs the game's own per-slot wiring for the state just written.</summary>
        private void Fire(bool on)
        {
            if (ThisActiveChanged == null) return;
            try
            {
                ((Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), Slot, ThisActiveChanged))(on);
            }
            catch (Exception error)
            {
                MelonLogger.Warning(Tag + " the game's slot wiring threw: " + error.Message);
            }
        }

        private string Path()
        {
            string path = Slot.transform.name;
            for (Transform node = Slot.transform.parent; node != null; node = node.parent)
                path = node.name + "/" + path;
            return path;
        }
    }
}

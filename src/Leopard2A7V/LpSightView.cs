using System;
using System.Collections.Generic;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// GHPC looks through an optic by moving the main camera onto the CameraSlot that optic owns, and the
    /// donor's turret is cast with a moulded channel around each eyepiece. The imported one is solid there,
    /// so without this the sight view is a flat olive wash with the reticle buried behind the panel: the
    /// FERO Z18 eyepiece at (0.31, 1.90, 2.88) in unit frame sits 0.13 m from the imported barrel.
    ///
    /// The imported art is therefore cleared out of the way of an eyepiece view, which is done by scaling
    /// its pivots rather than by disabling renderers, because something in the vehicle's own visibility
    /// pass keeps switching renderers back on. Same trick CameraSlot.ZeroScaleItems uses.
    ///
    /// Only of an eyepiece, though. The commander's head-out slot is the camera standing in his own cupola,
    /// so the roof fittings are part of what he should see there - the loader's AA MG3, its mount and the
    /// belt box sit a metre and a half to port of him. Clearing them there as well used to leave that view
    /// without the machine gun that third person clearly showed.
    ///
    /// The donor's daylight channel is also authored as one fixed field of view, so the gunner cannot
    /// magnify at all: CycleZoom returns on the spot unless OtherFovs has an entry, and only the thermal
    /// channel is given any. The 2A7V's gunner sight gets the steps the real 12x channel needs here.
    /// </summary>
    public class LpSightView : MonoBehaviour
    {
        private Transform _unitRoot;
        private Transform[] _art = Array.Empty<Transform>();
        private readonly Dictionary<CameraSlot, bool> _logged = new Dictionary<CameraSlot, bool>();
        private Transform _pendingEye;
        private float _pendingAt;
        private bool _warned;
        private bool _artVisible = true;

        public static void Attach(GameObject unitRoot, LpVisual.Assembly art)
        {
            if (unitRoot == null || art == null) return;
            LpSightView view = unitRoot.GetComponent<LpSightView>();
            if (view == null) view = unitRoot.AddComponent<LpSightView>();
            view.Setup(unitRoot.transform, art);
        }

        /// <summary>Nudges the commander's camera slot so his own cupola fittings stay out of the frame.</summary>
        public static void ShiftCommanderSlot(Transform unitRoot)
        {
            Vector3 shift = LpConfig.CommanderView;
            if (shift == Vector3.zero) return;

            var slots = new List<CameraSlot>();
            foreach (CameraSlot slot in unitRoot.GetComponentsInChildren<CameraSlot>(true))
                if (IsCommanderSlot(slot.transform, unitRoot)) slots.Add(slot);

            foreach (CameraSlot slot in slots)
            {
                // A slot mounted on another commander slot travels with it, so nudging both doubles the move.
                if (IsBelowAny(slot.transform, slots)) continue;

                Transform node = slot.transform;
                node.position += unitRoot.TransformDirection(shift);
                MelonLogger.Msg($"[L2A7V] commander view {PathOf(node, unitRoot)} moved by " +
                                $"({shift.x:F2},{shift.y:F2},{shift.z:F2}) m in unit frame");
            }
        }

        /// <summary>Gives the gunner's daylight optic the magnification steps the real sight has.</summary>
        public static void AddGunSightZoom(Transform unitRoot)
        {
            foreach (CameraSlot slot in unitRoot.GetComponentsInChildren<CameraSlot>(true))
            {
                if (slot.IsExterior || slot.VisionType != NightVisionType.None) continue;
                // The cupola machine gun's eyepiece is a clone of the donor's, built to one authored field,
                // and its reticle plate is drawn to fill exactly that.
                if (IsRoofGunSlot(slot.transform)) continue;
                // The thermal channel already steps (9 -> 2.5), and re-running this on a converted template
                // must not keep appending steps to the same slot.
                if (slot.OtherFovs != null && slot.OtherFovs.Length != 0) continue;

                float[] levels = ZoomLevels(slot.DefaultFov);
                slot.OtherFovs = levels;
                MelonLogger.Msg($"[L2A7V] gunner sight {PathOf(slot.transform, unitRoot)} can now zoom " +
                                $"{slot.DefaultFov:0.#} / {string.Join(" / ", levels)} deg");
            }
        }

        /// <summary>
        /// The steps to hand a daylight eyepiece, from the field it opens with. The donor authors its
        /// daylight channel as one fixed field of view, so CycleZoom returns on the spot unless OtherFovs
        /// holds an entry and only the thermal channel is given any.
        ///
        /// A wide eyepiece gets the intermediate step as well, the way the real 12x channel walks, and a
        /// narrow one is already the high power of its sight, so it gets the single deeper step the game
        /// uses for its thermals (9 -> 2.5) and for the T-64B's 1G42 (20 -> 8.4). fovAspect keeps
        /// CurrentFov horizontal whatever the window's aspect ratio, so these stay horizontal degrees.
        /// </summary>
        private static float[] ZoomLevels(float baseFov)
        {
            if (baseFov > 8f) return new[] { baseFov * 0.75f, baseFov * 0.5f };
            return new[] { baseFov * 0.5f };
        }

        /// <summary>
        /// Both of the turret's daylight channels are the gunner's: the FERO Z18 hangs off the main gun's
        /// script group and the GPS optic off the turret's, and which one the player is looking through is
        /// not something the hierarchy announces.
        /// </summary>
        private static bool IsRoofGunSlot(Transform node)
        {
            while (node != null)
            {
                string name = node.name.ToLowerInvariant();
                if (name.Contains("roofgun") || name.Contains("roof gun")) return true;
                node = node.parent;
            }
            return false;
        }

        private void Setup(Transform unitRoot, LpVisual.Assembly art)
        {
            _unitRoot = unitRoot;
            var pieces = new List<Transform>();
            Add(pieces, art.Hull);
            Add(pieces, art.Turret);
            Add(pieces, art.Gun);
            Add(pieces, art.CommanderGear);
            Add(pieces, art.RoofGunYawArt);
            Add(pieces, art.RoofGunPitchArt);
            _art = pieces.ToArray();

            ShiftCommanderSlot(unitRoot);
            AddGunSightZoom(unitRoot);
            Apply(CameraSlot.ActiveInstance);
        }

        private static void Add(List<Transform> list, Transform node)
        {
            if (node != null) list.Add(node);
        }

        private void OnEnable()
        {
            CameraSlot.ActiveInstanceChanged += HandleSlotChanged;
        }

        private void OnDisable()
        {
            CameraSlot.ActiveInstanceChanged -= HandleSlotChanged;
            Apply(null);
        }

        private void HandleSlotChanged(CameraSlot slot)
        {
            Apply(slot);
        }

        private void Apply(CameraSlot slot)
        {
            bool mine = slot != null && _unitRoot != null && IsBelow(slot.transform, _unitRoot);
            bool eyepiece = mine && !slot.IsExterior;

            if (_artVisible != !eyepiece)
            {
                _artVisible = !eyepiece;
                if (!_artVisible && !_warned)
                {
                    _warned = true;
                    MelonLogger.Msg("[L2A7V] clearing the imported art out of the eyepiece");
                }
            }
            Enforce();

            if (mine) Log(slot, eyepiece);
        }

        /// <summary>
        /// A pivot scaled to nothing draws nothing however often another system re-enables its renderers,
        /// and the game's own visibility passes do change scale too, so the wanted state is re-asserted
        /// every frame rather than once per slot switch.
        /// </summary>
        private void LateUpdate()
        {
            Enforce();
            if (_pendingEye != null && Time.unscaledTime >= _pendingAt)
            {
                ReportOccluders(_pendingEye);
                _pendingEye = null;
            }
        }

        private void Enforce()
        {
            SetVisible(_art, _artVisible);
        }

        private static void SetVisible(Transform[] pivots, bool visible)
        {
            foreach (Transform pivot in pivots)
            {
                if (pivot == null) continue;
                Vector3 target = visible ? RestScale(pivot) : Vector3.zero;
                if (pivot.localScale == target) continue;
                pivot.localScale = target;
            }
        }

        /// <summary>
        /// The rest scale is the one the pivot was built with, which is the inverse of its drive bone's
        /// lossy scale rather than a plain one.
        /// </summary>
        private static Vector3 RestScale(Transform pivot)
        {
            Transform bone = pivot.parent;
            if (bone == null) return Vector3.one;
            Vector3 s = bone.lossyScale;
            return new Vector3(Reciprocal(s.x), Reciprocal(s.y), Reciprocal(s.z));
        }

        private static float Reciprocal(float value)
        {
            return Mathf.Abs(value) < 1e-5f ? 1f : 1f / value;
        }

        private void Log(CameraSlot slot, bool eyepiece)
        {
            if (_logged.ContainsKey(slot)) return;
            _logged[slot] = eyepiece;

            Vector3 local = _unitRoot.InverseTransformPoint(slot.transform.position);
            UnityEngine.Camera cam = CameraManager.MainCam;
            MelonLogger.Msg($"[L2A7V] sight slot {PathOf(slot.transform, _unitRoot)} " +
                            $"exterior={slot.IsExterior} fov={slot.DefaultFov:F0} " +
                            $"vision={slot.VisionType} " +
                            $"hiddenArt={(eyepiece ? "all" : "none")} " +
                            $"unit-local=({local.x:F2},{local.y:F2},{local.z:F2}) " +
                            $"camMask={(cam == null ? -1 : cam.cullingMask)}");
            _pendingEye = slot.transform;
            _pendingAt = Time.unscaledTime + 0.35f;
        }

        /// <summary>
        /// Names whatever is still drawn in front of the eyepiece, the only way to tell our own art apart
        /// from the donor's sight tube or a game overlay when a view still looks wrong.
        /// </summary>
        private void ReportOccluders(Transform eye)
        {
            Renderer[] all = FindObjectsOfType<Renderer>();
            int reported = 0;
            for (int i = 0; i < all.Length && reported < 8; i++)
            {
                Renderer renderer = all[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds bounds = renderer.bounds;
                if (bounds.size.sqrMagnitude > 100f) continue;
                for (float distance = 0.05f; distance <= 4f; distance *= 1.6f)
                {
                    if (!bounds.Contains(eye.position + eye.forward * distance)) continue;
                    MelonLogger.Msg($"[L2A7V]   in {PathOf(eye, _unitRoot)} view: " +
                                    $"{PathOf(renderer.transform, null)} at {distance:F2} m, layer " +
                                    $"{renderer.gameObject.layer}, ours={IsOurArt(renderer.transform)}" +
                                    DescribeMaterial(renderer));
                    reported++;
                    break;
                }
            }
            if (reported == 0) MelonLogger.Msg("[L2A7V]   nothing at all is drawn in front of it");
        }

        private static bool IsOurArt(Transform node)
        {
            return LpVisual.IsImportedArt(node);
        }

        /// <summary>
        /// The material is what tells a painted hull panel, the donor's optic lens and a game overlay
        /// apart, and those three need completely different fixes.
        /// </summary>
        private static string DescribeMaterial(Renderer renderer)
        {
            var builder = new System.Text.StringBuilder();
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length && i < 3; i++)
            {
                Material material = materials[i];
                if (material == null) continue;
                builder.Append(", mat ").Append(material.name)
                    .Append("/").Append(material.shader == null ? "?" : material.shader.name);
            }
            return builder.ToString();
        }

        private static bool IsCommanderSlot(Transform node, Transform unitRoot)
        {
            string path = PathOf(node, unitRoot).ToLowerInvariant();
            return path.Contains("command") || path.Contains("cupola");
        }

        private static bool IsBelowAny(Transform node, List<CameraSlot> others)
        {
            foreach (CameraSlot other in others)
                if (!ReferenceEquals(other.transform, node) && IsBelow(node, other.transform)) return true;
            return false;
        }

        private static bool IsBelow(Transform node, Transform ancestor)
        {
            while (node != null)
            {
                if (node == ancestor) return true;
                node = node.parent;
            }
            return false;
        }

        private static string PathOf(Transform node, Transform unitRoot)
        {
            var names = new List<string>();
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

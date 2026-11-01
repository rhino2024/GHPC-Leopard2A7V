using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Thermals;
using GHPC.Utility;
using GHPC.Weapons;
using GHPC.Weaponry;
using HarmonyLib;
using MelonLoader;
using Reticle;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Gives the commander a real sight of his own instead of the head-out camera.
    ///
    /// GHPC's commander view is a free-look head, not an optic: no plate, no zoom stops, no eye relief. The
    /// 2A7V's commander sits behind a PERI R17A3 whose picture is a black glass-etched scale with two
    /// magnifications - 2x and 8x in War Thunder's own specification for this mark - and a thermal channel of
    /// his own. This builds that channel by copying one of the vehicle's own interior eyepieces, cutting every
    /// wire that would let the copy fire or drive anything, moving it onto the commander's sight head and
    /// handing it a plate drawn here, with the rectangular surround the sight has. Z takes the player in and
    /// out; the mouse turns the sight head, so the cross stays on the middle of the picture while the world
    /// moves under it, and T inside the sight switches the channel between its glass and its imager.
    /// </summary>
    public static class LpCommanderSight
    {
        internal const string CloneName = "L2A7V_CommanderSight";

        private const ReticleTree.Light.Type Lit = ReticleTree.Light.Type.NightIllumination;
        private const ReticleTree.Light.Type Powered = ReticleTree.Light.Type.Powered;

        internal const string OpticalPlateName = "L2A7V_CMDR";
        internal const string ThermalPlateName = "L2A7V_CMDR_TH";

        /// <summary>The green the vanilla imagers draw their overlay in; a white mark is swallowed by white-hot.</summary>
        private static readonly RGB ThermalGreen = new RGB(0f, 0.99f, 0.11f);

        /// <summary>War Thunder's commander optics for the 2A7V: 2.0x - 8.0x.</summary>
        internal const float ZoomLow = 2f;
        internal const float ZoomHigh = 8f;

        /// <summary>Field the commander's own sight opens on; the plate's scale is drawn to fill it.</summary>
        internal const float CommanderFov = 20f;

        /// <summary>
        /// Field the thermal picture is looked through. It follows the daylight channel, because on this
        /// vehicle the imager is the gunner's sight with a different sensor, not a second telescope: same
        /// magnification, same marks, same window.
        /// </summary>
        internal const float ThermalFov = 15.07f;

        /// <summary>
        /// The commander's sight head in unit-frame metres. The cupola sight housing occupies x 0.05-0.50,
        /// y 2.59-2.98, z -0.56--0.10 in the imported art, so this stands in the middle of it, at the height
        /// of the eyepiece.
        /// </summary>
        private static readonly Vector3 Eye = new Vector3(0.27f, 2.78f, -0.33f);

        /// <summary>How far the sight head may be turned from straight ahead, in degrees.</summary>
        internal const float YawLimit = 40f;
        internal const float PitchLimit = 15f;
        internal const float Sensitivity = 0.9f;

        private static readonly FieldInfo SlotReparentTarget =
            AccessTools.Field(typeof(CameraSlot), "_reparentTarget");

        /// <summary>
        /// The blur the glass channel is authored with. The imager zeroes both of these for its own turn of the
        /// channel, because BlurManager multiplies them by 60 / field, so leaving the switch to put back
        /// anything other than these two numbers would be a different sight than the one that was built.
        /// </summary>
        internal const float GlassVibrationBlur = 0.1f;
        internal const float GlassVibrationShake = 0.15f;

        private static ReticleSO _opticalPlate;
        private static ReticleSO _thermalPlate;

        /// <summary>
        /// The glass picture: black etched marks on the illumination light, which the I key and the game's own
        /// night lighting drive.
        /// </summary>
        internal static ReticleSO OpticalPlate
        {
            get
            {
                if (_opticalPlate == null) _opticalPlate = BuildPlate(OpticalPlateName, Lit, new RGB(0f, 0f, 0f), CommanderFov);
                return _opticalPlate;
            }
        }

        /// <summary>
        /// The same measuring scale as an electronic overlay: green marks on the imager's powered light, which
        /// UsableOptic holds at full for as long as the slot is the view, so switching to the imager does not
        /// have to wait on the illumination key to see the graticule.
        /// </summary>
        internal static ReticleSO ThermalPlate
        {
            get
            {
                if (_thermalPlate == null) _thermalPlate = BuildPlate(ThermalPlateName, Powered, ThermalGreen, CommanderFov);
                return _thermalPlate;
            }
        }

        /// <summary>Horizontal degrees for a magnification, against the game's 60 degree reference field.</summary>
        private static float FovFor(float magnification)
        {
            // War Thunder magnification is a ratio of tangents against the eye's own field, and this mod's
            // gunner ladder already fixes that frame: the deepest gunner field, 8.26 degrees, is 11x. Anchored
            // there, 2x comes out at 43.3 degrees - a commander's sight has to see wide - and 8x at 11.34.
            const float deepest = 8.26f;
            const float deepestZoom = 11f;
            return 2f * Mathf.Atan(Mathf.Tan(deepest * 0.5f * Mathf.Deg2Rad) *
                                   deepestZoom / Mathf.Max(1f, magnification)) * Mathf.Rad2Deg;
        }

        internal const string PivotName = "L2A7V_CmdSightPivot";

        /// <summary>The node the sight head's own art hangs from, which is what has to stop drawing inboard.</summary>
        internal const string HeadArtName = PivotName + "_Art";

        /// <summary>
        /// How far behind the rear of the sight head the eye sits. The head turns about its own centre, so the
        /// eye has to stand behind the housing rather than inside it: at 0.15 m forward of the centre the camera
        /// was looking at the inside of the box and its glass panel, which is the black mass and grey column a
        /// live session named as the only thing in front of the eyepiece (L2A7V_CmdSightPivot_Art at 0.05 m).
        /// </summary>
        private const float EyeClearance = 0.08f;

        /// <summary>The node the imported commander's sight head hangs from, so its art can turn with the view.</summary>
        internal static Transform FindHead(GameObject unitRoot)
        {
            // The pivot hangs off the turret bone, not off the unit root, so this has to walk the tree.
            foreach (Transform node in unitRoot.GetComponentsInChildren<Transform>(true))
            {
                if (node != null && node.name == PivotName) return node;
            }
            return null;
        }

        internal static Transform FindHeadArt(Transform head)
        {
            return head == null ? null : head.Find(HeadArtName);
        }

        /// <summary>Half the depth of the sight head's own art, plus the clearance the eye needs behind it.</summary>
        internal static float EyeBehind(Transform head)
        {
            Transform art = FindHeadArt(head);
            MeshFilter filter = art == null ? null : art.GetComponent<MeshFilter>();
            float half = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds.extents.z : 0.32f;
            return half + EyeClearance;
        }

        /// <summary>
        /// Moves an eyepiece onto the sight head, at the rear of the device rather than inside it.
        /// </summary>
        internal static void Mount(Transform eye, Transform head)
        {
            eye.SetParent(head, false);
            eye.localPosition = new Vector3(0f, 0f, -EyeBehind(head));
            eye.localRotation = Quaternion.identity;
            eye.localScale = Vector3.one;
        }

        /// <summary>The mod's own commander eyepiece, wherever it ended up in the tree.</summary>
        private static Transform FindClone(GameObject unitRoot)
        {
            foreach (Transform node in unitRoot.GetComponentsInChildren<Transform>(true))
            {
                if (node != null && node.name == CloneName) return node;
            }
            return null;
        }

        /// <summary>True for this mod's commander channel and anything under it, so ladders leave it alone.</summary>
        internal static bool IsCommanderSlot(Transform node)
        {
            int guard = 0;
            while (node != null && guard++ < 32)
            {
                if (node.name == PivotName || node.name == CloneName) return true;
                node = node.parent;
            }
            return false;
        }

        /// <summary>True for the art of the commander's own sight device, as opposed to the cupola around it.</summary>
        internal static bool IsSightPart(string objectName)
        {
            return objectName != null &&
                   objectName.IndexOf("optic_commander", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Splits the sight device out of the cupola's merged mesh and hangs it on its own pivot, at the
        /// centre of its own geometry, so the model can be turned with the view. Returns the pivot, or null
        /// when the export carries no such part.
        /// </summary>
        public static Transform AttachHead(Transform turretBone, Vector3 ring)
        {
            if (turretBone == null) return null;

            Bounds bounds = new Bounds();
            bool any = false;
            foreach (ObjGeometry part in ObjModel.Geometries)
            {
                if (part.Group != LpGroup.CommanderGear || !IsSightPart(part.ObjectName)) continue;
                if (!any) { bounds = part.Bounds; any = true; }
                else bounds.Encapsulate(part.Bounds);
            }
            if (!any)
            {
                MelonLogger.Msg("[L2A7V] the export carries no optic_commander part, so the sight device cannot turn");
                return null;
            }

            Vector3 centre = bounds.center;
            Transform pivot = LpVisual.MakePivot(turretBone, PivotName, centre - ring);
            LpVisual.AttachParts(pivot, PivotName + "_Art", part =>
                part.Group == LpGroup.CommanderGear && IsSightPart(part.ObjectName), centre);
            return pivot;
        }

        public static void Apply(GameObject unitRoot)
        {
            if (unitRoot == null) return;
            if (FindClone(unitRoot) != null) return;

            UsableOptic donorOptic = FindDonorOptic(unitRoot);
            if (donorOptic == null)
            {
                MelonLogger.Warning("[L2A7V] the commander's sight stays unbuilt: no interior eyepiece to copy");
                return;
            }

            GameObject source = CloneSource(donorOptic);
            if (source == null)
            {
                MelonLogger.Warning("[L2A7V] the commander's sight stays unbuilt: the eyepiece carries no CameraSlot");
                return;
            }

            // A copied group carries the donor's Reparent scripts, and those move their own GameObject in
            // Awake - which runs the moment the copy is instantiated. Building it under an inactive parent is
            // what keeps the copy where it was put: it would otherwise wake up inside the gun's script group,
            // several metres forward and above, and the mod's own gunner-sight pass would then treat it as
            // the gunner channel and rewrite its field of view.
            var staging = new GameObject(CloneName + "_staging");
            staging.SetActive(false);
            GameObject clone = UnityEngine.Object.Instantiate(source, staging.transform, false);
            clone.name = CloneName;
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;

            Strip(clone);

            // The donor eyepiece carries its own circular ring around the picture. This sight is framed by
            // the rectangular border instead, so the round one has to go - and its name is not dependable
            // across donors, so it is recognised by the scope art it wears, with every renderer logged so a
            // miss shows up as a name in the log rather than as a circle on screen.
            foreach (MeshRenderer ring in clone.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (ring == null) continue;
                Material material = ring.sharedMaterial;
                Texture art = material != null ? material.mainTexture : null;
                string artName = art != null ? art.name.ToLowerInvariant() : string.Empty;
                string nodeName = ring.transform.name.ToLowerInvariant();
                bool isRing = artName.Contains("gunsight") || artName.Contains("scope") ||
                              nodeName.Contains("quad") || nodeName.Contains("scope") ||
                              nodeName.Contains("ring") || nodeName.Contains("bezel");
                MelonLogger.Msg("[L2A7V] commander eyepiece renderer '" + ring.transform.name + "' art='" +
                                artName + "' -> " + (isRing ? "ring switched off" : "kept"));
                if (isRing) ring.enabled = false;
            }

            UsableOptic optic = clone.GetComponentInChildren<UsableOptic>(true);
            CameraSlot slot = ResolveSlot(clone, optic);
            ReticleMesh mesh = optic != null ? optic.reticleMesh : null;
            if (optic == null || slot == null || mesh == null)
            {
                MelonLogger.Warning("[L2A7V] the copied eyepiece lost its optic, slot or reticle, so the " +
                                    "commander keeps the head-out camera");
                UnityEngine.Object.Destroy(clone);
                return;
            }

            MakePhysical(optic, slot);

            mesh.reticleSO = OpticalPlate;
            SkinnedMeshRenderer renderer = mesh.GetComponent<SkinnedMeshRenderer>();
            if (renderer != null) renderer.sharedMesh = null;
            mesh.Load();
            mesh.GetLight(Lit, out float cached);
            mesh.SetLight(Lit, float.IsNaN(cached) || cached <= 0f ? 0f : 1f);
            LpReticleLight.Attach(mesh);



            Transform head = FindHead(unitRoot);
            if (head != null) Mount(clone.transform, head);
            else
            {
                clone.transform.SetParent(unitRoot.transform, false);
                clone.transform.localPosition = Eye;
            }
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
            clone.SetActive(true);
            UnityEngine.Object.Destroy(staging);

            LpCommanderToggle toggle = unitRoot.AddComponent<LpCommanderToggle>();
            toggle.Slot = slot;
            toggle.Mesh = mesh;
            toggle.Sight = head != null ? head : clone.transform;
            toggle.Clone = clone.transform;
            toggle.Unit = unitRoot;
            toggle.Art = FindHeadArt(head);
            toggle.Mounted = head != null;
            toggle.Border = BorderTexture(256, 0.015f, 0.035f);

            MelonLogger.Msg($"[L2A7V] commander's sight built at {Eye} from '{source.name}': " +
                            $"{ZoomLow:0.#}x/{ZoomHigh:0.#}x ({slot.DefaultFov:0.##}/{slot.OtherFovs[0]:0.##} deg), " +
                            $"plate {mesh.reticleSO.name}, Z to enter and leave, mouse to turn the head, " +
                            (head != null
                                ? $"mounted on the sight head {head.name} at {clone.transform.localPosition.z:0.##} m"
                                : "the sight head is built later, so the eyepiece waits for it") + ", " +
                            (LpCommanderThermal.Available() ? "T switches the imager on and off"
                                                            : "and no thermal camera in this scene, so T has " +
                                                              "nothing to switch"));
        }

        private static UsableOptic FindDonorOptic(GameObject unitRoot)
        {
            UsableOptic fallback = null;
            foreach (UsableOptic optic in unitRoot.GetComponentsInChildren<UsableOptic>(true))
            {
                if (optic == null || optic.slot == null) continue;
                if (optic.slot.IsExterior) continue;
                if (LpVisual.IsImportedArt(optic.transform)) continue;
                if (optic.slot.VisionType != NightVisionType.None) continue;
                if (optic.reticleMesh == null || optic.reticleMesh.reticleSO == null) continue;
                if (optic.reticleMesh.reticleSO.name == LpReticle.PlateName) return optic;
                if (fallback == null) fallback = optic;
            }
            return fallback;
        }

        private static GameObject CloneSource(UsableOptic donor)
        {
            Transform node = donor.transform;
            for (int guard = 0; node != null && guard < 8; guard++, node = node.parent)
            {
                if (node.GetComponent<CameraSlot>() != null) return node.gameObject;
            }
            return null;
        }

        private static CameraSlot ResolveSlot(GameObject clone, UsableOptic optic)
        {
            CameraSlot slot = optic != null ? optic.slot : null;
            if (slot != null && slot.transform.IsChildOf(clone.transform)) return slot;
            return clone.GetComponentInChildren<CameraSlot>(true);
        }

        private static void Strip(GameObject clone)
        {
            foreach (Reparent reparent in clone.GetComponentsInChildren<Reparent>(true))
                UnityEngine.Object.DestroyImmediate(reparent);
            foreach (FireControlSystem fcs in clone.GetComponentsInChildren<FireControlSystem>(true))
                UnityEngine.Object.DestroyImmediate(fcs);
            foreach (MissileGuidanceUnit guidance in clone.GetComponentsInChildren<MissileGuidanceUnit>(true))
                UnityEngine.Object.DestroyImmediate(guidance);
        }

        /// <summary>
        /// Cuts every wire that would let a copied eyepiece shoot, range, or switch the donor's own optics on
        /// and off. The sight head is turned by our own component, so the slot itself stays fixed.
        /// </summary>
        private static void MakePhysical(UsableOptic optic, CameraSlot slot)
        {
            optic.FCS = null;
            optic.RegisterAtStart = false;
            optic.Alignment = OpticAlignment.Fixed;
            optic.Mounts = Array.Empty<AimablePlatform>();
            optic.RotateAzimuth = false;
            optic.RotateElevation = false;
            optic.CantCorrect = false;
            optic.UseRotationForShake = false;
            optic.ForceHorizontalReticleAlign = false;
            optic.RangeText = null;
            optic.ReadyToFireObject = null;
            optic.UnreadyToFireObject = null;
            optic.FaultObject = null;
            optic.OverridingObject = null;
            optic.RegisteredAmmoTypes = Array.Empty<AmmoCodexScriptable>();

            slot.VisionType = NightVisionType.None;
            // The wiring starts the imager for this flag alone, so an inherited one would survive T's off.
            slot.DoMechanicalFLIR = false;
            // 1:1 with the War Thunder plate: the wide stop is the 20 degrees that plate was drawn for, so
            // the sight picture sits at its authored size at the lowest magnification. The deep stop keeps
            // the magnification ratio through the tangent, as every other ladder in this mod does.
            const float authoredFov = CommanderFov;
            slot.DefaultFov = authoredFov;
            slot.OtherFovs = new[]
            {
                2f * Mathf.Atan(Mathf.Tan(authoredFov * 0.5f * Mathf.Deg2Rad) * ZoomLow / ZoomHigh) * Mathf.Rad2Deg
            };
            slot.AllowFreeLook = false;
            slot.AllowFreeZoom = false;
            slot.DirectAttachOnly = true;
            slot.NightSightAtNightOnly = false;
            slot.LinkedNightSight = null;
            slot.HitZone = null;
            slot.SlotToUseOnDestroy = null;
            slot.ZeroScaleItems = Array.Empty<Transform>();
            slot.ExclusiveItems = Array.Empty<GameObject>();
            slot.ExclusiveWeapons = Array.Empty<WeaponSystem>();
            slot.BaseBlur = 0f;
            slot.VibrationBlurScale = GlassVibrationBlur;
            slot.VibrationShakeMultiplier = GlassVibrationShake;
            if (SlotReparentTarget != null) SlotReparentTarget.SetValue(slot, null);
        }

        /// <summary>
        /// The PERI's picture, as War Thunder draws the commander's sight: a long horizontal measuring scale
        /// with a tick every 2 mrad, and a vertical ladder of dashes either side of the centre. Built twice -
        /// once as glass etched in black on the illumination light, once as the imager's green overlay on its
        /// powered light - because the two channels light their marks differently.
        /// </summary>
        private static ReticleSO BuildPlate(string plateName, ReticleTree.Light.Type light, RGB colour, float fov)
        {
            var group = new ReticleTree.Angular(Vector2.zero, null, ReticleTree.GroupBase.Alignment.Boresight)
            {
                name = "CommanderScale",
                elements = new List<ReticleTree.TransformElement>()
            };

// Half the field: the commander's picture is a cross with a measuring scale across the middle, and
            // a scale that spans the whole window is not what the sight the reference was taken from shows.
            // 1:1 with the low stop's field, as on the 99A: the scale spans the screen at the first stop.
            float halfSpan = Mathf.Clamp(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 1000f, 20f, 900f);
            // Thickness is a fraction of the span rather than a fixed angle: a 0.09 mrad mark is a hairline
            // at 43 degrees, and the picture has to read the same whatever the field is.
            float stroke = Mathf.Clamp(halfSpan / 320f, 0.25f, 2.5f);

            // The measuring scale: one tick every 2 mrad, every tenth one taller, and a gap at the centre.
            Add(group.elements, -halfSpan, 0f, -2.5f, 0f, stroke * 0.8f, light);
            Add(group.elements, 2.5f, 0f, halfSpan, 0f, stroke * 0.8f, light);
            float step = Mathf.Max(2f, halfSpan / 40f);
            for (float x = step; x <= halfSpan; x += step)
            {
                bool major = Mathf.Abs(Mathf.Repeat(x, step * 5f)) < 0.01f;
                float h = major ? 2.2f : 1.1f;
                Add(group.elements, x, -h, x, h, stroke * 0.6f, light);
                Add(group.elements, -x, -h, -x, h, stroke * 0.6f, light);
            }

            // The vertical marks above the cross: isolated dashes, no climbing line and no millimetre ladder
            // under them. Every 2 mrad, longer at the tens, and carried all the way to the top of the screen -
            // the field is quoted horizontally, so the height they have to reach is that field divided by the
            // screen's aspect ratio.
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.7778f;
            float toTop = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 1000f / Mathf.Max(1f, aspect);
            for (float y = 2f; y <= toTop; y += 2f)
            {
                float w = Mathf.Abs(Mathf.Repeat(y, 10f)) < 0.01f ? 3.2f : 1.8f;
                Add(group.elements, -w, y, w, y, stroke * 0.6f, light);
            }

            var plane = new ReticleTree.FocalPlane(ReticleTree.FocalPlane.Type.First)
            {
                elements = new List<ReticleTree.TransformElement> { group }
            };

            var plate = ScriptableObject.CreateInstance<ReticleSO>();
            plate.name = plateName;
            plate.hideFlags = HideFlags.DontUnloadUnusedAsset;
            plate.planes = new List<ReticleTree.FocalPlane> { plane };

            // Both lights, always, in this order: the mesh pairs its cached light states with this list by
            // index while the colour of a mark is looked up by the light's type, so a plate that listed only
            // one of the two would have the other's colour land on the wrong index when the channel switched.
            plate.lights = new List<ReticleTree.Light>
            {
                new ReticleTree.Light { type = Lit,     color = light == Lit ? colour : new RGB(0f, 0f, 0f) },
                new ReticleTree.Light { type = Powered, color = light == Powered ? colour : new RGB(1f, 1f, 1f) }
            };
            return plate;
        }

        private static Texture2D BorderTexture(int size, float solid, float fade)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = Mathf.Min(x, size - 1 - x) / (float)(size - 1);
                    float v = Mathf.Min(y, size - 1 - y) / (float)(size - 1);
                    float d = Mathf.Min(u, v);
                    float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(solid, solid + fade, d));
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static void Add(List<ReticleTree.TransformElement> elements,
            float x1, float y1, float x2, float y2, float stroke, ReticleTree.Light.Type light)
        {
            float dx = x2 - x1, dy = y2 - y1;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            if (length < 0.01f) return;
            elements.Add(new ReticleTree.Line(
                new Vector2((x1 + x2) * 0.5f, (y1 + y2) * 0.5f),
                Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, length, stroke) { illumination = light });
        }
    }

    /// <summary>
    /// Z enters the commander's sight and Z again leaves it; the mouse turns the sight head while inside, so
    /// the plate stays on the middle of the picture instead of being left behind by a free-look camera; and T
    /// inside the sight switches its channel between the glass and the thermal imager.
    /// </summary>
    public class LpCommanderToggle : MonoBehaviour
    {
        private const float YawLimit = 40f;
        private const float PitchLimit = 15f;
        private const float Sensitivity = 0.9f;

        /// <summary>How often to look for the sight head, which the art pass builds whenever it is ready.</summary>
        private const float MountInterval = 0.5f;

        internal CameraSlot Slot;
        internal Transform Sight;
        internal Texture2D Border;
        internal GameObject Unit;
        internal Transform Clone;
        internal Transform Art;
        internal bool Mounted;

        /// <summary>The plate the sight was built with; the channel switch swaps it for the imager's own.</summary>
        internal ReticleMesh Mesh;

        private bool _inside;
        private bool _reported;
        private bool _thermal;
        private bool _warnedNoImager;
        private float _yaw;
        private float _pitch;
        private float _parentYaw;
        private float _parentPitch;
        private bool _haveParent;
        private float _nextMount;

        private void Update()
        {
            CameraManager manager = CameraManager.Instance;
            if (manager == null || Slot == null) return;

            if (!Mounted) MountWhenReady();

            if (Input.GetKeyDown(KeyCode.Z))
            {
                if (_inside)
                {
                    manager.ResetTempAttach();
                    _inside = false;
                }
                else
                {
                    if (!Slot.IsAvailable)
                    {
                        if (!_reported)
                        {
                            _reported = true;
                            MelonLogger.Warning("[L2A7V] commander's sight is not available yet (slot " +
                                                Slot.name + "), so Z cannot enter it");
                        }
                        return;
                    }
                    _yaw = 0f;
                    _pitch = 0f;
                    if (Sight != null) Sight.localRotation = Quaternion.identity;
                    // Whatever channel the sight was left on is the one the game's wiring will draw with, so
                    // this component agrees with it rather than starting from the glass every time.
                    _thermal = Slot.VisionType == NightVisionType.Thermal;
                    manager.TempAttachToSlot(Slot);
                    _inside = true;
                    if (!_reported)
                    {
                        _reported = true;
                        MelonLogger.Msg("[L2A7V] commander's sight: entered, zoom stops " +
                                        $"{Slot.DefaultFov:0.##} deg then {Slot.OtherFovs[0]:0.##} deg " +
                                        "(Z again to leave, mouse turns the head, T switches the channel)");
                    }
                }
            }

            // This sight has a thermal channel of its own, and switching to it makes a child slot the
            // active one. Testing for the slot object alone dropped the sight to "outside" on that switch,
            // which took the rectangular border away and stopped the head turning.
            if (_inside)
            {
                CameraSlot active = CameraSlot.ActiveInstance;
                bool ours = active == Slot ||
                            (active != null && Sight != null && active.transform.IsChildOf(Sight));
                if (!ours) _inside = false;
            }

            if (_inside && Input.GetKeyDown(KeyCode.T)) SwitchThermal();

            if (_inside)
            {
                // Turn the head, not the camera: the plate rides on the sight, so it stays centred on the
                // picture while the world swings past it - which is what a commander's panoramic sight does.
                // 360 degrees: a commander's panoramic sight has no end stop, so the head keeps turning and
                // only the pitch is limited. Clamping yaw was what made it read as a 40 degree slot.
                _yaw = Mathf.Repeat(_yaw + Input.GetAxisRaw("Mouse X") * Sensitivity + 180f, 360f) - 180f;
                _pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * Sensitivity, -PitchLimit, PitchLimit);

                // Stabilisation. The head hangs under the turret, so every degree the hull or turret turns is a
                // degree the picture moves, exactly like an unstabilised sight. A panoramic sight on these
                // vehicles is gyro-stabilised, so the parent's rotation is measured each frame and taken back
                // out of the head: the sight keeps pointing where it was while the vehicle turns and pitches.
                if (Sight != null && Sight.parent != null)
                {
                    Vector3 parent = Sight.parent.eulerAngles;
                    if (_haveParent)
                    {
                        _yaw -= Mathf.DeltaAngle(_parentYaw, parent.y);
                        _pitch = Mathf.Clamp(_pitch - Mathf.DeltaAngle(_parentPitch, parent.x),
                                             -PitchLimit, PitchLimit);
                    }
                    _parentYaw = parent.y;
                    _parentPitch = parent.x;
                    _haveParent = true;
                    Sight.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
                }
            }

            HideHeadArt();
        }

        /// <summary>
        /// The sight device is split out of the cupola by the art pass, which runs seconds after this component
        /// is added, so the head is hunted for rather than assumed. Until it is found the mouse turns the
        /// eyepiece on its own, which is why the imported sight stayed pointing straight ahead while the view
        /// swung round.
        /// </summary>
        private void MountWhenReady()
        {
            if (Unit == null || Clone == null || Time.unscaledTime < _nextMount) return;
            _nextMount = Time.unscaledTime + MountInterval;

            Transform head = LpCommanderSight.FindHead(Unit);
            if (head == null) return;

            LpCommanderSight.Mount(Clone, head);
            Sight = head;
            Art = LpCommanderSight.FindHeadArt(head);
            Mounted = true;
            _yaw = 0f;
            _pitch = 0f;
            MelonLogger.Msg("[L2A7V] commander's eyepiece mounted on the sight head " + head.name + ": eye " +
                            Clone.localPosition.z.ToString("0.##") + " m aft of its centre, device art " +
                            (Art == null ? "not found" : "'" + Art.name + "'") + ", so the device now turns " +
                            "with the view");
        }

        /// <summary>
        /// Looking through a sight means not seeing it. The housing and its glass panel are the only art in
        /// front of this eyepiece, and scaling the node rather than disabling the renderer is what survives the
        /// vehicle's own visibility pass putting renderers back on.
        /// </summary>
        private void HideHeadArt()
        {
            if (Art == null) return;
            Vector3 wanted = _inside ? Vector3.zero : Vector3.one;
            if (Art.localScale != wanted) Art.localScale = wanted;
        }

        /// <summary>
        /// Switches the commander's channel between the sight's glass and its own imager.
        ///
        /// The game's T key is not this key. In digital control it only advances PlayerControl's thermal mode,
        /// and that mode reaches both the picture and the environment from swapCameras alone, so a commander
        /// looking through his own sight with the free-look camera off would have nothing change.
        /// </summary>
        private void SwitchThermal()
        {
            if (!LpCommanderThermal.Available())
            {
                if (_warnedNoImager) return;
                _warnedNoImager = true;
                MelonLogger.Warning("[L2A7V] commander's sight: T asked for the thermal channel but this " +
                                    "scene has no thermal camera, so the sight stays on its glass");
                return;
            }

            _thermal = !_thermal;
            MelonLogger.Msg("[L2A7V] commander's sight " + (_thermal ? "on thermal: " : "on glass: ") +
                            LpCommanderThermal.Set(Slot, Mesh, Unit, _thermal));
        }

        /// <summary>
        /// The rectangular surround, drawn over everything while the sight is the view. A texture is cheaper
        /// than a UI canvas here and needs no uGUI reference in the mod assembly.
        /// </summary>
        private void OnGUI()
        {
            if (!_inside || Border == null) return;

            // The imager's blit material already frames its picture with an eyepiece, and a rectangle drawn
            // over it reads as a second, wrongly-sized vignette.
            if (_thermal && Slot != null && Slot.FLIRBlitMaterialOverride != null) return;

            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Border,
                ScaleMode.StretchToFill, true);
        }
    }


    /// <summary>
    /// Keeps the sight picture one size on screen at every zoom stop: a plate is a mesh at a fixed distance,
    /// so its marks subtend a fixed angle and shrink as the field opens up.
    /// </summary>
    internal sealed class LpReticleScale : MonoBehaviour
    {
        internal CameraSlot Slot;
        internal float ReferenceFov = 11.34f;
        private float _last = -1f;

        private void Update()
        {
            if (Slot == null) return;
            float fov = Mathf.Max(1f, Slot.CurrentFov);
            float scale = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) /
                          Mathf.Tan(ReferenceFov * 0.5f * Mathf.Deg2Rad);
            transform.localScale = Vector3.one * scale;
            if (Mathf.Abs(scale - _last) > 0.05f)
            {
                _last = scale;
                MelonLogger.Msg("[L2A7V] commander reticle scale x" + scale.ToString("0.00") +
                                " at " + fov.ToString("0.##") + " deg (reference " + ReferenceFov.ToString("0.##") + ")");
            }
        }
    }


    /// <summary>
    /// Holds the sight picture at the size it is wanted at each stop: 400% of the screen width at the low
    /// magnification and 600% at the high one. A plate is a fixed mesh, so without this the picture would
    /// simply grow fourfold with the field ratio; this measures the field each frame and scales the mesh to
    /// whatever proportion the ladder asks for.
    /// </summary>
    public class LpCommanderReticleFit : MonoBehaviour
    {
        internal CameraSlot Slot;
        internal float LowFieldMrad;
        internal float HighFieldMrad;
        internal float AuthoredSpanMrad;
        internal float LowProportion = 4f;
        internal float HighProportion = 6f;

        private void Update()
        {
            if (Slot == null) return;
            float field = 2f * Mathf.Tan(Mathf.Max(1f, Slot.CurrentFov) * 0.5f * Mathf.Deg2Rad) * 1000f;
            float t = Mathf.Clamp01((field - HighFieldMrad) / Mathf.Max(1f, LowFieldMrad - HighFieldMrad));
            float proportion = Mathf.Lerp(HighProportion, LowProportion, t);
            float scale = proportion * field / Mathf.Max(1f, AuthoredSpanMrad);
            transform.localScale = Vector3.one * scale;
        }
    }
}

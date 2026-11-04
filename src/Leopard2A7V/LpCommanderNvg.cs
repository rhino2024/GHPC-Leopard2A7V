using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Player;
using GHPC.Thermals;
using HarmonyLib;
using MelonLoader;
using Reticle;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V
{
    /// <summary>
    /// Builds the commander a sight of his own out of a copy of his head-out view.
    ///
    /// The night-vision device is left exactly where the game put it: T still drops the camera into the donor's
    /// goggles, with their own post volume and sprite, because that slot is never touched here.
    ///
    /// The sight itself is a copy of the commander's head-out (binocular) slot. That choice is the whole point:
    /// the copy is a daylight, free-look camera carrying the commander's own controls, so the vanilla aim
    /// override - hold the aim button and the gunner's weapon is laid on the view - resolves through exactly the
    /// same free-look path it uses in the head-out view. Nothing about the night-vision chain is cloned with it.
    ///
    /// The copy is then stated as an eyepiece at the cupola sight head, given the War Thunder commander's
    /// graticule, and handed the vehicle's working thermal channel as a second mode that the rig switches on T.
    /// </summary>
    public static class LpCommanderNvg
    {
        private const string Tag = "[L2A7V][cmdr-sight]";

        /// <summary>The commander's head-out slot, by the name the rig gives it.</summary>
        private const string SlotNameFragment = "commander head";

        private static readonly FieldInfo DesignatedSlots =
            AccessTools.Field(typeof(GHPC.Unit), "_designatedCameraSlots");

        internal static void Apply(GameObject unitRoot)
        {
            if (unitRoot == null) return;
            if (unitRoot.GetComponent<LpCommanderSightRig>() != null) return;

            CameraSlot view = FindSlot(unitRoot);
            if (view == null)
            {
                MelonLogger.Warning(Tag + " no commander head slot on " + unitRoot.name + "; no sight is built");
                return;
            }

            // T keeps the goggles the game authored, day or night.
            view.NightSightAtNightOnly = false;

            CameraSlot sight = CloneView(view, unitRoot);
            if (sight == null)
            {
                MelonLogger.Warning(Tag + " the commander head slot could not be copied, so Z has no sight");
                return;
            }

            Transform head = LpCommanderSight.FindHead(unitRoot);
            if (head != null) AttachToHead(sight, head);
            // The head pivot is built by the art pass, which has not run yet at spawn time; the rig finishes the
            // placement as soon as it appears.

            sight.AllowFreeLook = true;
            sight.AllowFreeZoom = false;
            // An eyepiece: the sight's own art is cleared out of the picture (LpSightView) and the audio takes the
            // buttoned-up side, exactly like the gunner's channels.
            sight.IsExterior = false;
            sight.SpriteType = CameraSpriteManager.SpriteType.DefaultScope;
            sight.DirectAttachOnly = true;

            // The field the plate is drawn for, stated horizontally: its scale runs to tan(fov/2) at the focal
            // plane, i.e. to the left and right edges when the camera's field is that same number. Unity states
            // FOV vertically, so without fovAspect a 20 degree slot is a 34 degree window and the graticule sits
            // at about sixty percent of the width.
            sight.fovAspect = true;
            sight.DefaultFov = LpCommanderSight.CommanderFov;
            sight.OtherFovs = new[]
            {
                2f * Mathf.Atan(Mathf.Tan(LpCommanderSight.CommanderFov * 0.5f * Mathf.Deg2Rad) *
                                LpCommanderSight.ZoomLow / LpCommanderSight.ZoomHigh) * Mathf.Rad2Deg
            };

            // Stated as glass. The imager settings the vehicle ships stay on the slot for the thermal mode, but
            // they are put in the state that does not switch the imager on: CameraSlot.ThisActiveChanged starts
            // it for a thermal VisionType or a mechanical flag, and a daylight channel wants neither.
            sight.VisionType = NightVisionType.None;
            sight.DoMechanicalFLIR = false;
            sight.OverrideFLIRResolution = false;
            sight.CanToggleFlirPolarity = false;
            sight.FLIRBlitMaterialOverride = null;
            sight.BaseBlur = 0f;
            sight.VibrationBlurScale = LpCommanderSight.GlassVibrationBlur;
            sight.VibrationShakeMultiplier = 0f;

            // What the thermal mode will be stated as. The gunner's FLIR is the one imager that renders on this
            // hull, so its mechanical raster and its palette material are what this channel is given; the
            // palette is de-greened into a copy, because the authored one draws the gunner's eyepiece in green.
            CameraSlot imager = ThermalDonor(unitRoot, sight);
            if (imager != null)
            {
                sight.MechanicalScanWidth = imager.MechanicalScanWidth;
                sight.MechanicalScanHeight = imager.MechanicalScanHeight;
                MelonLogger.Msg(Tag + " thermal mode takes the gunner's raster " + imager.MechanicalScanWidth +
                                "x" + imager.MechanicalScanHeight + " and palette '" +
                                (imager.FLIRBlitMaterialOverride == null
                                    ? "<default>"
                                    : imager.FLIRBlitMaterialOverride.name) + "'");
            }
            else
            {
                MelonLogger.Warning(Tag + " this unit carries no thermal channel to copy the imager settings from");
            }

            Renderer graticule = BuildGraticule(sight);
            KeepOutOfTheCycle(unitRoot, sight);
            LogComponents(sight);

            var rig = unitRoot.AddComponent<LpCommanderSightRig>();
            rig.Night = sight;
            rig.Head = head;
            rig.Reticle = graticule;
            rig.ReticleScale = Vector3.one;
            rig.Donor = imager;

            MelonLogger.Msg(Tag + " commander sight: '" + PathOf(sight.transform) + "' " +
                            (head == null ? "queued for the cupola sight" : "at the cupola sight") + ", " +
                            sight.DefaultFov.ToString("0.#") + " deg + " + Fields(sight.OtherFovs) +
                            " deg, daylight glass with the War Thunder graticule, T switches its imager, Z " +
                            "enters and leaves, free look keeps the vanilla aim override");
        }

        private static string Fields(float[] fields)
        {
            if (fields == null || fields.Length == 0) return "no";
            var parts = new string[fields.Length];
            for (int i = 0; i < fields.Length; i++) parts[i] = fields[i].ToString("0.#");
            return string.Join("/", parts);
        }

        /// <summary>
        /// A copy of the head-out slot, stripped of everything that is not a camera. The night sight it carries as
        /// a child is destroyed with the rest, so the copy cannot put a second night-vision channel in the world.
        /// </summary>
        private static CameraSlot CloneView(CameraSlot source, GameObject unitRoot)
        {
            if (source == null) return null;
            Transform parent = source.transform.parent != null ? source.transform.parent : unitRoot.transform;

            CameraSlot clone = UnityEngine.Object.Instantiate(source, parent, false);
            if (clone == null) return null;
            clone.name = "L2A7V_CmdSight";

            // A serialized Reparent walks its own GameObject somewhere else on Awake; the copy belongs where the
            // head-out slot is, and the rig moves it onto the sight head afterwards.
            FieldInfo reparent = AccessTools.Field(typeof(CameraSlot), "_reparentTarget");
            if (reparent != null) reparent.SetValue(clone, null);
            clone.transform.SetParent(parent, false);
            clone.transform.localPosition = source.transform.localPosition;
            clone.transform.localRotation = source.transform.localRotation;
            clone.transform.localScale = source.transform.localScale;

            foreach (CameraSlot child in clone.GetComponentsInChildren<CameraSlot>(true))
                if (child != null && child != clone) UnityEngine.Object.DestroyImmediate(child.gameObject);

            clone.LinkedNightSight = null;
            clone.LinkedDaySight = null;
            clone.IsLinkedNightSight = false;
            return clone;
        }

        /// <summary>The vehicle's own thermal channel, which is the one imager that is known to render here.</summary>
        private static CameraSlot ThermalDonor(GameObject unitRoot, CameraSlot sight)
        {
            CameraSlot fallback = null;
            foreach (CameraSlot slot in unitRoot.GetComponentsInChildren<CameraSlot>(true))
            {
                if (slot == null || slot == sight) continue;
                if (slot.VisionType != NightVisionType.Thermal) continue;
                if (slot.DoMechanicalFLIR) return slot;
                if (fallback == null) fallback = slot;
            }
            return fallback;
        }

        /// <summary>
        /// A de-greened copy of an imager's palette material. The authored one draws the gunner's eyepiece in the
        /// green the vehicle was authored with; this sight wants the same picture without the tint, so every
        /// colour property the shader admits is stated white and the copy is used in its place.
        /// </summary>
        internal static Material GrayPalette(Material donor)
        {
            if (donor == null) return null;

            Material copy = new Material(donor);
            copy.name = donor.name + " (gray)";

            Shader shader = copy.shader;
            if (shader == null) return copy;

            var stated = new List<string>();
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Color) continue;
                string name = shader.GetPropertyName(i);
                if (string.IsNullOrEmpty(name)) continue;
                string lower = name.ToLowerInvariant();
                if (!lower.Contains("color") && !lower.Contains("tint")) continue;

                copy.SetColor(name, Color.white);
                stated.Add(name);
            }

            MelonLogger.Msg(Tag + " imager palette '" + donor.name + "' copied, white-stated: " +
                            (stated.Count == 0 ? "no colour property, so the tint lives elsewhere" :
                                                string.Join(", ", stated)));
            return copy;
        }

        /// <summary>
        /// Puts the copy on the sight head without putting it *under* it: the art pass scales that pivot to zero
        /// while an eyepiece is up, and a child of it would be scaled away - taking the graticule with it. As a
        /// sibling it keeps its size, and the rig places it on the head every frame.
        /// </summary>
        internal static void AttachToHead(CameraSlot sight, Transform head)
        {
            if (sight == null || head == null) return;
            Transform parent = head.parent != null ? head.parent : head;
            sight.transform.SetParent(parent, true);
            sight.transform.position = head.position;
            sight.transform.localScale = Vector3.one;
        }

        /// <summary>
        /// The sight must not become a C-key stop. It is created after the unit built its designated slot list, so
        /// it is only ever entered through the Z key; this makes sure of it if a list is ever rebuilt.
        /// </summary>
        private static void KeepOutOfTheCycle(GameObject unitRoot, CameraSlot sight)
        {
            sight.DirectAttachOnly = true;
            if (DesignatedSlots == null) return;

            GHPC.Unit unit = unitRoot.GetComponent<GHPC.Unit>();
            if (unit == null) return;
            CameraSlot[] slots = DesignatedSlots.GetValue(unit) as CameraSlot[];
            if (slots == null || Array.IndexOf(slots, sight) < 0) return;

            var kept = new List<CameraSlot>(slots.Length);
            foreach (CameraSlot slot in slots)
                if (slot != null && slot != sight) kept.Add(slot);
            DesignatedSlots.SetValue(unit, kept.ToArray());
        }

        /// <summary>
        /// Puts the War Thunder commander's graticule on the sight. The plate is black etched glass, carried by the
        /// illumination light the way the old sight's glass channel carried it.
        /// </summary>
        private static ReticleMesh FitCommanderPlate(CameraSlot sight, GameObject unitRoot)
        {
            ReticleMesh mesh = sight.GetComponentInChildren<ReticleMesh>(true);
            if (mesh == null) mesh = CloneDonorReticle(sight, unitRoot);

            if (mesh == null)
            {
                MelonLogger.Warning(Tag + " no reticle mesh on the copied view and none on the gunner's optics, " +
                                    "so the commander's plate is not fitted");
                return null;
            }

            mesh.reticleSO = LpCommanderSight.OpticalPlate;
            SkinnedMeshRenderer renderer = mesh.GetComponent<SkinnedMeshRenderer>();
            if (renderer != null) renderer.sharedMesh = null;
            mesh.Load();
            mesh.GetLight(ReticleTree.Light.Type.NightIllumination, out float cached);
            mesh.SetLight(ReticleTree.Light.Type.NightIllumination,
                float.IsNaN(cached) || cached <= 0f ? 0f : 1f);

            ReportGraticule(mesh);

            MelonLogger.Msg(Tag + " commander plate '" + mesh.reticleSO.name + "' fitted at " +
                            sight.DefaultFov.ToString("0.#") + " deg + " + Fields(sight.OtherFovs) + " deg");
            return mesh;
        }

        /// <summary>One line about the built graticule, so "no reticle on screen" can be read out of a log.</summary>
        private static void ReportGraticule(ReticleMesh mesh)
        {
            SkinnedMeshRenderer renderer = mesh.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null)
            {
                MelonLogger.Warning(Tag + " the graticule has no SkinnedMeshRenderer");
                return;
            }

            Mesh built = renderer.sharedMesh;
            MelonLogger.Msg(Tag + " graticule: active=" + mesh.gameObject.activeInHierarchy +
                            " renderer=" + renderer.enabled +
                            " post=" + (mesh.GetComponent<PostMeshComp>() != null) +
                            " layer=" + mesh.gameObject.layer +
                            " mesh=" + (built == null
                                ? "<none>"
                                : built.name + " " + built.vertexCount + " verts " + built.subMeshCount +
                                  " submesh(es) bounds " + built.bounds.center + "/" + built.bounds.extents) +
                            " materials=" + (renderer.sharedMaterials == null ? 0 : renderer.sharedMaterials.Length) +
                            " scale=" + mesh.transform.localScale);
        }

        /// <summary>
        /// The copied view carries no graticule, so one is copied from the gunner's optics. A reticle is authored
        /// at the eye it belongs to: its mesh is built on a simulated focal plane in its own local space, and
        /// ReticleMesh.Load resets any offset it finds, so the copy sits at the slot origin at unit scale.
        /// </summary>
        private static ReticleMesh CloneDonorReticle(CameraSlot sight, GameObject unitRoot)
        {
            ReticleMesh donor = null;
            CameraSlot donorSlot = null;

            foreach (UsableOptic optic in unitRoot.GetComponentsInChildren<UsableOptic>(true))
            {
                if (optic == null || optic.reticleMesh == null) continue;
                if (optic.slot == null || optic.slot == sight) continue;

                bool thermal = optic.slot.VisionType == NightVisionType.Thermal;
                if (donor == null || thermal)
                {
                    donor = optic.reticleMesh;
                    donorSlot = optic.slot;
                }
                if (thermal) break;
            }

            if (donor == null || donorSlot == null) return null;

            ReticleMesh clone = UnityEngine.Object.Instantiate(donor, sight.transform, false);
            clone.name = "L2A7V_CmdReticle";
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;

            // A reticle is drawn by PostMeshComp, which registers its renderer with the post pass from OnEnable.
            // A copy of an inactive object never runs OnEnable, so it is neither registered nor drawn - switched
            // on here before its plate is built.
            if (!clone.gameObject.activeSelf) clone.gameObject.SetActive(true);

            MelonLogger.Msg(Tag + " commander graticule carried over from '" + PathOf(donor.transform) +
                            "', framed against " + sight.DefaultFov.ToString("0.#") + " deg");
            return clone;
        }

        /// <summary>
        /// Draws the War Thunder commander's graticule as our own mesh in front of the eye.
        ///
        /// Deliberately not a ReticleMesh: that system registers every mesh with a global post pass keyed by a
        /// fixed number of render-order slots, so a copied mesh competes with the vehicle's own reticles - with
        /// several 2A7Vs in the scene the ones that lose that race disappear. A plain quad mesh with an unlit
        /// material has no such list: it draws for this sight alone and cannot disturb anyone else's picture.
        ///
        /// The geometry is the plate the old sight drew: a horizontal measuring scale with a gap at the centre,
        /// ticks every 2 mrad (longer at the tens) and dashes carried to the top of the window.
        /// </summary>
        private static Renderer BuildGraticule(CameraSlot sight)
        {
            const float distance = 1f;
            float half = Mathf.Tan(LpCommanderSight.CommanderFov * 0.5f * Mathf.Deg2Rad) * distance;
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.7778f;
            float top = half / Mathf.Max(1f, aspect);
            const float stroke = 0.0011f;
            const float step = 0.002f;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            void Bar(float x1, float y1, float x2, float y2, float width)
            {
                float dx = x2 - x1, dy = y2 - y1;
                float length = Mathf.Sqrt(dx * dx + dy * dy);
                if (length < 1e-6f) return;

                float nx = -dy / length * width * 0.5f;
                float ny = dx / length * width * 0.5f;
                int at = vertices.Count;
                vertices.Add(new Vector3(x1 + nx, y1 + ny, distance));
                vertices.Add(new Vector3(x1 - nx, y1 - ny, distance));
                vertices.Add(new Vector3(x2 - nx, y2 - ny, distance));
                vertices.Add(new Vector3(x2 + nx, y2 + ny, distance));
                triangles.Add(at);
                triangles.Add(at + 1);
                triangles.Add(at + 2);
                triangles.Add(at);
                triangles.Add(at + 2);
                triangles.Add(at + 3);
            }

            Bar(-half, 0f, -0.004f, 0f, stroke);
            Bar(0.004f, 0f, half, 0f, stroke);

            for (float x = step; x <= half; x += step)
            {
                bool major = Mathf.Abs(Mathf.Repeat(x, step * 5f)) < step * 0.25f;
                float h = major ? 0.0026f : 0.0014f;
                Bar(x, -h, x, h, stroke * 0.8f);
                Bar(-x, -h, -x, h, stroke * 0.8f);
            }

            for (float y = 0.003f; y <= top; y += 0.003f)
            {
                float w = Mathf.Abs(Mathf.Repeat(y, 0.015f)) < 0.002f ? 0.0032f : 0.0018f;
                Bar(-w, y, w, y, stroke * 0.8f);
            }

            var mesh = new Mesh();
            mesh.name = "L2A7V_CMDR_mesh";
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            var node = new GameObject("L2A7V_CmdGraticule");
            node.transform.SetParent(sight.transform, false);
            node.transform.localPosition = Vector3.zero;
            node.transform.localRotation = Quaternion.identity;
            node.transform.localScale = Vector3.one;
            node.layer = sight.gameObject.layer;
            node.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer renderer = node.AddComponent<MeshRenderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("GUI/Text Shader");

            var material = new Material(shader);
            material.color = new Color(0f, 0f, 0f, 1f);
            material.renderQueue = 3999;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            MelonLogger.Msg(Tag + " commander graticule drawn: " + vertices.Count + " verts over " +
                            (Mathf.Atan(half / distance) * 2f * Mathf.Rad2Deg).ToString("0.#") +
                            " deg, shader " + (shader == null ? "<missing>" : shader.name) +
                            ", layer " + node.layer);
            return renderer;
        }

        /// <summary>Everything the copied view brought with it, once, so a log explains its behaviour.</summary>
        private static void LogComponents(CameraSlot sight)
        {
            var names = new List<string>();
            foreach (Component component in sight.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                names.Add(component.GetType().Name);
            }
            names.Sort(StringComparer.Ordinal);
            MelonLogger.Msg(Tag + " sight components: " + string.Join(", ", names));
        }

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

        private static string PathOf(Transform node)
        {
            if (node == null) return "<null>";
            string path = node.name;
            for (Transform parent = node.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }
    }

    /// <summary>
    /// Runs the commander's sight: Z opens and closes it, T switches its imager, the head follows the view, and the
    /// graticule is drawn only while the sight is up.
    /// </summary>
    public class LpCommanderSightRig : MonoBehaviour
    {
        private const string Tag = "[L2A7V][cmdr-sight]";

        internal CameraSlot Night;
        internal Transform Head;
        internal CameraSlot Donor;
        internal Renderer Reticle;
        internal Vector3 ReticleScale = Vector3.one;

        /// <summary>Elevation the head is allowed to take. The housing meets the cupola ring past this.</summary>
        private const float PitchLimit = 12f;

        private const float FollowRate = 14f;

        /// <summary>The game's per-slot wiring; private, and the only place a slot's imager settings are read.</summary>
        private static readonly MethodInfo ThisActiveChanged =
            AccessTools.Method(typeof(CameraSlot), "ThisActiveChanged");

        private bool _wasActive;
        private bool _thermal;
        private bool _mounted;
        private float _nextLook;

        private void Update()
        {
            if (Night == null) return;

            if (!_mounted)
            {
                if (Time.unscaledTime < _nextLook) return;
                _nextLook = Time.unscaledTime + 0.5f;

                if (Head == null) Head = LpCommanderSight.FindHead(gameObject);
                if (Head == null) return;

                LpCommanderNvg.AttachToHead(Night, Head);
                _mounted = true;
                MelonLogger.Msg(Tag + " commander sight mounted on '" + Head.name + "'");
                return;
            }

            // The sight sits beside the art subtree that is scaled away in an eyepiece view, so its place on the
            // head is asserted here instead of being inherited from a parent transform.
            if (Head != null) Night.transform.position = Head.position;

            PlayerInput input = PlayerInput.Instance;
            if (input == null) return;
            GHPC.Unit player = LpSmoke.CurrentPlayerUnit(input);
            if (player == null || player.gameObject != gameObject) return;

            bool inside = CameraSlot.ActiveInstance == Night;
            if (Input.GetKeyDown(KeyCode.Z))
            {
                CameraManager manager = CameraManager.Instance;
                if (manager == null) return;

                if (inside)
                {
                    manager.ResetTempAttach();
                    MelonLogger.Msg(Tag + " commander sight closed (Z)");
                }
                else
                {
                    manager.TempAttachToSlot(Night);
                    MelonLogger.Msg(Tag + " commander sight opened (Z)");
                }
                return;
            }

            if (inside && Input.GetKeyDown(KeyCode.T)) ToggleImager();
        }

        /// <summary>
        /// Switches the sight between glass and the vehicle's imager. The imager is started and stopped by the
        /// game's own per-slot wiring, so the picture is produced exactly the way an authored thermal is.
        /// </summary>
        private void ToggleImager()
        {
            FLIRCamera imager = FLIRCamera.Instance;
            if (imager == null)
            {
                MelonLogger.Warning(Tag + " T ignored: this scene has no thermal camera");
                return;
            }

            if (!_thermal)
            {
                Night.VisionType = NightVisionType.Thermal;
                // Staring, not the mechanical raster: this hull's authored imager is a scanning one, and its
                // raster draws as bands at this sight's field. The staring path renders a full frame.
                Night.DoMechanicalFLIR = false;
                Night.OverrideFLIRResolution = true;
                Vector2Int size = LpConfig.ThermalSize;
                Night.FLIRWidth = size.x;
                Night.FLIRHeight = size.y;
                Night.FLIRFilterMode = FilterMode.Bilinear;
                Night.CanToggleFlirPolarity = true;
                Night.BaseBlur = 0f;
                Night.VibrationBlurScale = 0f;
                Night.VibrationShakeMultiplier = 0.1f;
                // The palette the vehicle ships ("FLIR Green") is the only one that produces a picture here; the
                // imager's own default material is the flat cyan this channel had before.
                Night.FLIRBlitMaterialOverride = Donor != null ? Donor.FLIRBlitMaterialOverride : null;

                Fire(true);
                _thermal = true;
                MelonLogger.Msg(Tag + " imager on (" + size.x + "x" + size.y + ", palette " +
                                (Night.FLIRBlitMaterialOverride == null
                                    ? "<default>"
                                    : Night.FLIRBlitMaterialOverride.name) + "), T back to the glass");
                return;
            }

            SetGlass();
            _thermal = false;
            MelonLogger.Msg(Tag + " back on the glass");
        }

        /// <summary>Puts the slot back in the state the glass channel is authored with, imager off.</summary>
        private void SetGlass()
        {
            Night.VisionType = NightVisionType.None;
            Night.DoMechanicalFLIR = false;
            Night.OverrideFLIRResolution = false;
            Night.CanToggleFlirPolarity = false;
            Night.FLIRBlitMaterialOverride = null;
            Night.BaseBlur = 0f;
            Night.VibrationBlurScale = LpCommanderSight.GlassVibrationBlur;
            Night.VibrationShakeMultiplier = 0f;

            // With neither a thermal type nor a mechanical flag the game's branch no longer runs, so the imager is
            // switched off here; its OnDisable is what puts the main camera's render state back.
            Fire(false);
            FLIRCamera imager = FLIRCamera.Instance;
            if (imager != null) imager.enabled = false;
        }

        private void Fire(bool on)
        {
            if (ThisActiveChanged == null) return;
            try
            {
                ThisActiveChanged.Invoke(Night, new object[] { on });
            }
            catch (Exception error)
            {
                Exception cause = error.InnerException ?? error;
                MelonLogger.Warning(Tag + " the game's slot wiring threw: " + cause.Message);
            }
        }

        private void LateUpdate()
        {
            if (Night == null || Head == null)
            {
                ShowReticle(false);
                _wasActive = false;
                return;
            }
            if (CameraSlot.ActiveInstance != Night)
            {
                // Leaving the sight always leaves it on the glass, so the next entry starts where it opened.
                if (_thermal) SetGlass();
                _thermal = false;
                ShowReticle(false);
                _wasActive = false;
                return;
            }

            ShowReticle(true);

            Camera view = CameraManager.MainCam;
            if (view == null) return;

            Transform turret = Head.parent;
            Vector3 direction = view.transform.forward;
            Vector3 local = turret != null ? turret.InverseTransformDirection(direction) : direction;
            if (local.sqrMagnitude < 1e-6f) return;

            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float elevation = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;

            // The first frame snaps (the head is still where the last look left it), the rest trail a frame or two,
            // which is what a power-traversed sight looks like.
            float blend = _wasActive ? Mathf.Clamp01(Time.deltaTime * FollowRate) : 1f;
            _wasActive = true;

            Head.localRotation = Quaternion.Slerp(Head.localRotation,
                Quaternion.Euler(-Mathf.Clamp(elevation, -PitchLimit, PitchLimit), yaw, 0f), blend);

            // The graticule is a mesh hanging off this slot and the camera is free-look, so the slot itself faces
            // where the camera looks. Without this the plate would slide out of the window as the head turned.
            Night.transform.rotation = view.transform.rotation;
        }

        /// <summary>
        /// The graticule is our own mesh, not one the game switches with the slot. Scaled, not disabled: the
        /// vehicle's own visibility pass switches renderers back on, and one switched off here would be on again a
        /// frame later - which is the graticule that hangs in the air outside the sight.
        /// </summary>
        private void ShowReticle(bool show)
        {
            if (Reticle == null) return;

            // Enabled every frame: the renderer arrives switched off with the donor's reticle, and the vehicle's
            // own visibility pass keeps re-deciding renderer flags. Hiding is done by scale, which no pass
            // re-derives - a renderer switched off here would be on again a frame later, which is the graticule
            // that hangs in the air outside the sight.
            if (!Reticle.enabled) Reticle.enabled = true;

            Vector3 scale = show ? ReticleScale : Vector3.zero;
            if (Reticle.transform.localScale != scale) Reticle.transform.localScale = scale;
        }
    }
}

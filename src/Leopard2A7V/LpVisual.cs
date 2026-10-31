using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC.Thermals;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Builds the imported Leopard 2A7V art on the bones the donor's own art is bound to.
    ///
    /// The prefab dump settles which transforms those are: the AimablePlatform on "---TURRET SCRIPTS---"
    /// poses leo2_rig/hull/turret and the one on "---MAIN GUN SCRIPTS---/Rh-120_L/44" poses its child
    /// mantlet, while the "TURRET" and "GUN" nodes under M1IP_interior only carry LateFollow and are
    /// therefore passengers. Every other turret-mounted thing (the armour colliders, the interior shell,
    /// the optics, the sight covers, the camo nets) is re-parented or re-followed onto those same two
    /// bones, so hanging the imported turret and gun on them leaves the whole donor vehicle logic intact
    /// and lines our art up with the colliders and eyepieces that were authored against it.
    ///
    /// Each group is placed so that its own pivot point sits exactly on the origin of the bone that
    /// turns it. That matters: the turret ring is 7.7 cm aft of and 3.2 cm below the donor's bone, and
    /// a group hung off-centre would swing through an arc of that radius as the turret traverses.
    /// </summary>
    public static class LpVisual
    {
        public static Material TemplateMaterial;

        /// <summary>Submesh order per group cache key, published by LpAssets.BuildMesh.</summary>
        public static readonly Dictionary<string, List<int>> GroupMaterials = new Dictionary<string, List<int>>();

        private const string RigRoot = "leo2_rig";
        private const string HullBone = "hull";
        private const string TurretBone = "turret";
        private const string MantletBone = "mantlet";
        private const string VehicleShaderName = "GHPC/VehicleShader";

        private static int _visualLayer = -1;
        private static readonly FieldInfo HeatBackingField = AccessTools.Field(typeof(HeatSource), "_heat");

        /// <summary>The pivots of the last unit built, in the order LpSightView needs them.</summary>
        public sealed class Assembly
        {
            public Transform Hull;
            public Transform Turret;
            public Transform Gun;
            public Transform CommanderGear;
            public Transform RoofGunYaw;
            public Transform RoofGunPitch;

            /// <summary>
            /// The machine gun's own art, kept separate from its pivots because a sight view hides art by
            /// scaling it down, and shrinking a pivot would take the muzzle, the rack and the eyepiece that
            /// hang under it along with it.
            /// </summary>
            public Transform RoofGunYawArt;
            public Transform RoofGunPitchArt;

            public GameObject Marker;
        }

        public static bool IsReady => ObjModel.Loaded && !ObjModel.Failed;

        public static Assembly Convert(GameObject unitRoot)
        {
            if (!IsReady) return null;
            if (FindDeep(unitRoot.transform, LpConfig.VisualRootName) != null) return null;
            if (!CaptureTemplateMaterial(unitRoot)) return null;
            int repaired = LpAssets.RepairShader(TemplateMaterial.shader);
            repaired += LpInterior.RepairShader(TemplateMaterial.shader);
            if (repaired > 0)
                MelonLogger.Warning($"[L2A7V] re-pointed {repaired} cached material(s) onto " +
                                    TemplateMaterial.shader.name + ", the previous bundle copy had died");

            Transform unit = unitRoot.transform;
            Transform rig = FindDeep(unit, RigRoot);
            Transform hullBone = rig == null ? null : rig.Find(HullBone);
            Transform turretBone = hullBone == null ? null : hullBone.Find(TurretBone);
            Transform mantletBone = turretBone == null ? null : turretBone.Find(MantletBone);

            if (turretBone == null || mantletBone == null)
            {
                MelonLogger.Error("[L2A7V] the donor rig bones " + RigRoot + "/" + HullBone + "/" + TurretBone +
                                  "/" + MantletBone + " are missing, so the imported art has nothing to ride");
                return null;
            }
            if (rig != null) MelonLogger.Msg($"[L2A7V] drive bones: turret {Describe(turretBone)}, gun {Describe(mantletBone)}");

            // The art's own origin, which is what the road-wheel fit aligns, rides the hull bone so the
            // imported hull follows the suspension pose exactly like the donor's skinned hull art does.
            // The turret and gun anchors stay in model space: BuildMesh subtracts them from the model's
            // own vertex positions, so only the hull pivot carries the unit-frame offset. Reading them
            // against the bones leaves the 7.7 cm ring and 7.5 cm trunnion residuals, which is the shift
            // this design accepts at the hidden joints rather than in the visible hull.
            Vector3 offset = LpConfig.Offset;
            Vector3 ring = LpConfig.TurretAxis;
            Vector3 trunnion = LpConfig.GunAxis;

            var result = new Assembly();
            try
            {
                var marker = new GameObject(LpConfig.VisualRootName);
                marker.transform.SetParent(unit, false);
                result.Marker = marker;

                Transform hullBoneOrRoot = hullBone != null ? hullBone : unit;
                Vector3 hullLocal = hullBone != null
                    ? hullBone.InverseTransformPoint(unit.TransformPoint(offset))
                    : offset;
                result.Hull = MakePivot(hullBoneOrRoot, "L2A7V_HullPivot", hullLocal);
                AttachGroup(result.Hull, "Hull", LpGroup.Hull, Vector3.zero);

                if (!LpConfig.UseDonorRunningGear)
                    LpRunningGear.Attach(result.Hull, unit);

                result.Turret = MakePivot(turretBone, "L2A7V_TurretPivot", Vector3.zero);
                AttachGroup(result.Turret, "Turret", LpGroup.Turret, ring);

                // The cupola sight tube and the anti-aircraft gun. The donor rotates its own PERI through
                // the "peri" bone, but that bone is a skin bind of the turret art we hide, so this group
                // rides the turret instead: the tube stays with the hatch rather than with his view.
                // The mount and receiver leave the bucket when the machine gun is made drivable, because
                // they turn on their own two axes rather than with the cupola.
                result.CommanderGear = MakePivot(turretBone, "L2A7V_CupolaPivot", Vector3.zero);
                AttachParts(result.CommanderGear, "CommanderGear",
                    part => part.Group == LpGroup.CommanderGear &&
                          !(LpConfig.AddRoofGun && LpRoofGun.IsRoofGunPart(part.ObjectName)), ring);

                result.Gun = MakePivot(mantletBone, "L2A7V_GunPivot", Vector3.zero);
                AttachGroup(result.Gun, "Gun", LpGroup.Gun, trunnion);

                LpRoofGun.Apply(unitRoot, result, ring);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[L2A7V] visual build failed: " + ex);
                if (result.Marker != null) UnityEngine.Object.Destroy(result.Marker);
                return null;
            }

            LpInterior.Attach(unitRoot, result);
            LpSightView.Attach(unitRoot, result);
            LpThermal.Apply(unitRoot);
            LpBaseRig.Apply(unit);
            LpDonorArt.Request(unitRoot);
            LpArmorLog.Apply(unitRoot);
            ApplyHeat(result);
            LpDiagnostics.DumpOnce(unitRoot);
            return result;
        }

        /// <summary>
        /// Creates the node a group hangs from. A zero local position puts the pivot on the drive bone's
        /// own origin, which is where that bone's rotation has no lever arm and therefore no arc.
        /// </summary>
        public static Transform MakePivot(Transform bone, string name, Vector3 localPosition)
        {
            var node = new GameObject(name);
            Transform t = node.transform;
            t.SetParent(bone, false);
            t.localPosition = localPosition;
            t.localRotation = Quaternion.identity;

            Vector3 parentScale = bone.lossyScale;
            var compensation = new Vector3(
                SafeReciprocal(parentScale.x), SafeReciprocal(parentScale.y), SafeReciprocal(parentScale.z));
            if (compensation != Vector3.one)
                MelonLogger.Warning($"[L2A7V] {bone.name} is scaled {parentScale}, {name} compensates with {compensation}");
            t.localScale = compensation;
            return t;
        }

        private static float SafeReciprocal(float value)
        {
            return Mathf.Abs(value) < 1e-5f ? 1f : 1f / value;
        }

        private static void AttachGroup(Transform parent, string name, LpGroup group, Vector3 anchor)
        {
            AttachParts(parent, name, part => part.Group == group, anchor);
        }

        /// <summary>
        /// Builds and attaches whatever geometry the selector accepts. A group is only a bucket of parts, so
        /// a caller that needs a bucket split - the roof gun, whose mount turns one way and whose receiver
        /// another - selects the parts by name instead of taking the whole group.
        /// </summary>
        public static MeshRenderer AttachParts(Transform parent, string name, System.Predicate<ObjGeometry> select,
            Vector3 anchor)
        {
            return AttachMesh(parent, name, LpAssets.BuildWhere(select, name, anchor));
        }

        /// <summary>
        /// Puts a finished mesh on the tree as a shadow-casting, probe-lit piece of our own art. The material
        /// list is resolved by node name, which is the key LpAssets published the submesh order under. The
        /// renderer comes back so a caller whose materials are not part of that table - the scrolling tread,
        /// which clones the donor's sheet instead of the painted template - can hand it over.
        /// </summary>
        public static MeshRenderer AttachMesh(Transform parent, string name, Mesh mesh)
        {
            if (mesh == null)
            {
                MelonLogger.Warning("[L2A7V] no geometry for " + name);
                return null;
            }

            var node = new GameObject(name);
            node.transform.SetParent(parent, false);
            node.layer = _visualLayer >= 0 ? _visualLayer : parent.gameObject.layer;

            var filter = node.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = node.AddComponent<MeshRenderer>();
            List<int> order;
            if (GroupMaterials.TryGetValue(name, out order))
            {
                var materials = new Material[order.Count];
                for (int i = 0; i < order.Count; i++)
                {
                    materials[i] = LpAssets.GetMaterial(order[i], name);
                    if (materials[i] == null)
                        MelonLogger.Warning("[L2A7V] material unavailable for index " + order[i]);
                }
                renderer.sharedMaterials = materials;
            }
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
            return renderer;
        }

        /// <summary>
        /// Clones the donor's painted vehicle material so the imported art inherits its shader, keywords,
        /// render queue and layer in one step. Scored on the object path because the Leopard's paint is
        /// spread over "LEO2_mesh/hull 2A0" and "turret A1" rather than one obvious shell.
        ///
        /// The re-capture test matters: GHPC unloads the bundle that holds GHPC/VehicleShader when a mission
        /// ends, but our static template reference and our cached material clones survive it. A Material whose
        /// shader was unloaded reports Hidden/InternalErrorShader and Unity draws it magenta, which is why a
        /// second spawn in the same session used to turn the whole vehicle pink; so the cache is only trusted
        /// while it still points at a live copy of that shader.
        /// </summary>
        private static bool CaptureTemplateMaterial(GameObject unitRoot)
        {
            if (TemplateMaterial != null && TemplateMaterial.shader != null &&
                string.Equals(TemplateMaterial.shader.name, VehicleShaderName, StringComparison.Ordinal))
                return true;

            Renderer best = null;
            Material bestMaterial = null;
            int bestScore = int.MinValue;

            foreach (Renderer renderer in unitRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (IsImportedArt(renderer.transform)) continue;
                Material material = null;
                foreach (Material candidate in renderer.sharedMaterials)
                {
                    if (candidate != null && candidate.shader != null &&
                        candidate.shader.name == VehicleShaderName)
                    {
                        material = candidate;
                        break;
                    }
                }
                if (material == null) continue;

                string materialName = material.name.ToLowerInvariant();
                string path = PathOf(renderer.transform).ToLowerInvariant();
                int score = 1;
                if (path.Contains("leo2_mesh")) score += 8;
                if (materialName.StartsWith("leo2")) score += 4;
                if (path.Contains("hull") || path.Contains("turret")) score += 2;
                if (materialName.Contains("tricolour")) score += 1;
                if (materialName.Contains("void") || materialName.Contains("see-through")) score -= 12;
                if (path.Contains("interior") || path.Contains("collider") || path.Contains("hitzone")) score -= 10;
                if (path.Contains("running gear") || path.Contains("track")) score -= 3;
                if (score <= bestScore) continue;
                bestScore = score;
                best = renderer;
                bestMaterial = material;
            }

            if (best == null)
            {
                Shader live = Shader.Find(VehicleShaderName);
                if (live == null || TemplateMaterial == null)
                {
                    MelonLogger.Error("[L2A7V] could not find a live " + VehicleShaderName +
                                      " to clone, so this spawn is left as the donor");
                    return false;
                }
                TemplateMaterial.shader = live;
                MelonLogger.Warning("[L2A7V] no donor renderer reported " + VehicleShaderName +
                                    ", revived the cached template through Shader.Find");
                return true;
            }

            TemplateMaterial = bestMaterial;
            _visualLayer = best.gameObject.layer;
            LpAssets.ConfigureFromTemplate(TemplateMaterial);
            MelonLogger.Msg($"[L2A7V] shader template '{TemplateMaterial.name}' / " +
                            $"'{TemplateMaterial.shader.name}' from {PathOf(best.transform)}, layer {_visualLayer}");
            return true;
        }

        private static void ApplyHeat(Assembly art)
        {
            if (!LpConfig.AddHeatSource || art.Hull == null) return;
            var source = art.Hull.gameObject.AddComponent<HeatSource>();
            if (HeatBackingField != null) HeatBackingField.SetValue(source, float.NaN);
            source.Heat = 0.85f;
        }

        /// <summary>True for the nodes this mod created, which the donor-visual pass must never touch.</summary>
        public static bool IsImportedArt(Transform transform)
        {
            Transform node = transform;
            int guard = 0;
            while (node != null && guard++ < 32)
            {
                if (node.name.StartsWith("L2A7V_", StringComparison.Ordinal)) return true;
                node = node.parent;
            }
            return false;
        }

        private static string Describe(Transform bone)
        {
            Vector3 p = bone.position;
            return $"{bone.name} at ({p.x:0.000},{p.y:0.000},{p.z:0.000}) scale {bone.lossyScale}";
        }

        private static string PathOf(Transform transform)
        {
            var builder = new System.Text.StringBuilder(transform.name);
            Transform parent = transform.parent;
            int guard = 0;
            while (parent != null && guard++ < 24)
            {
                builder.Insert(0, parent.name + "/");
                parent = parent.parent;
            }
            return builder.ToString();
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}

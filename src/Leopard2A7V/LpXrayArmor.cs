using System;
using System.Collections.Generic;
using System.IO;
using GHPC;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Builds the vehicle's armour from a baked copy of the 2A7V's x-ray model.
    ///
    /// The armour geometry started life as the x-ray OBJ that comes with the model - composite_armor_hull_01..05,
    /// composite_armor_turret_01..11, the two firewalls and the spall liners - but reading eight megabytes of
    /// text on the first spawn was slow and left every unit to allocate its own meshes. The asset shipped with
    /// the mod now is those groups already converted: a small binary with names, vertices and indices only,
    /// and one shared Mesh per group behind it, which is what a prefab would have given us without needing a
    /// Unity editor in the build.
    /// </summary>
    public static class LpXrayArmor
    {
        internal const string Tag = "[L2A7V][armour]";
        private const string TagOld = "[L2A7V][armour]";

        private const string FileName = "l2a7v_armor.bin";

        private const string Marker = "L2A7V_XrayArmor";

        /// <summary>Plates a composite array is split into, so a round is stopped layer by layer.</summary>
        private const int LayeredArrayCount = 3;

        private static int _logged;
        private static readonly System.Collections.Generic.List<Bounds> OurBounds =
            new System.Collections.Generic.List<Bounds>();

        private static List<Group> _groups;

        private static readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);

        private sealed class Group
        {
            internal string Name;
            internal Mesh Mesh;
        }

        internal static string ModFolder
        {
            get
            {
                string folder = Path.Combine(MelonEnvironment.ModsDirectory, "Leopard2A7V");
                return Directory.Exists(folder) ? folder : MelonEnvironment.ModsDirectory;
            }
        }

        public static bool Build(GameObject unitRoot)
        {
            if (unitRoot == null) return false;
            List<Group> groups = Load();
            if (groups == null || groups.Count == 0) return false;

            // The pieces ride the same visual bones the imported art rides, and are placed in the same frame
            // the art is placed in. Parenting them to the collider roots put them in world orientation near the
            // world origin instead of under the tank, which is why a hit anywhere met no armour.
            Transform hull = FindBone(unitRoot, "hull") ?? FindColliderRoot(unitRoot, "hull");
            Transform turret = FindBone(unitRoot, "turret") ?? FindColliderRoot(unitRoot, "turret");
            Transform gun = FindBone(unitRoot, "Rh-120_L/44") ?? FindBone(unitRoot, "mantlet");
            if (hull == null)
            {
                MelonLogger.Warning(Tag + " no hull bone on " + unitRoot.name + "; the donor armour stays");
                return false;
            }

            // The axis each group is authored around, exactly as the art pass uses it: the hull pivot carries
            // the unit-frame offset, while the turret and gun groups are built with their ring/trunnion
            // subtracted from the vertices, so the mesh sits at -axis under its bone.
            Unit unit = LpFind.Unit(unitRoot);
            Transform frame = unit != null ? unit.transform : unitRoot.transform;
            Vector3 hullLocal = hull.InverseTransformPoint(frame.TransformPoint(LpConfig.Offset));
            Vector3 turretLocal = -LpConfig.TurretAxis;
            Vector3 gunLocal = -LpConfig.GunAxis;
            MelonLogger.Msg(Tag + " anchors: hull " + hullLocal.ToString("0.00") + " turret " +
                            turretLocal.ToString("0.00") + " gun " + gunLocal.ToString("0.00"));

            OurBounds.Clear();

            // The donor's authored plates carry an armour codex: whether a piece normalises, shatters long
            // rods, ricochets, and how it spalls. A plate built from scratch has none of that, so it behaves
            // like bare steel whatever its RHA figures say. Copying the matching codex is what makes our
            // arrays behave like the arrays they replace.
            DonorCodex = CollectCodex(unitRoot);

            int built = 0, noZone = 0;
            foreach (Group group in groups)
            {
                bool onGun = GroupOnGun(group.Name);
                bool onTurret = !onGun &&
                    group.Name.IndexOf("turret", StringComparison.OrdinalIgnoreCase) >= 0;
                Transform bone = onGun && gun != null ? gun : onTurret && turret != null ? turret : hull;
                Vector3 anchor = onGun && gun != null ? gunLocal : onTurret && turret != null ? turretLocal : hullLocal;

                float ke, ce;
                string zoneName;
                int dataLayers;
                if (!LpArmor.TryZoneForXrayGroup(group.Name, out ke, out ce, out zoneName))
                {
                    if (!LpData.TryArmour(group.Name, out ke, out ce, out dataLayers))
                    {
                        noZone++;
                        continue;
                    }
                    zoneName = group.Name;
                }
                else if (LpData.TryArmour(group.Name, out float dataKe, out float dataCe, out dataLayers))
                {
                    // a line in L2A7V_data.txt always wins over the compiled table
                    ke = dataKe;
                    ce = dataCe;
                }

                // Composite arrays go in as a stack, the way the vanilla LEO2A3 and M1 author their armour:
                // the solver subtracts each plate's LOS thickness from the round's remaining penetration, so
                // equal layers add up to exactly the same total and the round is stopped layer by layer.
                // Liners and firewalls stay single - they are one plate in the real vehicle too.
                int layers = group.Name.StartsWith("composite_armor_", StringComparison.OrdinalIgnoreCase)
                    ? LayeredArrayCount
                    : 1;
                if (LpData.TryArmour(group.Name, out _, out _, out int overrideLayers) && overrideLayers > 0)
                    layers = overrideLayers;

                if (BuildOne(bone, anchor, group, ke, ce, zoneName, layers, CodexFor(group.Name))) built++;
            }

            int retired = built > 0 ? RetireDonorArmour(unitRoot) : 0;
            MelonLogger.Msg(Tag + " built " + built + " piece(s) from the baked armour" +
                            (noZone > 0 ? ", " + noZone + " group(s) had no zone entry" : string.Empty) +
                            (retired > 0 ? ", retired " + retired + " donor plate(s)" : string.Empty));
            return built > 0;
        }

        private static bool GroupOnGun(string name)
        {
            return name.IndexOf("breech", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("barrel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("_gun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.StartsWith("gun_", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Switches the donor's own armour off once this mod's plates are in place.
        ///
        /// Without this the vehicle wears both sets: the donor's authored plates were never removed, so a round
        /// had to beat the donor's armour and then the 2A7V's, which is twice the protection the x-ray figures
        /// describe. The T-90M mod does the same thing from a table ("off|Upper Front Plate"); here the rule is
        /// simply everything that is not one of ours.
        /// </summary>
        private static int RetireDonorArmour(GameObject unitRoot)
        {
            int retired = 0, kept = 0;
            foreach (UniformArmor plate in unitRoot.GetComponentsInChildren<UniformArmor>(true))
            {
                if (plate == null) continue;
                if (plate.gameObject.name.StartsWith(Marker, System.StringComparison.Ordinal)) continue;

                // Only the plates this mod actually replaces are switched off. Retiring everything was
                // simplest, but it also stripped the parts the x-ray export does not cover - the mantlet, the
                // hull floor, the skirts - and a hit there then met no armour at all.
                bool named = LpData.IsVanillaOff(plate.name);
                if (!named && !Covered(plate)) { kept++; continue; }

                foreach (Collider collider in plate.GetComponentsInChildren<Collider>(true))
                    if (collider != null) collider.enabled = false;
                plate.enabled = false;
                retired++;
            }
            foreach (VariableArmor measured in unitRoot.GetComponentsInChildren<VariableArmor>(true))
            {
                if (measured == null) continue;
                Collider collider = measured.GetComponentInChildren<Collider>(true);
                if (collider == null) continue;
                bool named = LpData.IsVanillaOff(measured.name);
                bool ours = false;
                foreach (Bounds bounds in OurBounds)
                {
                    Bounds grown = bounds;
                    grown.Expand(1.2f);
                    if (grown.Intersects(collider.bounds)) { ours = true; break; }
                }
                if (!named && !ours) continue;
                collider.enabled = false;
                measured.enabled = false;
                retired++;
            }
            if (kept > 0)
                MelonLogger.Msg(Tag + " kept " + kept + " donor plate(s) outside the x-ray coverage");
            return retired;
        }

        /// <summary>True when one of this mod's plates occupies the same space as the donor plate.</summary>
        private static bool Covered(UniformArmor plate)
        {
            Bounds donor = BoundsOf(plate);
            if (donor.size == Vector3.zero) return false;
            foreach (Bounds ours in OurBounds)
            {
                // Inflated because the donor's own plate is the outer skin: it sits half a metre or more outside
                // the composite array this mod replaces, so a plain intersection test left most of them active
                // and the vehicle wore both sets.
                Bounds grown = ours;
                grown.Expand(1.2f);
                if (grown.Intersects(donor)) return true;
            }
            return false;
        }

        private static Bounds BoundsOf(UniformArmor plate)
        {
            Collider collider = plate.GetComponentInChildren<Collider>(true);
            if (collider == null) return new Bounds(plate.transform.position, Vector3.zero);
            return collider.bounds;
        }

        private struct Codex
        {
            internal string Name;
            internal ArmorCodexScriptable Type;
        }

        private static Codex[] DonorCodex = new Codex[0];

        private static Codex[] CollectCodex(GameObject unitRoot)
        {
            var list = new System.Collections.Generic.List<Codex>();
            foreach (UniformArmor piece in unitRoot.GetComponentsInChildren<UniformArmor>(true))
            {
                if (piece == null || piece.ArmorType == null) continue;
                string name = piece.ArmorType.name;
                if (string.IsNullOrEmpty(name)) name = piece.name;
                bool seen = false;
                foreach (Codex known in list) if (known.Name == name) { seen = true; break; }
                if (!seen) list.Add(new Codex { Name = name, Type = piece.ArmorType });
            }
            if (list.Count > 0)
                MelonLogger.Msg(Tag + " donor armour codex: " + list.Count + " type(s) available to copy");
            return list.ToArray();
        }

        /// <summary>The donor codex closest to this group: same station first, then anything.</summary>
        private static ArmorCodexScriptable CodexFor(string groupName)
        {
            string want = groupName.IndexOf("turret", StringComparison.OrdinalIgnoreCase) >= 0 ? "turret"
                        : groupName.IndexOf("hull", StringComparison.OrdinalIgnoreCase) >= 0 ? "hull"
                        : null;
            if (want != null)
                foreach (Codex codex in DonorCodex)
                    if (codex.Name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                        return codex.Type;
            foreach (Codex codex in DonorCodex)
                if (codex.Name.IndexOf("array", StringComparison.OrdinalIgnoreCase) >= 0)
                    return codex.Type;
            return DonorCodex.Length > 0 ? DonorCodex[0].Type : null;
        }

        private static void SetPrivate(object target, string field, object value)
        {
            if (target == null || value == null) return;
            var info = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (info != null) info.SetValue(target, value);
        }

        private static bool BuildOne(Transform bone, Vector3 anchor, Group group, float ke, float ce, string zoneName, int layers, ArmorCodexScriptable codex)
        {
            if (bone == null || group.Mesh == null) return false;
            Mesh mesh = group.Mesh;

            // The stack runs along the plate's own normal so the layers sit inside the real array's thickness.
            Vector3 normal = AverageNormal(mesh);
            float thickness = ExtentAlong(mesh, normal);
            float spacing = layers > 1 ? thickness / layers : 0f;

            for (int layer = 0; layer < layers; layer++)
            {
                float offset = layers > 1 ? (layer - (layers - 1) * 0.5f) * spacing : 0f;
                BuildLayer(bone, anchor, group, mesh, ke / layers, ce / layers, codex,
                           layers > 1 ? zoneName + " layer " + (layer + 1) + "/" + layers : zoneName,
                           normal * offset);
            }
            return true;
        }

        private static Transform Rig(Transform from)
        {
            foreach (Transform node in from.GetComponentsInParent<Transform>(true))
                if (node != null && node.name.Equals("leo2_rig", StringComparison.OrdinalIgnoreCase)) return node;
            return from.root;
        }

        private static void BuildLayer(Transform bone, Vector3 anchor, Group group, Mesh mesh, float ke, float ce, ArmorCodexScriptable codex,
            string zoneName, Vector3 offset)
        {
            var go = new GameObject(Marker + " " + group.Name + " " + zoneName);
            go.transform.SetParent(bone, false);

            // Model space, placed the way the art is placed: the vertex positions already carry the model
            // origin, so the anchor (unit offset, -ring or -trunnion) puts the piece where the visible part of
            // the vehicle is. No world-space trickery, which is what used to leave every plate at the origin.
            go.transform.localPosition = anchor + offset;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = 8;
            go.tag = "Penetrable";

            Transform space = Rig(bone);
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false;
            OurBounds.Add(new Bounds(go.transform.TransformPoint(mesh.bounds.center),
                                     go.transform.TransformVector(mesh.bounds.size)));

            float scale = LpConfig.ArmorScale;
            var armor = go.AddComponent<UniformArmor>();
            armor.PrimarySabotRha = ke * scale;
            armor.PrimaryHeatRha = ce * scale;
            // SabotRha/CeRha return the Secondary figures once a piece counts as detonated, and those were left
            // at zero - a round that reached a piece in that state met no armour at all.
            armor.SecondarySabotRha = ke * scale;
            armor.SecondaryHeatRha = ce * scale;
            armor.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
            armor.AngleMatters = true;
            armor.DetonatedObjects = Array.Empty<GameObject>();
            armor.UndetonatedObjects = Array.Empty<GameObject>();
            armor.CenterPoint = go.transform;
            armor.SetName(zoneName);
            if (codex != null) SetPrivate(armor, "_armorType", codex);

            var unit = go.GetComponentInParent<Unit>();
            if (unit != null) armor.Unit = unit;
            else MelonLogger.Warning(Tag + " piece '" + group.Name + "' has no Unit; hits may not be attributed");

            // Where the piece actually landed, in world space: if the mesh and the transform disagree the
            // collider can sit metres away from the tank, which reads as "no armour at all".
            if (_logged < 6)
            {
                _logged++;
                MeshCollider mc = go.GetComponent<MeshCollider>();
                Bounds world = mc != null && mc.sharedMesh != null
                    ? new Bounds(go.transform.TransformPoint(mc.sharedMesh.bounds.center),
                                 go.transform.TransformVector(mc.sharedMesh.bounds.size))
                    : new Bounds(go.transform.position, Vector3.zero);
                Vector3 expected = mesh.bounds.center;
                float error = (space.InverseTransformPoint(world.center) - expected).magnitude;
                MelonLogger.Msg(Tag + " piece '" + group.Name + "' centre " +
                                world.center.ToString("0.00") + " size " + world.size.ToString("0.00") +
                                " layer " + go.layer + " unit " + (armor.Unit != null) +
                                " | rig-space error " + error.ToString("0.00") + " m vs x-ray " +
                                expected.ToString("0.00"));
            }

            AarVisual aar = go.AddComponent<AarVisual>();
            aar.HideUntilAar = true;
            Material xray = LpArmorOwn.XrayMaterial();
            if (xray != null)
            {
                aar.SwitchMaterials = true;
                aar.AarMaterial = xray;
                aar.UpdateVisibility();
            }
        }

        /// <summary>Area-weighted mean normal of a mesh, which is the direction its plates face.</summary>
        private static Vector3 AverageNormal(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                sum += Vector3.Cross(b - a, c - a);
            }
            return sum.sqrMagnitude > 0.000001f ? sum.normalized : Vector3.up;
        }

        /// <summary>How thick the piece is along that normal - the stacking distance the layers share.</summary>
        private static float ExtentAlong(Mesh mesh, Vector3 normal)
        {
            Vector3[] vertices = mesh.vertices;
            if (vertices.Length == 0) return 0f;
            float min = float.MaxValue, max = float.MinValue;
            foreach (Vector3 v in vertices)
            {
                float d = Vector3.Dot(v, normal);
                if (d < min) min = d;
                if (d > max) max = d;
            }
            return Mathf.Max(0.02f, max - min);
        }

        /// <summary>Reads the baked armour, once per session.</summary>
        private static List<Group> Load()
        {
            if (_groups != null) return _groups;

            _groups = new List<Group>();
            if (TryLoadBundle()) return _groups;

            // The asset travels inside the DLL the way the T-90A mod packs its own: a resource cannot go
            // missing from the mod folder, and there is nothing for the player to keep in sync. A loose file
            // next to the mod still wins, which is what makes iterating on the geometry possible.
            Stream source = null;
            string loose = Path.Combine(ModFolder, FileName);
            if (File.Exists(loose))
            {
                source = File.OpenRead(loose);
            }
            else
            {
                source = typeof(LpXrayArmor).Assembly.GetManifestResourceStream("armor.bin");
                if (source == null)
                {
                    MelonLogger.Warning(Tag + " no baked armour inside the DLL and no " + FileName +
                                        " beside it; the armour falls back to the donor pieces");
                    return _groups;
                }
            }

            try
            {
                using (var reader = new BinaryReader(source))
                {
                    var magic = reader.ReadBytes(8);
                    if (magic.Length != 8 || magic[0] != (byte)'G' || magic[1] != (byte)'H')
                    {
                        MelonLogger.Warning(Tag + " " + FileName + " is not a baked armour file; the donor armour stays");
                        return _groups;
                    }

                    int count = reader.ReadInt32();
                    for (int g = 0; g < count; g++)
                    {
                        int nameLength = reader.ReadInt32();
                        string name = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                        int vertexCount = reader.ReadInt32();
                        var vertices = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                            vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        int indexCount = reader.ReadInt32();
                        var triangles = new int[indexCount];
                        for (int i = 0; i < indexCount; i++) triangles[i] = reader.ReadInt32();
                        if (!_meshes.TryGetValue(name, out Mesh mesh) || mesh == null)
                        {
                            mesh = new Mesh { name = name };
                            if (vertices.Length > 65000)
                                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                            mesh.vertices = vertices;
                            mesh.triangles = triangles;
                            mesh.RecalculateBounds();
                            _meshes[name] = mesh;
                        }
                        _groups.Add(new Group { Name = name, Mesh = mesh });
                    }
                }
            }
            catch (Exception error)
            {
                MelonLogger.Warning(Tag + " could not read " + FileName + ": " + error.Message);
                _groups.Clear();
                return _groups;
            }

            MelonLogger.Msg(Tag + " loaded " + _groups.Count + " armour group(s) from " + FileName);
            return _groups;
        }

        private const string BundleFile = "l2a7v_armor.bundle";

        private const string PrefabName = "L2A7V_armor";

        private static AssetBundle _bundle;

        /// <summary>
        /// Loads the prefab out of the AssetBundle the editor build produces, when it is shipped. The bundle
        /// carries the meshes as Unity assets, so nothing is parsed at all - what the T-90A mod gets from its
        /// own prefab. The baked file below stays as the fallback for a mod folder without a bundle.
        /// </summary>
        private static bool TryLoadBundle()
        {
            if (_bundle == null)
            {
                string path = Path.Combine(ModFolder, BundleFile);
                if (!File.Exists(path)) return false;
                _bundle = AssetBundle.LoadFromFile(path);
                if (_bundle == null)
                {
                    MelonLogger.Warning(Tag + " " + BundleFile + " exists but could not be loaded; using the baked file");
                    return false;
                }
            }

            GameObject prefab = _bundle.LoadAsset<GameObject>(PrefabName);
            if (prefab == null)
            {
                MelonLogger.Warning(Tag + " the bundle holds no " + PrefabName + " prefab; using the baked file");
                return false;
            }

            int count = 0;
            foreach (Transform child in prefab.transform)
            {
                MeshFilter filter = child.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                _groups.Add(new Group { Name = child.name, Mesh = filter.sharedMesh });
                count++;
            }

            if (count == 0)
            {
                MelonLogger.Warning(Tag + " the prefab holds no armour meshes; using the baked file");
                return false;
            }

            MelonLogger.Msg(Tag + " loaded " + count + " armour mesh(es) from " + BundleFile);
            return true;
        }

        private static Transform FindColliderRoot(GameObject unitRoot, string kind)
        {
            foreach (Transform node in unitRoot.GetComponentsInChildren<Transform>(true))
            {
                if (node == null) continue;
                string name = node.name.ToLowerInvariant();
                if (name.IndexOf("collider", StringComparison.Ordinal) < 0) continue;
                if (name.IndexOf(kind, StringComparison.Ordinal) < 0) continue;
                return node;
            }
            return null;
        }

        private static Transform FindBone(GameObject unitRoot, params string[] names)
        {
            foreach (Transform node in unitRoot.GetComponentsInChildren<Transform>(true))
            {
                if (node == null) continue;
                string name = node.name.ToLowerInvariant();
                foreach (string wanted in names)
                    if (name == wanted) return node;
            }
            return null;
        }
    }
}

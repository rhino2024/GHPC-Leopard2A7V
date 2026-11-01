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
        private const string Tag = "[L2A7V][armour]";

        private const string FileName = "l2a7v_armor.bin";

        private const string Marker = "L2A7V_XrayArmor";

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

            Transform hull = FindBone(unitRoot, "hull");
            Transform turret = FindBone(unitRoot, "turret");
            if (hull == null)
            {
                MelonLogger.Warning(Tag + " no hull bone on " + unitRoot.name + "; the donor armour stays");
                return false;
            }

            int built = 0, noZone = 0;
            foreach (Group group in groups)
            {
                Transform bone = group.Name.IndexOf("turret", StringComparison.OrdinalIgnoreCase) >= 0 && turret != null
                    ? turret : hull;

                float ke, ce;
                string zoneName;
                if (!LpArmor.TryZoneForXrayGroup(group.Name, out ke, out ce, out zoneName))
                {
                    noZone++;
                    continue;
                }

                // Composite arrays go in as a stack, the way the vanilla LEO2A3 and M1 author their armour:
                // the solver subtracts each plate's LOS thickness from the round's remaining penetration, so
                // equal layers add up to exactly the same total and the round is stopped layer by layer.
                // Liners and firewalls stay single - they are one plate in the real vehicle too.
                int layers = group.Name.StartsWith("composite_armor_", StringComparison.OrdinalIgnoreCase)
                    ? Mathf.Clamp(LpConfig.ArmorLayers == null ? 3 : LpConfig.ArmorLayers.Value, 1, 6)
                    : 1;

                if (BuildOne(bone, group, ke, ce, zoneName, layers)) built++;
            }

            MelonLogger.Msg(Tag + " built " + built + " piece(s) from the baked armour" +
                            (noZone > 0 ? ", " + noZone + " group(s) had no zone entry" : string.Empty));
            return built > 0;
        }

        private static bool BuildOne(Transform bone, Group group, float ke, float ce, string zoneName, int layers)
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
                BuildLayer(bone, group, mesh, ke / layers, ce / layers,
                           layers > 1 ? zoneName + " layer " + (layer + 1) + "/" + layers : zoneName,
                           normal * offset);
            }
            return true;
        }

        private static void BuildLayer(Transform bone, Group group, Mesh mesh, float ke, float ce,
            string zoneName, Vector3 offset)
        {
            var go = new GameObject(Marker + " " + group.Name + " " + zoneName);
            go.transform.SetParent(bone, false);

            // The mesh is in model space and every unit shares it, so the pose is put on the object instead of
            // baked into the vertices: identity in world, compensated for whatever the bone is doing.
            go.transform.localPosition = bone.InverseTransformPoint(Vector3.zero + offset);
            go.transform.localRotation = Quaternion.Inverse(bone.rotation);
            go.transform.localScale = Vector3.one;
            go.layer = 8;
            go.tag = "Penetrable";

            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false;

            float scale = LpConfig.ArmorScale;
            var armor = go.AddComponent<UniformArmor>();
            armor.PrimarySabotRha = ke * scale;
            armor.PrimaryHeatRha = ce * scale;
            armor.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
            armor.AngleMatters = true;
            armor.DetonatedObjects = Array.Empty<GameObject>();
            armor.UndetonatedObjects = Array.Empty<GameObject>();
            armor.CenterPoint = go.transform;
            armor.SetName(zoneName);

            var unit = go.GetComponentInParent<Unit>();
            if (unit != null) armor.Unit = unit;

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

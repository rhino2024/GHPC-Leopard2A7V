using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V
{
    /// <summary>
    /// The War Thunder compartment, hung on the same pivots as the exterior art and registered as x-ray art.
    ///
    /// This is the L2A7HU mod's interior pass. Every module keeps its own class colour, taken from the donor's
    /// own interior sheets (the numbers in the palette comments are the clusters lp_interior_palette.txt
    /// measured on LEO2_int / M1IP_interior), so the after-action view reads as a compartment rather than one
    /// grey mass. The materials are flat clones of the donor's paint template through
    /// <see cref="LpAssets.CreateFlatMaterial"/>, which keeps them on the vehicle shader with the same keywords
    /// and render queue as the rest of the vehicle.
    ///
    /// AarVisual with XrayOnly is the game's own switch for "drawn only in the x-ray view": it moves the
    /// renderers to the x-ray layer and leaves them off everywhere else. The node is created inactive and
    /// activated only after the component's mode is set, because the mode is read in Awake.
    ///
    /// Riding the art pivots also keeps the compartment out of an eyepiece: LpSightView scales those pivots to
    /// nothing inside the gunner's sight, and an interior drawn between the eye and the reticle would blank the
    /// view.
    /// </summary>
    public static class LpInterior
    {
        /// <summary>
        /// Triangles of the War Thunder crew figures in the compartment (class int_crew). Non-zero is what
        /// lets the donor's own soldiers be taken out without leaving empty seats behind.
        /// </summary>
        public static int CrewTriangles { get; private set; }

        public static bool Loaded { get { return LpXrayParts.Loaded; } }
        public static bool Failed { get { return LpXrayParts.Failed; } }
        public static string Error { get { return LpXrayParts.Error; } }

        private static readonly Dictionary<string, Color32> Palette = new Dictionary<string, Color32>
        {
            { "int_armor", new Color32(172, 160, 118, 255) },     // LEO2_int_co 36.1% (136,136,120)
            { "int_liner", new Color32(140, 152, 148, 255) },     // M1IP_interior_co 5.9% (120,120,104)
            { "int_firewall", new Color32(122, 84, 62, 255) },    // M1IP_interior_co 18.2% (56,56,40)
            { "int_ammo", new Color32(198, 166, 72, 255) },       // M1IP_interior_co 4.7% (96,96,80)
            { "int_engine", new Color32(78, 82, 96, 255) },       // M1IP_interior_co 9.7% (40,40,40)
            { "int_fcs", new Color32(96, 124, 66, 255) },         // LEO2_int_co 5.7% (56,72,40)
            { "int_metal", new Color32(156, 158, 166, 255) },     // LEO2_int_co 8.4% (120,120,120)
            { "int_crew", new Color32(184, 158, 116, 255) },      // khaki, as the x-ray art uses for people
        };

        private static readonly Dictionary<string, byte> Gloss = new Dictionary<string, byte>
        {
            { "int_armor", 28 }, { "int_liner", 12 }, { "int_firewall", 20 }, { "int_ammo", 30 },
            { "int_engine", 30 }, { "int_fcs", 44 }, { "int_metal", 38 }, { "int_crew", 14 },
        };

        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, List<string>> Submeshes = new Dictionary<string, List<string>>();

        private sealed class Built
        {
            public Mesh Mesh;
            public List<string> Classes;
        }

        public static void Preload(string directory)
        {
            // Parsed whether or not the compartment is drawn: the added armour arrays are read from the same
            // export, so turning the interior off must not take the x-ray armour with it.
            LpXrayParts.Preload(directory);
        }

        public static int Attach(GameObject unitRoot, LpVisual.Assembly art)
        {
            if (!LpConfig.ShowInterior || art == null) return 0;
            if (!LpXrayParts.Loaded || LpXrayParts.Failed) return 0;

            // Counted once, on the first spawn: the donor's own soldiers are only taken out when the imported
            // compartment carries mannequins of its own to stand in for them.
            if (CrewTriangles == 0)
            {
                int crew = 0;
                foreach (LpXrayParts.Part part in LpXrayParts.Parts)
                    if (part.Class == "int_crew") crew += part.Triangles.Length / 3;
                CrewTriangles = crew;
            }

            int total = 0;
            total += AttachBone("Hull", art.Hull, Vector3.zero);
            total += AttachBone("Turret", art.Turret, LpConfig.TurretAxis);
            total += AttachBone("Gun", art.Gun, LpConfig.GunAxis);
            if (total > 0)
                MelonLogger.Msg("[L2A7V] x-ray compartment attached: " + total + " triangle(s) on the hull, " +
                                "turret and gun pivots, drawn only in the after-action x-ray view");
            return total;
        }

        private static int AttachBone(string bone, Transform pivot, Vector3 anchor)
        {
            if (pivot == null) return 0;
            if (pivot.Find("L2A7V_Interior_" + bone) != null) return 0;

            Built built = Build(bone, anchor);
            if (built == null || built.Mesh == null) return 0;

            var node = new GameObject("L2A7V_Interior_" + bone);
            node.SetActive(false);
            node.transform.SetParent(pivot, false);
            node.layer = pivot.gameObject.layer;

            node.AddComponent<MeshFilter>().sharedMesh = built.Mesh;
            var renderer = node.AddComponent<MeshRenderer>();
            var materials = new Material[built.Classes.Count];
            for (int i = 0; i < materials.Length; i++) materials[i] = GetMaterial(built.Classes[i]);
            renderer.sharedMaterials = materials;
            // Inside a closed hull these can only shadow themselves.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var xray = node.AddComponent<GHPC.AarVisual>();
            xray.RenderMode = GHPC.AarVisual.AarVisualRenderMode.XrayOnly;
            node.SetActive(true);
            return built.Mesh.vertexCount > 0 ? CountTriangles(built.Mesh) : 0;
        }

        private static int CountTriangles(Mesh mesh)
        {
            int total = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) total += (int)mesh.GetIndexCount(i) / 3;
            return total;
        }

        private static Built Build(string bone, Vector3 anchor)
        {
            Mesh cached;
            if (Meshes.TryGetValue(bone, out cached))
                return new Built { Mesh = cached, Classes = Submeshes[bone] };

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new Dictionary<string, List<int>>();
            var order = new List<string>();

            foreach (LpXrayParts.Part part in LpXrayParts.Parts)
            {
                if (!string.Equals(part.Bone, bone, System.StringComparison.OrdinalIgnoreCase)) continue;
                string cls = Palette.ContainsKey(part.Class) ? part.Class : "int_metal";
                List<int> indices;
                if (!triangles.TryGetValue(cls, out indices))
                {
                    indices = new List<int>();
                    triangles.Add(cls, indices);
                    order.Add(cls);
                }

                int offset = vertices.Count;
                for (int i = 0; i < part.Positions.Length; i++)
                {
                    vertices.Add(part.Positions[i] - anchor);
                    normals.Add(part.Normals[i]);
                }
                for (int i = 0; i < part.Triangles.Length; i++)
                    indices.Add(part.Triangles[i] + offset);
            }

            if (vertices.Count == 0 || order.Count == 0) return null;

            var mesh = new Mesh
            {
                name = "L2A7V_Interior_" + bone,
                indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = order.Count;
            for (int i = 0; i < order.Count; i++)
                mesh.SetTriangles(triangles[order[i]], i);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);

            Meshes[bone] = mesh;
            Submeshes[bone] = order;
            return new Built { Mesh = mesh, Classes = order };
        }

        /// <summary>
        /// Re-points the compartment materials at the shader the current mission loaded, for the same reason
        /// LpAssets.RepairShader exists: the static cache outlives the vehicles bundle.
        /// </summary>
        public static int RepairShader(Shader live)
        {
            if (live == null) return 0;
            int repaired = 0;
            foreach (Material material in Materials.Values)
            {
                if (material == null || material.shader == live) continue;
                material.shader = live;
                repaired++;
            }
            return repaired;
        }

        private static Material GetMaterial(string cls)
        {
            Material material;
            if (Materials.TryGetValue(cls, out material) && material != null)
            {
                // The cached clone can outlive the bundle that held its shader; the donor's fresh template
                // revives it the same way the exterior materials are revived.
                if (material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                    material.shader = LpVisual.TemplateMaterial.shader;
                return material;
            }

            Color32 color;
            if (!Palette.TryGetValue(cls, out color)) color = new Color32(156, 158, 166, 255);
            byte gloss;
            if (!Gloss.TryGetValue(cls, out gloss)) gloss = 30;
            material = LpAssets.CreateFlatMaterial("L2A7V " + cls, color, gloss);
            Materials[cls] = material;
            return material;
        }
    }
}

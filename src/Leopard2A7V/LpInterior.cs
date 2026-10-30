using MelonLoader;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// The War Thunder x-ray interior, brought in as a second mesh set beside the exterior art.
    ///
    /// The x-ray export is the same model as the exterior one, in the same frame and to the same scale,
    /// so no calibration is needed: the three groups are attached with exactly the anchors the exterior
    /// groups use, which puts the engine behind the firewall, the carousel under the turret floor and
    /// the breech inside the mantlet without a second offset to maintain.
    ///
    /// The meshes ride the exterior art's pivots on nodes of their own. Riding those pivots is what keeps
    /// the compartment out of an eyepiece view - the sight pass scales the art pivots to nothing there,
    /// and a compartment drawn inside that view would sit between the eye and the reticle - while the
    /// nodes still get skipped by the donor-stripping pass, which ignores anything named after this mod.
    ///
    /// Each node is then registered as x-ray art the way the game's own prefabs do, so the compartment is
    /// drawn only while the x-ray view is live. That matters here more than it does for a vanilla unit: the
    /// donor's authored interior subtrees are marked hide-until-AAR, so a live x-ray view of this vehicle
    /// shows our modules and nothing else, and they are close enough to the outer surface to read as
    /// damage on the paint if they were allowed to draw all the time.
    /// </summary>
    public static class LpInterior
    {
        public static volatile bool Loaded;
        public static volatile bool Failed;
        public static string Error;

        private sealed class Part
        {
            public string Bone;
            public string Material;
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();
            public readonly Dictionary<Vector3Int, int> VertexMap = new Dictionary<Vector3Int, int>();
        }

        private static readonly List<Part> Parts = new List<Part>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();

        /// <summary>
        /// One colour per module class, taken from the donor's own compartment sheets rather than invented:
        /// ModSources/tools/lp_interior_palette.txt quantises LEO2_int_co and M1IP_interior_co (1024x1024,
        /// the two materials the LEO2A3 draws its interior with) into sRGB clusters, and these are the
        /// cluster means of the classes those clusters belong to. GHPC draws the compartment under the
        /// vehicle's own interior lighting, so the values are albedo, not the lit colour; the gloss is the
        /// alpha of the smoothness map, the same channel the vehicle shader reads on the painted hull.
        /// int_crew is the one author's choice - the donor's crew is skinned art with its own texture, and
        /// the materials named Crew/AarCrew in the vehicles bundle are the AAR highlight outlines, not a
        /// uniform colour, so there is nothing to measure.
        /// </summary>
        private static readonly Dictionary<string, Color32> Palette = new Dictionary<string, Color32>
        {
            { "int_armor", new Color32(136, 136, 120, 255) },     // LEO2_int_co 36.1%
            { "int_liner", new Color32(120, 120, 104, 255) },     // M1IP_interior_co 5.9%
            { "int_firewall", new Color32(56, 56, 40, 255) },     // M1IP_interior_co 18.2%
            { "int_ammo", new Color32(96, 96, 80, 255) },         // M1IP_interior_co 4.7% - rack frames only
            { "int_engine", new Color32(40, 40, 40, 255) },       // M1IP_interior_co 9.7%
            { "int_fcs", new Color32(56, 72, 40, 255) },          // LEO2_int_co 5.7% olive cabinets
            { "int_metal", new Color32(120, 120, 120, 255) },     // LEO2_int_co 8.4%
            { "int_crew", new Color32(96, 92, 76, 255) },
        };

        private static readonly Dictionary<string, byte> Gloss = new Dictionary<string, byte>
        {
            { "int_armor", 28 },
            { "int_liner", 12 },
            { "int_firewall", 20 },
            { "int_ammo", 30 },
            { "int_engine", 30 },
            { "int_fcs", 44 },
            { "int_metal", 38 },
            { "int_crew", 14 },
        };

        public static void Preload(string directory)
        {
            if (!LpConfig.ShowInterior.Value) return;
            string path = Path.Combine(directory, "Interior.obj");
            if (!File.Exists(path))
            {
                MelonLogger.Msg("[L2A7V] no Interior.obj beside the art, the crew compartment stays the donor's");
                Failed = true;
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    Parse(path);
                    Loaded = true;
                }
                catch (Exception ex)
                {
                    Error = ex.Message;
                    Failed = true;
                    MelonLogger.Error("[L2A7V] interior parse failed: " + ex);
                }
            });
        }

        private static void Parse(string path)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            Part active = null;
            string bone = null;
            string material = null;

            using (var reader = new StreamReader(path))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length < 3 || line[0] == '#') continue;
                    char second = line[1];
                    if (line[0] == 'v')
                    {
                        if (second == ' ') positions.Add(ReadVector(line));
                        else if (second == 'n' && line[2] == ' ') normals.Add(ReadVector(line));
                    }
                    else if (line[0] == 'g' && second == ' ')
                    {
                        string name = line.Substring(2).Trim();
                        bone = name.StartsWith("interior_", StringComparison.OrdinalIgnoreCase)
                            ? name.Substring("interior_".Length).ToLowerInvariant()
                            : name.ToLowerInvariant();
                        active = null;
                    }
                    else if (line[0] == 'u' && line.StartsWith("usemtl"))
                    {
                        material = line.Substring(6).Trim();
                        active = null;
                    }
                    else if (line[0] == 'f' && second == ' ')
                    {
                        if (bone == null || material == null) continue;
                        ReadFace(line, bone, material, positions, normals, ref active);
                    }
                }
            }

            int triangles = 0;
            foreach (Part part in Parts) triangles += part.Triangles.Count / 3;
            MelonLogger.Msg($"[L2A7V] interior parsed: {Parts.Count} part(s), {triangles} triangle(s), " +
                            $"{Palette.Count} material class(es)");
        }

        private static Vector3 ReadVector(string line)
        {
            string[] p = line.Substring(line[1] == ' ' ? 2 : 3).Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3) return Vector3.zero;
            // The same swap the exterior parser runs: War Thunder is +X forward with +Z to starboard,
            // GHPC is +Z forward with +X to starboard.
            return new Vector3(ParseFloat(p[2]), ParseFloat(p[1]), ParseFloat(p[0]));
        }

        private static float ParseFloat(string token)
        {
            float value;
            return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0f;
        }

        private static void ReadFace(string line, string bone, string material,
            List<Vector3> positions, List<Vector3> normals, ref Part active)
        {
            if (active == null || active.Bone != bone || active.Material != material)
            {
                active = new Part { Bone = bone, Material = material };
                Parts.Add(active);
            }

            string[] tokens = line.Substring(2).Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var corner = new int[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                string[] comps = tokens[i].Split('/');
                int positionId = IndexOf(comps, 0, positions.Count);
                int normalId = comps.Length > 2 && comps[2].Length > 0 ? IndexOf(comps, 2, normals.Count) : -1;
                var key = new Vector3Int(positionId, normalId, 0);
                int index;
                if (!active.VertexMap.TryGetValue(key, out index))
                {
                    index = active.Positions.Count;
                    active.VertexMap.Add(key, index);
                    active.Positions.Add(positions[Mathf.Clamp(positionId, 0, positions.Count - 1)]);
                    active.Normals.Add(normalId >= 0 && normalId < normals.Count ? normals[normalId] : Vector3.up);
                }
                corner[i] = index;
            }

            for (int i = 2; i < tokens.Length; i++)
            {
                // Swapping the axes mirrors the mesh, so the winding is reversed to keep the faces out.
                active.Triangles.Add(corner[0]);
                active.Triangles.Add(corner[i]);
                active.Triangles.Add(corner[i - 1]);
            }
        }

        private static int IndexOf(string[] components, int slot, int count)
        {
            if (components.Length <= slot) return 0;
            int value;
            if (!int.TryParse(components[slot], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return 0;
            return value < 0 ? count + value : value - 1;
        }

        /// <summary>
        /// Hangs the three groups on the pivots the exterior art uses. Returns the number of triangles
        /// that made it onto the vehicle, which is the number the log reports.
        /// </summary>
        public static int Attach(GameObject unitRoot, LpVisual.Assembly art)
        {
            if (!LpConfig.ShowInterior.Value || art == null) return 0;
            if (Failed)
            {
                return 0;
            }
            if (!Loaded)
            {
                MelonLogger.Msg("[L2A7V] the interior is still parsing, this spawn keeps the donor's compartment");
                return 0;
            }

            int total = 0;
            _xrayNodes = 0;
            total += AttachBone("Hull", art.Hull, Vector3.zero);
            total += AttachBone("Turret", art.Turret, LpConfig.TurretAxis);
            total += AttachBone("Gun", art.Gun, LpConfig.GunAxis);
            MelonLogger.Msg($"[L2A7V] interior attached: {total} triangle(s) on the hull, turret and gun bones, " +
                            $"{_xrayNodes} node(s) registered as x-ray only");
            return total;
        }

        private static int _xrayNodes;

        private static int AttachBone(string bone, Transform pivot, Vector3 anchor)
        {
            if (pivot == null) return 0;
            if (pivot.Find("L2A7V_Interior_" + bone) != null) return 0;
            Mesh mesh = Build(bone, anchor);
            if (mesh == null) return 0;

            var node = new GameObject("L2A7V_Interior_" + bone);
            node.SetActive(false);
            node.transform.SetParent(pivot, false);
            node.layer = pivot.gameObject.layer;

            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.AddComponent<MeshRenderer>();
            var materials = new Material[mesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++)
                materials[i] = GetMaterial(mesh.name + "|" + i);
            renderer.sharedMaterials = materials;
            // Inside a closed hull these can only shadow themselves, and the vehicle's own interior art
            // already carries the compartment's shading.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;

            // AarVisual is how GHPC says "this is only drawn in the x-ray view": it switches the renderer off
            // unless the AAR camera is live and moves it onto the x-ray-only layer, which no ordinary camera
            // draws. The compartment is built in the same frame as the exterior art and its armour modules
            // reach the outer surface, so an unregistered node pokes through the hull with x-ray off. The
            // mode is read inside Awake, hence the node going active only after the component is described.
            var xray = node.AddComponent<GHPC.AarVisual>();
            xray.RenderMode = GHPC.AarVisual.AarVisualRenderMode.XrayOnly;
            node.SetActive(true);
            _xrayNodes++;
            return mesh.vertexCount > 0 ? CountTriangles(mesh) : 0;
        }

        private static int CountTriangles(Mesh mesh)
        {
            int total = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) total += (int)mesh.GetIndexCount(i) / 3;
            return total;
        }

        private static Mesh Build(string bone, Vector3 anchor)
        {
            string key = bone + "|" + anchor;
            Mesh cached;
            if (Meshes.TryGetValue(key, out cached)) return cached;

            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var submeshes = new Dictionary<string, List<int>>();
            var order = new List<string>();

            foreach (Part part in Parts)
            {
                if (!string.Equals(part.Bone, bone, StringComparison.OrdinalIgnoreCase)) continue;
                string material = Palette.ContainsKey(part.Material) ? part.Material : "int_metal";
                List<int> triangles;
                if (!submeshes.TryGetValue(material, out triangles))
                {
                    triangles = new List<int>();
                    submeshes.Add(material, triangles);
                    order.Add(material);
                }

                int offset = positions.Count;
                for (int i = 0; i < part.Positions.Count; i++)
                {
                    positions.Add(part.Positions[i] - anchor);
                    normals.Add(part.Normals[i]);
                }
                for (int i = 0; i < part.Triangles.Count; i++)
                    triangles.Add(part.Triangles[i] + offset);
            }

            if (positions.Count == 0) return null;

            var mesh = new Mesh
            {
                name = "L2A7V_Interior_" + bone,
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, new Vector2[positions.Count]);
            mesh.subMeshCount = order.Count;
            for (int i = 0; i < order.Count; i++)
                mesh.SetTriangles(submeshes[order[i]], i);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);

            _materialOrder = _materialOrder ?? new Dictionary<string, string[]>();
            _materialOrder[mesh.name] = order.ToArray();
            Meshes[key] = mesh;
            MelonLogger.Msg($"[L2A7V] interior {bone}: {positions.Count} vertices / {CountTriangles(mesh)} triangle(s) " +
                            $"in {order.Count} submesh(es): {string.Join(", ", order)}");
            return mesh;
        }

        /// <summary>
        /// Re-points the compartment materials at the shader the current mission loaded, for the same
        /// reason LpAssets.RepairShader exists: the static cache outlives the vehicles bundle.
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

        private static Dictionary<string, string[]> _materialOrder;

        private static Material GetMaterial(string key)
        {
            Material cached;
            if (Materials.TryGetValue(key, out cached)) return cached;

            string meshName = key.Substring(0, key.LastIndexOf('|'));
            int index = int.Parse(key.Substring(key.LastIndexOf('|') + 1));
            string[] order;
            string name = _materialOrder != null && _materialOrder.TryGetValue(meshName, out order) &&
                          index < order.Length ? order[index] : "int_metal";

            Color32 albedo;
            if (!Palette.TryGetValue(name, out albedo)) albedo = Palette["int_metal"];
            byte gloss;
            if (!Gloss.TryGetValue(name, out gloss)) gloss = 32;

            Material material = LpAssets.CreateFlatMaterial("L2A7V interior " + name, albedo, gloss);
            Materials[key] = material;
            return material;
        }
    }
}

using MelonLoader;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Leopard2A7V
{
    public enum LpGroup
    {
        Hull,
        Turret,
        CommanderGear,
        Gun,
        RunningGear
    }

    public sealed class MtlDef
    {
        public string Name;
        public string AlbedoPath;
        public string NormalPath;
        public string OcclusionPath;
        public string GlossPath;
        public Color Diffuse = Color.white;
    }

    public sealed class ObjGeometry
    {
        public string ObjectName;
        public int MaterialIndex;
        public LpGroup Group;
        public Vector3[] Positions;
        public Vector2[] Uvs;
        public int[] Triangles;
        public Vector3[] Normals;
        public Vector4[] Tangents;
        public Bounds Bounds;
    }

    /// <summary>
    /// Parses the Wavefront export once, off the main thread, into Unity-ready geometry.
    ///
    /// The War Thunder rig this file was exported from is right handed with +X forward, +Y up and +Z
    /// to starboard, while GHPC's unit frame is left handed with +X to starboard, +Y up and +Z forward.
    /// Swapping X with Z converts one into the other and flips the handedness exactly once, which is
    /// the correction that has to happen; the face winding is reversed to match, and the UVs are
    /// untouched because the swap does not mirror the texture.
    /// </summary>
    public static class ObjModel
    {
        public static volatile bool Loaded;
        public static volatile bool Failed;
        public static string Error;

        public static readonly List<MtlDef> Materials = new List<MtlDef>();
        public static readonly List<ObjGeometry> Geometries = new List<ObjGeometry>();

        /// <summary>
        /// The rolling gear, which the donor animates through its own bones. LpRunningGear builds one node per
        /// object name so each wheel can ride a bone; the group is only kept whole when UseDonorRunningGear is
        /// on and the imported art is dropped in favour of the donor's.
        /// </summary>
        private static readonly string[] RunningGearKeys = { "track_", "wheel_", "suspension_" };

        /// <summary>
        /// Everything that must swing with the barrel. turret_cap is the breech cover over the
        /// mantlet, and is authored with the gun material rather than the turret's.
        /// </summary>
        private static readonly string[] GunKeys = { "gun_barrel", "bone_gun", "turret_cap" };

        /// <summary>
        /// Cupola fittings that sit between the commander's camera and his view, so they need a node of
        /// their own to be able to step out of it. bone_turret_01 is the AA gun's turret ring mount and
        /// ammo the belt box next to it.
        /// </summary>
        private static readonly string[] CommanderGearKeys =
        {
            "optic_commander", "mg_aa", "bone_mg_aa", "bone_turret_01", "ammo"
        };

        /// <summary>
        /// The turret's own bone parts. Checked after the cupola fittings, which share the prefix, and
        /// before the material role, because bone_turret has a handful of vision-block faces dressed in
        /// the glass texture - a material name that says nothing about the turret.
        /// </summary>
        private static readonly string[] TurretKeys = { "bone_turret" };

        /// <summary>
        /// Named parts that carry the turret or gun material in the atlas but belong to the hull, which
        /// is the only way to tell them apart from the turret appliqué beside them.
        /// </summary>
        private static readonly string[] HullKeys = { "roll_engine" };

        public static void Preload(string directory)
        {
            Task.Run(() =>
            {
                try
                {
                    ParseDirectory(directory);
                    Loaded = true;
                }
                catch (Exception ex)
                {
                    Error = ex.Message;
                    Failed = true;
                    MelonLogger.Error("[L2A7V] model parse failed: " + ex);
                }
            });
        }

        /// <summary>
        /// Name rules first, then the material's own role: the export splits the vehicle into one body,
        /// one turret and one gun texture, and the appliqué armor, hatches and antennas follow that split
        /// far more reliably than their names do.
        /// </summary>
        public static LpGroup GroupOf(string objectName, string materialName)
        {
            string n = objectName.ToLowerInvariant();
            foreach (string key in RunningGearKeys)
                if (n.Contains(key)) return LpGroup.RunningGear;
            foreach (string key in GunKeys)
                if (n.Contains(key)) return LpGroup.Gun;
            foreach (string key in CommanderGearKeys)
                if (n.Contains(key)) return LpGroup.CommanderGear;
            foreach (string key in TurretKeys)
                if (n.Contains(key)) return LpGroup.Turret;
            foreach (string key in HullKeys)
                if (n.Contains(key)) return LpGroup.Hull;

            string m = materialName == null ? string.Empty : materialName.ToLowerInvariant();
            if (m.Contains("turret_ger_camo")) return LpGroup.Turret;
            if (m.Contains("gun_ger_camo")) return LpGroup.Gun;
            return LpGroup.Hull;
        }

        private sealed class PartBuild
        {
            public string ObjectName;
            public int MaterialIndex;
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();

            /// <summary>Keyed by the file's (v, vt, vn) triple, so a hard edge keeps its two normals.</summary>
            public readonly Dictionary<Vector3Int, int> VertexMap = new Dictionary<Vector3Int, int>();
        }

        private sealed class ObjParser
        {
            private readonly List<Vector3> _positions = new List<Vector3>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly Dictionary<string, int> _materialMap;
            private readonly List<PartBuild> _parts = new List<PartBuild>();
            private readonly HashSet<string> _skipped = new HashSet<string>();
            private PartBuild _active;
            private string _objectName;
            private int _currentMaterial;

            public ObjParser(Dictionary<string, int> materialMap)
            {
                _materialMap = materialMap;
            }

            public List<PartBuild> Parts => _parts;
            public HashSet<string> SkippedMaterials => _skipped;

            public void Parse(string objPath)
            {
                using (var reader = new StreamReader(objPath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length < 3 || line[0] == '#') continue;
                        char second = line[1];
                        if (line[0] == 'v')
                        {
                            if (second == ' ') ReadVertex(line);
                            else if (second == 't' && line[2] == ' ') ReadUv(line);
                            else if (second == 'n' && line[2] == ' ') ReadNormal(line);
                        }
                        else if (line[0] == 'f' && second == ' ')
                        {
                            ReadFace(line);
                        }
                        else if ((line[0] == 'o' || line[0] == 'g') && second == ' ')
                        {
                            // War Thunder's export names its parts with g lines; older toolchains use o.
                            _objectName = line.Substring(2).Trim();
                            _active = null;
                        }
                        else if (line[0] == 'u' && line.StartsWith("usemtl"))
                        {
                            SetMaterial(line.Substring(6).Trim());
                            _active = null;
                        }
                    }
                }
            }

            private void SetMaterial(string name)
            {
                int index;
                if (_materialMap.TryGetValue(name, out index))
                {
                    _currentMaterial = index;
                    return;
                }
                // The camo netting has no maps of its own and no place on the vehicle, so its faces are
                // dropped instead of being dressed in the hull texture.
                _currentMaterial = -1;
                _active = null;
                _skipped.Add(name);
            }

            private void ReadVertex(string line)
            {
                string[] p = Tokens(line);
                if (p.Length < 3) return;
                float x = ParseFloat(p[0]);
                float y = ParseFloat(p[1]);
                float z = ParseFloat(p[2]);
                _positions.Add(new Vector3(z, y, x));
            }

            private void ReadUv(string line)
            {
                string[] p = Tokens(line);
                float u = p.Length > 0 ? ParseFloat(p[0]) : 0f;
                float v = p.Length > 1 ? ParseFloat(p[1]) : 0f;
                _uvs.Add(new Vector2(u, v));
            }

            private void ReadNormal(string line)
            {
                string[] p = Tokens(line);
                if (p.Length < 3)
                {
                    _normals.Add(Vector3.zero);
                    return;
                }
                // The same axis swap as the positions: a normal is a direction on the surface it belongs to,
                // so it has to leave the mirrored frame with the geometry.
                _normals.Add(new Vector3(ParseFloat(p[2]), ParseFloat(p[1]), ParseFloat(p[0])));
            }

            private void ReadFace(string line)
            {
                if (_currentMaterial < 0) return;
                if (string.IsNullOrEmpty(_objectName)) _objectName = "mesh";
                EnsureActive(_currentMaterial);

                string[] tokens = Tokens(line);
                var vertexIds = new int[tokens.Length];
                var uvIds = new int[tokens.Length];
                var normalIds = new int[tokens.Length];
                for (int i = 0; i < tokens.Length; i++)
                {
                    string[] comps = tokens[i].Split('/');
                    vertexIds[i] = IndexOf(comps, 0, _positions.Count);
                    uvIds[i] = comps.Length > 1 && comps[1].Length > 0
                        ? IndexOf(comps, 1, _uvs.Count)
                        : -1;
                    normalIds[i] = comps.Length > 2 && comps[2].Length > 0
                        ? IndexOf(comps, 2, _normals.Count)
                        : -1;
                }

                for (int i = 2; i < tokens.Length; i++)
                {
                    // Swapping two axes mirrors the geometry, so the winding goes the other way to keep
                    // the faces outward.
                    AddCorner(_active, vertexIds[0], uvIds[0], normalIds[0]);
                    AddCorner(_active, vertexIds[i], uvIds[i], normalIds[i]);
                    AddCorner(_active, vertexIds[i - 1], uvIds[i - 1], normalIds[i - 1]);
                }
            }

            private void AddCorner(PartBuild part, int vertexId, int uvId, int normalId)
            {
                var key = new Vector3Int(vertexId, uvId, normalId);
                int index;
                if (!part.VertexMap.TryGetValue(key, out index))
                {
                    index = part.Positions.Count;
                    part.VertexMap.Add(key, index);
                    part.Positions.Add(_positions[Mathf.Clamp(vertexId, 0, _positions.Count - 1)]);
                    part.Uvs.Add(uvId >= 0 && uvId < _uvs.Count ? _uvs[uvId] : Vector2.zero);
                    part.Normals.Add(normalId >= 0 && normalId < _normals.Count ? _normals[normalId] : Vector3.zero);
                }
                part.Triangles.Add(index);
            }

            private void EnsureActive(int materialIndex)
            {
                if (_active != null && _active.MaterialIndex == materialIndex &&
                    string.Equals(_active.ObjectName, _objectName, StringComparison.Ordinal))
                    return;
                _active = new PartBuild { ObjectName = _objectName, MaterialIndex = materialIndex };
                _parts.Add(_active);
            }

            private static string[] Tokens(string line) =>
                line.Substring(line[1] == ' ' ? 2 : 3).Split(
                    new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            private static int IndexOf(string[] components, int slot, int count)
            {
                if (components.Length <= slot) return 0;
                int value;
                if (!int.TryParse(components[slot], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    return 0;
                return value < 0 ? count + value : value - 1;
            }
        }

        private static float ParseFloat(string token)
        {
            float value;
            return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0f;
        }

        private static void ParseDirectory(string directory)
        {
            string objPath = Path.Combine(directory, "Leopard2A7V.obj");
            if (!File.Exists(objPath)) throw new FileNotFoundException("Leopard2A7V.obj missing", objPath);

            var materialMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            SmoothShadedParts = 0;
            FlatShadedParts = 0;
            _facetSamples = 0;
            _facetDegrees = 0f;
            _facetOverFive = 0;
            ParseMtl(Path.Combine(directory, "Leopard2A7V.mtl"), directory, materialMap);
            if (Materials.Count == 0) throw new FileNotFoundException("Leopard2A7V.mtl has no materials", directory);

            var parser = new ObjParser(materialMap);
            parser.Parse(objPath);

            foreach (PartBuild part in parser.Parts)
            {
                if (part.Triangles.Count < 3) continue;
                var geometry = new ObjGeometry
                {
                    ObjectName = part.ObjectName,
                    MaterialIndex = part.MaterialIndex,
                    Group = GroupOf(part.ObjectName, Materials[part.MaterialIndex].Name),
                    Positions = part.Positions.ToArray(),
                    Uvs = part.Uvs.ToArray(),
                    Normals = part.Normals.ToArray(),
                    Triangles = part.Triangles.ToArray()
                };
                BuildNormalsAndTangents(geometry);
                Geometries.Add(geometry);
            }

            int vertices = 0;
            foreach (ObjGeometry geometry in Geometries) vertices += geometry.Positions.Length;
            FacetMeanDegrees = _facetSamples > 0 ? _facetDegrees / _facetSamples : 0f;
            FacetOverFivePercent = _facetSamples > 0 ? 100f * _facetOverFive / _facetSamples : 0f;
            MelonLogger.Msg($"[L2A7V] parsed {Geometries.Count} parts / {vertices} vertices / " +
                            $"{Materials.Count} materials from {directory}; " +
                            $"{SmoothShadedParts} part(s) shaded on the file's normals, {FlatShadedParts} on derived ones, " +
                            $"triangle vs shading normal {FacetMeanDegrees:0.0} deg mean / " +
                            $"{FacetOverFivePercent:0} pc over 5 deg on {_facetSamples} samples");
            if (parser.SkippedMaterials.Count > 0)
                MelonLogger.Msg("[L2A7V] dropped the segments of the untextured materials: " +
                                string.Join(", ", parser.SkippedMaterials));

            foreach (MtlDef material in Materials)
                if (material.AlbedoPath == null)
                    MelonLogger.Warning("[L2A7V] material '" + material.Name + "' has no albedo map");

            var perGroup = new Dictionary<LpGroup, int[]>();
            foreach (ObjGeometry geometry in Geometries)
            {
                int[] totals;
                if (!perGroup.TryGetValue(geometry.Group, out totals)) perGroup[geometry.Group] = totals = new int[2];
                totals[0] += geometry.Positions.Length;
                totals[1] += geometry.Triangles.Length / 3;
            }
            foreach (KeyValuePair<LpGroup, int[]> entry in perGroup)
                MelonLogger.Msg($"[L2A7V] group {entry.Key}: {entry.Value[0]} vertices / {entry.Value[1]} triangles");
        }

        private static void ParseMtl(string mtlPath, string directory, Dictionary<string, int> materialMap)
        {
            if (!File.Exists(mtlPath)) return;
            using (var reader = new StreamReader(mtlPath))
            {
                MtlDef current = null;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    int separator = IndexOfSeparator(line);
                    if (separator < 0) continue;
                    string tag = line.Substring(0, separator).ToLowerInvariant();
                    string rest = line.Substring(separator + 1).Trim();
                    switch (tag)
                    {
                        case "newmtl":
                            current = new MtlDef { Name = rest };
                            if (!materialMap.ContainsKey(rest)) materialMap.Add(rest, Materials.Count);
                            Materials.Add(current);
                            break;
                        case "kd":
                            if (current != null) current.Diffuse = ParseColor(rest);
                            break;
                        case "map_kd":
                            if (current != null) current.AlbedoPath = Resolve(directory, rest);
                            break;
                        case "map_normal":
                        case "bump":
                            if (current != null) current.NormalPath = Resolve(directory, rest);
                            break;
                        case "map_ao":
                            if (current != null) current.OcclusionPath = Resolve(directory, rest);
                            break;
                        case "map_ks":
                            if (current != null) current.GlossPath = Resolve(directory, rest);
                            break;
                    }
                }
            }
        }

        private static int IndexOfSeparator(string line)
        {
            for (int i = 0; i < line.Length; i++)
                if (line[i] == ' ' || line[i] == '\t') return i;
            return -1;
        }

        private static Color ParseColor(string rest)
        {
            string[] p = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3) return Color.white;
            return new Color(ParseFloat(p[0]), ParseFloat(p[1]), ParseFloat(p[2]), 1f);
        }

        private static string Resolve(string directory, string relativePath)
        {
            string combined = Path.Combine(directory, relativePath.Trim('"').Trim()
                .Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(combined) ? combined : null;
        }

        /// <summary>Parts shaded by the export's own smoothed normals, and parts left on derived flat ones.</summary>
        public static int SmoothShadedParts;

        public static int FlatShadedParts;

        /// <summary>
        /// Mean angle, in degrees, between a sampled triangle's flat normal and the normal it is shaded with,
        /// plus the share of those samples more than 5 degrees apart. Zero means every triangle still carries
        /// its own plane, which is what made the hull read as a pile of facets.
        /// </summary>
        public static float FacetMeanDegrees;

        public static float FacetOverFivePercent;

        private static int _facetSamples;
        private static float _facetDegrees;
        private static int _facetOverFive;

        /// <summary>
        /// Builds the tangent basis, and the normals too only when the file did not supply them.
        ///
        /// This export tears its mesh: every corner of every triangle is its own vertex, so summing face
        /// normals per index returns that triangle's own plane and paints the hull as a pile of facets. The
        /// vn field carries the artist's smoothing groups (measured: the worst corner of 60.5% of the
        /// triangles sits more than 5 deg off its own plane), so it is used as-is, corners the export left
        /// without one take their face's plane, and the per-index sum survives only as the fallback for a
        /// file with no normals at all.
        /// </summary>
        private static void BuildNormalsAndTangents(ObjGeometry geometry)
        {
            int vertexCount = geometry.Positions.Length;
            bool useFileNormals = HasCompleteNormals(geometry.Normals, vertexCount);
            if (useFileNormals) SmoothShadedParts++;
            else FlatShadedParts++;

            var normals = useFileNormals ? geometry.Normals : new Vector3[vertexCount];
            var tangentSums = new Vector4[vertexCount];
            var bounds = new Bounds(geometry.Positions[0], Vector3.zero);

            for (int t = 0; t + 2 < geometry.Triangles.Length; t += 3)
            {
                int i0 = geometry.Triangles[t];
                int i1 = geometry.Triangles[t + 1];
                int i2 = geometry.Triangles[t + 2];

                Vector3 p0 = geometry.Positions[i0];
                Vector3 p1 = geometry.Positions[i1];
                Vector3 p2 = geometry.Positions[i2];
                bounds.Encapsulate(p0);
                bounds.Encapsulate(p1);
                bounds.Encapsulate(p2);

                Vector3 edge1 = p1 - p0;
                Vector3 edge2 = p2 - p0;
                Vector3 faceNormal = Vector3.Cross(edge1, edge2);
                if (faceNormal.sqrMagnitude > 1e-12f) faceNormal.Normalize();
                else faceNormal = Vector3.up;

                if (useFileNormals)
                {
                    if (normals[i0].sqrMagnitude < 1e-12f) normals[i0] = faceNormal;
                    if (normals[i1].sqrMagnitude < 1e-12f) normals[i1] = faceNormal;
                    if (normals[i2].sqrMagnitude < 1e-12f) normals[i2] = faceNormal;
                }
                else
                {
                    normals[i0] += faceNormal;
                    normals[i1] += faceNormal;
                    normals[i2] += faceNormal;
                }

                if ((t & 63) == 0)
                {
                    Vector3 shaded = normals[i0];
                    float angle = shaded.sqrMagnitude > 1e-12f
                        ? Vector3.Angle(faceNormal, shaded.normalized)
                        : 0f;
                    _facetSamples++;
                    _facetDegrees += angle;
                    if (angle > 5f) _facetOverFive++;
                }

                float du1 = geometry.Uvs[i1].x - geometry.Uvs[i0].x;
                float dv1 = geometry.Uvs[i1].y - geometry.Uvs[i0].y;
                float du2 = geometry.Uvs[i2].x - geometry.Uvs[i0].x;
                float dv2 = geometry.Uvs[i2].y - geometry.Uvs[i0].y;
                float determinant = du1 * dv2 - du2 * dv1;
                if (Mathf.Abs(determinant) > 1e-10f)
                {
                    float r = 1f / determinant;
                    Vector3 tangent = (edge1 * dv2 - edge2 * dv1) * r;
                    Vector3 bitangent = (edge2 * du1 - edge1 * du2) * r;
                    float handedness = Vector3.Dot(Vector3.Cross(faceNormal, tangent), bitangent) < 0f ? -1f : 1f;
                    var value = new Vector4(tangent.x, tangent.y, tangent.z, handedness);
                    tangentSums[i0] += value;
                    tangentSums[i1] += value;
                    tangentSums[i2] += value;
                }
            }

            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 normal = normals[i];
                if (normal.sqrMagnitude < 1e-12f) normal = Vector3.up;
                normal.Normalize();
                normals[i] = normal;

                Vector4 sum = tangentSums[i];
                Vector3 tangent = sum.sqrMagnitude < 1e-12f
                    ? Perpendicular(normal)
                    : new Vector3(sum.x, sum.y, sum.z) - normal * Vector3.Dot(normal, new Vector3(sum.x, sum.y, sum.z));
                if (tangent.sqrMagnitude < 1e-8f) tangent = Perpendicular(normal);
                else tangent.Normalize();
                tangentSums[i] = new Vector4(tangent.x, tangent.y, tangent.z, sum.w < 0f ? -1f : 1f);
            }

            geometry.Normals = normals;
            geometry.Tangents = tangentSums;
            geometry.Bounds = bounds;
        }

        /// <summary>
        /// True when the file supplied a direction for essentially every corner. The export carries a handful
        /// of degenerate normals (measured one in 282,598), so a near-complete field is adopted and those few
        /// corners fall back to their own face rather than costing the whole part its smoothing.
        /// </summary>
        private static bool HasCompleteNormals(Vector3[] normals, int vertexCount)
        {
            if (normals == null || normals.Length != vertexCount || vertexCount == 0) return false;
            int missing = 0;
            for (int i = 0; i < normals.Length; i++)
                if (normals[i].sqrMagnitude < 1e-8f) missing++;
            return missing <= vertexCount / 500;
        }

        private static Vector3 Perpendicular(Vector3 normal)
        {
            Vector3 reference = Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right;
            return Vector3.Cross(reference, normal).normalized;
        }
    }
}

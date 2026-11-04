using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// The War Thunder x-ray export (Interior.obj): the armour arrays and the compartment modules, in the same
    /// frame and scale as the exterior art.
    ///
    /// This is the L2A7HU mod's parser, and the whole point of it is that one file feeds two consumers:
    /// <see cref="LpArmorOverlay"/> takes the arrays the visible plates do not cover, and <see cref="LpInterior"/>
    /// takes the compartment itself. Keeping the armour here instead of in a baked mesh is what lets an array
    /// be named, classified and given its own zone at the point it is read, rather than looked up by a string
    /// that has to keep matching a binary.
    ///
    /// The export's axes are the exterior model's: +X forward, +Y up, +Z starboard. ObjModel swaps X and Z once
    /// for GHPC; this parser does the same, which is why the module centres land inside the hull with no second
    /// calibration.
    /// </summary>
    public static class LpXrayParts
    {
        private const string Tag = "[L2A7V]";

        public static volatile bool Loaded;
        public static volatile bool Failed;
        public static string Error;

        public sealed class Part
        {
            public string Name;
            public string Bone;
            public string Class;
            public Vector3[] Positions;
            public Vector3[] Normals;
            public int[] Triangles;
            public Vector3 Center;
            public Vector3 Size;
        }

        public static readonly List<Part> Parts = new List<Part>();

        public static int Triangles
        {
            get
            {
                int total = 0;
                foreach (Part part in Parts) total += part.Triangles.Length / 3;
                return total;
            }
        }

        public static void Preload(string directory)
        {
            string path = Path.Combine(directory, "Interior.obj");
            if (!File.Exists(path))
            {
                Failed = true;
                MelonLogger.Msg(Tag + " no Interior.obj beside the art; the x-ray armour and compartment stay out");
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
                    MelonLogger.Error(Tag + " x-ray parse failed: " + ex);
                }
            });
        }

        private sealed class Builder
        {
            public string Name;
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();
            public readonly Dictionary<long, int> Map = new Dictionary<long, int>();
        }

        private static void Parse(string path)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var builders = new List<Builder>();
            Builder active = null;

            using (var reader = new StreamReader(path))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length < 3 || line[0] == '#') continue;
                    char second = line[1];
                    if (line[0] == 'v' && second == ' ')
                    {
                        positions.Add(ReadVector(line));
                    }
                    else if (line[0] == 'v' && second == 'n' && line[2] == ' ')
                    {
                        normals.Add(ReadVector(line));
                    }
                    else if ((line[0] == 'g' || line[0] == 'o') && second == ' ')
                    {
                        active = new Builder { Name = line.Substring(2).Trim() };
                        builders.Add(active);
                    }
                    else if (line[0] == 'f' && second == ' ')
                    {
                        if (active == null)
                        {
                            active = new Builder { Name = "xray" };
                            builders.Add(active);
                        }
                        ReadFace(line, active, positions, normals);
                    }
                }
            }

            foreach (Builder builder in builders)
            {
                if (builder.Triangles.Count < 3) continue;
                var part = new Part
                {
                    Name = builder.Name,
                    Positions = builder.Positions.ToArray(),
                    Normals = builder.Normals.ToArray(),
                    Triangles = builder.Triangles.ToArray(),
                };
                Measure(part);
                Classify(part);
                Parts.Add(part);
            }

            int armour = 0, modules = 0;
            foreach (Part part in Parts)
            {
                if (part.Class == "int_armor") armour += part.Triangles.Length / 3;
                else modules += part.Triangles.Length / 3;
            }
            MelonLogger.Msg(Tag + " x-ray parsed: " + Parts.Count + " module(s) / " + Triangles +
                            " triangles (" + armour + " armour shell, " + modules + " compartment) from " + path);
        }

        private static Vector3 ReadVector(string line)
        {
            string[] p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            float x = ParseFloat(p, 1);
            float y = ParseFloat(p, 2);
            float z = ParseFloat(p, 3);
            // The export is +X forward / +Z starboard; GHPC is +Z forward / +X starboard.
            return new Vector3(z, y, x);
        }

        private static float ParseFloat(string[] tokens, int index)
        {
            if (index >= tokens.Length) return 0f;
            float value;
            return float.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : 0f;
        }

        private static void ReadFace(string line, Builder builder, List<Vector3> positions, List<Vector3> normals)
        {
            string[] tokens = line.Substring(2).Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3) return;

            var corners = new int[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                string[] parts = tokens[i].Split('/');
                int vertex = IndexOf(parts, 0, positions.Count);
                int normal = parts.Length > 2 && parts[2].Length > 0 ? IndexOf(parts, 2, normals.Count) : -1;
                corners[i] = AddCorner(builder, vertex, normal, positions, normals);
            }
            for (int i = 2; i < corners.Length; i++)
            {
                // Swapping the axes mirrors the geometry, so the winding is reversed to keep faces outward.
                builder.Triangles.Add(corners[0]);
                builder.Triangles.Add(corners[i]);
                builder.Triangles.Add(corners[i - 1]);
            }
        }

        private static int AddCorner(Builder builder, int vertex, int normal, List<Vector3> positions,
            List<Vector3> normals)
        {
            long key = ((long)vertex << 32) ^ (uint)normal;
            int index;
            if (builder.Map.TryGetValue(key, out index)) return index;

            index = builder.Positions.Count;
            builder.Map[key] = index;
            builder.Positions.Add(positions[Mathf.Clamp(vertex, 0, positions.Count - 1)]);
            builder.Normals.Add(normal >= 0 && normal < normals.Count ? normals[normal] : Vector3.zero);
            return index;
        }

        private static int IndexOf(string[] parts, int slot, int count)
        {
            if (parts.Length <= slot) return 0;
            int value;
            if (!int.TryParse(parts[slot], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return 0;
            return value < 0 ? count + value : value - 1;
        }

        private static void Measure(Part part)
        {
            Vector3 min = part.Positions[0];
            Vector3 max = min;
            for (int i = 1; i < part.Positions.Length; i++)
            {
                min = Vector3.Min(min, part.Positions[i]);
                max = Vector3.Max(max, part.Positions[i]);
            }
            part.Center = (min + max) * 0.5f;
            part.Size = max - min;
        }

        private static void Classify(Part part)
        {
            string name = part.Name.ToLowerInvariant();
            part.Bone = BoneFor(name);
            part.Class = ClassFor(name);
        }

        /// <summary>Which pivot the module rides. The breech and the main barrel recoil with the gun; the
        /// roof machine-gun barrel and the gunner's sight head sit on the turret roof and turn with it, not
        /// with the gun. Everything else with "turret" in it turns with the turret, the rest is hull-side.</summary>
        private static string BoneFor(string name)
        {
            if (name.Contains("cannon_breech") || name.Contains("gun_barrel_01")) return "Gun";
            if (name.Contains("gun_barrel_02") || name.Contains("optic_gun")) return "Turret";
            if (name.Contains("turret") || name.Contains("commander") || name.Contains("gunner") ||
                name.Contains("loader") || name.Contains("fire_control") || name.Contains("radio") ||
                name.Contains("drive_turret"))
                return "Turret";
            return "Hull";
        }

        /// <summary>Colour class for the x-ray picture, the same grouping the donor's compartment sheets
        /// quantise to (armour, liner, engine, fire control, ammunition, crew, metal).</summary>
        private static string ClassFor(string name)
        {
            if (name.StartsWith("composite_armor") || name.StartsWith("spall_liner") ||
                name.StartsWith("firewall_armor"))
                return "int_armor";
            if (name.StartsWith("ammo")) return "int_ammo";
            if (name == "driver_xray" || name == "gunner_xray" || name == "loader_xray" ||
                name == "commander_xray")
                return "int_crew";
            if (name.Contains("engine") || name.Contains("transmission") || name.Contains("radiator") ||
                name.Contains("power_system"))
                return "int_engine";
            if (name.Contains("fire_control") || name.Contains("radio") || name.Contains("optic") ||
                name.Contains("panoramic") || name.Contains("driver_controls"))
                return "int_fcs";
            return "int_metal";
        }
    }
}

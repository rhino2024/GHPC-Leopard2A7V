using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// The mod's data table, in the format the T-90M mod uses: one line per entry, pipe separated, sections
    /// opened by the first field. Keeping the numbers in a text file means armour values, the list of donor
    /// plates to switch off and the hatch hinges can be tuned without a rebuild.
    ///
    ///   armour|composite_armor_hull_01_xray|560|1100|3
    ///   vanilla_off|Upper Front Plate
    ///   hatch|hatch_03|commander|0,1,0|95|0.45,2.55,-0.11
    ///
    /// Missing file, missing section or a bad line is not fatal: the built-in defaults stay in force.
    /// </summary>
    public static class LpData
    {
        private const string Tag = "[L2A7V][data]";
        private const string FileName = "L2A7V_data.txt";

        internal sealed class Armour
        {
            internal float Ke;
            internal float Ce;
            internal int Layers;
        }

        internal sealed class Module
        {
            internal string Name;
            internal Vector3 Centre;
            internal Vector3 Size;
            internal float Health;
            internal float Threshold;
        }

        internal sealed class Hatch
        {
            internal string Group;
            internal string Role;
            internal Vector3 Axis = Vector3.up;
            internal float Degrees = 95f;
            internal Vector3 Hinge;
            internal bool HasHinge;
        }

        private static bool _loaded;

        internal static readonly Dictionary<string, Armour> Armours =
            new Dictionary<string, Armour>(StringComparer.OrdinalIgnoreCase);

        internal static readonly List<string> VanillaOff = new List<string>();

        internal static readonly Dictionary<string, string> CodexOverrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static readonly List<Hatch> Hatches = new List<Hatch>();

        internal static readonly List<Module> Modules = new List<Module>();

        internal struct Role
        {
            internal Color Albedo;
            internal int Gloss;
        }

        internal static readonly Dictionary<string, Role> InteriorRoles =
            new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);

        internal static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            string path = Path.Combine(LpXrayArmor.ModFolder, FileName);
            if (!File.Exists(path))
            {
                MelonLogger.Msg(Tag + " no " + FileName + "; using the built-in numbers");
                return;
            }

            int bad = 0, lines = 0;
            try
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    lines++;
                    string[] f = line.Split('|');
                    string section = f[0].Trim().ToLowerInvariant();
                    try
                    {
                        switch (section)
                        {
                            case "armour": ReadArmour(f); break;
                            case "vanilla_off": ReadVanillaOff(f); break;
                            case "hatch": ReadHatch(f); break;
                            case "codex": ReadCodex(f); break;
                            case "module": ReadModule(f); break;
                            case "interior_role": ReadRole(f); break;
                            default: bad++; break;
                        }
                    }
                    catch (Exception)
                    {
                        bad++;
                    }
                }
            }
            catch (Exception error)
            {
                MelonLogger.Warning(Tag + " could not read " + FileName + ": " + error.Message);
                return;
            }

            MelonLogger.Msg(Tag + " loaded " + Armours.Count + " armour override(s), " + Modules.Count + " module(s), " + VanillaOff.Count +
                            " donor plate name(s) and " + Hatches.Count + " hatch hinge(s) from " + FileName +
                            (bad > 0 ? " (" + bad + " line(s) ignored of " + lines + ")" : string.Empty));
        }

        private static void ReadArmour(string[] f)
        {
            if (f.Length < 5) return;
            var entry = new Armour
            {
                Ke = Float(f[2]),
                Ce = Float(f[3]),
                Layers = Mathf.Clamp(Mathf.RoundToInt(Float(f[4])), 1, 6),
            };
            Armours[f[1].Trim()] = entry;
        }

        private static void ReadRole(string[] f)
        {
            if (f.Length < 3) return;
            string[] rgb = f[2].Split(',');
            if (rgb.Length != 3) return;
            InteriorRoles[f[1].Trim()] = new Role
            {
                Albedo = new Color(Float(rgb[0]), Float(rgb[1]), Float(rgb[2]), 1f),
                Gloss = f.Length > 3 ? Mathf.RoundToInt(Float(f[3])) : 32,
            };
        }

        private static void ReadModule(string[] f)
        {
            if (f.Length < 6) return;
            string[] centre = f[2].Split(',');
            string[] size = f[3].Split(',');
            if (centre.Length != 3 || size.Length != 3) return;
            Modules.Add(new Module
            {
                Name = f[1].Trim(),
                Centre = new Vector3(Float(centre[0]), Float(centre[1]), Float(centre[2])),
                Size = new Vector3(Float(size[0]), Float(size[1]), Float(size[2])),
                Health = Float(f[4]),
                Threshold = f.Length > 5 ? Float(f[5]) : 25f,
            });
        }

        private static void ReadCodex(string[] f)
        {
            if (f.Length < 3) return;
            CodexOverrides[f[1].Trim()] = f[2].Trim();
        }

        private static void ReadVanillaOff(string[] f)
        {
            if (f.Length < 2) return;
            string name = f[1].Trim();
            if (name.Length > 0) VanillaOff.Add(name);
        }

        private static void ReadHatch(string[] f)
        {
            if (f.Length < 4) return;
            var hatch = new Hatch
            {
                Group = f[1].Trim(),
                Role = f[2].Trim(),
            };
            string[] axis = f[3].Split(',');
            if (axis.Length == 3)
            {
                var vector = new Vector3(Float(axis[0]), Float(axis[1]), Float(axis[2]));
                if (vector.sqrMagnitude > 0.0001f) hatch.Axis = vector.normalized;
            }
            if (f.Length > 4) hatch.Degrees = Float(f[4]);
            if (f.Length > 5)
            {
                string[] hinge = f[5].Split(',');
                if (hinge.Length == 3)
                {
                    hatch.Hinge = new Vector3(Float(hinge[0]), Float(hinge[1]), Float(hinge[2]));
                    hatch.HasHinge = true;
                }
            }
            Hatches.Add(hatch);
        }

        private static float Float(string text)
        {
            return float.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        internal static bool TryArmour(string groupName, out float ke, out float ce, out int layers)
        {
            Load();
            Armour entry;
            if (Armours.TryGetValue(groupName, out entry))
            {
                ke = entry.Ke;
                ce = entry.Ce;
                layers = entry.Layers;
                return true;
            }
            ke = ce = 0f;
            layers = 0;
            return false;
        }

        /// <summary>The codex name fragment the table assigns to this group, if any.</summary>
        internal static string CodexFor(string groupName)
        {
            Load();
            string fragment;
            return CodexOverrides.TryGetValue(groupName, out fragment) ? fragment : null;
        }

        internal static bool TryRole(string name, out Color albedo, out int gloss)
        {
            Load();
            Role role;
            if (InteriorRoles.TryGetValue(name, out role))
            {
                albedo = role.Albedo;
                gloss = role.Gloss;
                return true;
            }
            albedo = Color.white;
            gloss = 32;
            return false;
        }

        internal static bool IsVanillaOff(string plateName)
        {
            Load();
            foreach (string name in VanillaOff)
                if (plateName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        internal static Hatch HatchFor(string groupName, string role)
        {
            Load();
            foreach (Hatch hatch in Hatches)
            {
                if (hatch.Group.Equals(groupName, StringComparison.OrdinalIgnoreCase)) return hatch;
                if (!string.IsNullOrEmpty(role) &&
                    hatch.Role.Equals(role, StringComparison.OrdinalIgnoreCase) && hatch.Group.Length == 0)
                    return hatch;
            }
            return null;
        }
    }
}

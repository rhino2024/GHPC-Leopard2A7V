using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using MelonLoader;

namespace Leopard2A7V;

// L2A7V values and source meshes. The data-driven schema is this mod's own
// design; no external mod's protection values, geometry or material
// parameters are used.
public sealed class LpArmourSpec
{
    public sealed class Entry
    {
        public string Piece;
        public string Group;
        public string Mode;
        public float Ke;
        public float Ce;
        public bool AngleMatters;
        public string Material;
        public string Replace;
        public string Name;
        public string Source;
        public bool Convex => Mode == "composite";
        public bool Drawn => Mode == "drawn";
        public bool Edit => Mode == "edit";
        public bool Slab => Mode == "slab";
        // Edit and slab entries drive an existing donor piece instead of
        // spawning baked geometry, so they never need a Meshes lookup.
        public bool NeedsGeometry => !Drawn && !Edit && !Slab;
    }

    public readonly List<Entry> Entries = new List<Entry>();
    private static LpArmourSpec _cached;

    public static LpArmourSpec Load()
    {
        if (_cached != null) return _cached;
        LpArmourSpec result = new LpArmourSpec();
        using (Stream stream = typeof(LpArmourSpec).Assembly.GetManifestResourceStream("armour_l2a7v.tsv"))
        {
            if (stream == null) throw new InvalidDataException("embedded armour_l2a7v.tsv is missing");
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                int number = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    number++;
                    line = line.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    string[] f = line.Split('|');
                    if (f.Length != 10) throw new FormatException("armour table line " + number + " must have 10 fields");
                    Entry e = new Entry
                    {
                        Piece = Clean(f[0]), Group = Clean(f[1]), Mode = Clean(f[2]).ToLowerInvariant(),
                        Ke = Number(f[3]), Ce = Number(f[4]), AngleMatters = f[5].Trim() == "1",
                        Material = Clean(f[6]), Replace = Clean(f[7]), Name = Clean(f[8]), Source = Clean(f[9])
                    };
                    if (e.Piece.Length == 0 || (e.Mode != "uniform" && e.Mode != "composite" && e.Mode != "drawn"
                        && e.Mode != "edit" && e.Mode != "slab"))
                        throw new FormatException("invalid armour table line " + number);
                    // Edit/slab drive an existing donor piece by exact name.
                    if ((e.Edit || e.Slab) && (e.Replace.Length == 0 || e.Replace == "-"))
                        throw new FormatException("armour table line " + number + ": " + e.Mode + " needs an exact donor replacement name");
                    if (!e.Drawn && (e.Ke < 0f || e.Ce < 0f)) throw new FormatException("negative protection on line " + number);
                    result.Entries.Add(e);
                }
            }
        }
        if (result.Entries.Count == 0) throw new InvalidDataException("armour table has no entries");
        _cached = result;
        return result;
    }

    private static string Clean(string text) => (text ?? string.Empty).Trim();
    private static float Number(string text) => float.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
}

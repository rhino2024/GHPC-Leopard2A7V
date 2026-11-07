using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

// Data model for modules_l2a7v.json. The file is an authored L2A7V
// table: coordinates marked in the file are leo2_rig export space and are
// converted exactly once by the builder; Interior.obj part vertices are
// already in leo2_rig Unity space and are never re-converted here.
public sealed class LpModuleSpec
{
    public sealed class Box
    {
        public Vector3 Centre;
        public Vector3 Size;
    }

    public sealed class Hook
    {
        public string Name;
        public string Target;
    }

    public sealed class Module
    {
        public string Id;
        public string Kind;
        public string Name;
        public string Group;

        public readonly List<string> Parts = new List<string>();
        public readonly List<Box> Boxes = new List<Box>();

        public float Health;
        public float Threshold;
        public float Pressure;

        // Normal RHAe mm; 0/0 = damage zone with no added resistance.
        public float Ke;
        public float Ce;

        public readonly List<string> Replaces = new List<string>();
        public readonly List<Hook> Hooks = new List<Hook>();

        public string Status;
        public string Rack;
    }

    private const string Resource = "modules_l2a7v.json";

    public readonly List<Module> Modules = new List<Module>();

    // Load-time validation messages. They describe authored-table defects,
    // not runtime donor state, so they are reported once and never retried.
    public readonly List<string> Problems = new List<string>();

    private static LpModuleSpec _cached;

    public static LpModuleSpec Load()
    {
        if (_cached != null) return _cached;
        try
        {
            using (Stream stream = typeof(LpModuleSpec).Assembly.GetManifestResourceStream(Resource))
            {
                if (stream == null) throw new InvalidDataException(Resource + " is missing from the assembly");
                string text;
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    text = reader.ReadToEnd();
                }
                _cached = Parse(text);
                if (_cached.Problems.Count > 0)
                {
                    foreach (string problem in _cached.Problems)
                        MelonLogger.Warning("[L2A7V][dm-modules] table problem: " + problem);
                }
                MelonLogger.Msg("[L2A7V][dm-modules] loaded " + _cached.Modules.Count + " module definition(s) from " + Resource);
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Error("[L2A7V][dm-modules] table load failed: " + ex.Message);
            _cached = new LpModuleSpec();
            _cached.Problems.Add(ex.Message);
        }
        return _cached;
    }

    private static LpModuleSpec Parse(string text)
    {
        LpModuleSpec spec = new LpModuleSpec();
        object root = LpMiniJson.Parse(text);
        Dictionary<string, object> rootObject = LpMiniJson.Object(root);
        List<object> modules = rootObject == null ? null : LpMiniJson.Array(rootObject.TryGetValue("modules", out object value) ? value : null);
        if (modules == null)
        {
            spec.Problems.Add("no 'modules' array");
            return spec;
        }
        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (object node in modules)
        {
            Dictionary<string, object> entry = LpMiniJson.Object(node);
            if (entry == null)
            {
                spec.Problems.Add("a module entry is not an object");
                continue;
            }
            Module module = new Module
            {
                Id = LpMiniJson.String(entry, "id"),
                Kind = LpMiniJson.String(entry, "kind"),
                Name = LpMiniJson.String(entry, "name"),
                Group = LpMiniJson.String(entry, "group"),
                Status = LpMiniJson.String(entry, "status"),
                Rack = LpMiniJson.String(entry, "rack")
            };
            module.Health = (float)LpMiniJson.Number(entry, "health");
            module.Threshold = (float)LpMiniJson.Number(entry, "threshold");
            module.Pressure = (float)LpMiniJson.Number(entry, "pressure");
            if (float.IsNaN(module.Pressure)) module.Pressure = 1f;
            module.Ke = (float)LpMiniJson.Number(entry, "ke");
            module.Ce = (float)LpMiniJson.Number(entry, "ce");
            if (float.IsNaN(module.Ke)) module.Ke = 0f;
            if (float.IsNaN(module.Ce)) module.Ce = 0f;
            if (module.Ke < 0f || module.Ce < 0f)
            { spec.Problems.Add("'" + module.Id + "': ke/ce must not be negative"); continue; }

            if (string.IsNullOrEmpty(module.Id) || !ids.Add(module.Id))
            {
                spec.Problems.Add("missing or duplicate module id");
                continue;
            }
            // Number() yields NaN for a missing key and NaN fails every
            // comparison, so a plain "health <= 0" test would let NaN through.
            if (float.IsNaN(module.Health) || float.IsNaN(module.Threshold))
            { spec.Problems.Add("'" + module.Id + "': health and threshold are required numbers"); continue; }
            if (string.IsNullOrEmpty(module.Name)) { spec.Problems.Add("'" + module.Id + "': no name"); continue; }
            if (module.Group != "Hull" && module.Group != "Turret" && module.Group != "Gun")
            { spec.Problems.Add("'" + module.Id + "': group must be Hull, Turret or Gun"); continue; }
            if (module.Health <= 0f) { spec.Problems.Add("'" + module.Id + "': health must be positive"); continue; }
            if (module.Threshold <= 0f || module.Threshold >= module.Health)
            { spec.Problems.Add("'" + module.Id + "': threshold must be within (0, health)"); continue; }
            if (module.Rack != null && module.Rack != "hull" && module.Rack != "turret")
            { spec.Problems.Add("'" + module.Id + "': rack must be 'hull' or 'turret'"); continue; }

            List<string> partNames = ReadStrings(entry, "parts");
            if (partNames != null) module.Parts.AddRange(partNames);
            List<string> replaces = ReadStrings(entry, "replaces");
            if (replaces != null) module.Replaces.AddRange(replaces);
            if (LpMiniJson.Array(entry.TryGetValue("boxes", out object boxesNode) ? boxesNode : null) is List<object> boxList)
            {
                foreach (object boxNode in boxList)
                {
                    Dictionary<string, object> boxEntry = LpMiniJson.Object(boxNode);
                    if (boxEntry == null) { spec.Problems.Add("'" + module.Id + "': a box is not an object"); continue; }
                    Vector3 centre = ReadVector(boxEntry, "c");
                    Vector3 size = ReadVector(boxEntry, "s");
                    if (float.IsNaN(centre.x) || float.IsNaN(size.x))
                    { spec.Problems.Add("'" + module.Id + "': a box needs 'c' and 's' triplets"); continue; }
                    if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
                    { spec.Problems.Add("'" + module.Id + "': box sizes must be positive"); continue; }
                    module.Boxes.Add(new Box { Centre = centre, Size = size });
                }
            }
            if (module.Parts.Count == 0 && module.Boxes.Count == 0)
            { spec.Problems.Add("'" + module.Id + "': no geometry (parts or boxes)"); continue; }

            if (LpMiniJson.Array(entry.TryGetValue("hooks", out object hooksNode) ? hooksNode : null) is List<object> hookList)
            {
                foreach (object hookNode in hookList)
                {
                    Dictionary<string, object> hookEntry = LpMiniJson.Object(hookNode);
                    if (hookEntry == null) { spec.Problems.Add("'" + module.Id + "': a hook is not an object"); continue; }
                    Hook hook = new Hook
                    {
                        Name = LpMiniJson.String(hookEntry, "hook"),
                        Target = LpMiniJson.String(hookEntry, "target")
                    };
                    if (string.IsNullOrEmpty(hook.Name))
                    { spec.Problems.Add("'" + module.Id + "': a hook has no name"); continue; }
                    module.Hooks.Add(hook);
                }
            }
            spec.Modules.Add(module);
        }
        return spec;
    }

    private static List<string> ReadStrings(Dictionary<string, object> entry, string key)
    {
        if (entry == null || !entry.TryGetValue(key, out object value)) return null;
        List<object> list = LpMiniJson.Array(value);
        if (list == null) return null;
        List<string> result = new List<string>();
        foreach (object item in list)
        {
            if (item is string text && text.Length > 0) result.Add(text);
        }
        return result;
    }

    private static Vector3 ReadVector(Dictionary<string, object> entry, string key)
    {
        if (entry == null || !entry.TryGetValue(key, out object value)) return new Vector3(float.NaN, float.NaN, float.NaN);
        List<object> list = LpMiniJson.Array(value);
        if (list == null || list.Count != 3) return new Vector3(float.NaN, float.NaN, float.NaN);
        double x, y, z;
        if (!TryNumber(list[0], out x) || !TryNumber(list[1], out y) || !TryNumber(list[2], out z))
            return new Vector3(float.NaN, float.NaN, float.NaN);
        // Authored triplets are leo2_rig export space: +X forward, +Y up, +Z right.
        return new Vector3((float)z, (float)y, (float)x);
    }

    private static bool TryNumber(object node, out double value)
    {
        if (node is double number) { value = number; return true; }
        value = 0.0;
        return false;
    }
}

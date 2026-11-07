using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.AI.Radios;
using GHPC.Camera;
using GHPC.Equipment;
using GHPC.Equipment.Optics;
using GHPC.PhysicsHelpers;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weapons;
using MelonLoader;
using UnityEngine;
// The assembly carries a legacy global-namespace Dc (old hit-trigger class).
// A global type shadows any using import, so the GHPC.Equipment one is bound
// through an alias - the same reason LpModulesOwn spells the full name out.
using Dc = GHPC.Equipment.DestructibleComponent;

namespace Leopard2A7V;

// Marks an authored module damage zone so retries can tell a complete zone
// from a half-built one left behind by an interrupted build.
public sealed class LpModuleZone : MonoBehaviour
{
    public string Id;
    public bool Registered;
}

// Data-driven compartments for the L2A7V: Build() creates the
// Interior.obj / authored-box damage zones, Wire() waits for the donor
// systems, then swaps the named donor hit zones for the new ones and hooks
// the native damage / repair events. Both stages are bounded by LpLateInit
// and are strictly per-vehicle: nothing here searches other tanks.
public static class LpModuleBuilder
{
    private sealed class HookDef
    {
        public Type Owner;
        public string Field;
        public (string evt, string method)[] Handlers;

        public HookDef(Type owner, string field, params (string, string)[] handlers)
        {
            Owner = owner;
            Field = field;
            Handlers = handlers;
        }
    }

    private sealed class ResolvedHook
    {
        public LpModuleSpec.Module Module;
        public LpModuleSpec.Hook Hook;
        public Dc Dc;
        public HookDef Def;
        public object Owner;
        public AimablePlatform Platform;
        public FireControlSystem Fcs;
        public AmmoRack Rack;
    }

    public sealed class ModuleSet : MonoBehaviour, IDamageStatus
    {
        internal LpModuleSpec Spec;
        internal readonly List<(LpModuleSpec.Module m, Dc dc)> Built = new List<(LpModuleSpec.Module, Dc)>();
        internal readonly List<(Dc dc, string text)> Lines = new List<(Dc, string)>();
        internal readonly List<string> Problems = new List<string>();
        internal readonly List<Dc> Replaced = new List<Dc>();
        internal bool BuiltDone;
        internal bool Wired;
        internal string Waiting;

        public bool Damaged => Lines.Any(l => l.dc != null && l.dc.IsDestroyed);

        public string DamageStatus
        {
            get
            {
                string text = string.Concat(Lines.Where(l => l.dc != null && l.dc.IsDestroyed).Select(l => l.text + "\n").ToArray());
                if (text.Length == 0) text = "No module damage reported\n";
                return text;
            }
        }
    }

    private const string Tag = "[L2A7V][dm-modules]";
    private const float IgnitionDelay = 1.2f;

    private static readonly Dictionary<string, HookDef> HookTable = new Dictionary<string, HookDef>
    {
        ["ChassisDamageManager.EngineHitZone"] = new HookDef(typeof(ChassisDamageManager), "EngineHitZone", ("Destroyed", "engineDestroyed"), ("HealthChanged", "engineDamaged"), ("Repaired", "engineRepaired")),
        ["ChassisDamageManager.TransmissionHitZone"] = new HookDef(typeof(ChassisDamageManager), "TransmissionHitZone", ("Destroyed", "transmissionDestroyed"), ("Repaired", "transmissionRepaired")),
        ["ChassisDamageManager.RadiatorHitZone"] = new HookDef(typeof(ChassisDamageManager), "RadiatorHitZone", ("Destroyed", "radiatorDestroyed"), ("Repaired", "radiatorRepaired")),
        ["AimablePlatform.HitZones"] = new HookDef(typeof(AimablePlatform), "HitZones", ("Destroyed", "hitZoneDestroyed"), ("Repaired", "hitZoneRepaired")),
        ["FireControlSystem.ComputerComponent"] = new HookDef(typeof(FireControlSystem), "ComputerComponent", ("Destroyed", "ComputerDestroyed")),
        ["FireControlSystem.LaserComponent"] = new HookDef(typeof(FireControlSystem), "LaserComponent", ("Destroyed", "LaserDestroyed")),
        ["CameraSlot.HitZone"] = new HookDef(typeof(CameraSlot), "HitZone", ("Destroyed", "HandleSlotDestroyed")),
        ["WeaponSystem.BarrelHitZone"] = new HookDef(typeof(WeaponSystem), "BarrelHitZone", ("Destroyed", "SingleBarrelDestroyed")),
        ["WeaponSystem.BreechHitZone"] = new HookDef(typeof(WeaponSystem), "BreechHitZone", ("Destroyed", "BreechDestroyed")),
        ["Radio._radioDestructible"] = new HookDef(typeof(Radio), "_radioDestructible", ("Destroyed", "DestroyRadio"))
    };

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly FieldInfo UnitStatuses = typeof(Unit).GetField("DamageStatuses", Any);
    private static readonly FieldInfo GroupColliders = typeof(RaycastColliderGroup).GetField("childColliders", Any);

    public static bool Build(GameObject unitRoot)
    {
        if (unitRoot == null) return true;
        ModuleSet set = unitRoot.GetComponent<ModuleSet>() ?? unitRoot.AddComponent<ModuleSet>();
        if (set.BuiltDone) return true;
        try
        {
            Transform rig = LpArmourBuilder.Rig(unitRoot);
            Unit unit = LpFind.Unit(unitRoot);
            LpModuleSpec spec = LpModuleSpec.Load();
            if (rig == null || unit == null || spec == null || spec.Modules.Count == 0) return false;

            // Mesh modules need the Interior.obj geometry; authored-box modules
            // can build without it, so only wait while a mesh module is pending.
            bool meshesPending = spec.Modules.Any(m => m.Parts.Count > 0 && set.Built.All(b => b.m != m));
            if (meshesPending && !LpXrayParts.Loaded && !LpXrayParts.Failed)
            {
                set.Waiting = "waiting for Interior.obj x-ray geometry";
                return false;
            }

            List<RaycastColliderGroup> groups = LpArmourBuilder.Groups(rig);
            if (groups.Count == 0)
            {
                set.Waiting = "no native collider group linked to leo2_rig";
                return false;
            }
            set.Spec = spec;

            int built = 0;
            int adopted = 0;
            foreach (LpModuleSpec.Module module in spec.Modules)
            {
                if (set.Built.Any(b => b.m == module)) continue;
                Dc existing = ExistingZone(unitRoot.transform, module.Id);
                if (existing != null)
                {
                    set.Built.Add((module, existing));
                    adopted++;
                    continue;
                }
                if (BuildOne(unitRoot, rig, groups, set, module)) built++;
            }
            set.BuiltDone = true;
            set.Waiting = null;
            MelonLogger.Msg(Tag + " built " + built + " zone(s), adopted " + adopted + " existing zone(s); " + set.Problems.Count + " problem(s) recorded on '" + unitRoot.name + "'");
            return true;
        }
        catch (Exception ex)
        {
            set.Waiting = ex.Message;
            MelonLogger.Warning(Tag + " build will retry: " + ex.Message);
            return false;
        }
    }

    public static bool Wire(GameObject unitRoot)
    {
        if (unitRoot == null) return true;
        ModuleSet set = unitRoot.GetComponent<ModuleSet>();
        if (set == null) return false;
        if (set.Wired) return true;
        try
        {
            if (!set.BuiltDone) return false;
            if (set.Built.Count == 0)
            {
                // Every module failed its build transaction and was recorded in
                // Problems; there is nothing to wire, so end the stage cleanly
                // instead of spinning until the bounded-init timeout.
                MelonLogger.Warning(Tag + " no module zone was built on '" + unitRoot.name + "'; wiring skipped");
                set.Wired = true;
                return true;
            }
            Transform rig = LpArmourBuilder.Rig(unitRoot);
            Unit unit = LpFind.Unit(unitRoot);
            WeaponSystem main = MainWeapon(unitRoot);
            if (rig == null || unit == null) return false;

            // Resolve every hook target and donor before touching anything, so
            // an incomplete donor state leaves the vehicle completely stock.
            List<string> waiting = new List<string>();
            List<Dc> all = unitRoot.GetComponentsInChildren<Dc>(true)
                .Where(z => z != null).Distinct().ToList();
            List<Dc> donors = ResolveDonors(set, all, waiting);
            List<ResolvedHook> resolved = new List<ResolvedHook>();
            foreach ((LpModuleSpec.Module module, Dc dc) in set.Built)
            {
                foreach (LpModuleSpec.Hook hook in module.Hooks)
                {
                    ResolvedHook item = ResolveHook(unitRoot, main, module, hook, dc, waiting);
                    if (item != null) resolved.Add(item);
                }
            }
            if (waiting.Count > 0)
            {
                string message = string.Join("; ", waiting.Distinct().ToArray());
                if (set.Waiting != message)
                {
                    MelonLogger.Warning(Tag + " wiring waits for " + message + "; no donor hit zone was modified");
                    set.Waiting = message;
                }
                return false;
            }

            // Apply: retire donors, install the new zones, subscribe the
            // native damage / repair events, publish the status lines.
            List<RaycastColliderGroup> groups = LpArmourBuilder.Groups(rig);
            Dictionary<Type, Component[]> owners = OwnerIndex(unitRoot);
            foreach (Dc donor in donors)
            {
                try
                {
                    SwitchOff(donor, groups, owners);
                    set.Replaced.Add(donor);
                }
                catch (Exception ex)
                {
                    // One stubborn donor must not abort the apply phase: a
                    // retry would re-subscribe already-installed events.
                    set.Problems.Add("donor '" + donor.Name + "': " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            foreach (ResolvedHook item in resolved)
            {
                try
                {
                    ApplyHook(set, item);
                }
                catch (Exception ex)
                {
                    set.Problems.Add("'" + item.Module.Id + "' " + item.Hook.Name + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            foreach ((LpModuleSpec.Module m, Dc dc) in set.Built)
            {
                if (m.Status != null) set.Lines.Add((dc, m.Status));
            }
            AddStatus(unit, set);
            set.Wired = true;
            set.Waiting = null;
            MelonLogger.Msg(Tag + " wired " + resolved.Count + " native hook(s), replaced " + set.Replaced.Count + " donor hit zone(s) on '" + unitRoot.name + "'");
            foreach (string problem in set.Problems)
            {
                MelonLogger.Warning(Tag + " wiring problem: " + problem);
            }
            return true;
        }
        catch (Exception ex)
        {
            set.Waiting = ex.Message;
            MelonLogger.Warning(Tag + " wiring will retry: " + ex.Message);
            return false;
        }
    }

    private static bool BuildOne(GameObject unitRoot, Transform rig, List<RaycastColliderGroup> groups, ModuleSet set, LpModuleSpec.Module module)
    {
        Transform bone = LpArmourBuilder.Bone(rig, module.Group);
        Transform group = LpArmourBuilder.GroupRoot(groups, module.Group);
        if (bone == null || group == null)
        {
            set.Problems.Add("'" + module.Id + "': no " + module.Group + " bone or collider group");
            return false;
        }
        Matrix4x4 rigToBone = bone.worldToLocalMatrix * rig.localToWorldMatrix;
        Matrix4x4 boneToGroup = group.worldToLocalMatrix * bone.localToWorldMatrix;
        Mesh mesh = MeshOf(module, rigToBone);
        if (mesh == null && module.Boxes.Count == 0)
        {
            set.Problems.Add("'" + module.Id + "': no Interior.obj part matches the table");
            return false;
        }
        GameObject go = new GameObject("L2A7V_Mod " + module.Id);
        try
        {
            go.SetActive(false);
            go.AddComponent<LpModuleZone>().Id = module.Id;
            go.transform.SetParent(group, false);
            // The zone transform mirrors the bone inside the collider group, so
            // turret and gun zones ride their LateFollow parents exactly once.
            go.transform.localPosition = boneToGroup.MultiplyPoint3x4(Vector3.zero);
            go.transform.localRotation = boneToGroup.rotation;
            go.transform.localScale = Vector3.one;
            go.layer = CodeUtils.LAYER_INDEX_RAYCAST_ONLY;
            try { go.tag = "Penetrable"; } catch (UnityException) { }

            Collider collider;
            Material xray = LpArmorOwn.XrayMaterial();
            if (mesh != null)
            {
                MeshCollider meshCollider = go.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = mesh;
                meshCollider.convex = false;
                meshCollider.isTrigger = false;
                collider = meshCollider;
                MeshFilter filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                if (xray != null) renderer.sharedMaterial = xray;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            else
            {
                BoxCollider first = null;
                foreach (LpModuleSpec.Box box in module.Boxes)
                {
                    BoxCollider boxCollider = go.AddComponent<BoxCollider>();
                    boxCollider.center = rigToBone.MultiplyPoint3x4(box.Centre);
                    Vector3 size = rigToBone.MultiplyVector(box.Size);
                    boxCollider.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                    boxCollider.isTrigger = false;
                    if (first == null) first = boxCollider;
                }
                collider = first;
            }

            Dc dc = go.AddComponent<Dc>();
            dc.SetName(module.Name);
            Configure(dc, module);
            if (collider != null) dc.ComponentCollision = collider;
            if (module.Ke > 0f || module.Ce > 0f)
            {
                UniformArmor armour = go.AddComponent<UniformArmor>();
                armour.SetName(module.Name);
                armour.PrimarySabotRha = module.Ke * LpConfig.ArmorScale;
                armour.PrimaryHeatRha = module.Ce * LpConfig.ArmorScale;
                armour.SecondarySabotRha = armour.PrimarySabotRha;
                armour.SecondaryHeatRha = armour.PrimaryHeatRha;
                armour.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
                armour.AngleMatters = true;
                armour.DetonatedObjects = Array.Empty<GameObject>();
                armour.UndetonatedObjects = Array.Empty<GameObject>();
                armour.CenterPoint = go.transform;
                armour.Unit = LpFind.Unit(unitRoot);
                ArmorCodexScriptable codex = LpArmourBuilder.Codex(unitRoot, "armour steel");
                if (codex != null) typeof(UniformArmor).GetField("_armorType", Any).SetValue(armour, codex);
            }
            dc.HealthChanged += delegate (IDestructible source, float percent)
            {
                MelonLogger.Msg(Tag + " '" + module.Id + "' health " + (percent * 100f).ToString("0") + "%");
            };

            AarVisual aar = go.AddComponent<AarVisual>();
            aar.RenderMode = AarVisual.AarVisualRenderMode.XrayOnly;
            aar.HideUntilAar = true;
            aar.SwitchMaterials = xray != null;
            aar.AarMaterial = xray;

            go.SetActive(true);
            RaycastColliderGroup.AddNewColliderSetToParentGroup(go.transform);
            go.GetComponent<LpModuleZone>().Registered = true;
            aar.UpdateVisibility();
            set.Built.Add((module, dc));
            MelonLogger.Msg(Tag + " zone '" + module.Id + "' under '" + group.name + "' health " + module.Health.ToString("0") + (mesh != null ? " (Interior.obj mesh)" : " (" + module.Boxes.Count + " authored box(es))"));
            return true;
        }
        catch (Exception ex)
        {
            set.Problems.Add("'" + module.Id + "': " + ex.GetType().Name + ": " + ex.Message);
            UnityEngine.Object.Destroy(go);
            return false;
        }
    }

    // Real Interior.obj geometry: part vertices are already in leo2_rig Unity
    // space, so they are only baked from the rig into the zone's bone space.
    // Authored JSON boxes arrive already converted (see LpModuleSpec).
    private static Mesh MeshOf(LpModuleSpec.Module module, Matrix4x4 rigToBone)
    {
        if (module.Parts.Count == 0) return null;
        List<LpXrayParts.Part> parts = new List<LpXrayParts.Part>();
        foreach (string name in module.Parts)
        {
            LpXrayParts.Part part = LpXrayParts.Parts.FirstOrDefault(p => p != null && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (part == null) return null; // every named part must exist; no partial geometry
            parts.Add(part);
        }
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<int> triangles = new List<int>();
        foreach (LpXrayParts.Part part in parts)
        {
            int start = vertices.Count;
            foreach (Vector3 v in part.Positions) vertices.Add(rigToBone.MultiplyPoint3x4(v));
            foreach (Vector3 n in part.Normals) normals.Add(rigToBone.MultiplyVector(n));
            foreach (int index in part.Triangles) triangles.Add(index + start);
        }
        Mesh mesh = new Mesh { name = "L2A7V DM " + module.Id };
        mesh.SetVertices(vertices);
        if (normals.Count == vertices.Count && normals.Any(n => n.sqrMagnitude > 0.001f)) mesh.SetNormals(normals);
        else mesh.RecalculateNormals();
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void Configure(Component component, LpModuleSpec.Module module)
    {
        Type type = component.GetType();
        SetFloat(type, component, "_health", module.Health);
        SetFloat(type, component, "_fullHealth", module.Health);
        SetFloat(type, component, "_damageThreshold", module.Threshold);
        SetFloat(type, component, "_pressureTolerance", module.Pressure);
        FieldInfo children = type.GetField("_children", Any);
        if (children != null && children.GetValue(component) == null)
        {
            children.SetValue(component, Activator.CreateInstance(children.FieldType));
        }
        SetFloat(type, component, "Health", module.Health);
        SetFloat(type, component, "MinimumRhaTrigger", module.Threshold);
    }

    private static void SetFloat(Type type, object target, string name, float value)
    {
        FieldInfo field = type.GetField(name, Any);
        if (field != null && field.FieldType == typeof(float)) field.SetValue(target, value);
    }

    private static Dc ExistingZone(Transform root, string id)
    {
        string wanted = "L2A7V_Mod " + id;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t.name != wanted) continue;
            LpModuleZone marker = t.GetComponent<LpModuleZone>();
            Dc dc = t.GetComponent<Dc>();
            Collider collider = t.GetComponent<Collider>();
            if (marker != null && marker.Id == id && dc != null && collider != null && collider.enabled &&
                t.gameObject.layer == CodeUtils.LAYER_INDEX_RAYCAST_ONLY)
            {
                // Register once: a build retry re-enters this path and a second
                // call would append the same colliders to childColliders again.
                if (!marker.Registered)
                {
                    RaycastColliderGroup.AddNewColliderSetToParentGroup(t);
                    marker.Registered = true;
                }
                return dc;
            }
            // Destroy is deferred to the end of the frame. Rename and deactivate
            // first so a same-frame retry cannot adopt the half-built zone.
            t.name = "L2A7V_IncompleteMod " + id;
            t.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(t.gameObject);
            MelonLogger.Warning(Tag + " removing an incomplete '" + id + "' zone before retrying");
            return null;
        }
        return null;
    }

    private static List<Dc> ResolveDonors(ModuleSet set, List<Dc> all, List<string> waiting)
    {
        List<Dc> donors = new List<Dc>();
        HashSet<Dc> ours = new HashSet<Dc>(set.Built.Select(b => b.dc));
        foreach ((LpModuleSpec.Module module, Dc dc) in set.Built)
        {
            foreach (string wanted in module.Replaces)
            {
                // Replace every explicitly named donor component, not an
                // arbitrary first match: 'gunner's sight' exists twice.
                List<Dc> matches = all.Where(z => z != null && !ours.Contains(z) &&
                    string.Equals(z.Name, wanted, StringComparison.Ordinal)).Distinct().ToList();
                if (matches.Count == 0)
                {
                    // A wrong authored name degrades only that module: the
                    // donor keeps working and the hook reports it. Blocking
                    // here would kill the whole wiring over one bad string.
                    set.Problems.Add("'" + module.Id + "': base hit zone '" + wanted + "' to replace not found; donor retained");
                    continue;
                }
                donors.AddRange(matches);
            }
        }
        return donors.Distinct().ToList();
    }

    private static ResolvedHook ResolveHook(GameObject unitRoot, WeaponSystem main, LpModuleSpec.Module module, LpModuleSpec.Hook hook, Dc dc, List<string> waiting)
    {
        ResolvedHook item = new ResolvedHook { Module = module, Hook = hook, Dc = dc };
        if (hook.Name == "Module.PowerDrive" || hook.Name == "Module.HandDrive")
        {
            item.Platform = Platform(main, hook.Target);
            if (item.Platform == null)
            {
                waiting.Add("'" + module.Id + "': no '" + hook.Target + "' aimable platform");
                return null;
            }
            return item;
        }
        if (hook.Name == "Module.Stabiliser")
        {
            item.Fcs = main == null ? null : main.FCS;
            if (item.Fcs == null || item.Fcs.Mounts == null || item.Fcs.Mounts.Length == 0)
            {
                waiting.Add("'" + module.Id + "': no main fire-control mounts");
                return null;
            }
            return item;
        }
        if (hook.Name == "Module.AmmoRack")
        {
            item.Rack = FindRack(unitRoot, hook.Target);
            if (item.Rack == null)
            {
                waiting.Add("'" + module.Id + "': no unique '" + hook.Target + "' ammunition rack");
                return null;
            }
            return item;
        }
        if (!HookTable.TryGetValue(hook.Name, out HookDef def))
        {
            waiting.Add("'" + module.Id + "': unknown hook '" + hook.Name + "'");
            return null;
        }
        item.Def = def;
        item.Owner = ResolveOwner(unitRoot, main, def.Owner, hook.Target);
        if (item.Owner == null)
        {
            waiting.Add("'" + module.Id + "': no " + def.Owner.Name + " '" + (hook.Target ?? "-") + "'");
            return null;
        }
        return item;
    }

    private static object ResolveOwner(GameObject unitRoot, WeaponSystem main, Type ownerType, string target)
    {
        if (ownerType == typeof(ChassisDamageManager) || ownerType == typeof(Radio))
        {
            return unitRoot.GetComponentInChildren(ownerType, true);
        }
        if (target == "main")
        {
            if (ownerType == typeof(WeaponSystem)) return main;
            if (ownerType == typeof(FireControlSystem)) return main == null ? null : main.FCS;
        }
        if (ownerType == typeof(AimablePlatform) && (target == "turret" || target == "gun"))
        {
            return Platform(main, target);
        }
        if (ownerType == typeof(CameraSlot) && (target == "gunner_day" || target == "gunner_night"))
        {
            return GunnerSlot(main, target);
        }
        return null;
    }

    // The main gun follows the donor loadout selection, never a first-match
    // WeaponSystem of the same class (roof MGs would steal the hooks).
    private static WeaponSystem MainWeapon(GameObject root)
    {
        if (root == null) return null;
        WeaponsManager manager = root.GetComponent<WeaponsManager>();
        LoadoutManager loadout = root.GetComponent<LoadoutManager>();
        if (manager == null || loadout == null || manager.Weapons == null || manager.Weapons.Length == 0) return null;
        int index = loadout.WeaponIndex;
        if (index < 0 || index >= manager.Weapons.Length) return null;
        return manager.Weapons[index] == null ? null : manager.Weapons[index].Weapon;
    }

    private static AimablePlatform Platform(WeaponSystem main, string target)
    {
        FireControlSystem fcs = main == null ? null : main.FCS;
        if (fcs == null || fcs.Mounts == null) return null;
        AimablePlatform[] mounts = fcs.Mounts.Where(x => x != null).ToArray();
        if (target == "turret") return mounts.FirstOrDefault(x => x.Axis.ToString() == "Y");
        if (target == "gun") return mounts.FirstOrDefault(x => x.Axis.ToString() == "X");
        return null;
    }

    private static CameraSlot GunnerSlot(WeaponSystem main, string target)
    {
        FireControlSystem fcs = main == null ? null : main.FCS;
        UsableOptic optic = fcs == null ? null : fcs.MainOptic;
        if (optic == null || optic.slot == null) return null;
        CameraSlot day = optic.slot.IsLinkedNightSight ? optic.slot.LinkedDaySight : optic.slot;
        if (day == null) return null;
        return target == "gunner_night" ? day.LinkedNightSight : day;
    }

    // Precise rack mapping: hull spare rack -> Hull, ready rack -> Turret.
    // A name/location mismatch or an ambiguous duplicate aborts the wiring
    // instead of falling back to the nearest or first rack.
    private static AmmoRack FindRack(GameObject unitRoot, string zone)
    {
        VehicleInfo info = LpFind.VehicleInfo(unitRoot);
        if (info == null || info.AmmoRacks == null) return null;
        AmmoRack.AmmoRackLocation expected = zone == "hull" ? AmmoRack.AmmoRackLocation.Hull : AmmoRack.AmmoRackLocation.Turret;
        string objectName = zone == "hull" ? "hull spare rack" : "ready rack";
        AmmoRack found = null;
        foreach (AmmoRack rack in info.AmmoRacks)
        {
            if (rack == null || rack.Location != expected || !string.Equals(rack.name, objectName, StringComparison.OrdinalIgnoreCase)) continue;
            if (found != null) return null;
            found = rack;
        }
        return found;
    }

    private static Dictionary<Type, Component[]> OwnerIndex(GameObject unitRoot)
    {
        Dictionary<Type, Component[]> index = new Dictionary<Type, Component[]>();
        foreach (HookDef def in HookTable.Values)
        {
            if (index.ContainsKey(def.Owner)) continue;
            index[def.Owner] = unitRoot.GetComponentsInChildren(def.Owner, true).Cast<Component>().Where(c => c != null).Distinct().ToArray();
        }
        return index;
    }

    private static void ApplyHook(ModuleSet set, ResolvedHook item)
    {
        if (item.Hook.Name == "Module.PowerDrive")
        {
            AimablePlatform platform = item.Platform;
            bool stabilized = platform.Stabilized;
            item.Dc.Destroyed += delegate
            {
                platform.SetPowerActive(false);
                platform.Stabilized = false;
            };
            item.Dc.Repaired += delegate
            {
                platform.SetPowerActive(true);
                platform.Stabilized = stabilized;
            };
            return;
        }
        if (item.Hook.Name == "Module.HandDrive")
        {
            AimablePlatform platform = item.Platform;
            float hand = platform.SpeedUnpowered;
            item.Dc.Destroyed += delegate { platform.SpeedUnpowered = 0f; };
            item.Dc.Repaired += delegate { platform.SpeedUnpowered = hand; };
            return;
        }
        if (item.Hook.Name == "Module.Stabiliser")
        {
            AimablePlatform[] mounts = item.Fcs.Mounts.Where(x => x != null).ToArray();
            bool[] was = mounts.Select(x => x.Stabilized).ToArray();
            item.Dc.Destroyed += delegate
            {
                foreach (AimablePlatform mount in mounts) mount.Stabilized = false;
            };
            item.Dc.Repaired += delegate
            {
                for (int i = 0; i < mounts.Length; i++)
                {
                    if (mounts[i].PowerEnabled) mounts[i].Stabilized = was[i];
                }
            };
            return;
        }
        if (item.Hook.Name == "Module.AmmoRack")
        {
            ModuleSet holder = set;
            AmmoRack rack = item.Rack;
            string id = item.Module.Id;
            item.Dc.Destroyed += delegate { CookOff(holder, rack, id); };
            return;
        }
        FieldInfo field = item.Def.Owner.GetField(item.Def.Field, Any);
        if (field == null) throw new MissingFieldException(item.Def.Owner.Name, item.Def.Field);
        if (field.FieldType.IsArray)
        {
            Dc[] current = field.GetValue(item.Owner) as Dc[] ?? new Dc[0];
            List<Dc> list = current.Where(z => z != null && !set.Replaced.Contains(z)).ToList();
            if (!list.Contains(item.Dc)) list.Add(item.Dc);
            field.SetValue(item.Owner, list.ToArray());
        }
        else
        {
            object value = field.GetValue(item.Owner);
            if (value is Dc occupied)
            {
                if (occupied == item.Dc) return;
                if (!set.Replaced.Contains(occupied))
                {
                    throw new InvalidOperationException(item.Def.Owner.Name + "." + item.Def.Field + " still holds an unreplaced hit zone '" + occupied.Name + "'");
                }
            }
            field.SetValue(item.Owner, item.Dc);
        }
        foreach ((string evt, string method) in item.Def.Handlers)
        {
            Subscribe(item.Dc, evt, item.Owner, method);
        }
    }

    private static void Subscribe(Dc dc, string evt, object owner, string method)
    {
        EventInfo info = typeof(Dc).GetEvent(evt);
        MethodInfo handler = owner.GetType().GetMethod(method, Any);
        if (info == null) throw new MissingMemberException("Dc." + evt);
        if (handler == null) throw new MissingMethodException(owner.GetType().Name, method);
        Delegate existing = typeof(Dc).GetField(evt, Any)?.GetValue(dc) as Delegate;
        if (existing != null && existing.GetInvocationList().Any(d => d.Target == owner && d.Method == handler)) return;
        info.AddEventHandler(dc, Delegate.CreateDelegate(info.EventHandlerType, owner, handler));
    }

    private static void SwitchOff(Dc old, List<RaycastColliderGroup> groups, Dictionary<Type, Component[]> owners)
    {
        foreach (HookDef def in HookTable.Values)
        {
            if (!owners.TryGetValue(def.Owner, out Component[] found)) continue;
            foreach (Component owner in found)
            {
                if (owner == null) continue;
                FieldInfo field = def.Owner.GetField(def.Field, Any);
                if (field == null) continue;
                if (field.FieldType.IsArray)
                {
                    if (field.GetValue(owner) is Dc[] current && current.Contains(old))
                    {
                        field.SetValue(owner, current.Where(z => z != null && z != old).ToArray());
                    }
                }
                else if (field.GetValue(owner) is Dc reference && reference == old)
                {
                    field.SetValue(owner, null);
                }
                else
                {
                    continue;
                }
                foreach ((string evt, string method) in def.Handlers)
                {
                    MethodInfo handler = owner.GetType().GetMethod(method, Any);
                    EventInfo info = typeof(Dc).GetEvent(evt);
                    if (handler == null || info == null) continue;
                    try
                    {
                        info.RemoveEventHandler(old, Delegate.CreateDelegate(info.EventHandlerType, owner, handler));
                    }
                    catch (ArgumentException)
                    {
                        // The donor was never subscribed through this owner; nothing to undo.
                    }
                }
            }
        }
        Collider[] colliders = old.GetComponents<Collider>();
        foreach (RaycastColliderGroup group in groups)
        {
            if (!(GroupColliders?.GetValue(group) is List<Collider> children)) continue;
            foreach (Collider collider in colliders) children.Remove(collider);
        }
        foreach (Collider collider in colliders) UnityEngine.Object.Destroy(collider);
        old.Hidden = true;
        if (old.GetComponent<IArmor>() == null && old.transform.childCount == 0)
        {
            old.gameObject.SetActive(false);
        }
        foreach (Renderer renderer in old.GetComponents<Renderer>()) UnityEngine.Object.Destroy(renderer);
        foreach (AarVisual aar in old.GetComponents<AarVisual>()) UnityEngine.Object.Destroy(aar);
    }

    // Cook-off keeps the bugfix1 semantics: a rack with blowout panels vents
    // up to three occupied, undamaged round slots; anything else ignites after
    // a short delay through a coroutine instead of a per-frame Update.
    private static void CookOff(ModuleSet set, AmmoRack rack, string id)
    {
        MelonLogger.Msg(Tag + " ammunition zone '" + id + "' destroyed");
        if (rack == null)
        {
            MelonLogger.Warning(Tag + " no rack mapping for '" + id + "'; no fire will be invented");
            return;
        }
        if (rack.OnFire) return;
        if (rack.HasBlowoutPanels)
        {
            int damaged = 0;
            RackRound[] inventory = rack.RackInventory;
            if (inventory != null)
            {
                for (int i = 0; i < inventory.Length && damaged < 3; i++)
                {
                    if (inventory[i] != null && !inventory[i].Damaged)
                    {
                        rack.DamageRoundBySlot(i);
                        damaged++;
                    }
                }
            }
            MelonLogger.Msg(Tag + " '" + rack.GetFriendlyName() + "' has blowout panels; " + damaged + " occupied, undamaged round slot(s) processed");
            return;
        }
        set.StartCoroutine(CookOffAfter(rack));
    }

    private static IEnumerator CookOffAfter(AmmoRack rack)
    {
        yield return new WaitForSeconds(IgnitionDelay);
        if (rack != null && !rack.OnFire)
        {
            rack.NotifyRackFire();
            MelonLogger.Msg(Tag + " cook-off started in '" + rack.GetFriendlyName() + "'");
        }
    }

    private static void AddStatus(Unit unit, ModuleSet set)
    {
        if (unit == null || UnitStatuses == null) return;
        IDamageStatus[] current = UnitStatuses.GetValue(unit) as IDamageStatus[] ?? new IDamageStatus[0];
        if (current.Contains(set)) return;
        UnitStatuses.SetValue(unit, current.Concat(new IDamageStatus[] { set }).ToArray());
    }
}

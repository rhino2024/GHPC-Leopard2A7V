using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GHPC;
using GHPC.Equipment;
using GHPC.PhysicsHelpers;
using GHPC.Utility;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V;

public sealed class LpArmourPiece : MonoBehaviour
{
    public string Piece;
}

public static class LpArmourBuilder
{
    private sealed class State : MonoBehaviour
    {
        internal bool Built;
        internal int Pieces;
        internal int Retired;
        internal string Pending = "not built";
    }

    private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ArmorCodexScriptable> Codices = new Dictionary<string, ArmorCodexScriptable>(StringComparer.OrdinalIgnoreCase);
    private static readonly FieldInfo ArmorTypeField = typeof(UniformArmor).GetField("_armorType", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo ChildColliders = typeof(RaycastColliderGroup).GetField("childColliders", BindingFlags.Instance | BindingFlags.NonPublic);
    private static bool _assetsRead;
    private static string _assetError;

    public static bool Build(GameObject unitRoot)
    {
        if (unitRoot == null) return true;
        State state = unitRoot.GetComponent<State>() ?? unitRoot.AddComponent<State>();
        if (state.Built) return true;
        try
        {
            Transform rig = Rig(unitRoot);
            Unit unit = LpFind.Unit(unitRoot);
            if (rig == null || unit == null || !ObjModel.Loaded || ObjModel.Failed || !LpXrayParts.Loaded || LpXrayParts.Failed)
            {
                state.Pending = "waiting for leo2_rig, Unit and OBJ geometry";
                return false;
            }
            EnsureRawGeometry();
            if (_assetError != null)
            {
                state.Pending = _assetError;
                return false;
            }
            List<RaycastColliderGroup> groups = Groups(rig);
            if (groups.Count == 0)
            {
                state.Pending = "no native collider group linked to leo2_rig";
                return false;
            }
            LpArmourSpec spec = LpArmourSpec.Load();
            // Vehicle-scoped one-pass lookup: every entry must resolve inside
            // this unit only. A scene-root search would let a second L2A7V
            // adopt the first tank's pieces and retire its own donors anyway.
            HashSet<string> existing = ExistingPieces(unitRoot.transform);
            int pieces = 0;
            int missing = 0;
            int edits = 0;
            foreach (LpArmourSpec.Entry entry in spec.Entries)
            {
                if (existing.Contains(entry.Piece)) continue;
                // donor_edit: keep the donor geometry/collider, rewrite its
                // protection values from the table. Idempotent on retry.
                if (entry.Edit)
                {
                    edits += EditDonor(groups, entry);
                    continue;
                }
                // donor slab: build an own slab over the donor's collider
                // bounds, then let the shared retire path remove the donor.
                if (entry.Slab)
                {
                    if (SpawnSlab(unitRoot, groups, entry, unit))
                    {
                        pieces++;
                        existing.Add(entry.Piece);
                    }
                    continue;
                }
                if (!Meshes.TryGetValue(entry.Piece, out Mesh mesh))
                {
                    MelonLogger.Warning("[L2A7V][dm-armour] no source geometry for '" + entry.Piece + "'; donor remains");
                    missing++;
                    continue;
                }
                Transform bone = Bone(rig, entry.Group);
                Transform group = GroupRoot(groups, entry.Group);
                if (bone == null || group == null)
                {
                    MelonLogger.Warning("[L2A7V][dm-armour] no " + entry.Group + " bone/group for '" + entry.Piece + "'; donor remains");
                    missing++;
                    continue;
                }
                if (Spawn(unitRoot, rig, bone, group, entry, mesh, unit)) pieces++;
            }
            // Retire whenever this vehicle owns at least one piece: a retry
            // after a mid-loop exception finds all pieces existing and must
            // still complete donor retirement instead of failing forever.
            if (pieces == 0 && edits == 0 && existing.Count == 0)
            {
                state.Pending = "no valid replacement geometry";
                return false;
            }
            int retired = RetireDonors(existing, groups, spec);
            state.Built = true;
            state.Pieces = pieces + existing.Count;
            state.Retired = retired;
            state.Pending = null;
            MelonLogger.Msg("[L2A7V][dm-armour] built " + pieces + " data-defined piece(s), edited " + edits + " donor value(s), adopted " + existing.Count + " existing piece(s), retired " + retired + " explicitly named donor plate(s); crew, tracks and unlisted armour retained");
            return true;
        }
        catch (Exception ex)
        {
            state.Pending = ex.Message;
            MelonLogger.Warning("[L2A7V][dm-armour] build will retry: " + ex);
            return false;
        }
    }

    private static bool Spawn(GameObject root, Transform rig, Transform bone, Transform group, LpArmourSpec.Entry entry, Mesh mesh, Unit unit)
    {
        GameObject parent = root.transform.Find("L2A7V_DM")?.gameObject;
        if (parent == null)
        {
            parent = new GameObject("L2A7V_DM");
            parent.transform.SetParent(root.transform, false);
        }
        GameObject go = new GameObject(entry.Piece);
        go.SetActive(false);
        go.AddComponent<LpArmourPiece>().Piece = entry.Piece;
        go.transform.SetParent(parent.transform, false);
        go.layer = CodeUtils.LAYER_INDEX_RAYCAST_ONLY;
        try { go.tag = "Penetrable"; } catch (UnityException) { }
        Matrix4x4 sourceToBone = bone.worldToLocalMatrix * rig.localToWorldMatrix;
        Matrix4x4 boneToGroup = group.worldToLocalMatrix * bone.localToWorldMatrix;
        go.transform.SetParent(group, false);
        go.transform.localPosition = boneToGroup.MultiplyPoint3x4(Vector3.zero);
        go.transform.localRotation = boneToGroup.rotation;
        go.transform.localScale = Vector3.one;
        Mesh baked = Bake(entry.Piece, mesh, sourceToBone * Matrix4x4.Translate(-Anchor(entry)));
        if (baked == null || baked.triangles.Length < 3)
        {
            UnityEngine.Object.Destroy(go);
            return false;
        }
        MeshFilter filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = baked;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        Material xray = LpArmorOwn.XrayMaterial();
        if (xray != null) renderer.sharedMaterial = xray;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        if (!entry.Drawn)
        {
            MeshCollider collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = baked;
            collider.convex = entry.Convex;
            collider.isTrigger = false;
            UniformArmor armour = go.AddComponent<UniformArmor>();
            armour.SetName(entry.Name);
            armour.PrimarySabotRha = entry.Ke * LpConfig.ArmorScale;
            armour.PrimaryHeatRha = entry.Ce * LpConfig.ArmorScale;
            armour.SecondarySabotRha = armour.PrimarySabotRha;
            armour.SecondaryHeatRha = armour.PrimaryHeatRha;
            armour.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
            armour.AngleMatters = entry.AngleMatters;
            armour.DetonatedObjects = Array.Empty<GameObject>();
            armour.UndetonatedObjects = Array.Empty<GameObject>();
            armour.CenterPoint = go.transform;
            armour.Unit = unit;
            if (ArmorTypeField != null)
            {
                ArmorCodexScriptable codex = Codex(root, entry.Material);
                if (codex != null) ArmorTypeField.SetValue(armour, codex);
                else MelonLogger.Warning("[L2A7V][dm-armour] no L2A7V material codex '" + entry.Material + "'; nominal RHA values retained for testing");
            }
        }
        AarVisual aar = go.AddComponent<AarVisual>();
        aar.RenderMode = AarVisual.AarVisualRenderMode.XrayOnly;
        aar.HideUntilAar = true;
        aar.SwitchMaterials = xray != null;
        aar.AarMaterial = xray;
        go.SetActive(true);
        if (!entry.Drawn) RaycastColliderGroup.AddNewColliderSetToParentGroup(go.transform);
        aar.UpdateVisibility();
        return true;
    }

    // donor_edit mode: the donor's own geometry and collider keep running;
    // only its protection values are rewritten from the table. UniformArmor
    // exposes the RHA fields publicly, so no reflection is needed. Variable
    // armor derives its protection from mesh thickness and cannot be value
    // edited, which is what the slab mode is for.
    private static int EditDonor(List<RaycastColliderGroup> groups, LpArmourSpec.Entry entry)
    {
        int edited = 0;
        HashSet<UniformArmor> seen = new HashSet<UniformArmor>();
        foreach (RaycastColliderGroup candidate in groups)
        {
            foreach (UniformArmor armour in candidate.GetComponentsInChildren<UniformArmor>(true))
            {
                if (armour == null || !seen.Add(armour)) continue;
                if (armour.GetComponent<LpArmourPiece>() != null) continue;
                if (!string.Equals(armour.gameObject.name, entry.Replace, StringComparison.OrdinalIgnoreCase)) continue;
                armour.PrimarySabotRha = entry.Ke * LpConfig.ArmorScale;
                armour.PrimaryHeatRha = entry.Ce * LpConfig.ArmorScale;
                armour.SecondarySabotRha = armour.PrimarySabotRha;
                armour.SecondaryHeatRha = armour.PrimaryHeatRha;
                armour.AngleMatters = entry.AngleMatters;
                edited++;
            }
        }
        if (edited == 0) MelonLogger.Warning("[L2A7V][dm-armour] donor_edit target '" + entry.Replace + "' not found; vanilla values kept");
        return edited;
    }

    // donor slab mode: build an own convex slab over the donor collider's
    // world bounds expressed in the native collider-group space, then the
    // shared retire path removes the donor. This replaces mesh-thickness
    // VariableArmor pieces (whose protection cannot be value edited) with
    // table-driven uniform plates.
    private static bool SpawnSlab(GameObject root, List<RaycastColliderGroup> groups, LpArmourSpec.Entry entry, Unit unit)
    {
        Component donor = groups.SelectMany(g => g.GetComponentsInChildren<UniformArmor>(true).Cast<Component>()
                .Concat(g.GetComponentsInChildren<VariableArmor>(true).Cast<Component>()))
            .FirstOrDefault(a => a != null && a.GetComponent<LpArmourPiece>() == null &&
                                 string.Equals(a.gameObject.name, entry.Replace, StringComparison.OrdinalIgnoreCase));
        if (donor == null)
        {
            MelonLogger.Warning("[L2A7V][dm-armour] slab donor '" + entry.Replace + "' not found; no replacement spawned");
            return false;
        }
        Collider donorCollider = donor.GetComponent<Collider>();
        if (donorCollider == null)
        {
            MelonLogger.Warning("[L2A7V][dm-armour] slab donor '" + entry.Replace + "' has no collider; no replacement spawned");
            return false;
        }
        string token = entry.Group.ToUpperInvariant() + " COLLIDERS";
        RaycastColliderGroup rcg = groups.FirstOrDefault(g => g.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        if (rcg == null)
        {
            MelonLogger.Warning("[L2A7V][dm-armour] no " + entry.Group + " collider group for slab '" + entry.Piece + "'");
            return false;
        }
        Bounds world = donorCollider.bounds;
        Matrix4x4 worldToGroup = rcg.transform.worldToLocalMatrix;
        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = worldToGroup.MultiplyPoint3x4(new Vector3(
                (i & 1) == 0 ? world.min.x : world.max.x,
                (i & 2) == 0 ? world.min.y : world.max.y,
                (i & 4) == 0 ? world.min.z : world.max.z));
            min = Vector3.Min(min, corner);
            max = Vector3.Max(max, corner);
        }
        Vector3 center = (min + max) * 0.5f;
        Vector3 size = max - min;
        // Tighten a degenerate axis so the slab stays a plate, not a plane.
        if (size.x < 0.005f) size.x = 0.005f;
        if (size.y < 0.005f) size.y = 0.005f;
        if (size.z < 0.005f) size.z = 0.005f;
        Mesh slab = SlabMesh(entry.Piece, center, size);
        GameObject go = new GameObject(entry.Piece);
        try
        {
            go.SetActive(false);
            go.AddComponent<LpArmourPiece>().Piece = entry.Piece;
            go.layer = CodeUtils.LAYER_INDEX_RAYCAST_ONLY;
            try { go.tag = "Penetrable"; } catch (UnityException) { }
            go.transform.SetParent(rcg.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = slab;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            Material xray = LpArmorOwn.XrayMaterial();
            if (xray != null) renderer.sharedMaterial = xray;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            MeshCollider collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = slab;
            collider.convex = true;
            collider.isTrigger = false;
            UniformArmor armour = go.AddComponent<UniformArmor>();
            armour.SetName(entry.Name);
            armour.PrimarySabotRha = entry.Ke * LpConfig.ArmorScale;
            armour.PrimaryHeatRha = entry.Ce * LpConfig.ArmorScale;
            armour.SecondarySabotRha = armour.PrimarySabotRha;
            armour.SecondaryHeatRha = armour.PrimaryHeatRha;
            armour.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
            armour.AngleMatters = entry.AngleMatters;
            armour.DetonatedObjects = Array.Empty<GameObject>();
            armour.UndetonatedObjects = Array.Empty<GameObject>();
            armour.CenterPoint = go.transform;
            armour.Unit = unit;
            if (ArmorTypeField != null)
            {
                ArmorCodexScriptable codex = Codex(root, entry.Material);
                if (codex != null) ArmorTypeField.SetValue(armour, codex);
            }
            AarVisual aar = go.AddComponent<AarVisual>();
            aar.RenderMode = AarVisual.AarVisualRenderMode.XrayOnly;
            aar.HideUntilAar = true;
            aar.SwitchMaterials = xray != null;
            aar.AarMaterial = xray;
            go.SetActive(true);
            RaycastColliderGroup.AddNewColliderSetToParentGroup(go.transform);
            aar.UpdateVisibility();
            return true;
        }
        catch (Exception)
        {
            UnityEngine.Object.Destroy(go);
            throw;
        }
    }

    private static Mesh SlabMesh(string key, Vector3 center, Vector3 size)
    {
        string cacheKey = "slab|" + key + "|" + Sig(Matrix4x4.Translate(center) * Matrix4x4.Scale(size));
        if (Meshes.TryGetValue(cacheKey, out Mesh cached) && cached != null) return cached;
        Vector3 half = size * 0.5f;
        Vector3[] v =
        {
            center + new Vector3(-half.x, -half.y, -half.z),
            center + new Vector3( half.x, -half.y, -half.z),
            center + new Vector3( half.x,  half.y, -half.z),
            center + new Vector3(-half.x,  half.y, -half.z),
            center + new Vector3(-half.x, -half.y,  half.z),
            center + new Vector3( half.x, -half.y,  half.z),
            center + new Vector3( half.x,  half.y,  half.z),
            center + new Vector3(-half.x,  half.y,  half.z)
        };
        // Outward-facing cube winding (quads 0-3 -Z, 4-7 +Z, sides, caps).
        int[] t =
        {
            0, 2, 1, 0, 3, 2, // back (-Z)
            4, 5, 6, 4, 6, 7, // front (+Z)
            0, 1, 5, 0, 5, 4, // bottom (-Y)
            3, 6, 2, 3, 7, 6, // top (+Y)
            0, 4, 7, 0, 7, 3, // left (-X)
            1, 2, 6, 1, 6, 5  // right (+X)
        };
        Mesh mesh = new Mesh { name = "L2A7V slab " + key, indexFormat = IndexFormat.UInt16 };
        mesh.vertices = v;
        mesh.triangles = t;
        mesh.RecalculateBounds();
        Meshes[cacheKey] = mesh;
        return mesh;
    }

    private static int RetireDonors(HashSet<string> existing, List<RaycastColliderGroup> groups, LpArmourSpec spec)
    {
        int count = 0;
        HashSet<string> completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // One collection pass over the groups; a per-entry scan repeated the
        // whole subtree walk for every replacement line.
        List<Component> candidates = groups.SelectMany(g => g.GetComponentsInChildren<UniformArmor>(true).Cast<Component>()
                .Concat(g.GetComponentsInChildren<VariableArmor>(true).Cast<Component>()))
            .Where(a => a != null && a.GetComponent<LpArmourPiece>() == null)
            .Distinct().ToList();
        foreach (LpArmourSpec.Entry entry in spec.Entries)
        {
            if (entry.Drawn || entry.Replace == "-" || entry.Replace.Length == 0 || !completed.Add(entry.Replace)) continue;
            if (!existing.Contains(entry.Piece)) continue;
            List<Component> matches = candidates
                .Where(a => string.Equals(a.gameObject.name, entry.Replace, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 0)
            {
                MelonLogger.Warning("[L2A7V][dm-armour] donor plate '" + entry.Replace + "' not found; no approximate armour removal");
                continue;
            }
            foreach (Component donor in matches)
            {
                Collider[] colliders = donor.GetComponents<Collider>();
                foreach (RaycastColliderGroup group in groups)
                {
                    if (!(ChildColliders?.GetValue(group) is List<Collider> children)) continue;
                    foreach (Collider collider in colliders) children.Remove(collider);
                }
                foreach (Collider collider in colliders) UnityEngine.Object.Destroy(collider);
                UnityEngine.Object.Destroy(donor);
                count++;
            }
        }
        return count;
    }

    private static HashSet<string> ExistingPieces(Transform scope)
    {
        HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Transform t in scope.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && t.GetComponent<LpArmourPiece>() != null) result.Add(t.name);
        }
        return result;
    }

    internal static List<RaycastColliderGroup> Groups(Transform rig)
    {
        HashSet<RaycastColliderGroup> result = new HashSet<RaycastColliderGroup>();
        foreach (LateFollowTarget target in rig.GetComponentsInChildren<LateFollowTarget>(true))
        {
            if (target.LateFollowers == null) continue;
            foreach (LateFollow follower in target.LateFollowers)
            {
                if (follower == null) continue;
                foreach (RaycastColliderGroup group in follower.GetComponentsInChildren<RaycastColliderGroup>(true)) result.Add(group);
                RaycastColliderGroup own = follower.GetComponent<RaycastColliderGroup>();
                if (own != null) result.Add(own);
            }
        }
        foreach (LateFollow follower in rig.GetComponentsInChildren<LateFollow>(true))
        {
            RaycastColliderGroup group = follower.GetComponent<RaycastColliderGroup>();
            if (group != null) result.Add(group);
        }
        return result.ToList();
    }

    internal static Transform GroupRoot(List<RaycastColliderGroup> groups, string kind)
    {
        string token = kind.ToUpperInvariant() + " COLLIDERS";
        RaycastColliderGroup group = groups.FirstOrDefault(g =>
            g.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        if (group == null) return null;
        string suffix = kind == "Hull" ? "hull" : "turret";
        return group.transform.Find("LEO2_aar_" + suffix)
            ?? group.transform.Find("LEO2_armour_" + suffix)
            ?? group.transform.Find("AAR")
            ?? group.transform.Find("ARMOR")
            ?? group.transform;
    }

    internal static Transform Rig(GameObject root) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "leo2_rig");

    internal static Transform Bone(Transform rig, string group)
    {
        if (group == "Hull") return rig.Find("hull") ?? rig;
        if (group == "Turret") return rig.Find("hull/turret");
        return rig.Find("hull/turret/mantlet");
    }

    private static Vector3 Anchor(LpArmourSpec.Entry entry)
    {
        // Raw armour is already in turret-pivot space. Visible OBJ geometry is
        // in rig space; the imported hull also has the visual pivot offset.
        // Subtract each pivot exactly once before baking rig -> bone.
        if (entry.Piece.StartsWith("ex_armor_", StringComparison.OrdinalIgnoreCase))
            return entry.Group == "Hull" ? LpConfig.Offset : LpConfig.TurretAxis;
        return entry.Group == "Turret" ? LpConfig.TurretAxis : entry.Group == "Gun" ? LpConfig.GunAxis : Vector3.zero;
    }

    private static Mesh Bake(string key, Mesh source, Matrix4x4 sourceToBone)
    {
        // Round-trip float formatting: Matrix4x4.ToString() truncates to five
        // decimals, so two distinct matrices could share one truncated key.
        string cacheKey = key + "|" + Sig(sourceToBone);
        if (Meshes.TryGetValue(cacheKey, out Mesh cached) && cached != null) return cached;
        Vector3[] vertices = source.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = sourceToBone.MultiplyPoint3x4(vertices[i]);
        Mesh result = new Mesh { name = "L2A7V DM " + key, indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        result.vertices = vertices;
        result.triangles = source.triangles;
        result.RecalculateBounds();
        // Bake only into bone space. The transform supplies bone -> group once;
        // including both matrices here would rotate/scale the mesh twice.
        Meshes[cacheKey] = result;
        return result;
    }

    private static string Sig(Matrix4x4 m)
    {
        StringBuilder builder = new StringBuilder(192);
        for (int i = 0; i < 16; i++) builder.Append(m[i].ToString("R", CultureInfo.InvariantCulture)).Append(';');
        return builder.ToString();
    }

    private static void EnsureRawGeometry()
    {
        if (_assetsRead) return;
        _assetsRead = true;
        try
        {
            using (Stream stream = typeof(LpArmourBuilder).Assembly.GetManifestResourceStream("armor.bin"))
            {
                if (stream == null) throw new InvalidDataException("armor.bin is missing");
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "GHPCARM1") throw new InvalidDataException("unsupported armour geometry header");
                    int count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        string name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
                        Vector3[] vertices = new Vector3[reader.ReadInt32()];
                        for (int j = 0; j < vertices.Length; j++) vertices[j] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        int[] triangles = new int[reader.ReadInt32()];
                        for (int j = 0; j < triangles.Length; j++) triangles[j] = reader.ReadInt32();
                        Vector3 offset = name.StartsWith("composite_armor_turret_", StringComparison.OrdinalIgnoreCase) ? LpConfig.TurretAxis : Vector3.zero;
                        for (int j = 0; j < vertices.Length; j++)
                        {
                            Vector3 v = vertices[j];
                            vertices[j] = new Vector3(v.z + offset.z, v.y, v.x - offset.z);
                        }
                        for (int j = 0; j < triangles.Length; j += 3) (triangles[j + 1], triangles[j + 2]) = (triangles[j + 2], triangles[j + 1]);
                        Mesh mesh = new Mesh { name = name, indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                        mesh.vertices = vertices;
                        mesh.triangles = triangles;
                        mesh.RecalculateBounds();
                        Meshes[name] = mesh;
                    }
                }
            }
            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjGeometry part in ObjModel.Geometries)
            {
                if (!part.ObjectName.StartsWith("ex_armor_", StringComparison.OrdinalIgnoreCase) || !unique.Add(part.ObjectName)) continue;
                List<ObjGeometry> pieces = ObjModel.Geometries.Where(p => p.ObjectName == part.ObjectName).ToList();
                List<Vector3> vertices = new List<Vector3>();
                List<int> triangles = new List<int>();
                foreach (ObjGeometry piece in pieces)
                {
                    int start = vertices.Count;
                    vertices.AddRange(piece.Positions);
                    triangles.AddRange(piece.Triangles.Select(t => t + start));
                }
                Mesh mesh = new Mesh { name = part.ObjectName, indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                Meshes[part.ObjectName] = mesh;
            }
        }
        catch (Exception ex) { _assetError = ex.Message; }
    }

    private static ArmorCodexScriptable Codex(GameObject root, string fragment)
    {
        if (Codices.TryGetValue(fragment, out ArmorCodexScriptable cached) && cached != null) return cached;
        ArmorCodexScriptable result = root.GetComponentsInChildren<UniformArmor>(true)
            .Select(a => a != null ? a.ArmorType : null)
            .FirstOrDefault(c => c != null && ((c.ArmorType != null && c.ArmorType.Name != null && c.ArmorType.Name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) || c.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0));
        if (result == null && (fragment == "armour steel" || fragment == "liner"))
        {
            ArmorType type = new ArmorType { Name = fragment, BHN = fragment == "liner" ? 2f : 313f, NormalizesHits = fragment != "liner", CanRicochet = fragment != "liner", CanShatterLongRods = fragment == "armour steel", SpallAngleMultiplier = fragment == "liner" ? 0.33f : 1f, SpallPowerMultiplier = fragment == "liner" ? 0.9f : 1f, CrushThicknessModifier = fragment == "liner" ? 0.05f : 1f };
            result = ScriptableObject.CreateInstance<ArmorCodexScriptable>();
            result.name = "L2A7V " + fragment;
            result.ArmorType = type;
            UnityEngine.Object.DontDestroyOnLoad(result);
        }
        Codices[fragment] = result;
        return result;
    }
}

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

// Marks a donor plate whose protection values were rewritten from the table.
// The coverage audit uses it to prove no donor piece keeps a vanilla number.
public sealed class LpArmourEdited : MonoBehaviour
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
                // retire/keep carry no geometry: they only claim donor names.
                if (entry.Retire || entry.Keep) continue;
                if (existing.Contains(entry.Piece)) continue;
                // donor_edit: keep the donor geometry/collider, rewrite its
                // protection values from the table. Idempotent on retry.
                if (entry.Edit)
                {
                    edits += EditDonor(unitRoot, entry);
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
                Transform pivot = ArtPivot(unitRoot, entry.Group);
                Transform group = GroupRoot(groups, entry.Group);
                if (pivot == null || group == null)
                {
                    state.Pending = "waiting for the " + entry.Group + " art pivot and native collider group";
                    return false;
                }
                if (Spawn(unitRoot, pivot, group, entry, mesh, unit)) pieces++;
            }
            // Retire whenever this vehicle owns at least one piece: a retry
            // after a mid-loop exception finds all pieces existing and must
            // still complete donor retirement instead of failing forever.
            if (pieces == 0 && edits == 0 && existing.Count == 0)
            {
                state.Pending = "no valid replacement geometry";
                return false;
            }
            HashSet<string> retiredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Retire rows claim donor plates unconditionally, so a replacement
            // that failed to resolve would leave a hole. Never strip the donor
            // unless every tabled piece actually spawned.
            int retired = 0;
            if (missing == 0) retired = RetireDonors(existing, groups, spec, retiredNames);
            else MelonLogger.Error("[L2A7V][dm-armour] " + missing + " replacement mesh(es) missing; donor protection left in place rather than opening a hole");
            state.Built = true;
            state.Pieces = pieces + existing.Count;
            state.Retired = retired;
            state.Pending = null;
            MelonLogger.Msg("[L2A7V][dm-armour] built " + pieces + " data-defined piece(s), edited " + edits + " donor value(s), adopted " + existing.Count + " existing piece(s), retired " + retired + " donor plate(s), " + missing + " missing geometry");
            Audit(unitRoot, rig, spec, retired, retiredNames);
            return true;
        }
        catch (Exception ex)
        {
            state.Pending = ex.Message;
            MelonLogger.Warning("[L2A7V][dm-armour] build will retry: " + ex);
            return false;
        }
    }

    private static bool Spawn(GameObject root, Transform pivot, Transform group, LpArmourSpec.Entry entry, Mesh mesh, Unit unit)
    {
        GameObject go = new GameObject(entry.Piece);
        go.SetActive(false);
        go.AddComponent<LpArmourPiece>().Piece = entry.Piece;
        go.layer = CodeUtils.LAYER_INDEX_RAYCAST_ONLY;
        try { go.tag = "Penetrable"; } catch (UnityException) { }
        go.transform.SetParent(group, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        // Bake rig -> collider group through the very pivot the visible art
        // rides, with the same anchor LpAssets.BuildWhere subtracts. Deriving
        // this from the drive bone instead subtracted the anchor twice.
        Matrix4x4 rigToGroup = group.worldToLocalMatrix * pivot.localToWorldMatrix
            * Matrix4x4.Translate(-ArtAnchor(entry.Group));
        Mesh baked = Bake(entry.Piece, mesh, rigToGroup);
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
    private static int EditDonor(GameObject unitRoot, LpArmourSpec.Entry entry)
    {
        int edited = 0;
        foreach (UniformArmor armour in unitRoot.GetComponentsInChildren<UniformArmor>(true))
        {
            if (armour == null) continue;
            if (armour.GetComponent<LpArmourPiece>() != null) continue;
            if (!string.Equals(armour.gameObject.name, entry.Replace, StringComparison.OrdinalIgnoreCase)) continue;
            LpArmourEdited prior = armour.GetComponent<LpArmourEdited>();
            if (prior != null && prior.Piece == entry.Piece) continue;
            armour.PrimarySabotRha = entry.Ke * LpConfig.ArmorScale;
            armour.PrimaryHeatRha = entry.Ce * LpConfig.ArmorScale;
            armour.SecondarySabotRha = armour.PrimarySabotRha;
            armour.SecondaryHeatRha = armour.PrimaryHeatRha;
            armour.AngleMatters = entry.AngleMatters;
            LpArmourEdited marker = armour.GetComponent<LpArmourEdited>() ?? armour.gameObject.AddComponent<LpArmourEdited>();
            marker.Piece = entry.Piece;
            edited++;
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

    private static int RetireDonors(HashSet<string> existing, List<RaycastColliderGroup> groups, LpArmourSpec spec, HashSet<string> retiredNames)
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
            if (entry.Drawn || entry.Keep || entry.Edit || entry.Replace == "-" || entry.Replace.Length == 0 || !completed.Add(entry.Replace)) continue;
            // A retire row claims a donor plate an imported mesh already covers,
            // so it has no piece of its own to wait for. Every other mode only
            // retires once its replacement actually exists.
            if (!entry.Retire && !existing.Contains(entry.Piece)) continue;
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
                retiredNames.Add(donor.gameObject.name);
                count++;
            }
        }
        return count;
    }

    // One game run has to prove the same thing the offline probe proves: no
    // donor exterior plate is left carrying a vanilla number, and every piece
    // we spawned lands on the rig-space centre the OBJ says it should.
    private static void Audit(GameObject unitRoot, Transform rig, LpArmourSpec spec, int retired, HashSet<string> retiredNames)
    {
        List<string> keep = spec.Entries.Where(e => e.Keep).Select(e => e.Replace).ToList();
        Matrix4x4 worldToRig = rig.worldToLocalMatrix;
        int ours = 0, edited = 0, kept = 0, interior = 0, crew = 0, gone = 0, interiorHeavy = 0, modules = 0;
        float interiorKe = 0f, interiorCe = 0f;
        List<string> unclaimed = new List<string>();
        StringBuilder detail = new StringBuilder();
        foreach (IArmor armour in unitRoot.GetComponentsInChildren<IArmor>(true))
        {
            if (armour == null || armour.Transform == null) continue;
            Component component = (Component)armour;
            string name = component.gameObject.name;
            // Destroy is deferred to the end of the frame, so a retired plate
            // is still enumerable here; the name set is what tells it apart
            // from a plate nobody ever claimed.
            if (component.GetComponent<LpArmourPiece>() == null && retiredNames.Contains(name)) { gone++; continue; }
            string path = HierarchyPath(component.transform, unitRoot.transform);
            float ke = armour is UniformArmor uniform ? uniform.PrimarySabotRha : armour.SabotRha;
            float ce = armour is UniformArmor heat ? heat.PrimaryHeatRha : armour.HeatRha;
            string bucket;
            if (component.GetComponent<LpArmourPiece>() != null) { bucket = "ours"; ours++; }
            else if (component.GetComponent<LpModuleZone>() != null) { bucket = "module"; modules++; }
            else if (component.GetComponent<LpArmourEdited>() != null) { bucket = "edited"; edited++; }
            else if (keep.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) { bucket = "kept"; kept++; }
            else if (IsCrew(path, name)) { bucket = "crew"; crew++; }
            else if (IsInterior(path)) { bucket = "interior"; interior++; interiorKe += ke; interiorCe += ce; if (ke >= 25f || ce >= 25f) interiorHeavy++; }
            else { bucket = "UNCLAIMED"; unclaimed.Add(path + " KE=" + ke.ToString("0.#", CultureInfo.InvariantCulture) + " CE=" + ce.ToString("0.#", CultureInfo.InvariantCulture)); }

            if (bucket == "ours")
            {
                Collider collider = component.GetComponent<Collider>();
                MeshFilter filter = component.GetComponent<MeshFilter>();
                string boxText = "no bounds";
                if (collider != null && filter != null && filter.sharedMesh != null)
                {
                    Bounds local = filter.sharedMesh.bounds;
                    Matrix4x4 toRig = worldToRig * component.transform.localToWorldMatrix;
                    Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                    Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = toRig.MultiplyPoint3x4(new Vector3(
                            (i & 1) == 0 ? local.min.x : local.max.x,
                            (i & 2) == 0 ? local.min.y : local.max.y,
                            (i & 4) == 0 ? local.min.z : local.max.z));
                        min = Vector3.Min(min, corner);
                        max = Vector3.Max(max, corner);
                    }
                    Vector3 centre = (min + max) * 0.5f;
                    Vector3 size = max - min;
                    boxText = string.Format(CultureInfo.InvariantCulture, "rig c({0:+0.00;-0.00},{1:+0.00;-0.00},{2:+0.00;-0.00}) s({3:0.00},{4:0.00},{5:0.00})",
                        centre.x, centre.y, centre.z, size.x, size.y, size.z);
                }
                detail.AppendLine("[L2A7V][dm-audit] ours " + name + " " + boxText
                    + " KE=" + ke.ToString("0.#", CultureInfo.InvariantCulture)
                    + " CE=" + ce.ToString("0.#", CultureInfo.InvariantCulture)
                    + " collider=" + (collider != null && collider.enabled) + " active=" + component.gameObject.activeInHierarchy);
            }
            else if (bucket == "interior" && (ke >= 25f || ce >= 25f))
            {
                detail.AppendLine("[L2A7V][dm-audit] interior " + name
                    + " KE=" + ke.ToString("0.#", CultureInfo.InvariantCulture)
                    + " CE=" + ce.ToString("0.#", CultureInfo.InvariantCulture));
            }
        }
        MelonLogger.Msg("[L2A7V][dm-audit] census on '" + unitRoot.name + "': ours=" + ours + " module=" + modules + " edited=" + edited
            + " kept=" + kept + " interior=" + interior + " crew=" + crew + " retired=" + gone + "/" + retired
            + " UNCLAIMED=" + unclaimed.Count);
        MelonLogger.Msg("[L2A7V][dm-audit] donor interior armour still alive: " + interior
            + " piece(s), " + interiorHeavy + " of them >=25 mm, summed KE=" + interiorKe.ToString("0.#", CultureInfo.InvariantCulture)
            + " CE=" + interiorCe.ToString("0.#", CultureInfo.InvariantCulture) + " mm (expected 0 after the WT interior swap; module zones carry the resistance instead)");
        foreach (string line in unclaimed) MelonLogger.Error("[L2A7V][dm-audit] UNCLAIMED donor armour " + line);
        if (unclaimed.Count == 0) MelonLogger.Msg("[L2A7V][dm-audit] no donor exterior plate keeps a vanilla value");
        MelonLogger.Msg(detail.ToString());
    }

    private static string HierarchyPath(Transform piece, Transform root)
    {
        StringBuilder builder = new StringBuilder(piece.name);
        for (Transform t = piece.parent; t != null && t != root; t = t.parent) builder.Insert(0, t.name + "/");
        return builder.ToString();
    }

    private static bool IsCrew(string path, string name) =>
        name.StartsWith("hitzone", StringComparison.OrdinalIgnoreCase) || path.IndexOf("swat:", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInterior(string path) =>
        path.IndexOf("LEO2_aar_", StringComparison.OrdinalIgnoreCase) >= 0
        || path.IndexOf("TURRET FLOOR COLLIDERS", StringComparison.OrdinalIgnoreCase) >= 0
        || path.IndexOf("LEO2_int", StringComparison.OrdinalIgnoreCase) >= 0;

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
        return group.transform.Find("LEO2_armour_" + suffix)
            ?? group.transform.Find("LEO2_aar_" + suffix)
            ?? group.transform.Find("ARMOR")
            ?? group.transform.Find("AAR")
            ?? group.transform;
    }

    internal static Transform Rig(GameObject root) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "leo2_rig");

    internal static Transform Bone(Transform rig, string group)
    {
        if (group == "Hull") return rig.Find("hull") ?? rig;
        if (group == "Turret") return rig.Find("hull/turret");
        return rig.Find("hull/turret/mantlet");
    }

    // The armour must land exactly where the visible art lands, so it rides
    // the same pivot LpVisual.MakePivot built rather than the drive bone.
    private static Transform ArtPivot(GameObject root, string group)
    {
        string name = group == "Hull" ? "L2A7V_HullPivot"
            : group == "Gun" ? "L2A7V_GunPivot"
            : "L2A7V_TurretPivot";
        return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t.name == name);
    }

    private static Vector3 ArtAnchor(string group)
    {
        if (group == "Turret") return LpConfig.TurretAxis;
        if (group == "Gun") return LpConfig.GunAxis;
        return Vector3.zero;
    }

    // Same frame the armour Spawn bakes through: rig-space vertices multiplied
    // by this matrix land on the visible art, inside the native collider group.
    internal static bool TryVisualFrame(GameObject unitRoot, string kind, out Transform parent, out Matrix4x4 rigToParent)
    {
        parent = null;
        rigToParent = Matrix4x4.identity;
        Transform rig = Rig(unitRoot);
        Transform pivot = ArtPivot(unitRoot, kind);
        Transform group = rig == null ? null : GroupRoot(Groups(rig), kind);
        if (pivot == null || group == null) return false;
        parent = group;
        rigToParent = group.worldToLocalMatrix * pivot.localToWorldMatrix * Matrix4x4.Translate(-ArtAnchor(kind));
        return true;
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
                        // The bin stores export-frame vertices: turret plates
                        // already had TurretAxis subtracted, everything else is
                        // plain (rig.z, rig.y, rig.x). Undo both in one step so
                        // every mesh ends up in true rig space.
                        Vector3 pivot = name.StartsWith("composite_armor_turret_", StringComparison.OrdinalIgnoreCase) ? LpConfig.TurretAxis : Vector3.zero;
                        for (int j = 0; j < vertices.Length; j++)
                        {
                            Vector3 v = vertices[j];
                            vertices[j] = new Vector3(v.z + pivot.z, v.y + pivot.y, v.x);
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

    internal static ArmorCodexScriptable Codex(GameObject root, string fragment)
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

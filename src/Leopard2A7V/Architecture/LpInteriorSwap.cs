using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.PhysicsHelpers;
using GHPC.Utility;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V;

public sealed class LpInteriorSwapped : MonoBehaviour
{
}

// Full interior swap (user decision 2026-11-07): the donor LEO2A3 compartment
// loses its renderers, colliders and armour components; War Thunder
// Interior.obj takes over the visuals. Module damage zones (with their own
// colliders and armour) are built by LpModuleBuilder; this pass renders every
// remaining part the module and armour tables do not use - rounds, crew
// mannequins, trunnion - and hides the game's soldier figures. Donor crew
// hitzones, ammo racks and DestructibleComponents stay: they are logic, not
// interior appearance.
public static class LpInteriorSwap
{
    private const string Tag = "[L2A7V][dm-interior]";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo GroupColliders = typeof(RaycastColliderGroup).GetField("childColliders", Any);

    public static bool Apply(GameObject unitRoot)
    {
        if (unitRoot == null) return true;
        if (unitRoot.GetComponent<LpInteriorSwapped>() != null) return true;
        // Without the WT geometry a strip would leave an empty compartment;
        // keep retrying until the bounded init gives up, donor untouched.
        if (!LpXrayParts.Loaded) return false;
        try
        {
            Transform rig = LpArmourBuilder.Rig(unitRoot);
            if (rig == null) return false;
            List<RaycastColliderGroup> groups = LpArmourBuilder.Groups(rig);
            if (groups.Count == 0) return false;

            List<Transform> containers = InteriorContainers(unitRoot);
            if (containers.Count == 0)
            {
                MelonLogger.Warning(Tag + " no donor interior container (LEO2_aar_*/LEO2_int/M1IP_interior/TURRET FLOOR) found; donor interior left untouched");
                return false;
            }

            int armours = 0, colliders = 0, renderers = 0;
            HashSet<Component> stripped = new HashSet<Component>();
            foreach (Transform container in containers)
            {
                foreach (IArmor armour in container.GetComponentsInChildren<IArmor>(true))
                {
                    if (armour == null || armour.Transform == null) continue;
                    Component component = (Component)armour;
                    if (component.GetComponent<LpArmourPiece>() != null || component.GetComponent<LpModuleZone>() != null) continue;
                    if (IsCrew(component.transform)) continue;
                    colliders += StripColliders(component.gameObject, groups);
                    stripped.Add(component);
                    UnityEngine.Object.Destroy(component);
                    armours++;
                }
                foreach (Renderer renderer in container.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null || LpVisual.IsImportedArt(renderer.transform)) continue;
                    if (IsCrew(renderer.transform)) continue;
                    UnityEngine.Object.Destroy(renderer);
                    renderers++;
                }
                foreach (AarVisual aar in container.GetComponentsInChildren<AarVisual>(true))
                {
                    if (aar == null || LpVisual.IsImportedArt(aar.transform)) continue;
                    if (IsCrew(aar.transform)) continue;
                    UnityEngine.Object.Destroy(aar);
                }
            }

            int crewHidden = HideDonorCrew(unitRoot);
            int visuals = RenderWarThunderInterior(unitRoot);

            // Destroy is deferred to frame end, so count survivors through the
            // stripped set rather than re-enumerating components.
            int remaining = containers.SelectMany(c => c.GetComponentsInChildren<IArmor>(true))
                .Count(a => a != null && !stripped.Contains((Component)a)
                         && ((Component)a).GetComponent<LpArmourPiece>() == null
                         && ((Component)a).GetComponent<LpModuleZone>() == null
                         && !IsCrew(((Component)a).transform));
            MelonLogger.Msg(Tag + " donor interior stripped: " + armours + " armour, " + colliders + " collider(s), "
                + renderers + " renderer(s); remaining donor interior armour=" + remaining
                + "; WT visuals=" + visuals + ", donor crew figures hidden=" + crewHidden);
            if (remaining > 0) MelonLogger.Error(Tag + " " + remaining + " donor interior armour piece(s) survived the strip");
            unitRoot.AddComponent<LpInteriorSwapped>();
            return true;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning(Tag + " swap will retry: " + ex.Message);
            return false;
        }
    }

    private static List<Transform> InteriorContainers(GameObject unitRoot)
    {
        List<Transform> found = new List<Transform>();
        foreach (Transform t in unitRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t == null) continue;
            if (t.name.StartsWith("LEO2_aar_", StringComparison.OrdinalIgnoreCase) ||
                t.name.StartsWith("LEO2_int", StringComparison.OrdinalIgnoreCase) ||
                t.name.StartsWith("M1IP_interior", StringComparison.OrdinalIgnoreCase) ||
                t.name.IndexOf("TURRET FLOOR COLLIDERS", StringComparison.OrdinalIgnoreCase) >= 0)
                found.Add(t);
        }
        // A container nested inside another would be stripped twice.
        return found.Where(t => !found.Any(o => o != t && t.IsChildOf(o))).ToList();
    }

    private static bool IsCrew(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
        {
            if (p.name.IndexOf("swat:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.name.StartsWith("hitzone", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static int StripColliders(GameObject go, List<RaycastColliderGroup> groups)
    {
        int count = 0;
        foreach (Collider collider in go.GetComponents<Collider>())
        {
            foreach (RaycastColliderGroup group in groups)
            {
                if (GroupColliders?.GetValue(group) is List<Collider> children) children.Remove(collider);
            }
            UnityEngine.Object.Destroy(collider);
            count++;
        }
        return count;
    }

    // The game's soldier figures are renderers named soldier_* plus an
    // AarVisual on a parent; hiding only the renderer leaves the x-ray copy.
    private static int HideDonorCrew(GameObject unitRoot)
    {
        int count = 0;
        HashSet<AarVisual> seen = new HashSet<AarVisual>();
        foreach (Renderer renderer in unitRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || LpVisual.IsImportedArt(renderer.transform)) continue;
            if (!renderer.name.StartsWith("soldier_", StringComparison.OrdinalIgnoreCase)) continue;
            renderer.enabled = false;
            count++;
            for (Transform p = renderer.transform; p != null; p = p.parent)
            {
                AarVisual aar = p.GetComponent<AarVisual>();
                if (aar == null || LpVisual.IsImportedArt(aar.transform) || !seen.Add(aar)) continue;
                aar.Hidden = true;
            }
        }
        return count;
    }

    private static int RenderWarThunderInterior(GameObject unitRoot)
    {
        HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (LpArmourSpec.Entry entry in LpArmourSpec.Load().Entries) used.Add(entry.Piece);
        foreach (LpModuleSpec.Module module in LpModuleSpec.Load().Modules)
            foreach (string part in module.Parts) used.Add(part);

        Material xray = LpArmorOwn.XrayMaterial();
        int count = 0;
        foreach (LpXrayParts.Part part in LpXrayParts.Parts)
        {
            if (part == null || used.Contains(part.Name) || part.Triangles.Length < 3) continue;
            string kind = part.Name.IndexOf("trunnion", StringComparison.OrdinalIgnoreCase) >= 0 ? "Turret" : part.Bone;
            if (kind != "Hull" && kind != "Turret" && kind != "Gun") kind = "Hull";
            if (!LpArmourBuilder.TryVisualFrame(unitRoot, kind, out Transform parent, out Matrix4x4 rigToParent))
            {
                MelonLogger.Warning(Tag + " no " + kind + " frame for '" + part.Name + "'; part not rendered");
                continue;
            }
            GameObject go = new GameObject("L2A7V_Int " + part.Name);
            go.AddComponent<LpArmourPiece>().Piece = part.Name;
            go.layer = CodeUtils.LAYER_INDEX_RAYCAST_ONLY;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            Vector3[] vertices = new Vector3[part.Positions.Length];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = rigToParent.MultiplyPoint3x4(part.Positions[i]);
            Mesh mesh = new Mesh { name = "L2A7V interior " + part.Name, indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = vertices;
            if (part.Normals != null && part.Normals.Length == vertices.Length)
            {
                Vector3[] normals = new Vector3[vertices.Length];
                for (int i = 0; i < normals.Length; i++) normals[i] = rigToParent.MultiplyVector(part.Normals[i]);
                mesh.normals = normals;
            }
            mesh.triangles = part.Triangles;
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            if (xray != null) renderer.sharedMaterial = xray;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            AarVisual aar = go.AddComponent<AarVisual>();
            aar.RenderMode = AarVisual.AarVisualRenderMode.XrayOnly;
            aar.HideUntilAar = true;
            aar.SwitchMaterials = xray != null;
            aar.AarMaterial = xray;
            aar.UpdateVisibility();
            count++;
        }
        return count;
    }
}
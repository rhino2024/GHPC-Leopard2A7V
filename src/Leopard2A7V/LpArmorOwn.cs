using System;
using System.Collections.Generic;
using GHPC;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Rebuilds the vehicle's protection as our own armour pieces, the way the T-90A mod does it.
    ///
    /// The donor's armour cannot be used directly: the 220 uniform and 33 raycast pieces live on the AAR
    /// root, which the game tears down within half a second of the spawn, and their names are the LEO2A3's
    /// rather than the 2A7V's. Rewriting their values therefore writes into objects that are about to be
    /// deleted. This class takes a snapshot of their geometry while they still exist - the one window that
    /// has them - and builds a permanent set of armour pieces from it: one box per piece, carrying the RHAe
    /// the table specifies, parented to the donor rig bones so it turns with the vehicle.
    /// </summary>
    public static class LpArmorOwn
    {
        private const string Tag = "[L2A7V][armour]";

        private const string Marker = "L2A7V_OwnArmour";

        private static readonly HashSet<string> _Built = new HashSet<string>(StringComparer.Ordinal);

        private sealed class Stats
        {
            internal int Seen;
            internal int Resolved;
            internal int NoGeometry;
            internal int Built;
            internal int BuildFailed;
        }

        public static bool Build(GameObject unitRoot)
        {
            if (unitRoot == null) return true;

            // Our own pivots are built by the visual pass two seconds later, so at this point the donor rig
            // bones are the only transforms that exist: they are what the artwork will be parented under
            // anyway, and they carry the turret and gun rotations.
            Transform hull = FindPivot(unitRoot, "L2A7V_HullPivot") ?? FindBone(unitRoot, "hull");
            Transform turret = FindPivot(unitRoot, "L2A7V_TurretPivot") ?? FindBone(unitRoot, "turret");
            Transform gun = FindPivot(unitRoot, "L2A7V_GunPivot") ?? FindBone(unitRoot, "mantlet", "gun");
            if (hull == null)
            {
                MelonLogger.Warning(Tag + " neither our hull pivot nor the donor's hull bone was found on " +
                                    unitRoot.name + "; the protection stays donor");
                return true;
            }

            var stats = new Stats();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (ArmorPiece piece in Snapshot(unitRoot, used, stats))
            {
                Transform bone = piece.IsGun ? gun : piece.IsTurret ? turret : hull;
                if (bone == null) bone = hull;

                if (!BuildPiece(bone, piece))
                {
                    stats.BuildFailed++;
                    continue;
                }

                // The donor's piece must stop answering hits, or two colliders sit in the same space and the
                // shot is resolved against whichever one the physics query returns first.
                Disable(piece);
                stats.Built++;
            }

            MelonLogger.Msg(Tag + " snapshot: " + stats.Seen + " donor piece(s), " + stats.Resolved +
                            " matched a zone, " + stats.NoGeometry + " had no geometry, " + stats.BuildFailed +
                            " could not be boxed; built " + stats.Built + " of our own on " + unitRoot.name);

            if (stats.Built == 0 && _Built.Contains(unitRoot.name)) return true;
            _Built.Add(unitRoot.name);
            return true;
        }

        /// <summary>One donor piece as it stands at spawn: where it is and what it should become.</summary>
        private sealed class ArmorPiece
        {
            internal Component Source;
            internal Collider Collider;
            internal Bounds Bounds;
            internal LpArmor.Zone Zone;
            internal bool IsTurret;
            internal bool IsGun;
        }

        private static List<ArmorPiece> Snapshot(GameObject unitRoot, HashSet<string> used, Stats stats)
        {
            var pieces = new List<ArmorPiece>();

            foreach (UniformArmor armor in unitRoot.GetComponentsInChildren<UniformArmor>(true))
            {
                if (armor == null) continue;
                stats.Seen++;
                LpArmor.Zone zone;
                if (!LpArmor.TryResolve(armor.transform.name, armor.Name, used, out zone)) continue;
                stats.Resolved++;
                if (!TryAdd(pieces, armor, armor.GetComponent<Collider>(), zone)) stats.NoGeometry++;
            }

            foreach (VariableArmor armor in unitRoot.GetComponentsInChildren<VariableArmor>(true))
            {
                if (armor == null) continue;
                stats.Seen++;
                LpArmor.Zone zone;
                if (!LpArmor.TryResolve(armor.transform.name, armor.Name, used, out zone)) continue;
                stats.Resolved++;
                if (!TryAdd(pieces, armor, armor.GetComponent<Collider>(), zone)) stats.NoGeometry++;
            }

            return pieces;
        }

        private static bool TryAdd(List<ArmorPiece> pieces, Component source, Collider collider, LpArmor.Zone zone)
        {
            Bounds bounds;
            if (!TryBounds(source, collider, out bounds)) return false;
            if (bounds.size.sqrMagnitude < 0.000001f) return false;

            string path = PathOf(source.transform);
            pieces.Add(new ArmorPiece
            {
                Source = source,
                Collider = collider,
                Bounds = bounds,
                Zone = zone,
                IsTurret = path.Contains("turret") || path.Contains("cupola") || path.Contains("peri"),
                IsGun = path.Contains("gun") || path.Contains("mantlet") || path.Contains("barrel"),
            });
            return true;
        }

        /// <summary>
        /// Where a donor piece is. Colliders are rare, renderers not much commoner, and a good number of the
        /// composite arrays are logic-only nodes whose mesh hangs off a child - so all three are tried, and the
        /// mesh is the last resort because a piece with none of them cannot be turned into anything.
        /// </summary>
        private static bool TryBounds(Component source, Collider collider, out Bounds bounds)
        {
            if (collider != null && collider.bounds.size.sqrMagnitude > 0.000001f)
            {
                bounds = collider.bounds;
                return true;
            }

            Renderer renderer = source.GetComponent<Renderer>() ?? source.GetComponentInChildren<Renderer>(true);
            if (renderer != null && renderer.bounds.size.sqrMagnitude > 0.000001f)
            {
                bounds = renderer.bounds;
                return true;
            }

            MeshFilter filter = source.GetComponent<MeshFilter>() ?? source.GetComponentInChildren<MeshFilter>(true);
            if (filter != null && filter.sharedMesh != null)
            {
                Bounds local = filter.sharedMesh.bounds;
                Vector3 centre = filter.transform.TransformPoint(local.center);
                Vector3 size = filter.transform.TransformVector(local.size);
                bounds = new Bounds(centre, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
                if (bounds.size.sqrMagnitude > 0.000001f) return true;
            }

            bounds = new Bounds();
            return false;
        }

        private static bool BuildPiece(Transform bone, ArmorPiece piece)
        {
            Vector3 centre = bone.InverseTransformPoint(piece.Bounds.center);
            Vector3 size = bone.InverseTransformVector(piece.Bounds.size);
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            if (size.x < 0.01f || size.y < 0.01f || size.z < 0.01f) return false;

            var go = new GameObject(Marker + " " + piece.Source.transform.name);
            go.transform.SetParent(bone, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = 8;
            go.tag = "Penetrable";

            var box = go.AddComponent<BoxCollider>();
            box.size = size;

            float scale = LpConfig.ArmorScale;
            var armor = go.AddComponent<UniformArmor>();
            armor.PrimarySabotRha = piece.Zone.Ke * scale;
            armor.PrimaryHeatRha = piece.Zone.Ce * scale;
            armor.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
            armor.AngleMatters = true;
            armor.DetonatedObjects = Array.Empty<GameObject>();
            armor.UndetonatedObjects = Array.Empty<GameObject>();

            var unit = go.GetComponentInParent<Unit>();
            if (unit != null) armor.Unit = unit;
            return true;
        }

        private static void Disable(ArmorPiece piece)
        {
            if (piece.Collider != null) piece.Collider.enabled = false;
            if (piece.Source is Behaviour behaviour) behaviour.enabled = false;
        }

        private static Transform FindPivot(GameObject unitRoot, string name)
        {
            foreach (Transform node in unitRoot.GetComponentsInChildren<Transform>(true))
                if (node != null && node.name == name) return node;
            return null;
        }

        /// <summary>The donor's own rig bone by name, used until our pivots exist.</summary>
        private static Transform FindBone(GameObject unitRoot, params string[] names)
        {
            foreach (Transform node in unitRoot.GetComponentsInChildren<Transform>(true))
            {
                if (node == null) continue;
                string name = node.name.ToLowerInvariant();
                foreach (string wanted in names)
                    if (name == wanted) return node;
            }
            return null;
        }

        private static string PathOf(Transform node)
        {
            string path = node != null ? node.name : string.Empty;
            for (Transform p = node != null ? node.parent : null; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path.ToLowerInvariant();
        }
    }
}

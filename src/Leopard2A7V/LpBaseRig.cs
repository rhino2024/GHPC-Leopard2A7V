using GHPC.Camera;
using GHPC.Equipment.Optics;
using System;
using System.Collections.Generic;
using System.Text;
using GHPC;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Switches the donor's own exterior art off while leaving everything that has to keep working alone:
    /// its animated tracks and road wheels, the sight covers that the gunner's optics animate, the crew
    /// compartment this art has no replacement for, the armour colliders and the electrics' lights.
    ///
    /// The rule is inverted on purpose. An allow-list of donor subtrees to hide was tried first and left 28
    /// renderers drawing in game - the donor's tricolour turret fittings, its blow-off panels, its three
    /// camo nets, its SEM25 launcher clusters and its licence plate all sit on rig bones outside LEO2_mesh,
    /// so they intersected the imported skin and read as panels of the wrong shade. Anything that is donor
    /// art and not on the keep list is exterior art now.
    /// </summary>
    public static class LpBaseRig
    {
        /// <summary>
        /// Path fragments of donor nodes that must keep drawing: the interiors and armour collider art this
        /// export reuses, the animated running gear, and the turret's inner shell, hatch and hole plugs,
        /// which are the crew-compartment view and the seal against seeing through the vehicle.
        ///
        /// "grass cutter" is the track-imprint brush, not art. LEO2A3 carries two of them (one per track) as
        /// 0.46 x 0.1 x 5.01 boxes on layer 24, and layer 24 is the exact cull mask of the TVE trax camera:
        /// JBooth.MicroSplat.TraxManager shoots an orthographic camera up from y-1000 through it and writes the
        /// vehicle's depth into the terrain's trax buffer, which is what leaves the tread marks in grass and
        /// dirt. A mesh renderer survey of the donor shows those two boxes are the only renderers on layer 24,
        /// so keeping them cannot put donor panels back on screen. They were being stripped by this pass, and
        /// a stripped mesh stays stripped no matter how often TraxMeshRender.Start re-enables its renderers.
        /// </summary>
        private static readonly string[] KeepFragments =
        {
            "leo2_int", "m1ip_interior", "leo2_volumes", "collider", "hitzone", "interior",
            "wheel", "suspension",
            "sight open", "sight closed", "turret parent", "ammo door", "loader's hatch", "turret holes",
            "grass cutter",
        };

        /// <summary>
        /// The donor's skinned road wheels. These are the art our own imported gear replaces, so they only stay
        /// visible while UseDonorRunningGear is on; the wheel bones keep turning either way, because the
        /// WheelControllers node drives the rig, not the renderer.
        /// </summary>
        private static readonly string[] WheelArtFragments = { "running gear" };

        /// <summary>
        /// The donor's skinned track loops, which are what this vehicle draws as its tracks: they are the only
        /// art in the stack whose UVs are authored face by face for their own topology, and the two renderers
        /// VehicleController.tracks actually scrolls are LEO2A3/LEO2_tracks/TRACK_L and TRACK_R. The matching
        /// TRACK_L and TRACK_R under LEO2A3/abrams_tracks have no bones at all and stay hidden, since the
        /// fragment below names only the LEO2_tracks subtree. See LpConfig.UseDonorTrackArt.
        /// </summary>
        private static readonly string[] TrackArtFragments = { "leo2_tracks" };

        /// <summary>
        /// Detail parts of the donor hull that a renderer switch cannot hide.
        ///
        /// The LEO2 hull is a skinned mesh: its skirts, fenders and grilles are not separate renderers but
        /// pieces of one skin driven by bones. Collapsing those bones is the only way to pull them inside the
        /// new hull - the same trick the T-90A mod uses on the T-72M1 (it also trims the mesh, which needs a
        /// rebuilt asset; collapsing is enough to stop the old shape showing).
        /// </summary>
        internal static int CollapseSkinDetailBones(Transform unitRoot)
        {
            int collapsed = 0;
            foreach (SkinnedMeshRenderer skin in unitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin == null || LpVisual.IsImportedArt(skin.transform)) continue;
                Transform[] bones = skin.bones;
                if (bones == null) continue;
                foreach (Transform bone in bones)
                {
                    if (bone == null || !IsHullDetailBone(bone.name)) continue;
                    if (bone.localScale == Vector3.zero) continue;
                    bone.localScale = Vector3.zero;
                    collapsed++;
                }
            }
            return collapsed;
        }

        /// <summary>Bones that carry parts hanging off the donor hull rather than the hull itself.</summary>
        internal static bool IsHullDetailBone(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.StartsWith("skirt_") || n.StartsWith("fender_") || n.StartsWith("flap") ||
                   n.StartsWith("gill_") || n.Contains("mudflap") || n.Contains("side_skirt");
        }

        public static void Apply(Transform unitRoot)
        {
            CollectProtected(unitRoot);
            int stripped = 0;
            int netting = 0;
            foreach (Renderer renderer in unitRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (LpVisual.IsImportedArt(renderer.transform)) continue;
                // A particle system is not a surface: the donor's own muzzle flash and bore evacuator smoke
                // hang under the gun bone, and switching their renderers off would take the main gun's smoke
                // away rather than clean up the paint.
                if (renderer is ParticleSystemRenderer) continue;
                if (!ShouldHide(renderer.transform, renderer.name)) continue;
                StripGeometry(renderer);
                renderer.enabled = false;
                // No scale collapse here. The geometry is already gone, which is what actually keeps the piece
                // off screen; collapsing the transform was the older trick and it is not safe - the node it
                // lands on can be a bone the running gear rides or the ancestor of the vehicle's colliders,
                // and a zero-scaled one takes the wheels and the hit detection with it.
                stripped++;
                string path = PathOf(renderer.transform).ToLowerInvariant();
                if (path.Contains("net") || path.Contains("camo") || path.Contains("marking")) netting++;
            }
            MelonLogger.Msg($"[L2A7V] stripped the geometry of {stripped} donor renderer(s), " +
                            $"{netting} of them netting or markings");

            int bones = CollapseSkinDetailBones(unitRoot);
            if (bones > 0)
                MelonLogger.Msg($"[L2A7V] collapsed {bones} skinned donor bone(s) (skirts, fenders, grilles) " +
                                "so the old hull cannot poke through");

            int imprints = 0;
            foreach (Renderer renderer in unitRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.gameObject.layer != ImprintLayer) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) imprints++;
            }
            MelonLogger.Msg($"[L2A7V] {imprints} track-imprint brush(es) kept on layer {ImprintLayer}, " +
                            "the only layer the TVE trax camera renders");
        }

        /// <summary>Measured on both donor prefabs: the trax cutters are the only renderers on layer 24.</summary>
        public const int ImprintLayer = 24;

        /// <summary>
        /// Takes the mesh away instead of only switching the renderer off. GHPC re-enables donor renderers from
        /// its own passes (the TVE grass cutter's TraxMeshRender.Start switches its children back on every
        /// load), so a disabled flag is a state the game overrules and the only reason an earlier build of
        /// this mod still showed 28 donor panels; a renderer with no geometry cannot be made to draw by
        /// anything that merely toggles a bool. The component and its GameObject stay in place, which is what
        /// keeps the donor's serialized renderer lists valid.
        ///
        /// A SkinnedMeshRenderer is the exception and must not be nulled: the donor's Animator keeps updating
        /// its bones, and Unity recalculates the bounds and runs the skinning job against sharedMesh - a null
        /// there faults inside native code, which is what crashed the mission load. It gets a copy of its own
        /// mesh with every index buffer emptied instead.
        /// </summary>
        internal static void StripGeometry(Renderer renderer)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) filter.sharedMesh = null;

            SkinnedMeshRenderer skin = renderer as SkinnedMeshRenderer;
            if (skin != null && skin.sharedMesh != null) skin.sharedMesh = EmptiedCopy(skin.sharedMesh);
        }

        /// <summary>Meshes emptied of triangles, per donor mesh, so a unit never clones the same one twice.</summary>
        private static readonly Dictionary<Mesh, Mesh> EmptiedMeshes = new Dictionary<Mesh, Mesh>();

        /// <summary>
        /// A stand-in mesh with the donor's shape of nothing: same submesh count so the renderer's material
        /// slots stay valid, same bounds so culling and behaviour that reads bounds keep working, no triangles.
        ///
        /// It is built empty rather than Instantiate()d from the donor's mesh, and that is the important part:
        /// a copy of an unreadable mesh is itself unreadable, and the game then throws "Not allowed to access
        /// triangles/indices on mesh '... (no faces)' (isReadable is false)" every time one of those passes
        /// touches the running gear or the turret skin - 25 of those per spawn in the measured log, which is
        /// exactly the kind of exception that leaves the after-action view half-drawn. A mesh created here is
        /// readable by construction.
        /// </summary>
        private static Mesh EmptiedCopy(Mesh source)
        {
            Mesh emptied;
            if (EmptiedMeshes.TryGetValue(source, out emptied) && emptied != null) return emptied;

            var copy = new Mesh { name = source.name + " (no faces)" };
            try
            {
                copy.subMeshCount = source.subMeshCount;
                var none = new int[0];
                for (int i = 0; i < copy.subMeshCount; i++) copy.SetTriangles(none, i);
                copy.bounds = source.bounds;
            }
            catch (Exception)
            {
                // A submesh table Unity refuses is not worth a crash: an empty one-submesh mesh still draws
                // nothing, which is the whole job.
                copy.subMeshCount = 1;
                copy.SetTriangles(new int[0], 0);
            }
            EmptiedMeshes[source] = copy;
            return copy;
        }

        public static bool ShouldHide(Transform node, string rendererName)
        {
            if (IsProtected(node)) return false;
            string path = PathOf(node) + "/" + rendererName;
            foreach (string fragment in KeepFragments)
                if (path.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return false;

            foreach (string fragment in WheelArtFragments)
                if (path.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return !LpConfig.UseDonorRunningGear;

            foreach (string fragment in TrackArtFragments)
                if (path.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return !(LpConfig.UseDonorRunningGear || LpConfig.UseDonorTrackArt ||
                             LpTracks.DonorFallback);
            return true;
        }

        /// <summary>
        /// Objects the camera renders through: division plates, lens glass, reticle overlays and the
        /// periscope linings the slot system switches on and off itself. Hiding one of these does not
        /// remove donor art the player can see - it blanks a sight view, which is far worse, and no name
        /// fragment identifies them reliably because the game references them by field, not by label.
        /// </summary>
        private static readonly HashSet<Transform> ProtectedTransforms = new HashSet<Transform>();

    private static readonly HashSet<string> _RigWarned = new HashSet<string>(System.StringComparer.Ordinal);

        private static void CollectProtected(Transform unitRoot)
        {
            ProtectedTransforms.Clear();
            foreach (CameraSlot slot in unitRoot.GetComponentsInChildren<CameraSlot>(true))
            {
                if (slot.ZeroScaleItems != null)
                    foreach (Transform item in slot.ZeroScaleItems)
                        AddProtected(item, unitRoot);
                if (slot.ExclusiveItems != null)
                    foreach (GameObject item in slot.ExclusiveItems)
                        AddIfInside(item != null ? item.transform : null, unitRoot);
                if (slot.LinkedNightSight != null) AddProtected(slot.LinkedNightSight.transform, unitRoot);
            }
            foreach (UsableOptic optic in unitRoot.GetComponentsInChildren<UsableOptic>(true))
            {
                AddProtected(optic.Transform, unitRoot);
                AddProtected(optic.LevelTransform, unitRoot);
                AddProtected(optic.ReadyToFireObject != null ? optic.ReadyToFireObject.transform : null, unitRoot);
                AddProtected(optic.UnreadyToFireObject != null ? optic.UnreadyToFireObject.transform : null, unitRoot);
                AddProtected(optic.FaultObject != null ? optic.FaultObject.transform : null, unitRoot);
                AddProtected(optic.OverridingObject != null ? optic.OverridingObject.transform : null, unitRoot);
            }
            foreach (Reticle.ReticleMesh reticle in unitRoot.GetComponentsInChildren<Reticle.ReticleMesh>(true))
                AddProtected(reticle.transform, unitRoot);
        }

        /// <summary>
        /// How many donor renderers a sight reference may cover and still be a piece of the sight picture.
        /// Measured on the donor Leopard with ModSources/tools/lp_protect_scope.txt: the eyepiece, the FLIR and
        /// the reticle holders each hold 0-3, while UsableOptic.LevelTransform names the turret bone
        /// leo2_rig/hull/turret and so covered 78 - the camo nets, the national markings and the SEM25
        /// launcher clusters, all of which the prefab re-parents under that bone at Awake. A reference that
        /// wide is a logic pointer, not art, and honouring it left the donor's netting draped over the
        /// imported turret.
        /// </summary>
        private const int MaxProtectedRenderers = 8;

        private static void AddProtected(Transform node, Transform unitRoot)
        {
            if (!IsInside(node, unitRoot)) return;

            Renderer[] art = node.GetComponentsInChildren<Renderer>(true);
            int donorCount = 0;
            for (int i = 0; i < art.Length; i++)
                if (!LpVisual.IsImportedArt(art[i].transform)) donorCount++;

            if (donorCount > MaxProtectedRenderers)
            {
                if (!_RigWarned.Add(PathOf(node))) return;
                MelonLogger.Warning($"[L2A7V] a sight reference names {PathOf(node)}, which covers " +
                                    $"{donorCount} donor renderer(s) - that is a rig bone, not sight art, " +
                                    "so its subtree still gets stripped");
                return;
            }
            ProtectedTransforms.Add(node);
        }

        private static bool IsInside(Transform node, Transform unitRoot)
        {
            if (node == null) return false;
            Transform current = node;
            int guard = 0;
            while (current != null && guard++ < 32)
            {
                if (ReferenceEquals(current, unitRoot)) return true;
                current = current.parent;
            }
            return false;
        }

        private static void AddIfInside(Transform node, Transform unitRoot)
        {
            if (IsInside(node, unitRoot)) ProtectedTransforms.Add(node);
        }

        private static bool IsProtected(Transform node)
        {
            if (ProtectedTransforms.Count == 0) return false;
            Transform current = node;
            int guard = 0;
            while (current != null && guard++ < 32)
            {
                if (ProtectedTransforms.Contains(current)) return true;
                current = current.parent;
            }
            return false;
        }

        /// <summary>
        /// The donor's own renderers, taken before the conversion adds ours. Everything the mod builds later
        /// (the reticle plate, the commander sight head) hangs under the same rig bones, so the only reliable
        /// way to tell "donor art" from "our art" is to look at what existed first.
        /// </summary>
        internal static Renderer[] DonorSnapshot;

        internal static void SnapshotDonor(Transform unitRoot)
        {
            if (unitRoot == null) return;
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer renderer in unitRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (renderer is ParticleSystemRenderer) continue;
                // Only the ones that are meant to go: the running gear and wheels are the donor's too, but the
                // vehicle drives on them.
                if (!ShouldHide(renderer.transform, renderer.name)) continue;
                if (renderer.gameObject.layer == ImprintLayer) continue;
                // Never touch anything inside an optic: the reticle plate lives under the sight housing, and
                // collapsing that housing to zero scale takes the plate with it - which is why the gunnery
                // sight had no aiming mark even though the log said the plate had been replaced.
                if (renderer.GetComponentInParent<UsableOptic>() != null) continue;
                // A hull that carries armour or the sight under it must not be zero-scaled: the colliders
                // inherit the scale and shrink to nothing, which is exactly how the vehicle lost its hit
                // detection and rounds started passing straight through.
                if (renderer.GetComponentInChildren<UniformArmor>(true) != null) continue;
                if (renderer.GetComponentInChildren(typeof(Reticle.ReticleMesh), true) != null) continue;
                list.Add(renderer);
            }
            DonorSnapshot = list.ToArray();
            MelonLogger.Msg("[L2A7V] donor snapshot: " + DonorSnapshot.Length + " renderer(s) before conversion");
        }

        internal static string PathOf(Transform node)
        {
            var builder = new StringBuilder(node.name);
            Transform parent = node.parent;
            int guard = 0;
            while (parent != null && guard++ < 24)
            {
                builder.Insert(0, parent.name + "/");
                parent = parent.parent;
            }
            return builder.ToString();
        }
    }
}

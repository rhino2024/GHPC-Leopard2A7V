using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Puts the imported wheels, idler and drive sprockets on the donor's own animated bones.
    ///
    /// The donor binds its road wheels, idlers, sprockets and track segments to 33 bones under leo2_rig, and
    /// the game turns those bones through the WheelControllers node - so the motion already exists and the
    /// only thing missing was art riding it. Building one mesh per Wavefront object name (the export splits
    /// the running gear that way: wheel_l_01 to _07, plus _drive and _front per side) lets each of our parts
    /// be parented to a bone of its own, which is what a skin would do with far less machinery.
    ///
    /// Wheel numbering direction is not stated anywhere in the export, so a part takes the *nearest* free
    /// bone of its side and role instead of trusting the index. That makes the match self-correcting, and the
    /// distance it reports is the alignment residual between the two models' suspension layouts.
    ///
    /// Tracks and suspension arms stay still on the hull: the donor's own skin binds its arms to the hull
    /// bone, not to the wheels, so an arm parented to a wheel bone would spin with the wheel.
    /// </summary>
    public static class LpRunningGear
    {
        private const string Tag = "[L2A7V][gear]";

        /// <summary>How far a part may be from the bone it takes; beyond this the match is only a guess.</summary>
        private const float MaxBindDistance = 0.6f;

        private sealed class Part
        {
            public string Name;
            public Bounds Box;
            public bool HasBox;

            public void Include(Bounds other)
            {
                if (!HasBox)
                {
                    Box = other;
                    HasBox = true;
                    return;
                }
                Vector3 min = Vector3.Min(Box.min, other.min);
                Vector3 max = Vector3.Max(Box.max, other.max);
                Box.center = (min + max) * 0.5f;
                Box.size = max - min;
            }
        }

        public static void Attach(Transform hullPivot, Transform unitRoot)
        {
            var stillNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Part> parts = CollectParts();

            List<Transform> bones = FindBones(unitRoot);
            if (parts == null || parts.Count == 0)
            {
                MelonLogger.Warning(Tag + " the model has no running gear objects");
                return;
            }
            if (bones == null || bones.Count == 0)
            {
                MelonLogger.Error(Tag + " the donor's wheel bones are gone, so the imported gear stays still");
                BuildStatic(hullPivot, null);
                AttachTracks(hullPivot, unitRoot, CountTrackLoops());
                return;
            }

            var pools = new Pools
            {
                RoadLeft = Take(bones, "wheel_l_", "top"),
                RoadRight = Take(bones, "wheel_r_", "top"),
                IdlerLeft = Take(bones, "idler_l", null),
                IdlerRight = Take(bones, "idler_r", null),
                SprocketLeft = Take(bones, "sprocket_l", null),
                SprocketRight = Take(bones, "sprocket_r", null),
            };
            MelonLogger.Msg(Tag + $" donor bones: {pools.RoadLeft.Count}+{pools.RoadRight.Count} road wheels, " +
                            $"{pools.IdlerLeft.Count}+{pools.IdlerRight.Count} idlers, " +
                            $"{pools.SprocketLeft.Count}+{pools.SprocketRight.Count} sprockets");

            int bound = 0;
            foreach (Part part in parts.Values)
            {
                Vector3 home = hullPivot.TransformPoint(part.Box.center);
                Transform bone = Nearest(PoolFor(part.Name, pools), home);
                if (bone == null)
                {
                    stillNames.Add(part.Name);
                    continue;
                }

                float distance = Vector3.Distance(home, bone.position);
                if (distance > MaxBindDistance)
                {
                    MelonLogger.Warning(Tag + $" {part.Name} is {distance:0.00} m from {bone.name}, left still");
                    stillNames.Add(part.Name);
                    continue;
                }

                // The holder cancels the bone's scale and sits on its origin, so the bone's rotation spins the
                // part about its own axle. Resetting the world rotation afterwards keeps our art's own stance.
                Transform holder = LpVisual.MakePivot(bone, "L2A7V_" + part.Name, Vector3.zero);
                holder.rotation = hullPivot.rotation;
                LpVisual.AttachMesh(holder, "L2A7V_" + part.Name,
                    LpAssets.BuildWhere(p => p.Group == LpGroup.RunningGear &&
                                             string.Equals(p.ObjectName, part.Name, StringComparison.OrdinalIgnoreCase),
                        "L2A7V_" + part.Name, part.Box.center));
                bound++;

                MelonLogger.Msg(Tag + $" {part.Name} -> {PathOf(bone)} offset {distance * 1000:0} mm");
            }

            BuildStatic(hullPivot, stillNames);
            MelonLogger.Msg(Tag + $" {bound} part(s) ride a bone, {stillNames.Count} object(s) have none");
            AttachTracks(hullPivot, unitRoot, CountTrackLoops());
        }

        /// <summary>How many exported track loops the model carries.</summary>
        private static int CountTrackLoops()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjGeometry geometry in ObjModel.Geometries)
            {
                if (geometry.Group != LpGroup.RunningGear) continue;
                if (IsTrackLoop(geometry.ObjectName)) names.Add(geometry.ObjectName);
            }
            return names.Count;
        }

        /// <summary>The exported track loops, by their Wavefront object names (track_l, track_r).</summary>
        private static bool IsTrackLoop(string objectName) =>
            objectName != null && objectName.StartsWith("track_", StringComparison.OrdinalIgnoreCase);

        /// <summary>The donor bone pools a part may draw from, one per side and role.</summary>
        private sealed class Pools
        {
            public List<Transform> RoadLeft;
            public List<Transform> RoadRight;
            public List<Transform> IdlerLeft;
            public List<Transform> IdlerRight;
            public List<Transform> SprocketLeft;
            public List<Transform> SprocketRight;
        }

        /// <summary>
        /// Only the rolling parts get a bone: road wheels by side, the drive sprocket and the front idler.
        /// A null pool means the object stays with the hull, which covers the return rollers, the arms and
        /// both track loops.
        /// </summary>
        private static List<Transform> PoolFor(string objectName, Pools pools)
        {
            string n = objectName.ToLowerInvariant();
            if (!n.StartsWith("wheel_", StringComparison.Ordinal) || n.Contains("top")) return null;

            bool left = n.Contains("_l");
            if (n.Contains("drive")) return left ? pools.SprocketLeft : pools.SprocketRight;
            if (n.Contains("front")) return left ? pools.IdlerLeft : pools.IdlerRight;
            return left ? pools.RoadLeft : pools.RoadRight;
        }

        /// <summary>Takes the closest free bone to a part's own centre and removes it from the pool.</summary>
        private static Transform Nearest(List<Transform> pool, Vector3 home)
        {
            if (pool == null || pool.Count == 0) return null;

            Transform best = null;
            float bestDistance = float.MaxValue;
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                Transform bone = pool[i];
                if (bone == null)
                {
                    pool.RemoveAt(i);
                    continue;
                }
                float distance = Vector3.Distance(home, bone.position);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = bone;
            }
            if (best != null) pool.Remove(best);
            return best;
        }

        /// <summary>
        /// The return rollers and the suspension arms: everything that has no bone of its own and therefore
        /// travels with the hull, exactly as the donor's skin binds them. Passing null builds the whole group,
        /// which is what the no-bones fallback wants. The track loops never belong here: as static hull art
        /// they are the reason the user reported tracks that were present but standing still, so one side or
        /// the other always draws them - the donor's animated skin, or LpTracks' scrolling copy of ours.
        /// </summary>
        private static void BuildStatic(Transform hullPivot, HashSet<string> stillNames)
        {
            LpVisual.AttachMesh(hullPivot, "L2A7V_RunningGearStatic",
                LpAssets.BuildWhere(p => p.Group == LpGroup.RunningGear && !IsTrackLoop(p.ObjectName) &&
                                         (stillNames == null || stillNames.Contains(p.ObjectName)),
                    "L2A7V_RunningGearStatic", Vector3.zero));
        }

        /// <summary>Gives the loops to whoever is going to draw them, then reports which that was.</summary>
        private static void AttachTracks(Transform hullPivot, Transform unitRoot, int loops)
        {
            if (!LpConfig.UseDonorTrackArt.Value) LpTracks.Attach(hullPivot, unitRoot);
            MelonLogger.Msg(Tag + $" {loops} track loop(s) " +
                            (LpConfig.UseDonorTrackArt.Value
                                ? "left to the donor's animated art"
                                : "rebuilt to scroll, " + LpTracks.Status));
        }

        private static Dictionary<string, Part> CollectParts()
        {
            if (ObjModel.Geometries.Count == 0) return null;

            var parts = new Dictionary<string, Part>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjGeometry geometry in ObjModel.Geometries)
            {
                if (geometry.Group != LpGroup.RunningGear) continue;
                Part part;
                if (!parts.TryGetValue(geometry.ObjectName, out part))
                {
                    part = new Part { Name = geometry.ObjectName };
                    parts[geometry.ObjectName] = part;
                }
                part.Include(geometry.Bounds);
            }
            return parts;
        }

        private static List<Transform> Take(List<Transform> bones, string prefix, string exclude)
        {
            var found = new List<Transform>();
            foreach (Transform bone in bones)
            {
                if (bone == null) continue;
                string n = bone.name.ToLowerInvariant();
                if (!n.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (exclude != null && n.Contains(exclude)) continue;
                found.Add(bone);
            }
            return found;
        }

        /// <summary>
        /// The donor's own skin is the authority on which transforms are the wheels: the prefab has several
        /// nodes that are called wheel_l_1, and only the ones in this bone array are driven. The "running gear"
        /// skin is the one the LEO2A3 actually animates, so it wins outright; abrams_tracks carries a second,
        /// unused wheel rig that would otherwise be a plausible-looking pool in the wrong place.
        /// </summary>
        private static List<Transform> FindBones(Transform unitRoot)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "wheel", "idler", "sprocket",
            };

            List<Transform> best = null;
            foreach (SkinnedMeshRenderer skin in unitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (LpVisual.IsImportedArt(skin.transform)) continue;
                Transform[] bones = skin.bones;
                if (bones == null) continue;

                var matches = new List<Transform>();
                foreach (Transform bone in bones)
                {
                    if (bone == null) continue;
                    string lower = bone.name.ToLowerInvariant();
                    foreach (string name in names)
                        if (lower.StartsWith(name, StringComparison.Ordinal)) { matches.Add(bone); break; }
                }
                if (matches.Count < 4) continue;

                if (PathOf(skin.transform).IndexOf("running gear", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    MelonLogger.Msg(Tag + $" wheel bones from {PathOf(skin.transform)} ({matches.Count})");
                    return matches;
                }
                if (best == null || matches.Count > best.Count) best = matches;
            }

            if (best != null)
                MelonLogger.Warning(Tag + $" no running gear skin, using {best.Count} wheel bones from another mesh");
            return best;
        }

        private static string PathOf(Transform node)
        {
            var builder = new StringBuilder(node.name);
            Transform parent = node.parent;
            int guard = 0;
            while (parent != null && guard++ < 12)
            {
                builder.Insert(0, parent.name + "/");
                parent = parent.parent;
            }
            return builder.ToString();
        }
    }
}

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
    /// The seven suspension arms per side ride the donor's own arm bones rather than the hull. The animated
    /// "running gear" skin binds 33 bones - 14 wheels, an idler, a sprocket and 14 arm_l/r_1..7 - and the donor's
    /// TrackedWheelNodeConfig swings that same arm array, so the motion is already there and only the art was
    /// bolted to the hull. An arm is not matched by distance: our row spans 4.94 m against the donor's 4.85 m, so
    /// the middle arm of one is nearer its neighbour's hub than its own. Both sides number front to rear, and the
    /// measurements agree on what that means (art suspension_l_01 at +2.519 m through _07 at -2.420 m, against
    /// donor arm_l_1 at +2.614 and arm_l_7 at -2.240), so the number in the name is the match. The return
    /// rollers have no bone in that array and stay with the hull, which is what the donor's skin does with them.
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
                ArmLeft = Take(bones, "arm_l", null),
                ArmRight = Take(bones, "arm_r", null),
            };
            MelonLogger.Msg(Tag + $" donor bones: {pools.RoadLeft.Count}+{pools.RoadRight.Count} road wheels, " +
                            $"{pools.IdlerLeft.Count}+{pools.IdlerRight.Count} idlers, " +
                            $"{pools.SprocketLeft.Count}+{pools.SprocketRight.Count} sprockets, " +
                            $"{pools.ArmLeft.Count}+{pools.ArmRight.Count} arms");

            int bound = 0;
            bound += BindArms(hullPivot, parts, "suspension_l_", pools.ArmLeft, stillNames);
            bound += BindArms(hullPivot, parts, "suspension_r_", pools.ArmRight, stillNames);
            foreach (Part part in parts.Values)
            {
                if (IsArm(part.Name)) continue;   // paired above, onto the donor's swinging arm bones

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

        /// <summary>
        /// Hangs one side's suspension arms on the donor's arm bones, matched by the number each side puts in its
        /// own name (art suspension_l_01..07 against donor arm_l_1..7, both front to rear). Distance cannot do
        /// this between rows whose hubs are not where our art drew them.
        /// </summary>
        private static int BindArms(Transform hullPivot, Dictionary<string, Part> parts, string prefix,
            List<Transform> bones, HashSet<string> stillNames)
        {
            var byNumber = new Dictionary<int, Transform>();
            foreach (Transform bone in bones)
            {
                if (bone == null) continue;
                int number = FirstNumber(bone.name);
                if (number > 0 && !byNumber.ContainsKey(number)) byNumber[number] = bone;
            }

            int bound = 0;
            foreach (Part part in parts.Values)
            {
                if (!part.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                Transform bone;
                if (!byNumber.TryGetValue(FirstNumber(part.Name), out bone))
                {
                    MelonLogger.Warning(Tag + $" {part.Name} has no donor arm bone numbered with it, so it " +
                                        "stays on the hull");
                    stillNames.Add(part.Name);
                    continue;
                }
                BindRigid(hullPivot, part, bone);
                bound++;
            }
            return bound;
        }

        /// <summary>
        /// Puts a part on a bone without moving it: the mesh is built about the part's own centre, the holder is
        /// placed back on the spot the export drew it, and from then on every move the bone makes - the swing, and
        /// whatever travel the game gives its pivot - carries the part with it. An arm wants this and a wheel does
        /// not: the arm's job is to pivot with the torsion bar, so its place relative to the bone has to hold.
        /// </summary>
        private static void BindRigid(Transform hullPivot, Part part, Transform bone)
        {
            string node = "L2A7V_" + part.Name;
            Transform holder = LpVisual.MakePivot(bone, node, Vector3.zero);
            holder.position = hullPivot.TransformPoint(part.Box.center);
            holder.rotation = hullPivot.rotation;
            LpVisual.AttachMesh(holder, node,
                LpAssets.BuildWhere(p => p.Group == LpGroup.RunningGear &&
                                         string.Equals(p.ObjectName, part.Name, StringComparison.OrdinalIgnoreCase),
                    node, part.Box.center));

            float distance = Vector3.Distance(holder.position, bone.position);
            MelonLogger.Msg(Tag + $" {part.Name} -> {PathOf(bone)}, swings with it from its drawn place " +
                            $"{distance * 1000:0} mm off that bone's pivot");
        }

        /// <summary>The first run of digits in a name, which is how both sides number a row: arm_l_4, suspension_l_04.</summary>
        private static int FirstNumber(string name)
        {
            int value = 0;
            bool any = false;
            foreach (char c in name)
            {
                if (c < '0' || c > '9') { if (any) break; continue; }
                value = value * 10 + (c - '0');
                any = true;
            }
            return any ? value : 0;
        }

        /// <summary>One side's suspension arms.</summary>
        private static bool IsArm(string objectName) =>
            objectName.StartsWith("suspension_", StringComparison.OrdinalIgnoreCase);

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
            public List<Transform> ArmLeft;
            public List<Transform> ArmRight;
        }

        /// <summary>
        /// Only the rolling parts get a bone from this table: road wheels by side, the drive sprocket and the
        /// front idler. A null pool means the object stays with the hull, which covers the return rollers and
        /// both track loops; the suspension arms have bones of their own and are matched by BindArms instead.
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
        /// The return rollers: everything that has no bone of its own and therefore travels with the hull, exactly
        /// as the donor's skin binds them. Passing null builds the whole group,
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
            if (!LpConfig.UseDonorTrackArt) LpTracks.Attach(hullPivot, unitRoot);
            MelonLogger.Msg(Tag + $" {loops} track loop(s) " +
                            (LpConfig.UseDonorTrackArt
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
        ///
        /// The arm bones come out of the same array, because that is where the driven ones are: this skin's 33
        /// bones are 14 wheels, an idler and a sprocket per side and 14 arm_l/r_1..7 (tracked_bone_names.py), and
        /// the donor's TrackedWheelNodeConfig swings exactly leo2_rig/hull/arm_l_1..7 then arm_r_1..7, in the same
        /// order as its Wheel L1..L7 controllers and its arm_l_i/wheel_l_i VisualNodes (lp_track_nodes.txt).
        /// Without "arm" in this table the arm pool arrives empty and the suspension art has nothing to hang on.
        /// </summary>
        private static List<Transform> FindBones(Transform unitRoot)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "wheel", "idler", "sprocket", "arm",
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

using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Crew;
using GHPC.Equipment;
using GHPC.Equipment.Optics;
using GHPC.Player;
using GHPC.Utility;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using Reticle;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Turns the imported cupola machine gun into a weapon the game can drive.
    ///
    /// The donor Leopard 2A3 has no roof gun - it carries a main gun and a coaxial MG3 - so this builds one
    /// out of the pieces the game already understands rather than inventing a format: the coaxial MG3's
    /// WeaponSystem, feed and 4750-round belt rack are cloned whole (which is where the 1200 rpm sound, the
    /// 7.62 mm round and the reload timing come from), the FERO Z18 eyepiece is cloned whole for its lens and
    /// post-process wiring, and the two aiming platforms and the fire control system are laid out the way the
    /// T-64A's cupola machine gun does it. A RoofGun role needs no special casing anywhere in the game: the
    /// player cycles weapons over the crew brain's list and aims through whatever FireControlSystem that
    /// entry names, so a third entry with its own two Mounts is enough to make it selectable, aimable and
    /// firing.
    ///
    /// Everything is built under an inactive pivot and switched on at the end, because a fresh Awake runs
    /// against a finished object graph then - and a graph that is only finished at the end, since
    /// Instantiate rewires references inside the copy alone. Every reference that pointed out of the cloned
    /// subtree is therefore cut first: the coaxial belt bundle in the weapon's exclusive items, the FLIR in
    /// the sight's linked night mode, the donor's own gunner slot as the sight's reparent target. Left in
    /// place, each would have had our new hardware switching the donor's art on and off.
    /// </summary>
    public static class LpRoofGun
    {
        public const string WeaponName = "7.62 mm machine gun MG3 (AA)";

        /// <summary>The mount turns on the vertical axis, so its own mesh node.</summary>
        private static readonly string[] YawParts = { "mg_aa_mount_h" };

        /// <summary>The cradle, the gun body and the belt box all ride the elevation axis.</summary>
        private static readonly string[] PitchParts = { "mg_aa_mount_v", "bone_mg_aa_v", "ammo" };

        private static readonly FieldInfo WeaponExclusiveItems =
            AccessTools.Field(typeof(WeaponSystem), "ExclusiveItems");
        private static readonly FieldInfo WeaponCodexField =
            AccessTools.Field(typeof(WeaponSystem), "CodexEntry");
        private static readonly FieldInfo SlotReparentTarget =
            AccessTools.Field(typeof(CameraSlot), "_reparentTarget");
        private static readonly FieldInfo DesignatedSlots =
            AccessTools.Field(typeof(Unit), "_designatedCameraSlots");
        private static readonly MethodInfo RescanCamSlots =
            AccessTools.Method(typeof(CameraManager), "RescanCamSlots");

        private static WeaponSystemCodexScriptable _codex;

        /// <summary>True for the four parts the commander's machine gun is made of.</summary>
        public static bool IsRoofGunPart(string objectName)
        {
            return Matches(YawParts, objectName) || Matches(PitchParts, objectName);
        }

        public static void Apply(GameObject unitRoot, LpVisual.Assembly art, Vector3 ring)
        {
            if (!LpConfig.AddRoofGun.Value || unitRoot == null || art == null || art.Turret == null) return;
            if (art.RoofGunYaw != null) return;

            var manager = unitRoot.GetComponent<WeaponsManager>();
            Transform donorScripts = FindCoaxScripts(unitRoot);
            if (manager == null || donorScripts == null)
            {
                MelonLogger.Warning("[L2A7V] the roof gun stays decoration: " + (manager == null
                    ? "the unit has no weapons manager"
                    : "the donor has no coaxial machine gun to clone"));
                return;
            }

            Vector3 yawPoint = LpConfig.RoofGunYaw;
            Vector3 trunnionPoint = LpConfig.RoofGunTrunnion;

            // Both pivots hang off the turret bone, which is what the game's own traverse turns, and each is
            // placed so the axis it rotates about passes through its own origin: yaw on the mount's vertical
            // pin, pitch on the bore line. At rest the two cancel out and the art reads exactly as the
            // turret group it was taken out of does.
            Transform yaw = LpVisual.MakePivot(art.Turret.parent, "L2A7V_RoofGunYaw", yawPoint - ring);
            yaw.gameObject.SetActive(false);
            Transform pitch = LpVisual.MakePivot(yaw, "L2A7V_RoofGunPitch", trunnionPoint - yawPoint);
            art.RoofGunYaw = yaw;
            art.RoofGunPitch = pitch;

            var fcsNode = new GameObject("L2A7V_RoofGunFireControl");
            fcsNode.transform.SetParent(pitch, false);

            GameObject scripts = UnityEngine.Object.Instantiate(donorScripts.gameObject, pitch, false);
            scripts.name = "L2A7V_RoofGunScripts";
            DetachReparenters(scripts);

            WeaponSystem weapon = scripts.GetComponentInChildren<WeaponSystem>(true);
            if (weapon == null)
            {
                MelonLogger.Warning("[L2A7V] the copied coaxial weapon is gone, the mount stays decoration");
                return;
            }

            var feed = weapon.GetComponent<AmmoFeed>();
            GHPC.Weapons.AmmoRack rack = feed != null ? feed.ReadyRack : null;
            if (rack == null)
            {
                MelonLogger.Warning("[L2A7V] the copied coaxial feed lost its belt rack, " +
                                    "the mount stays decoration");
                return;
            }

            // The muzzle is the spawn point of every round and the rack node is where the belt box sits, both
            // measured from the elevation pivot the copied gun now hangs on.
            weapon.gameObject.name = "MG3 (AA)";
            weapon.transform.localPosition = LpConfig.RoofGunMuzzle - trunnionPoint;
            rack.transform.localPosition = LpConfig.RoofGunRackPoint - trunnionPoint;
            rack.Name = "Roof MG rack";
            weapon.DamageName = "Roof MG";
            if (WeaponExclusiveItems != null) WeaponExclusiveItems.SetValue(weapon, new GameObject[0]);
            AssignCodex(weapon);

            AimablePlatform yawPlatform = AddPlatform(yaw, "Roof MG traverse", EnumHelpers.Axis3.Y, 25f, 15f,
                Vector2.zero, false);
            AimablePlatform pitchPlatform = AddPlatform(pitch, "Roof MG elevation", EnumHelpers.Axis3.X, 20f, 12f,
                // War Thunder's MG3A1 block on this vehicle: -10 / +75. The yaw platform is Vector2.zero,
                // i.e. unlimited traverse, which is what a pintle in the commander's hatch is.
                new Vector2(-10f, 75f), true);
            pitchPlatform.ParentPlatform = yawPlatform;
            pitchPlatform.ProtectFromBadProjection = true;

            ConfigureFcs(fcsNode, yaw, yawPlatform, pitchPlatform, weapon);
            FireControlSystem fcs = fcsNode.GetComponent<FireControlSystem>();

            // Only the mesh nodes join the hide list. The pivots carry the scripts, the muzzle and the
            // eyepiece, and scaling a pivot away takes their world positions with it.
            MeshRenderer mountArt = LpVisual.AttachParts(yaw, "RoofGunMount", IsYawPart, yawPoint);
            MeshReceiverCheck(mountArt, "mount");
            art.RoofGunYawArt = mountArt != null ? mountArt.transform : null;
            MeshRenderer receiverArt = LpVisual.AttachParts(pitch, "RoofGunReceiver", IsPitchPart, trunnionPoint);
            MeshReceiverCheck(receiverArt, "receiver");
            art.RoofGunPitchArt = receiverArt != null ? receiverArt.transform : null;

            CameraSlot slot = BuildSight(pitch, FindDonorSight(unitRoot), weapon, fcs,
                LpConfig.RoofGunSight - trunnionPoint);

            yaw.gameObject.SetActive(true);

            // A Reparent component moves its own GameObject in Awake, and switching the hierarchy on is what
            // runs it, so the copied group is put back under the elevation pivot rather than only trusting
            // that the component was taken off it.
            scripts.transform.SetParent(pitch, false);
            scripts.transform.localPosition = Vector3.zero;
            scripts.transform.localRotation = Quaternion.identity;

            RefreshCaches(unitRoot, slot, AppendWeapon(manager, weapon, fcs));

            Vector3 bore = weapon.transform.position;
            MelonLogger.Msg($"[L2A7V] roof gun armed: '{WeaponName}' on a cloned coaxial MG3, " +
                            $"{rack.ClipCapacity} rounds, sight {(slot == null ? "missing" : "ready")}; " +
                            $"muzzle at ({bore.x:F2},{bore.y:F2},{bore.z:F2}) world, under " +
                            $"{(scripts.transform.parent == null ? "nothing" : scripts.transform.parent.name)}");
        }

        private static bool IsYawPart(ObjGeometry part)
        {
            return Matches(YawParts, part.ObjectName);
        }

        private static bool IsPitchPart(ObjGeometry part)
        {
            return Matches(PitchParts, part.ObjectName);
        }

        private static bool Matches(string[] names, string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return false;
            foreach (string name in names)
                if (string.Equals(objectName, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void MeshReceiverCheck(MeshRenderer renderer, string what)
        {
            if (renderer == null)
                MelonLogger.Warning("[L2A7V] the roof gun " + what + " has no geometry, " +
                                    "it still aims and fires but cannot be seen");
        }

        /// <summary>
        /// Takes GHPC's Reparent component off a freshly copied group. The donor keeps its script groups out
        /// of the rig and has each one moved into place at Awake: the coaxial group's reference is the
        /// mantlet bone, so a copy of it that keeps the component walks itself back there the moment the
        /// hierarchy is switched on, one and a half metres under the imported gun it was meant to be the
        /// breech of - and a machine gun whose muzzle is inside the turret fires through its own crew.
        /// DestroyImmediate rather than Destroy, because the pending removal would not land before the
        /// activation in this same pass runs the Awake it is meant to prevent.
        /// </summary>
        private static void DetachReparenters(GameObject clone)
        {
            foreach (Reparent reparent in clone.GetComponentsInChildren<Reparent>(true))
                UnityEngine.Object.DestroyImmediate(reparent);
        }

        /// <summary>
        /// The object to copy: the optic itself when it carries its own CameraSlot, otherwise the nearest
        /// ancestor that does. A donor that authors the optic as a child of the sight object would otherwise
        /// leave the copy's slot reference on that ancestor, which Instantiate cannot re-point because it was
        /// outside the copied subtree.
        /// </summary>
        private static GameObject CloneSource(UsableOptic donor)
        {
            for (Transform node = donor.transform; node != null; node = node.parent)
                if (node.GetComponentInChildren<CameraSlot>(true) != null) return node.gameObject;
            return donor.gameObject;
        }

        /// <summary>
        /// The slot this copy owns, or null. Writing ExclusiveWeapons onto a borrowed slot would mark the
        /// gunner's daylight sight unusable while the main gun is selected, and an interior pool with nothing
        /// in it is what leaves the player stuck in third person with no way back.
        /// </summary>
        private static CameraSlot ResolveSlot(GameObject clone, UsableOptic optic)
        {
            CameraSlot slot = optic != null ? optic.slot : null;
            if (slot == null || !IsInside(clone, slot.transform))
                slot = clone.GetComponentInChildren<CameraSlot>(true);
            if (slot == null || !IsInside(clone, slot.transform)) return null;
            if (optic != null && !ReferenceEquals(optic.slot, slot)) optic.slot = slot;
            return slot;
        }

        private static bool IsInside(GameObject root, Transform node) =>
            node != null && node.IsChildOf(root.transform);

        /// <summary>
        /// A copy of a whole sight assembly brings that sight's fire control system and ATGM guidance unit with
        /// it. Two computers aiming the same mount fight over it, so the copy keeps the camera, the lens and the
        /// reticle and drops the rest.
        /// </summary>
        private static void StripWeaponLogic(GameObject clone)
        {
            foreach (FireControlSystem extra in clone.GetComponentsInChildren<FireControlSystem>(true))
                UnityEngine.Object.DestroyImmediate(extra);
            foreach (MissileGuidanceUnit guidance in clone.GetComponentsInChildren<MissileGuidanceUnit>(true))
                UnityEngine.Object.DestroyImmediate(guidance);
        }

        /// <summary>The donor's coaxial script group, which holds the MG3, its feed and its belt rack.</summary>
        private static Transform FindCoaxScripts(GameObject unitRoot)
        {
            foreach (WeaponSystem weapon in unitRoot.GetComponentsInChildren<WeaponSystem>(true))
            {
                if (LpVisual.IsImportedArt(weapon.transform)) continue;
                if (weapon.Feed == null || weapon.Feed.ReadyRack == null) continue;
                string name = weapon.gameObject.name.ToLowerInvariant();
                if (!name.Contains("7.62") && !name.Contains("coax")) continue;
                return weapon.transform.parent;
            }
            return null;
        }

        /// <summary>
        /// The donor's daylight eyepiece, taken for its lens quad, post-process volume and reticle renderer.
        /// The gunner's FERO is the wanted one; any other interior daylight optic will do if it is gone.
        /// </summary>
        private static UsableOptic FindDonorSight(GameObject unitRoot)
        {
            UsableOptic fallback = null;
            foreach (UsableOptic optic in unitRoot.GetComponentsInChildren<UsableOptic>(true))
            {
                if (LpVisual.IsImportedArt(optic.transform)) continue;
                if (optic.slot == null || optic.reticleMesh == null) continue;
                if (optic.slot.IsExterior || optic.slot.VisionType != NightVisionType.None) continue;
                if (optic.transform.name.IndexOf("FERO", StringComparison.OrdinalIgnoreCase) >= 0) return optic;
                if (fallback == null) fallback = optic;
            }
            return fallback;
        }

        private static AimablePlatform AddPlatform(Transform pivot, string name, EnumHelpers.Axis3 axis,
            float powered, float unpowered, Vector2 eulerLimits, bool reverseEuler)
        {
            var platform = pivot.gameObject.AddComponent<AimablePlatform>();
            platform.Name = name;
            platform.Transform = pivot;
            platform.Axis = axis;
            platform.SpeedPowered = powered;
            platform.SpeedUnpowered = unpowered;
            // A hand-cranked roof mount: nothing stabilises it and the stabiliser key must not try.
            platform.Stabilized = false;
            platform.LocalEulerLimits = eulerLimits;
            platform.ReverseLocalEuler = reverseEuler;
            // Awake walks the hit zone list with no null guard, and an empty one is what a cupola mount has.
            platform.HitZones = Array.Empty<GHPC.Equipment.DestructibleComponent>();
            return platform;
        }

        private static void ConfigureFcs(GameObject node, Transform yaw, AimablePlatform yawPlatform,
            AimablePlatform pitchPlatform, WeaponSystem weapon)
        {
            FireControlSystem fcs = node.AddComponent<FireControlSystem>();
            fcs.ReferenceTransform = yaw;
            // Two Mounts is the minimum the aim rates read from: with one or none, AdjustAimByRatio returns
            // zero degrees per second and the mouse does nothing.
            fcs.Mounts = new[] { yawPlatform, pitchPlatform };
            fcs.LinkedWeaponSystems = new[] { weapon };
            fcs.GatedAimablePlatforms = Array.Empty<AimablePlatform>();
            fcs.ManualModeTriggers = Array.Empty<FcsManualModeStartTrigger>();
            fcs.AutoModeTriggers = Array.Empty<FcsManualModeCancelTrigger>();
            fcs.MaxLaserRange = 0f;
            fcs.RegisteredRangeLimits = new Vector2(0f, 1600f);
            fcs.RangeStep = 100;
            fcs.FireGateAngle = 0f;
            fcs.CurrentStabMode = StabilizationMode.None;
            fcs.WeaponAuthoritative = false;
            fcs.ComputerNeedsPower = false;
            fcs.SuperelevateWeapon = false;
            fcs.SuperleadWeapon = false;
            fcs.DynamicLead = false;
            fcs.InertialCompensation = false;
            fcs.RecordTraverseRateBuffer = false;

            // The sight's own copy of the FCS reference has to be in place before its Awake and Start run,
            // because Start is what subscribes it to the computer.
            weapon.FCS = fcs;
        }

        private static CameraSlot BuildSight(Transform pitch, UsableOptic donor, WeaponSystem weapon,
            FireControlSystem fcs, Vector3 localPosition)
        {
            if (donor == null || fcs == null) return null;

            GameObject clone = UnityEngine.Object.Instantiate(CloneSource(donor), pitch, false);
            clone.name = "L2A7V_MG gunsight";
            DetachReparenters(clone);
            StripWeaponLogic(clone);
            clone.transform.localPosition = localPosition;

            UsableOptic optic = clone.GetComponentInChildren<UsableOptic>(true);
            CameraSlot slot = ResolveSlot(clone, optic);
            if (optic == null || slot == null || optic.reticleMesh == null)
            {
                MelonLogger.Warning("[L2A7V] the copied gunsight has no optic, slot or reticle of its own to " +
                                    "rebuild, so the mount keeps no sight rather than editing the gunner's");
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            optic.FCS = fcs;
            optic.Mounts = Array.Empty<AimablePlatform>();
            slot.LinkedNightSight = null;
            slot.HitZone = null;
            slot.SlotToUseOnDestroy = null;
            slot.ExclusiveItems = Array.Empty<GameObject>();
            slot.ZeroScaleItems = Array.Empty<Transform>();
            slot.OtherFovs = Array.Empty<float>();
            slot.ExclusiveWeapons = new[] { weapon };
            if (SlotReparentTarget != null) SlotReparentTarget.SetValue(slot, null);

            float donorFov = slot.DefaultFov;
            slot.DefaultFov = Mathf.Clamp(LpConfig.RoofGunSightFov.Value, 1f, 60f);
            if (donorFov > 0f && !Mathf.Approximately(donorFov, slot.DefaultFov))
            {
                // The lens frame is a flat quad at a fixed distance, so widening the picture only keeps
                // filling the screen if the frame grows by the same tangent ratio.
                float ratio = Mathf.Tan(slot.DefaultFov * 0.5f * Mathf.Deg2Rad) /
                              Mathf.Tan(donorFov * 0.5f * Mathf.Deg2Rad);
                Vector3 frame = new Vector3(ratio, ratio, 1f);
                foreach (MeshRenderer lens in clone.GetComponentsInChildren<MeshRenderer>(true))
                    lens.transform.localScale = Vector3.Scale(lens.transform.localScale, frame);
            }

            ReticleMesh plate = optic.reticleMesh;
            plate.reticleSO = LpReticle.BuildRoofGunPlate(plate.reticleSO);
            // Load() keeps whatever mesh it built first, and this renderer arrives holding the donor's.
            SkinnedMeshRenderer renderer = plate.GetComponent<SkinnedMeshRenderer>();
            if (renderer != null) renderer.sharedMesh = null;

            // The donor eyepiece is switched off whenever nobody is looking through it, and a copy of an
            // inactive GameObject never gets an Awake. Its own Start switches it back off again through
            // slot.ActiveChanged, so this only has to hold until the hierarchy is activated.
            clone.SetActive(true);
            return slot;
        }

        private static WeaponSystemInfo AppendWeapon(WeaponsManager manager, WeaponSystem weapon,
            FireControlSystem fcs)
        {
            var info = new WeaponSystemInfo
            {
                Name = WeaponName,
                Role = WeaponSystemRole.RoofGun,
                Weapon = weapon,
                FCS = fcs
            };
            if (manager.Weapons == null)
            {
                manager.Weapons = new[] { info };
                return info;
            }
            foreach (WeaponSystemInfo existing in manager.Weapons)
            {
                if (existing == null || existing.Role != WeaponSystemRole.RoofGun) continue;
                existing.Weapon = weapon;
                existing.FCS = fcs;
                existing.Name = WeaponName;
                return existing;
            }

            var appended = new WeaponSystemInfo[manager.Weapons.Length + 1];
            Array.Copy(manager.Weapons, appended, manager.Weapons.Length);
            appended[manager.Weapons.Length] = info;
            manager.Weapons = appended;
            return info;
        }

        /// <summary>
        /// The unit's slot list is authored into the prefab, the gunner's weapon list is copied out of it at
        /// Awake and the camera manager caches the slots of the unit it adopted, so a weapon added after all
        /// three exist is invisible to the player unless each cache is told. On a template none of this is
        /// needed - the spawned copy re-runs Awake with our entries already in its fields - but it costs
        /// nothing there and it is what makes the in-place conversion work.
        /// </summary>
        private static void RefreshCaches(GameObject unitRoot, CameraSlot slot, WeaponSystemInfo info)
        {
            var unit = unitRoot.GetComponent<Unit>();
            if (unit == null) return;

            if (slot != null && DesignatedSlots != null)
            {
                var designated = DesignatedSlots.GetValue(unit) as CameraSlot[];
                // An empty authored list is not "no slots", it is "the game picks them off the hierarchy at
                // unit change", so the replacement has to start from that same set; appending to an empty
                // one hands the camera manager a single slot and drops every other view out of the cycle.
                if (designated == null || designated.Length == 0) designated = LiveSlots(unit);
                if (designated.Length == 0)
                {
                    MelonLogger.Warning("[L2A7V] the unit reports no camera slot of its own, so the roof gun " +
                                        "sight is not registered: an authored list of one would take every " +
                                        "other view out of the cycle");
                }
                else if (Array.IndexOf(designated, slot) < 0)
                    DesignatedSlots.SetValue(unit, AddTo(designated, slot));
            }

            var crew = unitRoot.GetComponent<CrewManager>();
            if (crew != null && crew.CrewMembers != null)
            {
                foreach (CrewManager.CrewMember member in crew.CrewMembers)
                {
                    if (member == null || member.Position != CrewPosition.Gunner) continue;
                    List<WeaponSystemInfo> weapons = member.Brain?.WeaponsModule?.Weapons;
                    if (weapons == null) continue;
                    if (!weapons.Contains(info)) weapons.Add(info);
                }
            }

            PlayerInput input = PlayerInput.Instance;
            CameraManager cameraManager = CameraManager.Instance;
            if (input == null || cameraManager == null || RescanCamSlots == null) return;
            if (!ReferenceEquals(input.CurrentPlayerUnit, unit)) return;
            CameraSlot[] current = unit.DesignatedCameraSlots;
            if (current != null && current.Length != 0)
                RescanCamSlots.Invoke(cameraManager, new object[] { current });
        }

        /// <summary>
        /// The slot list PlayerInput.SetPlayerUnit uses when the prefab designates none: the CameraSlots under
        /// the unit that are not direct-attach only. Inactive nodes count, because we convert while the unit is
        /// still switched off and the active-only scan then found none of the donor's own slots.
        /// </summary>
        private static CameraSlot[] LiveSlots(Unit unit)
        {
            var list = new List<CameraSlot>();
            foreach (CameraSlot cameraSlot in unit.GetComponentsInChildren<CameraSlot>(true))
                if (cameraSlot != null && !cameraSlot.DirectAttachOnly) list.Add(cameraSlot);
            return list.ToArray();
        }

        /// <summary>Grow a fixed-size serialized array by one entry, which is the only way to append to one.</summary>
        private static CameraSlot[] AddTo(CameraSlot[] array, CameraSlot extra)
        {
            var grown = new CameraSlot[array.Length + 1];
            Array.Copy(array, grown, array.Length);
            grown[array.Length] = extra;
            return grown;
        }

        private static void AssignCodex(WeaponSystem weapon)
        {
            if (WeaponCodexField == null) return;
            var donor = WeaponCodexField.GetValue(weapon) as WeaponSystemCodexScriptable;
            if (donor == null) return;

            if (_codex == null)
            {
                // The donor's asset names the coaxial MG3 of every vanilla Leopard in the mission, so ours
                // gets its own row instead of a shared one with two labels.
                _codex = ScriptableObject.CreateInstance<WeaponSystemCodexScriptable>();
                _codex.name = "L2A7V_MG3_AA";
                _codex.FriendlyName = WeaponName;
                _codex.CaliberMm = donor.CaliberMm;
                _codex.Type = donor.Type;
            }
            WeaponCodexField.SetValue(weapon, _codex);
        }
    }
}

using System;
using System.Reflection;
using GHPC.Weaponry;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Puts the 2A7V's own gun and round on the donor's weapon plumbing. The donor is a Rh-120 L/44 firing
    /// DM23; this vehicle carries the L/55 and fires DM53, and both facts reach the player through three
    /// pieces the game reads separately: the weapon codex (the name on the HUD), the clip (the round's name
    /// and the shells sitting in the rack) and the AmmoType (penetration, velocity, flight model and the
    /// ballistic computer's firing table). The gun is re-authored in place and the round is added to that
    /// list as its own type, so nothing the donor was built with stops being selectable.
    /// The feed, both racks, the reload
    /// stages and the recoil impulse stay the donor's, so the gun still reloads at its authored 6 seconds,
    /// which is also what an aced load in War Thunder takes.
    /// </summary>
    public static class LpWeapons
    {
        private const string GunName = "Rh-120 L/55";
        private const string WeaponEntryName = "Rh-120 L/55 120 mm";
        private const string ShellName = "DM53 APFSDS-T";

        // DM53 from the L/55: 653 mm of penetration at the muzzle (War Thunder's figure for this gun), a
        // 1750 m/s muzzle velocity and a 5 kg projectile. Velocity, mass and drag feed the ballistic
        // computer's firing table, so they matter as much as the penetration number does.
        private const float PenetrationMm = 653f;
        private const float MuzzleVelocity = 1750f;
        private const float ProjectileMassKg = 5f;

        private static readonly FieldInfo WeaponCodexField =
            AccessTools.Field(typeof(WeaponSystem), "CodexEntry");
        private static readonly MethodInfo RegisterBallistics =
            AccessTools.Method(typeof(LoadoutManager), "RegisterAllBallistics");

        private static AmmoType _shell;
        private static AmmoCodexScriptable _codex;
        private static AmmoClipCodexScriptable _clip;
        private static WeaponSystemCodexScriptable _gunCodex;
        private static bool _failed;

        /// <summary>
        /// Switches on each spawned copy of the imported round visual. The template is kept inactive so it
        /// cannot be seen in the world, and the rack does not activate what it spawns - every visual it was
        /// written for is a prefab asset, which is never in a scene to be switched off in the first place.
        /// </summary>
        public static void Install(HarmonyLib.Harmony harmony)
        {
            // The assembly carries a second, unrelated AmmoRack in the global namespace that shadows this
            // one, so the type has to be named in full or the hook lands on the wrong class.
            MethodInfo spawn = AccessTools.Method(typeof(GHPC.Weapons.AmmoRack), "SpawnVisualModel");
            if (spawn == null)
            {
                MelonLogger.Warning("[L2A7V] could not hook the ammo rack's visual spawn, so the imported " +
                                    "rounds will load but stay invisible in the rack");
                return;
            }
            harmony.Patch(spawn, postfix: new HarmonyMethod(typeof(LpWeapons), nameof(VisualSpawned)));
        }

        private static void VisualSpawned(AmmoType type, GameObject __result)
        {
            if (__result != null && _shell != null && type == _shell && !__result.activeSelf)
                __result.SetActive(true);
        }

        public static void Apply(GameObject root)
        {
            if (!LpConfig.Enabled.Value || root == null || _failed) return;

            var weapons = root.GetComponent<WeaponsManager>();
            var loadout = root.GetComponent<LoadoutManager>();
            if (weapons == null || loadout == null || weapons.Weapons == null || weapons.Weapons.Length == 0)
                return;

            int index = loadout.WeaponIndex;
            if (index < 0 || index >= weapons.Weapons.Length) return;
            WeaponSystemInfo entry = weapons.Weapons[index];
            if (entry == null || entry.Weapon == null) return;

            AmmoClipCodexScriptable[] clips = loadout.LoadedAmmoList.AmmoClips;
            if (clips == null || clips.Length == 0) return;

            int sabot = FindSabotClip(clips);
            if (sabot < 0)
            {
                MelonLogger.Warning("[L2A7V] the donor loadout holds no APFSDS clip, keeping its ammunition");
                return;
            }

            if (EnsureClip(clips[sabot]) == null) return;

            bool added = AppendClip(loadout, clips);
            entry.Name = WeaponEntryName;
            RenameWeapon(entry.Weapon);

            // The list was registered against the donor's clips in Awake, and Start reads the ballistics
            // out of this repository, so the new round has to be in it before either of them runs.
            if (RegisterBallistics != null) RegisterBallistics.Invoke(loadout, null);

            if (added)
                MelonLogger.Msg($"[L2A7V] main gun set to {WeaponEntryName}: {ShellName} " +
                                $"({PenetrationMm:F0} mm, {MuzzleVelocity:F0} m/s) added to the donor's " +
                                $"{clips.Length - 1} round type(s) as slot {clips.Length - 1}");
        }

        /// <summary>
        /// Puts the imported round at the end of the loadout's clip list and gives it a share of the
        /// ammunition total, which leaves every round the donor was built with still selectable. The list is
        /// a per-unit copy of the ammo codex, so this cannot reach the asset or another vehicle.
        ///
        /// Appending rather than replacing is also what keeps the rack authoring honest: a fixed slot choice
        /// and a forbidden ammo index are both positions in this list, and the new round takes the index
        /// nothing refers to.
        /// </summary>
        private static bool AppendClip(LoadoutManager loadout, AmmoClipCodexScriptable[] clips)
        {
            foreach (AmmoClipCodexScriptable clip in clips)
                if (ReferenceEquals(clip, _clip)) return false;

            int[] counts = loadout.TotalAmmoCounts ?? Array.Empty<int>();
            int total = 0;
            for (int i = 0; i < clips.Length; i++)
                if (i < counts.Length) total += counts[i];

            // The racks only hold as many clips as they have slots for - forty two on this vehicle - and
            // SpawnCurrentLoadout drops anything past that with an error, so the new round's share comes out
            // of the types already loaded rather than on top of them. An empty loadout stays empty and only
            // gains its zero.
            int share = total > 0 ? Mathf.Max(1, total / (clips.Length + 1)) : 0;
            var grown = new AmmoClipCodexScriptable[clips.Length + 1];
            Array.Copy(clips, grown, clips.Length);
            grown[clips.Length] = _clip;

            var newCounts = new int[clips.Length + 1];
            int kept = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                int own = i < counts.Length ? counts[i] : 0;
                newCounts[i] = total > 0 ? own * (total - share) / total : 0;
                kept += newCounts[i];
            }
            // Integer division rounds every type down; the clips it loses are the new round's.
            newCounts[clips.Length] = share + (total - kept - share);

            loadout.TotalAmmoCounts = newCounts;
            loadout.LoadedAmmoList.AmmoClips = grown;
            FitRackAllotments(loadout, grown.Length);
            return true;
        }

        /// <summary>
        /// SpawnCurrentLoadout reads a rack's fixed allotment as one entry per ammo type, so an authored row
        /// that is shorter than the list now is out of bounds. Empty rows are the auto allotment and stay as
        /// they are; a short one is dropped back to it, since the only count it could pin is one that no
        /// longer adds up.
        /// </summary>
        private static void FitRackAllotments(LoadoutManager loadout, int ammoTypes)
        {
            if (loadout.RackLoadouts == null) return;
            foreach (LoadoutManager.RackLoadout rack in loadout.RackLoadouts)
            {
                if (rack == null || rack.AmmoCounts == null || rack.AmmoCounts.Length == 0) continue;
                if (rack.AmmoCounts.Length >= ammoTypes) continue;
                MelonLogger.Warning($"[L2A7V] the fixed allotment on rack {rack.Name} is shorter than the " +
                                    "round list, so that rack falls back to its automatic share");
                rack.AmmoCounts = Array.Empty<int>();
            }
        }

        /// <summary>
        /// The imported round, built the first time a vehicle carrying it is spawned and shared from then on.
        /// </summary>
        private static AmmoClipCodexScriptable EnsureClip(AmmoClipCodexScriptable donorApClip)
        {
            if (_failed || donorApClip == null) return null;
            if (_clip != null)
            {
                EnsureVisual(donorApClip);
                return _clip;
            }
            if (!Build(donorApClip)) _failed = true;
            return _clip;
        }

        /// <summary>
        /// Attaches the in-rack shell model if the round was built before that prefab had been loaded, which
        /// is what happens on a later spawn in the same session. The donor clip the new vehicle carries is
        /// the source of the visual, so it has to come onto the shared round's own shell object.
        /// </summary>
        private static void EnsureVisual(AmmoClipCodexScriptable donorApClip)
        {
            if (_shell == null || _shell.VisualModel != null) return;
            AmmoType source = donorApClip.ClipType == null || donorApClip.ClipType.MinimalPattern == null ||
                              donorApClip.ClipType.MinimalPattern.Length == 0
                ? null : donorApClip.ClipType.MinimalPattern[0].AmmoType;
            if (source == null || source.VisualModel == null) return;

            AttachVisual(source);
            MelonLogger.Msg("[L2A7V] the imported round's rack visual reached the game after the round was " +
                            "built, so it is now attached to the shared round");
        }

        private static void AttachVisual(AmmoType source)
        {
            // A rack finds the shell it means to remove by the AmmoType on the visual, so the imported round
            // needs a visual that names it or every reload would leave the donor's shell behind.
            GameObject visual = UnityEngine.Object.Instantiate(source.VisualModel);
            visual.name = ShellName + " visual";
            visual.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(visual);
            AmmoStoredVisual stored = visual.GetComponent<AmmoStoredVisual>();
            if (stored != null)
            {
                stored.AmmoScriptable = _codex;
                stored.AmmoType = _shell;
            }
            _shell.VisualModel = visual;
        }

        public static int FindSabotClip(AmmoClipCodexScriptable[] clips)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                AmmoClipCodexScriptable clip = clips[i];
                if (clip == null || clip.ClipType == null) continue;
                AmmoCodexScriptable[] pattern = clip.ClipType.MinimalPattern;
                if (pattern == null || pattern.Length == 0 || pattern[0] == null) continue;
                AmmoType ammo = pattern[0].AmmoType;
                if (ammo != null && ammo.Category == AmmoType.AmmoCategory.Penetrator &&
                    ammo.ShortName == AmmoType.AmmoShortName.Sabot)
                    return i;
            }
            return -1;
        }

        private static bool Build(AmmoClipCodexScriptable donorClip)
        {
            AmmoType source = donorClip.ClipType.MinimalPattern[0].AmmoType;
            if (source == null) return false;

            // AmmoType is a plain serialized class, so a field-for-field copy carries the donor's impact
            // effects, sounds, decals and flight shape over with it, and only what identifies the round and
            // its ballistics has to change.
            var shell = new AmmoType();
            foreach (FieldInfo field in typeof(AmmoType).GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!field.IsLiteral && !field.IsInitOnly) field.SetValue(shell, field.GetValue(source));
            }

            shell.Name = ShellName;
            shell.RhaPenetration = PenetrationMm;
            shell.MuzzleVelocity = MuzzleVelocity;
            shell.Mass = ProjectileMassKg;
            // The impact-effect and decal databases key their tables by this index, and the copy has not been
            // cached against anything yet - the donor's index would point at the donor's row.
            shell.CachedIndex = -1;

            var codex = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
            codex.name = "L2A7V_" + ShellName;
            // The customizer builds these in the menu scene and the vehicle uses them in the mission, with an
            // asset unload in between, and a managed static is not what keeps a Unity asset alive.
            codex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            codex.AmmoType = shell;

            var clipType = new AmmoType.AmmoClip
            {
                Name = ShellName,
                Capacity = 1,
                MinimalPattern = new[] { codex }
            };
            var clip = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
            clip.name = "L2A7V_" + ShellName + "_clip";
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            clip.ClipType = clipType;
            clip.CompatibleWeaponSystems = donorClip.CompatibleWeaponSystems;

            _shell = shell;
            _codex = codex;
            _clip = clip;

            if (source.VisualModel != null) AttachVisual(source);

            MelonLogger.Msg($"[L2A7V] {ShellName} built from the donor's {source.Name}: " +
                            $"{PenetrationMm:F0} mm, {MuzzleVelocity:F0} m/s, {ProjectileMassKg:F1} kg" +
                            (shell.VisualModel == null ? ", no rack visual available yet" : string.Empty));
            return true;
        }

        private static void RenameWeapon(WeaponSystem weapon)
        {
            if (WeaponCodexField == null) return;
            var source = WeaponCodexField.GetValue(weapon) as WeaponSystemCodexScriptable;
            if (source == null) return;

            if (_gunCodex == null)
            {
                // Never write into the donor's asset: it is the one every vanilla LEO2A3 in the same
                // mission is named from.
                _gunCodex = ScriptableObject.CreateInstance<WeaponSystemCodexScriptable>();
                _gunCodex.name = "L2A7V_" + GunName;
                _gunCodex.FriendlyName = GunName;
                _gunCodex.CaliberMm = source.CaliberMm;
                _gunCodex.Type = source.Type;
            }
            if (!ReferenceEquals(WeaponCodexField.GetValue(weapon), _gunCodex))
                WeaponCodexField.SetValue(weapon, _gunCodex);
        }

        /// <summary>
        /// Writes the 2A7V's gun arc onto the platform that poses the mantlet, on every spawn.
        ///
        /// GHPC's only mechanical clamp for a modern turret is AimablePlatform.enforceEulerLimits, and it
        /// returns without clamping when LocalEulerLimits.x equals .y - a pair of zeros included - so an
        /// elevation platform that arrives without its authored pair lets the barrel travel unbounded. The
        /// two figures come from LpConfig, which states War Thunder's arc for this vehicle (-9 / +20 on the
        /// 120 mm Rh120 L/55 A1) rather than inheriting the donor LEO2A3 mount's -10 / +20. Writing them
        /// explicitly is also what makes the limit auditable: the line below reads the donor's values back out.
        /// </summary>
        public static void ApplyGunArc(GameObject root)
        {
            if (!LpConfig.Enabled.Value || root == null || !LpConfig.LimitMainGunElevation.Value) return;

            float elevation = Mathf.Abs(LpConfig.GunElevationDegrees.Value);
            float depression = -Mathf.Abs(LpConfig.GunDepressionDegrees.Value);

            GHPC.AimablePlatform gun = FindMainGunPlatform(root);
            if (gun == null)
            {
                MelonLogger.Warning("[L2A7V] this unit has no gun elevation platform, so its arc is " +
                                    "whatever the donor shipped");
                return;
            }

            Vector2 before = gun.LocalEulerLimits;
            gun.LocalEulerLimits = new Vector2(depression, elevation);
            int optics = AlignOptics(root, elevation, depression);
            MelonLogger.Msg($"[L2A7V] gun arc on '{gun.Name}' at {gun.transform.parent.name}/" +
                            $"{gun.transform.name}: donor {before.x:F1}..{before.y:F1} -> " +
                            $"{depression:F1}..{elevation:F1} deg (axis {gun.Axis}, " +
                            $"reversed {gun.ReverseLocalEuler}), {optics} sight tube(s) tightened to it");

            LpGunArcWatch watch = root.GetComponent<LpGunArcWatch>();
            if (watch == null) watch = root.AddComponent<LpGunArcWatch>();
            watch.Init(gun);
        }

        /// <summary>
        /// Pulls the rotating sight tubes in to the gun's arc. A tube is a separate limit
        /// (UsableOptic.LocalElevationLimits, same convention: x is depression, y is elevation), and the
        /// donor's GPS is authored wider than the gun, so the tube the player sees on the turret face and
        /// the view through it keep going past the point where the barrel stops. Only ever narrowed, and
        /// optics that do not turn in elevation are left alone.
        /// </summary>
        private static int AlignOptics(GameObject root, float elevation, float depression)
        {
            int tightened = 0;
            foreach (GHPC.Equipment.Optics.UsableOptic optic in
                     root.GetComponentsInChildren<GHPC.Equipment.Optics.UsableOptic>(true))
            {
                if (LpVisual.IsImportedArt(optic.transform) || !optic.RotateElevation) continue;
                Vector2 own = optic.LocalElevationLimits;
                if (own.x == 0f && own.y == 0f) continue;
                float tubeElevation = own.y != 0f ? own.y : 180f;
                float tubeDepression = own.x != 0f ? -own.x : 180f;
                float nextElevation = Mathf.Min(tubeElevation, elevation);
                float nextDepression = Mathf.Min(tubeDepression, -depression);
                if (Mathf.Approximately(nextElevation, tubeElevation) &&
                    Mathf.Approximately(nextDepression, tubeDepression)) continue;

                optic.LocalElevationLimits = new Vector2(-nextDepression, nextElevation);
                MelonLogger.Msg($"[L2A7V] sight tube '{optic.name}' ({optic.transform.parent.name}/" +
                                $"{optic.transform.name}): {own.x:F1}..{own.y:F1} -> " +
                                $"{-nextDepression:F1}..{nextElevation:F1} deg");
                tightened++;
            }
            return tightened;
        }

        /// <summary>
        /// The platform that turns the gun, found by what it is called rather than by position in the list:
        /// a unit has more than one platform, and grabbing the wrong one would clamp the turret's traverse to
        /// the gun's arc. Our own anti-aircraft gun brings its own limits, so imported art is excluded.
        /// </summary>
        private static GHPC.AimablePlatform FindMainGunPlatform(GameObject root)
        {
            foreach (GHPC.AimablePlatform platform in root.GetComponentsInChildren<GHPC.AimablePlatform>(true))
            {
                if (LpVisual.IsImportedArt(platform.transform)) continue;
                string label = (platform.Name + " " + platform.transform.name).ToLowerInvariant();
                if (label.Contains("main gun") || label.Contains("gun elevation") || label.Contains("rh-120"))
                    return platform;
            }
            return null;
        }
    }

    /// <summary>
    /// Reports where the barrel actually is, in three frames of reference at once.
    ///
    /// The prefab says the elevation platform poses the same bone the imported gun hangs on and that the
    /// platform is not a ball turret, so enforceEulerLimits should already be bending the art the player
    /// sees. If a game still shows more travel than the arc, these three numbers separate the two remaining
    /// causes: a clamp that is being bypassed (the angle the platform itself reports leaves its limits) and
    /// hull pitch stacking on top of a turret-relative arc, which is how the real vehicle and War Thunder
    /// both behave. Only a violation is logged, so a normal drive leaves nothing behind.
    /// </summary>
    public class LpGunArcWatch : MonoBehaviour
    {
        private const float IntervalSeconds = 0.5f;
        private const float ToleranceDegrees = 0.5f;

        private GHPC.AimablePlatform _platform;
        private float _nextSample;
        private float _elevationLimit;
        private float _depressionLimit;
        private bool _restReported;

        public void Init(GHPC.AimablePlatform platform)
        {
            _platform = platform;
            _elevationLimit = Mathf.Abs(LpConfig.GunElevationDegrees.Value);
            _depressionLimit = -Mathf.Abs(LpConfig.GunDepressionDegrees.Value);
        }

        private void Update()
        {
            if (_platform == null || _platform.Transform == null) return;
            if (Time.time < _nextSample) return;
            _nextSample = Time.time + IntervalSeconds;

            float reported = _platform.GetCurrentEulerAngle();
            float gunWorld = WorldElevation(_platform.Transform.forward);
            float hullWorld = WorldElevation(transform.forward);
            float relative = gunWorld - hullWorld;

            if (!_restReported)
            {
                _restReported = true;
                MelonLogger.Msg($"[L2A7V] gun arc baseline on '{_platform.Name}': platform reports " +
                                $"{reported:F1} deg while the bone the barrel rides sits {relative:F1} deg off " +
                                $"the hull (bone {gunWorld:F1}, hull {hullWorld:F1}) - if those two disagree, " +
                                $"the arc is measured from a different zero than the art's rest pose");
            }

            bool platformEscaped = reported > _elevationLimit + ToleranceDegrees ||
                                   reported < _depressionLimit - ToleranceDegrees;
            bool artEscaped = relative > _elevationLimit + ToleranceDegrees ||
                              relative < _depressionLimit - ToleranceDegrees;
            if (!platformEscaped && !artEscaped) return;

            MelonLogger.Warning($"[L2A7V] gun arc check on '{_platform.Name}': platform reports " +
                                $"{reported:F1} deg, barrel {_elevationLimit:F0}/{_depressionLimit:F0} " +
                                $"| barrel {gunWorld:F1} deg above horizon, hull {hullWorld:F1}, " +
                                $"so turret-relative {relative:F1} | clamp bypassed: {platformEscaped}, " +
                                $"art outside arc: {artEscaped}");
        }

        private static float WorldElevation(Vector3 forward)
        {
            return Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }
}

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
    /// ballistic computer's firing table). The gun is re-authored in place and the donor's APFSDS row is
    /// answered by DM53 at the same position, so the vehicle has no DM23 left to load, select or count: the
    /// round list keeps the donor's length, its HEAT row stays behind the sabot as key 2, and the racks keep
    /// the slot counts they were authored with.
    /// The feed, both racks, the reload
    /// stages and the recoil impulse stay the donor's, so the gun still reloads at its authored 6 seconds,
    /// which is also what an aced load in War Thunder takes.
    /// </summary>
    public static class LpWeapons
    {
        private const string GunName = "Rh-120 L/55";
        private const string WeaponEntryName = "Rh-120 L/55 120 mm";
        private const string ShellName = "DM53 APFSDS-T";

        // DM53 from the L/55: 800 mm of penetration, the figure the round is credited with, plus its real
        // 1750 m/s muzzle velocity and 5 kg projectile. RhaPenetration is a muzzle rating here - the game
        // applies the round's own speed decay on top of it (CurrentPenRating scales by currentSpeed^2 /
        // maxSpeed^2), so this reads about 665 mm at 2 km under the donor's drag. The stock 120 mm rounds
        // are authored the same way: DM23 is 520 here against a real ~470 mm at 2 km. Velocity, mass and
        // drag feed the ballistic computer's firing table, so they matter as much as the penetration does.
        private const float PenetrationMm = 800f;
        private const float MuzzleVelocity = 1750f;
        private const float ProjectileMassKg = 5f;

        private static readonly FieldInfo WeaponCodexField =
            AccessTools.Field(typeof(WeaponSystem), "CodexEntry");
        private static readonly MethodInfo RegisterBallistics =
            AccessTools.Method(typeof(LoadoutManager), "RegisterAllBallistics");
        // VehicleInfo is marked obsolete in this build but is still the component that owns the rack
        // refill, and it is sometimes added at runtime rather than carried on the prefab. It is reached by
        // name so the compile does not depend on a deprecated type, and its snapshot field is rewritten
        // from the loadout right before every refill.
        private static readonly Type VehicleInfoType = AccessTools.TypeByName("VehicleInfo");
        private static readonly FieldInfo AmmoCountsOriginalField = VehicleInfoType == null
            ? null : AccessTools.Field(VehicleInfoType, "_ammoCountsOriginal");
        private static readonly MethodInfo FillAmmoRacks = VehicleInfoType == null
            ? null : AccessTools.Method(VehicleInfoType, "fillAmmoRacks");

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

            if (FillAmmoRacks == null)
            {
                MelonLogger.Warning("[L2A7V] could not hook the vehicle's rack refill, so a refill will " +
                                    "reload the donor's two rounds and no DM53");
                return;
            }
            harmony.Patch(FillAmmoRacks, prefix: new HarmonyMethod(typeof(LpWeapons), nameof(RefreshAmmoSnapshot)));
        }

        /// <summary>
        /// Puts the loadout's counts in place of the donor snapshot the refill reads.
        ///
        /// VehicleInfo copies MainGun.AmmoCounts into _ammoCountsOriginal in its own Awake and fills the
        /// racks from that copy in Start and whenever the player asks for a refill, so a snapshot left at
        /// the donor's two entries would repopulate the vehicle with DM23 and DM12 only, however the
        /// loadout list reads. The loadout's own list is the stable statement of what the vehicle carries -
        /// the live gun counts shrink as rounds are fired - so that is what the snapshot is rebuilt from, and
        /// only for a unit whose list holds our clip.
        /// </summary>
        private static void RefreshAmmoSnapshot(object __instance)
        {
            if (__instance == null || AmmoCountsOriginalField == null) return;
            var component = __instance as Component;
            if (component == null) return;

            var loadout = component.GetComponent<LoadoutManager>();
            if (loadout == null) return;
            AmmoClipCodexScriptable[] clips = loadout.LoadedAmmoList.AmmoClips;
            int[] counts = loadout.TotalAmmoCounts;
            if (clips == null || counts == null || clips.Length == 0 || clips.Length != counts.Length) return;
            if (IndexOfClip(clips, _clip) < 0) return;

            AmmoCountsOriginalField.SetValue(__instance, (int[])counts.Clone());
        }

        private static int IndexOfClip(AmmoClipCodexScriptable[] clips, AmmoClipCodexScriptable wanted)
        {
            if (clips == null || wanted == null) return -1;
            for (int i = 0; i < clips.Length; i++)
                if (ReferenceEquals(clips[i], wanted)) return i;
            return -1;
        }

        private static void VisualSpawned(AmmoType type, GameObject __result)
        {
            if (__result != null && _shell != null && type == _shell && !__result.activeSelf)
                __result.SetActive(true);
        }

        /// <summary>
        /// Re-authors this unit's gun and round. Returns whether the unit is finished with: the donor's MainGun
        /// arrives with its art pass, a couple of seconds after the spawn, so a unit that has no gun yet has to
        /// be handed back to LpDonorArtWatch.
        /// </summary>
        public static bool Apply(GameObject root)
        {
            if (root == null || _failed) return true;

            var weapons = root.GetComponent<WeaponsManager>();
            var loadout = root.GetComponent<LoadoutManager>();
            if (weapons == null || loadout == null || weapons.Weapons == null || weapons.Weapons.Length == 0)
                return false;

            int index = loadout.WeaponIndex;
            if (index < 0 || index >= weapons.Weapons.Length) return false;
            WeaponSystemInfo entry = weapons.Weapons[index];
            if (entry == null || entry.Weapon == null) return false;

            AmmoClipCodexScriptable[] clips = loadout.LoadedAmmoList.AmmoClips;
            if (clips == null || clips.Length == 0) return false;

            int sabot = FindSabotClip(clips);
            if (sabot < 0)
            {
                MelonLogger.Warning("[L2A7V] the donor loadout holds no APFSDS clip, keeping its ammunition");
                return true;
            }

            if (EnsureClip(clips[sabot]) == null) return true;

            int firstCount = SubstituteSabot(loadout, clips, sabot);
            entry.Name = WeaponEntryName;
            RenameWeapon(entry.Weapon);
            bool gunSynced = SyncMainGun(root, firstCount);

            // The list was registered against the donor's clips in Awake, and Start reads the ballistics
            // out of this repository, so the new round has to be in it before either of them runs.
            if (RegisterBallistics != null) RegisterBallistics.Invoke(loadout, null);

            if (gunSynced)
            {
                int dm23 = CountDonorSabot(clips, loadout.TotalAmmoCounts);
                MelonLogger.Msg($"[L2A7V] main gun set to {WeaponEntryName}: the donor's APFSDS row " +
                                $"(was DM23) now carries {ShellName} ({PenetrationMm:F0} mm, " +
                                $"{MuzzleVelocity:F0} m/s) at key {sabot + 1}; " +
                                $"{clips.Length} round type(s) in the loadout, {dm23} DM23 round(s) left" +
                                (dm23 == 0 ? string.Empty : " - see the rows above"));
            }
            return gunSynced;
        }

        /// <summary>
        /// Retry entry for the deferred pass: the gun's own round list, which is the one thing the spawn
        /// cannot write because MainGun arrives with the donor's art pass. The loadout substitution has
        /// already run by then, so the count is not passed - the row keeps the number the donor authored for
        /// it, which is what the loadout carries for that row too.
        /// </summary>
        public static bool ApplyGunList(GameObject root)
        {
            if (root == null || _failed) return true;
            return SyncMainGun(root, -1);
        }

        /// <summary>
        /// Counts the rounds still carried under the donor's APFSDS name, so the log says in one number
        /// whether DM23 really left the vehicle.
        /// </summary>
        private static int CountDonorSabot(AmmoClipCodexScriptable[] clips, int[] counts)
        {
            int total = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                AmmoClipCodexScriptable clip = clips[i];
                string name = clip == null || clip.ClipType == null ? null : clip.ClipType.Name;
                if (name == null || !name.Contains("DM23")) continue;
                total += counts != null && counts.Length > i ? counts[i] : 0;
            }
            return total;
        }

        /// <summary>
        /// Answers the donor's APFSDS row with the imported round at the same position.
        ///
        /// Substituting rather than inserting is what keeps DM23 out of the vehicle entirely: the row is the
        /// one the donor's racks, reload logic and ballistics registry were authored against, so its slot
        /// count, its index in every rack's fixed choice and its key on the player's number row all stay
        /// valid without any of them being rewritten. The count travels with the row, so the sabot share of
        /// the loadout (21 of 42 on this donor) is the number of DM53 the vehicle carries.
        /// </summary>
        private static int SubstituteSabot(LoadoutManager loadout, AmmoClipCodexScriptable[] clips, int sabot)
        {
            if (ReferenceEquals(clips[sabot], _clip))
                return -1;

            int[] counts = loadout.TotalAmmoCounts;
            int held = counts != null && counts.Length > sabot ? counts[sabot] : 0;
            clips[sabot] = _clip;
            return held;
        }

        /// <summary>
        /// Keeps the gun's own round list in step with the loadout's. MainGun builds its ammo from a
        /// serialized array on the prefab and chambers from the index the player picks, so a round that only
        /// exists in the loadout can be carried in the racks but never fired. The swap is done in place, on
        /// the gun's APFSDS row: the list keeps its length, every other row keeps its key, and DM23 stops
        /// being selectable because nothing in the vehicle names it any more.
        ///
        /// The separate snapshot VehicleInfo fills the racks from is handled by RefreshAmmoSnapshot, which
        /// runs in front of that fill; doing it here too would only duplicate it for the spawn.
        /// </summary>
        private static bool SyncMainGun(GameObject root, int firstCount)
        {
            if (_codex == null || _shell == null || root == null) return false;
            MainGun gun = root.GetComponent<MainGun>();
            if (gun == null) return false;

            AmmoCodexScriptable[] ammo = gun.Ammo ?? Array.Empty<AmmoCodexScriptable>();
            if (ammo.Length == 0)
            {
                MelonLogger.Warning("[L2A7V] this unit's MainGun carries no round list, so the imported round " +
                                    "is in the loadout only and cannot be selected");
                return true;
            }

            int slot = FindSabotSlot(ammo);
            if (slot < 0)
            {
                MelonLogger.Warning("[L2A7V] the donor gun lists no APFSDS row, so " + ShellName +
                                    " stays in the loadout only and cannot be selected");
                return true;
            }
            if (ReferenceEquals(ammo[slot], _codex)) return true;

            ammo[slot] = _codex;
            gun.Ammo = ammo;

            var available = new AmmoType[ammo.Length];
            for (int i = 0; i < ammo.Length; i++)
                available[i] = ammo[i] == null ? null : ammo[i].AmmoType;
            gun.AvailableAmmo = available;

            int[] counts = gun.AmmoCounts;
            bool wroteCount = firstCount >= 0 && counts != null && counts.Length == ammo.Length;
            if (wroteCount) counts[slot] = firstCount;

            MelonLogger.Msg($"[L2A7V] gun round list: the donor's APFSDS row {slot + 1} of " +
                            $"{ammo.Length} is now {ShellName}" +
                            (wroteCount ? $" ({firstCount} rounds)" : string.Empty));
            return true;
        }

        /// <summary>
        /// The gun's APFSDS row, by what the round is rather than by position: this donor fires DM23 first
        /// and DM12 second, but a rebuilt prefab or another nation's vehicle would not have to.
        /// </summary>
        private static int FindSabotSlot(AmmoCodexScriptable[] ammo)
        {
            for (int i = 0; i < ammo.Length; i++)
            {
                if (ReferenceEquals(ammo[i], _codex)) return i;
                AmmoType type = ammo[i] == null ? null : ammo[i].AmmoType;
                if (type != null && type.Category == AmmoType.AmmoCategory.Penetrator &&
                    type.ShortName == AmmoType.AmmoShortName.Sabot)
                    return i;
            }
            return -1;
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
            if (root == null || !LpConfig.LimitMainGunElevation) return;

            float elevation = Mathf.Abs(LpConfig.GunElevationDegrees);
            float depression = -Mathf.Abs(LpConfig.GunDepressionDegrees);

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
            _elevationLimit = Mathf.Abs(LpConfig.GunElevationDegrees);
            _depressionLimit = -Mathf.Abs(LpConfig.GunDepressionDegrees);
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

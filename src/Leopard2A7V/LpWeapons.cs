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
        // applies the round's own speed decay on top of it, so the figure falls off with range. Velocity,
        // mass and drag feed the ballistic computer's firing table, so they matter as much as penetration.
        private const float PenetrationMm = 800f;
        private const float MuzzleVelocity = 1750f;
        private const float ProjectileMassKg = 5f;

        /// <summary>
        /// DM73: the current German long-rod for the L/55, the same 5 kg-class penetrator as DM63 with the
        /// newer propellant charge. Public figures credit it with about 800 mm of rolled armour at a muzzle
        /// velocity around 1780 m/s. It is carried alongside DM53 rather than instead of it.
        /// </summary>
        private const string Dm73Name = "DM73 APFSDS-T";
        private const float Dm73PenetrationMm = 800f;
        private const float Dm73MuzzleVelocity = 1780f;
        private const float Dm73MassKg = 5f;

        /// <summary>
        /// DM11: the programmable HE round for the L/55. About 870 m/s and a 3 kg-class HE filling; against
        /// armour it is nearly useless, and its job is soft targets, structures and airburst.
        /// </summary>
        private const string Dm11Name = "DM11 HE-FRAG";
        private const float Dm11PenetrationMm = 20f;
        private const float Dm11MuzzleVelocity = 870f;
        private const float Dm11MassKg = 4f;

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
        private static AmmoType _dm73Shell;
        private static AmmoCodexScriptable _dm73Codex;
        private static AmmoClipCodexScriptable _dm73Clip;
        private static AmmoType _dm11Shell;
        private static AmmoCodexScriptable _dm11Codex;
        private static AmmoClipCodexScriptable _dm11Clip;
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
            // DM11 is built but not carried yet. At spawn the vehicle has no MainGun component - it arrives
            // with the donor's art pass a second later - so the round list can only grow on the loadout, and
            // a loadout with three types against a gun with two is what made the tank explode on spawn.
            // AppendDm11(root);
            // One long-rod at a time, chosen in the config: the DM53 row carries either round and the HEAT
            // row is left alone. Two clips is what the racks and the refill snapshot are authored for.

            // The list was registered against the donor's clips in Awake, and Start reads the ballistics
            // out of this repository, so the new round has to be in it before either of them runs.
            if (RegisterBallistics != null) RegisterBallistics.Invoke(loadout, null);

            if (gunSynced)
            {
                int dm23 = CountDonorSabot(clips, loadout.TotalAmmoCounts);
                bool isDm73 = LpConfig.CarriesDm73;
                MelonLogger.Msg($"[L2A7V] main gun set to {WeaponEntryName}: the donor's APFSDS row " +
                                $"(was DM23) now carries {(isDm73 ? Dm73Name : ShellName)} " +
                                $"({(isDm73 ? Dm73PenetrationMm : PenetrationMm):F0} mm, " +
                                $"{(isDm73 ? Dm73MuzzleVelocity : MuzzleVelocity):F0} m/s) at key {sabot + 1}; " +
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
        /// Carries DM73 alongside DM53 instead of in place of it.
        ///
        /// The loadout and every rack were authored against two round types, so a third one is a new slot at
        /// the end of all three arrays: the clip list, the per-type counts, and each rack's own count array.
        /// Existing indices keep their meaning, which is what lets the donor's racks, keys and reload logic go
        /// on working untouched.
        /// </summary>
        private static void AppendDm73(GameObject root)
        {
            if (_clip == null || _dm73Clip != null) return;

            AmmoClipCodexScriptable donorClip = null;
            AmmoClipCodexScriptable[] clips = null;
            LoadoutManager loadout = root.GetComponent<LoadoutManager>();
            if (loadout == null) return;
            clips = loadout.LoadedAmmoList != null ? loadout.LoadedAmmoList.AmmoClips : loadout.LoadedAmmoTypes;
            if (clips == null) return;
            foreach (AmmoClipCodexScriptable clip in clips)
            {
                if (clip != null && clip.ClipType != null && clip.ClipType.MinimalPattern != null &&
                    clip.ClipType.MinimalPattern.Length > 0)
                {
                    donorClip = clip;
                    break;
                }
            }
            if (donorClip == null) return;

            if (!Build(donorClip, Dm73Name, Dm73PenetrationMm, Dm73MuzzleVelocity, Dm73MassKg, 1))
            {
                MelonLogger.Warning("[L2A7V] DM73 could not be built, so the vehicle carries the DM53 row only");
                return;
            }

            int[] counts = loadout.TotalAmmoCounts;
            int take = counts != null && counts.Length > 0 ? Mathf.Min(6, Mathf.Max(0, counts[0])) : 6;

            var newClips = new AmmoClipCodexScriptable[clips.Length + 1];
            clips.CopyTo(newClips, 0);
            newClips[clips.Length] = _dm73Clip;

            var newCounts = new int[newClips.Length];
            if (counts != null) counts.CopyTo(newCounts, 0);
            newCounts[newClips.Length - 1] = take;
            if (counts != null && counts.Length > 0) newCounts[0] = Mathf.Max(0, counts[0] - take);

            if (loadout.LoadedAmmoList != null) loadout.LoadedAmmoList.AmmoClips = newClips;
            loadout.LoadedAmmoTypes = newClips;
            loadout.TotalAmmoCounts = newCounts;

            int racks = 0, carried = 0;
            foreach (LoadoutManager.RackLoadout rack in loadout.RackLoadouts ?? Array.Empty<LoadoutManager.RackLoadout>())
            {
                if (rack == null || rack.AmmoCounts == null) continue;
                var rackCounts = new int[newClips.Length];
                Array.Copy(rack.AmmoCounts, rackCounts, Mathf.Min(rack.AmmoCounts.Length, newClips.Length));

                // The rack's own array indexes ammo types exactly like the loadout's, so the new slot starts
                // at zero and the rack would never carry a DM73 at all. Taking the rounds out of this rack's
                // DM53 slot is what actually puts them in the vehicle.
                if (rackCounts.Length > 0 && rackCounts[0] > 0)
                {
                    int moved = Mathf.Min(rackCounts[0], 2);
                    rackCounts[0] -= moved;
                    rackCounts[newClips.Length - 1] += moved;
                    carried += moved;
                }
                rack.AmmoCounts = rackCounts;
                racks++;
            }

            MainGun gun = root.GetComponent<MainGun>();
            if (gun != null && gun.Ammo != null && gun.Ammo.Length > 0)
            {
                var ammo = new AmmoCodexScriptable[gun.Ammo.Length + 1];
                gun.Ammo.CopyTo(ammo, 0);
                ammo[ammo.Length - 1] = _dm73Codex;
                gun.Ammo = ammo;

                var available = new AmmoType[ammo.Length];
                for (int i = 0; i < ammo.Length; i++) available[i] = ammo[i] == null ? null : ammo[i].AmmoType;
                gun.AvailableAmmo = available;

                var gunCounts = new int[ammo.Length];
                if (gun.AmmoCounts != null)
                    Array.Copy(gun.AmmoCounts, gunCounts, Mathf.Min(gun.AmmoCounts.Length, ammo.Length));
                gunCounts[ammo.Length - 1] = take;
                if (gunCounts.Length > 1 && gunCounts[0] >= take) gunCounts[0] -= take;
                gun.AmmoCounts = gunCounts;
            }

            MelonLogger.Msg($"[L2A7V] {Dm73Name} added beside {ShellName}: {take} round(s) taken from the " +
                            $"DM53 row, {carried} placed in {racks} rack(s), each extended to {newClips.Length} type(s)");
        }

        /// <summary>
        /// Puts DM73 in the row the donor uses for its HEAT round, so both long-rods are selectable.
        ///
        /// The loadout, the racks and the vehicle's refill snapshot are all authored for exactly two clips.
        /// Adding a third was tried and it left DM73 and HEAT both empty, so the second row is replaced
        /// instead: the DM53 row keeps its rounds, this one carries DM73, and nothing about the arrays or
        /// their indices changes.
        /// </summary>
        private static void SubstituteDm73ForHeat(GameObject root)
        {
            if (_clip == null) return;

            LoadoutManager loadout = root.GetComponent<LoadoutManager>();
            if (loadout == null) return;

            AmmoClipCodexScriptable[] clips = loadout.LoadedAmmoList != null
                ? loadout.LoadedAmmoList.AmmoClips
                : loadout.LoadedAmmoTypes;
            if (clips == null) return;

            if (_dm73Clip == null)
            {
                AmmoClipCodexScriptable donor = null;
                foreach (AmmoClipCodexScriptable candidate in clips)
                {
                    if (candidate == null || candidate.ClipType == null) continue;
                    AmmoCodexScriptable[] pattern = candidate.ClipType.MinimalPattern;
                    if (pattern == null || pattern.Length == 0 || pattern[0] == null) continue;
                    donor = candidate;
                    break;
                }
                if (donor == null) return;
                if (!Build(donor, Dm73Name, Dm73PenetrationMm, Dm73MuzzleVelocity, Dm73MassKg, 1))
                {
                    MelonLogger.Warning("[L2A7V] DM73 could not be built; the vehicle keeps the donor's second round");
                    return;
                }
            }

            int heat = FindHeatRow(clips);
            if (heat < 0)
            {
                MelonLogger.Warning("[L2A7V] the donor loadout holds no HEAT row to carry DM73, so only DM53 is carried");
                return;
            }
            if (ReferenceEquals(clips[heat], _dm73Clip)) return;

            int[] counts = loadout.TotalAmmoCounts;
            int held = counts != null && counts.Length > heat ? counts[heat] : 0;
            clips[heat] = _dm73Clip;
            if (counts != null && counts.Length > heat) counts[heat] = held;

            MainGun gun = root.GetComponent<MainGun>();
            if (gun != null && gun.Ammo != null)
            {
                AmmoCodexScriptable[] ammo = gun.Ammo;
                for (int i = 0; i < ammo.Length; i++)
                {
                    AmmoType type = ammo[i] == null ? null : ammo[i].AmmoType;
                    if (type == null || type.ShortName != AmmoType.AmmoShortName.Heat) continue;
                    ammo[i] = _dm73Codex;
                    gun.Ammo = ammo;
                    var available = new AmmoType[ammo.Length];
                    for (int j = 0; j < ammo.Length; j++)
                        available[j] = ammo[j] == null ? null : ammo[j].AmmoType;
                    gun.AvailableAmmo = available;
                    break;
                }
            }

            MelonLogger.Msg($"[L2A7V] the donor's HEAT row now carries {Dm73Name} ({Dm73PenetrationMm:F0} mm, " +
                            $"{Dm73MuzzleVelocity:F0} m/s) beside {ShellName}: {held} round(s), row {heat + 1} of " +
                            $"{clips.Length}");
        }

        /// <summary>The donor's HEAT row, by what the round is rather than by position.</summary>
        private static int FindHeatRow(AmmoClipCodexScriptable[] clips)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                AmmoClipCodexScriptable clip = clips[i];
                if (clip == null || clip.ClipType == null) continue;
                AmmoCodexScriptable[] pattern = clip.ClipType.MinimalPattern;
                if (pattern == null || pattern.Length == 0 || pattern[0] == null) continue;
                AmmoType type = pattern[0].AmmoType;
                if (type != null && type.ShortName == AmmoType.AmmoShortName.Heat) return i;
            }
            return -1;
        }

        /// <summary>
        /// Carries DM11 as a third round, in the HE row the donor never had.
        ///
        /// The loadout, the gun list and the refill snapshot are all arrays parallel to the round types, so a
        /// third type means one more slot at the end of each. The racks take their fill from the totals and
        /// carry no per-type table of their own (both rack counts are empty in the donor), which is what makes
        /// an appended type workable at all. Everything is done behind guards: if a length disagrees, the
        /// vehicle keeps two rounds rather than ending up with none, which is what happened the first time
        /// this was tried without them.
        /// </summary>
        private static void AppendDm11(GameObject root)
        {
            if (_clip == null) return;
            LoadoutManager loadout = root.GetComponent<LoadoutManager>();
            if (loadout == null) return;

            AmmoClipCodexScriptable[] clips = loadout.LoadedAmmoList != null
                ? loadout.LoadedAmmoList.AmmoClips
                : loadout.LoadedAmmoTypes;
            int[] counts = loadout.TotalAmmoCounts;
            if (clips == null || counts == null || clips.Length != counts.Length)
            {
                MelonLogger.Warning("[L2A7V] loadout arrays do not line up, so DM11 is not carried");
                return;
            }
            foreach (AmmoClipCodexScriptable existing in clips)
                if (ReferenceEquals(existing, _dm11Clip)) return;

            int heat = FindHeatRow(clips);
            if (heat < 0)
            {
                MelonLogger.Warning("[L2A7V] no HEAT row to copy for DM11, so it is not carried");
                return;
            }

            if (_dm11Clip == null && !Build(clips[heat], Dm11Name, Dm11PenetrationMm, Dm11MuzzleVelocity,
                                            Dm11MassKg, 2, AmmoType.AmmoCategory.Explosive,
                                            AmmoType.AmmoShortName.He))
            {
                MelonLogger.Warning("[L2A7V] DM11 could not be built, so the vehicle keeps two rounds");
                return;
            }

            int take = Mathf.Min(12, Mathf.Max(0, counts[heat]));
            if (take <= 0)
            {
                MelonLogger.Warning("[L2A7V] the HEAT row is empty, so DM11 has no rounds to take");
                return;
            }

            var newClips = new AmmoClipCodexScriptable[clips.Length + 1];
            clips.CopyTo(newClips, 0);
            newClips[clips.Length] = _dm11Clip;
            var newCounts = new int[counts.Length + 1];
            counts.CopyTo(newCounts, 0);
            newCounts[clips.Length] = take;
            newCounts[heat] = counts[heat] - take;

            if (loadout.LoadedAmmoList != null) loadout.LoadedAmmoList.AmmoClips = newClips;
            loadout.LoadedAmmoTypes = newClips;
            loadout.TotalAmmoCounts = newCounts;

            MainGun gun = root.GetComponent<MainGun>();
            bool gunOk = gun != null && gun.Ammo != null && gun.AmmoCounts != null &&
                         gun.Ammo.Length == gun.AmmoCounts.Length && gun.Ammo.Length + 1 == newClips.Length;
            if (gunOk)
            {
                var ammo = new AmmoCodexScriptable[newClips.Length];
                gun.Ammo.CopyTo(ammo, 0);
                ammo[newClips.Length - 1] = _dm11Codex;
                gun.Ammo = ammo;

                var available = new AmmoType[ammo.Length];
                for (int i = 0; i < ammo.Length; i++) available[i] = ammo[i] == null ? null : ammo[i].AmmoType;
                gun.AvailableAmmo = available;

                var gunCounts = new int[ammo.Length];
                gun.AmmoCounts.CopyTo(gunCounts, 0);
                gunCounts[heat] -= take;
                gunCounts[ammo.Length - 1] = take;
                gun.AmmoCounts = gunCounts;
            }

            LpDm11Fuse.Ensure();
            MelonLogger.Msg($"[L2A7V] {Dm11Name} added: {take} round(s) taken from the HEAT row, " +
                            $"{newClips.Length} type(s) in the loadout" +
                            (gunOk ? ", the gun list extended to match" : ", BUT the gun list could not be extended"));
        }

        /// <summary>The DM11 round, so the fuse control can program it before firing.</summary>
        internal static AmmoType Dm11Shell => _dm11Shell;

        /// <summary>Sets how long after leaving the muzzle DM11 bursts; zero is a contact fuze.</summary>
        internal static void SetDm11Fuse(float seconds)
        {
            if (_dm11Shell == null) return;
            _dm11Shell.RangedFuseTime = Mathf.Max(0f, seconds);
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
            bool dm73 = LpConfig.CarriesDm73;
            if (!Build(donorApClip,
                       dm73 ? Dm73Name : ShellName,
                       dm73 ? Dm73PenetrationMm : PenetrationMm,
                       dm73 ? Dm73MuzzleVelocity : MuzzleVelocity,
                       dm73 ? Dm73MassKg : ProjectileMassKg,
                       dm73 ? 1 : 0)) _failed = true;
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

        private static bool Build(AmmoClipCodexScriptable donorClip, string name, float penetration,
            float velocity, float mass, int slot,
            AmmoType.AmmoCategory? category = null, AmmoType.AmmoShortName? shortName = null)
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

            shell.Name = name;
            shell.RhaPenetration = penetration;
            shell.MuzzleVelocity = velocity;
            shell.Mass = mass;
            // The impact-effect and decal databases key their tables by this index, and the copy has not been
            // cached against anything yet - the donor's index would point at the donor's row.
            shell.CachedIndex = -1;
            if (category.HasValue) shell.Category = category.Value;
            if (shortName.HasValue) shell.ShortName = shortName.Value;

            var codex = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
            codex.name = "L2A7V_" + name;
            // The customizer builds these in the menu scene and the vehicle uses them in the mission, with an
            // asset unload in between, and a managed static is not what keeps a Unity asset alive.
            codex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            codex.AmmoType = shell;

            var clipType = new AmmoType.AmmoClip
            {
                Name = name,
                Capacity = 1,
                MinimalPattern = new[] { codex }
            };
            var clip = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
            clip.name = "L2A7V_" + name + "_clip";
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            clip.ClipType = clipType;
            clip.CompatibleWeaponSystems = donorClip.CompatibleWeaponSystems;

            switch (slot)
            {
                case 1:
                    _dm73Shell = shell;
                    _dm73Codex = codex;
                    _dm73Clip = clip;
                    break;
                case 2:
                    _dm11Shell = shell;
                    _dm11Codex = codex;
                    _dm11Clip = clip;
                    break;
                default:
                    _shell = shell;
                    _codex = codex;
                    _clip = clip;
                    break;
            }

            if (source.VisualModel != null) AttachVisualFor(source, codex, shell, name);

            MelonLogger.Msg($"[L2A7V] {name} built from the donor's {source.Name}: " +
                            $"{penetration:F0} mm, {velocity:F0} m/s, {mass:F1} kg");
            return true;
        }

        /// <summary>
        /// Puts the new round's name on a copy of the donor's rack visual, so the rack shows the right shell.
        /// </summary>
        private static void AttachVisualFor(AmmoType source, AmmoCodexScriptable codex, AmmoType shell, string name)
        {
            GameObject visual = UnityEngine.Object.Instantiate(source.VisualModel);
            visual.name = name + " visual";
            visual.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(visual);
            AmmoStoredVisual stored = visual.GetComponent<AmmoStoredVisual>();
            if (stored != null)
            {
                stored.AmmoScriptable = codex;
                stored.AmmoType = shell;
            }
            shell.VisualModel = visual;
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

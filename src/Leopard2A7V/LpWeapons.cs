using System;
using System.Reflection;
using GHPC;
using GHPC.Equipment.Optics;
using GHPC.Weaponry;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpWeapons
{
	private const string GunName = "Rh-120 L/55";

	private const string WeaponEntryName = "Rh-120 L/55 120 mm";

	private const string ShellName = "DM53 APFSDS-T";

	private const float PenetrationMm = 800f;

	private const float MuzzleVelocity = 1750f;

	private const float ProjectileMassKg = 5f;

	private const string Dm73Name = "DM73 APFSDS-T";

	private const float Dm73PenetrationMm = 800f;

	private const float Dm73MuzzleVelocity = 1780f;

	private const float Dm73MassKg = 5f;

	private const string Dm11Name = "DM11 HE-FRAG";

	private const float Dm11PenetrationMm = 20f;

	private const float Dm11MuzzleVelocity = 870f;

	private const float Dm11MassKg = 4f;

	private static readonly FieldInfo WeaponCodexField = AccessTools.Field(typeof(WeaponSystem), "CodexEntry");

	private static readonly MethodInfo RegisterBallistics = AccessTools.Method(typeof(LoadoutManager), "RegisterAllBallistics");

	private static readonly Type VehicleInfoType = AccessTools.TypeByName("VehicleInfo");

	private static readonly FieldInfo AmmoCountsOriginalField = ((VehicleInfoType == null) ? null : AccessTools.Field(VehicleInfoType, "_ammoCountsOriginal"));

	private static readonly MethodInfo FillAmmoRacks = ((VehicleInfoType == null) ? null : AccessTools.Method(VehicleInfoType, "fillAmmoRacks"));

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

	private static bool _gunMissingLogged;

	private static bool _gunFoundLogged;

	private static GameObject _pendingRefill;

	private static int[] _pendingCounts;

	internal static AmmoType Dm11Shell => _dm11Shell;

	public static void Install(HarmonyLib.Harmony harmony)
	{
		MethodInfo methodInfo = AccessTools.Method(typeof(GHPC.Weapons.AmmoRack), "SpawnVisualModel");
		if (methodInfo == null)
		{
			MelonLogger.Warning("[L2A7V] could not hook the ammo rack's visual spawn, so the imported rounds will load but stay invisible in the rack");
			return;
		}
		harmony.Patch(methodInfo, null, new HarmonyMethod(typeof(LpWeapons), "VisualSpawned"));
		if (FillAmmoRacks == null)
		{
			MelonLogger.Warning("[L2A7V] could not hook the vehicle's rack refill, so a refill will reload the donor's two rounds and no DM53");
		}
		else
		{
			harmony.Patch(FillAmmoRacks, new HarmonyMethod(typeof(LpWeapons), "RefreshAmmoSnapshot"));
		}
	}

	private static void RefreshAmmoSnapshot(object __instance)
	{
		if (__instance == null || AmmoCountsOriginalField == null)
		{
			return;
		}
		Component component = __instance as Component;
		if (component == null)
		{
			return;
		}
		LoadoutManager component2 = component.GetComponent<LoadoutManager>();
		if (!(component2 == null))
		{
			AmmoClipCodexScriptable[] ammoClips = component2.LoadedAmmoList.AmmoClips;
			int[] totalAmmoCounts = component2.TotalAmmoCounts;
			if (ammoClips != null && totalAmmoCounts != null && ammoClips.Length != 0 && ammoClips.Length == totalAmmoCounts.Length && IndexOfClip(ammoClips, _clip) >= 0)
			{
				AmmoCountsOriginalField.SetValue(__instance, (int[])totalAmmoCounts.Clone());
			}
		}
	}

	private static int IndexOfClip(AmmoClipCodexScriptable[] clips, AmmoClipCodexScriptable wanted)
	{
		if (clips == null || wanted == null)
		{
			return -1;
		}
		for (int i = 0; i < clips.Length; i++)
		{
			if ((object)clips[i] == wanted)
			{
				return i;
			}
		}
		return -1;
	}

	private static void VisualSpawned(AmmoType type, GameObject __result)
	{
		if (__result != null && _shell != null && type == _shell && !__result.activeSelf)
		{
			__result.SetActive(value: true);
		}
	}

	public static bool Apply(GameObject root)
	{
		if (root == null || _failed)
		{
			return true;
		}
		WeaponsManager component = root.GetComponent<WeaponsManager>();
		LoadoutManager component2 = root.GetComponent<LoadoutManager>();
		if (component == null || component2 == null || component.Weapons == null || component.Weapons.Length == 0)
		{
			return false;
		}
		int weaponIndex = component2.WeaponIndex;
		if (weaponIndex < 0 || weaponIndex >= component.Weapons.Length)
		{
			return false;
		}
		WeaponSystemInfo weaponSystemInfo = component.Weapons[weaponIndex];
		if (weaponSystemInfo == null || weaponSystemInfo.Weapon == null)
		{
			return false;
		}
		AmmoClipCodexScriptable[] ammoClips = component2.LoadedAmmoList.AmmoClips;
		if (ammoClips == null || ammoClips.Length == 0)
		{
			return false;
		}
		int num = FindSabotClip(ammoClips);
		if (num < 0)
		{
			MelonLogger.Warning("[L2A7V] the donor loadout holds no APFSDS clip, keeping its ammunition");
			return true;
		}
		if (EnsureClip(ammoClips[num]) == null)
		{
			return true;
		}
		int firstCount = SubstituteSabot(component2, ammoClips, num);
		weaponSystemInfo.Name = "Rh-120 L/55 120 mm";
		RenameWeapon(weaponSystemInfo.Weapon);
		bool flag = SyncMainGun(root, firstCount);
		if (RegisterBallistics != null)
		{
			RegisterBallistics.Invoke(component2, null);
		}
		if (flag)
		{
			int num2 = CountDonorSabot(ammoClips, component2.TotalAmmoCounts);
			bool carriesDm = LpConfig.CarriesDm73;
			MelonLogger.Msg("[L2A7V] main gun set to Rh-120 L/55 120 mm: the donor's APFSDS row (was DM23) now carries " + (carriesDm ? "DM73 APFSDS-T" : "DM53 APFSDS-T") + " " + $"({(carriesDm ? 800f : 800f):F0} mm, " + $"{(carriesDm ? 1780f : 1750f):F0} m/s) at key {num + 1}; " + $"{ammoClips.Length} round type(s) in the loadout, {num2} DM23 round(s) left" + ((num2 == 0) ? string.Empty : " - see the rows above"));
		}
		return flag;
	}

	public static bool ApplyGunList(GameObject root)
	{
		if (root == null || _failed)
		{
			return true;
		}
		bool num = SyncMainGun(root, -1);
		return num;
	}

	// DM11 and its programmable fuse are not part of the supported loadout.
	// Keep the active DM53/DM73 path explicit rather than silently doing nothing.
	public static void ApplyThirdRound(GameObject root)
	{
	}

	private static void AppendDm73(GameObject root)
	{
		if (_clip == null || _dm73Clip != null)
		{
			return;
		}
		AmmoClipCodexScriptable ammoClipCodexScriptable = null;
		AmmoClipCodexScriptable[] array = null;
		LoadoutManager component = root.GetComponent<LoadoutManager>();
		if (component == null)
		{
			return;
		}
		array = ((component.LoadedAmmoList != null) ? component.LoadedAmmoList.AmmoClips : component.LoadedAmmoTypes);
		if (array == null)
		{
			return;
		}
		AmmoClipCodexScriptable[] array2 = array;
		foreach (AmmoClipCodexScriptable ammoClipCodexScriptable2 in array2)
		{
			if (ammoClipCodexScriptable2 != null && ammoClipCodexScriptable2.ClipType != null && ammoClipCodexScriptable2.ClipType.MinimalPattern != null && ammoClipCodexScriptable2.ClipType.MinimalPattern.Length != 0)
			{
				ammoClipCodexScriptable = ammoClipCodexScriptable2;
				break;
			}
		}
		if (ammoClipCodexScriptable == null)
		{
			return;
		}
		if (!Build(ammoClipCodexScriptable, "DM73 APFSDS-T", 800f, 1780f, 5f, 1))
		{
			MelonLogger.Warning("[L2A7V] DM73 could not be built, so the vehicle carries the DM53 row only");
			return;
		}
		int[] totalAmmoCounts = component.TotalAmmoCounts;
		int num = ((totalAmmoCounts != null && totalAmmoCounts.Length != 0) ? Mathf.Min(6, Mathf.Max(0, totalAmmoCounts[0])) : 6);
		AmmoClipCodexScriptable[] array3 = new AmmoClipCodexScriptable[array.Length + 1];
		array.CopyTo(array3, 0);
		array3[array.Length] = _dm73Clip;
		int[] array4 = new int[array3.Length];
		totalAmmoCounts?.CopyTo(array4, 0);
		array4[array3.Length - 1] = num;
		if (totalAmmoCounts != null && totalAmmoCounts.Length != 0)
		{
			array4[0] = Mathf.Max(0, totalAmmoCounts[0] - num);
		}
		if (component.LoadedAmmoList != null)
		{
			component.LoadedAmmoList.AmmoClips = array3;
		}
		component.LoadedAmmoTypes = array3;
		component.TotalAmmoCounts = array4;
		int num2 = 0;
		int num3 = 0;
		LoadoutManager.RackLoadout[] array5 = component.RackLoadouts ?? Array.Empty<LoadoutManager.RackLoadout>();
		foreach (LoadoutManager.RackLoadout rackLoadout in array5)
		{
			if (rackLoadout != null && rackLoadout.AmmoCounts != null)
			{
				int[] array6 = new int[array3.Length];
				Array.Copy(rackLoadout.AmmoCounts, array6, Mathf.Min(rackLoadout.AmmoCounts.Length, array3.Length));
				if (array6.Length != 0 && array6[0] > 0)
				{
					int num4 = Mathf.Min(array6[0], 2);
					array6[0] -= num4;
					array6[array3.Length - 1] += num4;
					num3 += num4;
				}
				rackLoadout.AmmoCounts = array6;
				num2++;
			}
		}
		MainGun component2 = root.GetComponent<MainGun>();
		if (component2 != null && component2.Ammo != null && component2.Ammo.Length != 0)
		{
			AmmoCodexScriptable[] array7 = new AmmoCodexScriptable[component2.Ammo.Length + 1];
			component2.Ammo.CopyTo(array7, 0);
			array7[array7.Length - 1] = _dm73Codex;
			component2.Ammo = array7;
			AmmoType[] array8 = new AmmoType[array7.Length];
			for (int j = 0; j < array7.Length; j++)
			{
				array8[j] = ((array7[j] == null) ? null : array7[j].AmmoType);
			}
			component2.AvailableAmmo = array8;
			int[] array9 = new int[array7.Length];
			if (component2.AmmoCounts != null)
			{
				Array.Copy(component2.AmmoCounts, array9, Mathf.Min(component2.AmmoCounts.Length, array7.Length));
			}
			array9[array7.Length - 1] = num;
			if (array9.Length > 1 && array9[0] >= num)
			{
				array9[0] -= num;
			}
			component2.AmmoCounts = array9;
		}
		MelonLogger.Msg(string.Format("[L2A7V] {0} added beside {1}: {2} round(s) taken from the ", "DM73 APFSDS-T", "DM53 APFSDS-T", num) + $"DM53 row, {num3} placed in {num2} rack(s), each extended to {array3.Length} type(s)");
	}

	private static void SubstituteDm73ForHeat(GameObject root)
	{
		if (_clip == null)
		{
			return;
		}
		LoadoutManager component = root.GetComponent<LoadoutManager>();
		if (component == null)
		{
			return;
		}
		AmmoClipCodexScriptable[] array = ((component.LoadedAmmoList != null) ? component.LoadedAmmoList.AmmoClips : component.LoadedAmmoTypes);
		if (array == null)
		{
			return;
		}
		if (_dm73Clip == null)
		{
			AmmoClipCodexScriptable ammoClipCodexScriptable = null;
			AmmoClipCodexScriptable[] array2 = array;
			foreach (AmmoClipCodexScriptable ammoClipCodexScriptable2 in array2)
			{
				if (!(ammoClipCodexScriptable2 == null) && ammoClipCodexScriptable2.ClipType != null)
				{
					AmmoCodexScriptable[] minimalPattern = ammoClipCodexScriptable2.ClipType.MinimalPattern;
					if (minimalPattern != null && minimalPattern.Length != 0 && !(minimalPattern[0] == null))
					{
						ammoClipCodexScriptable = ammoClipCodexScriptable2;
						break;
					}
				}
			}
			if (ammoClipCodexScriptable == null)
			{
				return;
			}
			if (!Build(ammoClipCodexScriptable, "DM73 APFSDS-T", 800f, 1780f, 5f, 1))
			{
				MelonLogger.Warning("[L2A7V] DM73 could not be built; the vehicle keeps the donor's second round");
				return;
			}
		}
		int num = FindHeatRow(array);
		if (num < 0)
		{
			MelonLogger.Warning("[L2A7V] the donor loadout holds no HEAT row to carry DM73, so only DM53 is carried");
		}
		else
		{
			if ((object)array[num] == _dm73Clip)
			{
				return;
			}
			int[] totalAmmoCounts = component.TotalAmmoCounts;
			int num2 = ((totalAmmoCounts != null && totalAmmoCounts.Length > num) ? totalAmmoCounts[num] : 0);
			array[num] = _dm73Clip;
			if (totalAmmoCounts != null && totalAmmoCounts.Length > num)
			{
				totalAmmoCounts[num] = num2;
			}
			MainGun component2 = root.GetComponent<MainGun>();
			if (component2 != null && component2.Ammo != null)
			{
				AmmoCodexScriptable[] ammo = component2.Ammo;
				for (int j = 0; j < ammo.Length; j++)
				{
					AmmoType ammoType = ((ammo[j] == null) ? null : ammo[j].AmmoType);
					if (ammoType != null && ammoType.ShortName == AmmoType.AmmoShortName.Heat)
					{
						ammo[j] = _dm73Codex;
						component2.Ammo = ammo;
						AmmoType[] array3 = new AmmoType[ammo.Length];
						for (int k = 0; k < ammo.Length; k++)
						{
							array3[k] = ((ammo[k] == null) ? null : ammo[k].AmmoType);
						}
						component2.AvailableAmmo = array3;
						break;
					}
				}
			}
			MelonLogger.Msg(string.Format("[L2A7V] the donor's HEAT row now carries {0} ({1:F0} mm, ", "DM73 APFSDS-T", 800f) + string.Format("{0:F0} m/s) beside {1}: {2} round(s), row {3} of ", 1780f, "DM53 APFSDS-T", num2, num + 1) + $"{array.Length}");
		}
	}

	private static int FindHeatRow(AmmoClipCodexScriptable[] clips)
	{
		for (int i = 0; i < clips.Length; i++)
		{
			AmmoClipCodexScriptable ammoClipCodexScriptable = clips[i];
			if (ammoClipCodexScriptable == null || ammoClipCodexScriptable.ClipType == null)
			{
				continue;
			}
			AmmoCodexScriptable[] minimalPattern = ammoClipCodexScriptable.ClipType.MinimalPattern;
			if (minimalPattern != null && minimalPattern.Length != 0 && !(minimalPattern[0] == null))
			{
				AmmoType ammoType = minimalPattern[0].AmmoType;
				if (ammoType != null && ammoType.ShortName == AmmoType.AmmoShortName.Heat)
				{
					return i;
				}
			}
		}
		return -1;
	}

	internal static void SetDm11Fuse(float seconds)
	{
		if (_dm11Shell != null)
		{
			_dm11Shell.RangedFuseTime = Mathf.Max(0f, seconds);
		}
	}

	internal static void TryRefillRacks()
	{
		if (!(_pendingRefill == null) && _pendingCounts != null && !(VehicleInfoType == null) && !(AmmoCountsOriginalField == null) && !(FillAmmoRacks == null))
		{
			Component component = _pendingRefill.GetComponent(VehicleInfoType);
			if (component == null)
			{
				component = _pendingRefill.GetComponentInChildren(VehicleInfoType, includeInactive: true);
			}
			if (!(component == null))
			{
				AmmoCountsOriginalField.SetValue(component, (int[])_pendingCounts.Clone());
				FillAmmoRacks.Invoke(component, null);
				MelonLogger.Msg("[L2A7V] racks refilled so the third round has somewhere to come from");
				_pendingRefill = null;
				_pendingCounts = null;
			}
		}
	}

	private static int CountDonorSabot(AmmoClipCodexScriptable[] clips, int[] counts)
	{
		int num = 0;
		for (int i = 0; i < clips.Length; i++)
		{
			AmmoClipCodexScriptable ammoClipCodexScriptable = clips[i];
			string text = ((ammoClipCodexScriptable == null || ammoClipCodexScriptable.ClipType == null) ? null : ammoClipCodexScriptable.ClipType.Name);
			if (text != null && text.Contains("DM23"))
			{
				num += ((counts != null && counts.Length > i) ? counts[i] : 0);
			}
		}
		return num;
	}

	private static int SubstituteSabot(LoadoutManager loadout, AmmoClipCodexScriptable[] clips, int sabot)
	{
		if ((object)clips[sabot] == _clip)
		{
			return -1;
		}
		int[] totalAmmoCounts = loadout.TotalAmmoCounts;
		int result = ((totalAmmoCounts != null && totalAmmoCounts.Length > sabot) ? totalAmmoCounts[sabot] : 0);
		clips[sabot] = _clip;
		return result;
	}

	private static bool SyncMainGun(GameObject root, int firstCount)
	{
		if (_codex == null || _shell == null || root == null)
		{
			return false;
		}
		MainGun component = root.GetComponent<MainGun>();
		if (component == null)
		{
			if (!_gunMissingLogged)
			{
				_gunMissingLogged = true;
				MelonLogger.Msg("[L2A7V] no MainGun under " + root.name + "; the round list is written as soon as the donor's art pass adds it");
			}
			return false;
		}
		AmmoCodexScriptable[] array = component.Ammo ?? Array.Empty<AmmoCodexScriptable>();
		if (array.Length == 0)
		{
			MelonLogger.Warning("[L2A7V] this unit's MainGun carries no round list, so the imported round is in the loadout only and cannot be selected");
			return true;
		}
		int num = FindSabotSlot(array);
		if (num < 0)
		{
			MelonLogger.Warning("[L2A7V] the donor gun lists no APFSDS row, so DM53 APFSDS-T stays in the loadout only and cannot be selected");
			return true;
		}
		if ((object)array[num] == _codex)
		{
			return true;
		}
		array[num] = _codex;
		component.Ammo = array;
		AmmoType[] array2 = new AmmoType[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array2[i] = ((array[i] == null) ? null : array[i].AmmoType);
		}
		component.AvailableAmmo = array2;
		int[] ammoCounts = component.AmmoCounts;
		bool flag = firstCount >= 0 && ammoCounts != null && ammoCounts.Length == array.Length;
		if (flag)
		{
			ammoCounts[num] = firstCount;
		}
		MelonLogger.Msg($"[L2A7V] gun round list: the donor's APFSDS row {num + 1} of " + string.Format("{0} is now {1}", array.Length, "DM53 APFSDS-T") + (flag ? $" ({firstCount} rounds)" : string.Empty));
		return true;
	}

	private static MainGun FindGun(GameObject root)
	{
		if (root == null)
		{
			return null;
		}
		MainGun component = root.GetComponent<MainGun>();
		if (component != null)
		{
			return component;
		}
		component = root.GetComponentInChildren<MainGun>(includeInactive: true);
		if (component != null && !_gunFoundLogged)
		{
			_gunFoundLogged = true;
			MelonLogger.Msg("[L2A7V] main gun found under " + PathOf(component.transform) + ", not on the unit root");
		}
		return component;
	}

	private static string PathOf(Transform node)
	{
		string text = ((node != null) ? node.name : string.Empty);
		Transform transform = ((node != null) ? node.parent : null);
		while (transform != null)
		{
			text = transform.name + "/" + text;
			transform = transform.parent;
		}
		return text;
	}

	private static int FindSabotSlot(AmmoCodexScriptable[] ammo)
	{
		for (int i = 0; i < ammo.Length; i++)
		{
			if ((object)ammo[i] == _codex)
			{
				return i;
			}
			AmmoType ammoType = ((ammo[i] == null) ? null : ammo[i].AmmoType);
			if (ammoType != null && ammoType.Category == AmmoType.AmmoCategory.Penetrator && ammoType.ShortName == AmmoType.AmmoShortName.Sabot)
			{
				return i;
			}
		}
		return -1;
	}

	private static AmmoClipCodexScriptable EnsureClip(AmmoClipCodexScriptable donorApClip)
	{
		if (_failed || donorApClip == null)
		{
			return null;
		}
		if (_clip != null)
		{
			EnsureVisual(donorApClip);
			return _clip;
		}
		bool carriesDm = LpConfig.CarriesDm73;
		if (!Build(donorApClip, carriesDm ? "DM73 APFSDS-T" : "DM53 APFSDS-T", carriesDm ? 800f : 800f, carriesDm ? 1780f : 1750f, carriesDm ? 5f : 5f, 0))
		{
			_failed = true;
		}
		return _clip;
	}

	private static void EnsureVisual(AmmoClipCodexScriptable donorApClip)
	{
		if (_shell != null && !(_shell.VisualModel != null))
		{
			AmmoType ammoType = ((donorApClip.ClipType == null || donorApClip.ClipType.MinimalPattern == null || donorApClip.ClipType.MinimalPattern.Length == 0) ? null : donorApClip.ClipType.MinimalPattern[0].AmmoType);
			if (ammoType != null && !(ammoType.VisualModel == null))
			{
				AttachVisual(ammoType);
				MelonLogger.Msg("[L2A7V] the imported round's rack visual reached the game after the round was built, so it is now attached to the shared round");
			}
		}
	}

	private static void AttachVisual(AmmoType source)
	{
		GameObject gameObject = UnityEngine.Object.Instantiate(source.VisualModel);
		gameObject.name = "DM53 APFSDS-T visual";
		gameObject.SetActive(value: false);
		UnityEngine.Object.DontDestroyOnLoad(gameObject);
		AmmoStoredVisual component = gameObject.GetComponent<AmmoStoredVisual>();
		if (component != null)
		{
			component.AmmoScriptable = _codex;
			component.AmmoType = _shell;
		}
		_shell.VisualModel = gameObject;
	}

	public static int FindSabotClip(AmmoClipCodexScriptable[] clips)
	{
		for (int i = 0; i < clips.Length; i++)
		{
			AmmoClipCodexScriptable ammoClipCodexScriptable = clips[i];
			if (ammoClipCodexScriptable == null || ammoClipCodexScriptable.ClipType == null)
			{
				continue;
			}
			AmmoCodexScriptable[] minimalPattern = ammoClipCodexScriptable.ClipType.MinimalPattern;
			if (minimalPattern != null && minimalPattern.Length != 0 && !(minimalPattern[0] == null))
			{
				AmmoType ammoType = minimalPattern[0].AmmoType;
				if (ammoType != null && ammoType.Category == AmmoType.AmmoCategory.Penetrator && ammoType.ShortName == AmmoType.AmmoShortName.Sabot)
				{
					return i;
				}
			}
		}
		return -1;
	}

	private static bool Build(AmmoClipCodexScriptable donorClip, string name, float penetration, float velocity, float mass, int slot, AmmoType.AmmoCategory? category = null, AmmoType.AmmoShortName? shortName = null)
	{
		AmmoType ammoType = donorClip.ClipType.MinimalPattern[0].AmmoType;
		if (ammoType == null)
		{
			return false;
		}
		AmmoType ammoType2 = new AmmoType();
		FieldInfo[] fields = typeof(AmmoType).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (FieldInfo fieldInfo in fields)
		{
			if (!fieldInfo.IsLiteral && !fieldInfo.IsInitOnly)
			{
				fieldInfo.SetValue(ammoType2, fieldInfo.GetValue(ammoType));
			}
		}
		ammoType2.Name = name;
		ammoType2.RhaPenetration = penetration;
		ammoType2.MuzzleVelocity = velocity;
		ammoType2.Mass = mass;
		ammoType2.CachedIndex = -1;
		if (category.HasValue)
		{
			ammoType2.Category = category.Value;
		}
		if (shortName.HasValue)
		{
			ammoType2.ShortName = shortName.Value;
		}
		AmmoCodexScriptable ammoCodexScriptable = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
		ammoCodexScriptable.name = "L2A7V_" + name;
		ammoCodexScriptable.hideFlags = HideFlags.DontUnloadUnusedAsset;
		ammoCodexScriptable.AmmoType = ammoType2;
		AmmoType.AmmoClip ammoClip = new AmmoType.AmmoClip();
		ammoClip.Name = name;
		ammoClip.Capacity = 1;
		ammoClip.MinimalPattern = new AmmoCodexScriptable[1] { ammoCodexScriptable };
		AmmoType.AmmoClip clipType = ammoClip;
		AmmoClipCodexScriptable ammoClipCodexScriptable = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
		ammoClipCodexScriptable.name = "L2A7V_" + name + "_clip";
		ammoClipCodexScriptable.hideFlags = HideFlags.DontUnloadUnusedAsset;
		ammoClipCodexScriptable.ClipType = clipType;
		ammoClipCodexScriptable.CompatibleWeaponSystems = donorClip.CompatibleWeaponSystems;
		switch (slot)
		{
		case 1:
			_dm73Shell = ammoType2;
			_dm73Codex = ammoCodexScriptable;
			_dm73Clip = ammoClipCodexScriptable;
			break;
		case 2:
			_dm11Shell = ammoType2;
			_dm11Codex = ammoCodexScriptable;
			_dm11Clip = ammoClipCodexScriptable;
			break;
		default:
			_shell = ammoType2;
			_codex = ammoCodexScriptable;
			_clip = ammoClipCodexScriptable;
			break;
		}
		if (ammoType.VisualModel != null)
		{
			AttachVisualFor(ammoType, ammoCodexScriptable, ammoType2, name);
		}
		MelonLogger.Msg("[L2A7V] " + name + " built from the donor's " + ammoType.Name + ": " + $"{penetration:F0} mm, {velocity:F0} m/s, {mass:F1} kg");
		return true;
	}

	private static void AttachVisualFor(AmmoType source, AmmoCodexScriptable codex, AmmoType shell, string name)
	{
		GameObject gameObject = UnityEngine.Object.Instantiate(source.VisualModel);
		gameObject.name = name + " visual";
		gameObject.SetActive(value: false);
		UnityEngine.Object.DontDestroyOnLoad(gameObject);
		AmmoStoredVisual component = gameObject.GetComponent<AmmoStoredVisual>();
		if (component != null)
		{
			component.AmmoScriptable = codex;
			component.AmmoType = shell;
		}
		shell.VisualModel = gameObject;
	}

	private static void RenameWeapon(WeaponSystem weapon)
	{
		if (WeaponCodexField == null)
		{
			return;
		}
		WeaponSystemCodexScriptable weaponSystemCodexScriptable = WeaponCodexField.GetValue(weapon) as WeaponSystemCodexScriptable;
		if (!(weaponSystemCodexScriptable == null))
		{
			if (_gunCodex == null)
			{
				_gunCodex = ScriptableObject.CreateInstance<WeaponSystemCodexScriptable>();
				_gunCodex.name = "L2A7V_Rh-120 L/55";
				_gunCodex.FriendlyName = "Rh-120 L/55";
				_gunCodex.CaliberMm = weaponSystemCodexScriptable.CaliberMm;
				_gunCodex.Type = weaponSystemCodexScriptable.Type;
			}
			if ((object)WeaponCodexField.GetValue(weapon) != _gunCodex)
			{
				WeaponCodexField.SetValue(weapon, _gunCodex);
			}
		}
	}

	public static void ApplyGunArc(GameObject root)
	{
		if (root == null || !LpConfig.LimitMainGunElevation)
		{
			return;
		}
		float num = Mathf.Abs(20f);
		float num2 = 0f - Mathf.Abs(-9f);
		AimablePlatform aimablePlatform = FindMainGunPlatform(root);
		if (aimablePlatform == null)
		{
			MelonLogger.Warning("[L2A7V] this unit has no gun elevation platform, so its arc is whatever the donor shipped");
			return;
		}
		Vector2 localEulerLimits = aimablePlatform.LocalEulerLimits;
		aimablePlatform.LocalEulerLimits = new Vector2(num2, num);
		int num3 = AlignOptics(root, num, num2);
		MelonLogger.Msg("[L2A7V] gun arc on '" + aimablePlatform.Name + "' at " + aimablePlatform.transform.parent.name + "/" + $"{aimablePlatform.transform.name}: donor {localEulerLimits.x:F1}..{localEulerLimits.y:F1} -> " + $"{num2:F1}..{num:F1} deg (axis {aimablePlatform.Axis}, " + $"reversed {aimablePlatform.ReverseLocalEuler}), {num3} sight tube(s) tightened to it");
		LpGunArcWatch lpGunArcWatch = root.GetComponent<LpGunArcWatch>();
		if (lpGunArcWatch == null)
		{
			lpGunArcWatch = root.AddComponent<LpGunArcWatch>();
		}
		lpGunArcWatch.Init(aimablePlatform);
	}

	private static int AlignOptics(GameObject root, float elevation, float depression)
	{
		int num = 0;
		UsableOptic[] componentsInChildren = root.GetComponentsInChildren<UsableOptic>(includeInactive: true);
		foreach (UsableOptic usableOptic in componentsInChildren)
		{
			if (LpVisual.IsImportedArt(usableOptic.transform) || !usableOptic.RotateElevation)
			{
				continue;
			}
			Vector2 localElevationLimits = usableOptic.LocalElevationLimits;
			if (localElevationLimits.x != 0f || localElevationLimits.y != 0f)
			{
				float num2 = ((localElevationLimits.y != 0f) ? localElevationLimits.y : 180f);
				float num3 = ((localElevationLimits.x != 0f) ? (0f - localElevationLimits.x) : 180f);
				float num4 = Mathf.Min(num2, elevation);
				float num5 = Mathf.Min(num3, 0f - depression);
				if (!Mathf.Approximately(num4, num2) || !Mathf.Approximately(num5, num3))
				{
					usableOptic.LocalElevationLimits = new Vector2(0f - num5, num4);
					MelonLogger.Msg("[L2A7V] sight tube '" + usableOptic.name + "' (" + usableOptic.transform.parent.name + "/" + $"{usableOptic.transform.name}): {localElevationLimits.x:F1}..{localElevationLimits.y:F1} -> " + $"{0f - num5:F1}..{num4:F1} deg");
					num++;
				}
			}
		}
		return num;
	}

	private static AimablePlatform FindMainGunPlatform(GameObject root)
	{
		AimablePlatform[] componentsInChildren = root.GetComponentsInChildren<AimablePlatform>(includeInactive: true);
		foreach (AimablePlatform aimablePlatform in componentsInChildren)
		{
			if (!LpVisual.IsImportedArt(aimablePlatform.transform))
			{
				string text = (aimablePlatform.Name + " " + aimablePlatform.transform.name).ToLowerInvariant();
				if (text.Contains("main gun") || text.Contains("gun elevation") || text.Contains("rh-120"))
				{
					return aimablePlatform;
				}
			}
		}
		return null;
	}
}

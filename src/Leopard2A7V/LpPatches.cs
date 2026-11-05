using System;
using System.Reflection;
using GHPC;
using GHPC.AI;
using GHPC.Mission;
using GHPC.Mission.Data;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpPatches
{
	private static readonly FieldInfo UnitFriendlyName = AccessTools.Field(typeof(Unit), "_friendlyName");

	private static bool _installed;

	public static void Install(HarmonyLib.Harmony harmony)
	{
		if (!_installed)
		{
			_installed = true;
			MethodInfo methodInfo = AccessTools.Method(typeof(UnitSpawner), "Awake");
			if (methodInfo != null)
			{
				harmony.Patch(methodInfo, null, new HarmonyMethod(typeof(LpPatches), "SpawnerAwakePostfix"));
			}
			MethodInfo methodInfo2 = FindStringSpawnMethod();
			if (methodInfo2 != null)
			{
				harmony.Patch(methodInfo2, new HarmonyMethod(typeof(LpPatches), "SpawnUnitPrefix"), new HarmonyMethod(typeof(LpPatches), "SpawnUnitPostfix"));
			}
			else
			{
				MelonLogger.Error("[L2A7V] could not locate UnitSpawner.SpawnUnit(string, ...) to hook");
			}
			Type type = AccessTools.TypeByName("MissionConfigMenuPanel");
			MethodInfo methodInfo3 = ((type == null) ? null : AccessTools.Method(type, "OnEnable"));
			if (methodInfo3 != null)
			{
				harmony.Patch(methodInfo3, new HarmonyMethod(typeof(LpPatches), "MissionPanelOnEnablePrefix"));
				MelonLogger.Msg("[L2A7V] hooked the mission customization panel");
			}
			else
			{
				MelonLogger.Warning("[L2A7V] mission customization panel hook target not found");
			}
			LpWeapons.Install(harmony);
			LpAmmoMenu.Install(harmony);
			LpArmor.Install(harmony);
			LpCitv.InstallCameraHook(harmony);
		}
	}

	private static MethodInfo FindStringSpawnMethod()
	{
		MethodInfo[] methods = typeof(UnitSpawner).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (MethodInfo methodInfo in methods)
		{
			if (!(methodInfo.Name != "SpawnUnit"))
			{
				ParameterInfo[] parameters = methodInfo.GetParameters();
				if (parameters.Length == 4 && parameters[0].ParameterType == typeof(string))
				{
					return methodInfo;
				}
			}
		}
		return null;
	}

	public static void SpawnerAwakePostfix(UnitSpawner __instance)
	{
		if (__instance != null && LpUnitRegistry.Register(__instance.PrefabLookup))
		{
			MelonLogger.Msg("[L2A7V] registered unit key 'LEO2A7V' for the mission spawner");
		}
	}

	public static void MissionPanelOnEnablePrefix()
	{
		LpUnitRegistry.RegisterEverywhere();
	}

	public static bool SpawnUnitPrefix(UnitSpawner __instance, string uniqueName, UnitMetaData metaData, WaypointHolder startingWaypoints, Transform parentTransform, ref IUnit __result)
	{
		if (__instance == null)
		{
			return true;
		}
		bool num = uniqueName == "LEO2A7V";
		bool flag = LpConfig.ConvertSourceUnit && uniqueName == "LEO2A3";
		if (!num && !flag)
		{
			return true;
		}
		if (!LpTemplate.Ensure(__instance))
		{
			return true;
		}
		__result = __instance.SpawnUnit(LpTemplate.Current, metaData, startingWaypoints, parentTransform);
		if (__result == null)
		{
			MelonLogger.Error("[L2A7V] the spawner refused the converted template");
			return true;
		}
		Component component = __result as Component;
		if (component != null && !component.gameObject.activeSelf)
		{
			component.gameObject.SetActive(value: true);
		}
		return false;
	}

	public static void SpawnUnitPostfix(string uniqueName, IUnit __result)
	{
		if (__result == null)
		{
			return;
		}
		bool flag = uniqueName == "LEO2A7V";
		bool flag2 = LpConfig.ConvertSourceUnit && uniqueName == "LEO2A3";
		if (!flag && !flag2)
		{
			return;
		}
		Component component = __result as Component;
		if (component == null)
		{
			return;
		}
		GameObject root = component.gameObject;
		Guard("weapons", root, delegate
		{
			LpWeapons.Apply(root);
		});
		Guard("gun arc", root, delegate
		{
			LpWeapons.ApplyGunArc(root);
		});
		Guard("reticle", root, delegate
		{
			LpReticle.Apply(root.transform);
		});
		Guard("commander night channel", root, delegate
		{
			LpCommanderNvg.Apply(root);
		});
		if (!(root.GetComponent<LpConvertBehaviour>() != null))
		{
			if (UnitFriendlyName != null && flag)
			{
				UnitFriendlyName.SetValue(__result, "Leopard 2A7V");
			}
			Guard("donor art watch", root, delegate
			{
				LpDonorArt.Request(root);
			});
			Guard("donor protection log", root, delegate
			{
				LpArmorLog.Apply(root);
			});
			Guard("conversion marker", root, delegate
			{
				root.AddComponent<LpConvertBehaviour>();
				root.AddComponent<LpLateInit>();
			});
		}
	}

	private static void Guard(string step, GameObject root, Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V] " + step + " failed on '" + ((root == null) ? "?" : root.name) + "'; the rest of the spawn carries on: " + ex);
		}
	}
}

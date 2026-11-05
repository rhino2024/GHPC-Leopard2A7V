using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GHPC.Mission;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpUnitRegistry
{
	private static readonly HashSet<int> _sourceMissingWarned = new HashSet<int>();

	public static void RegisterEverywhere()
	{
		UnitPrefabLookupScriptable[] array = Resources.FindObjectsOfTypeAll<UnitPrefabLookupScriptable>();
		int num = 0;
		UnitPrefabLookupScriptable[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			if (Register(array2[i]))
			{
				num++;
			}
		}
		if (num > 0)
		{
			MelonLogger.Msg(string.Format("[L2A7V] registered unit key '{0}' in {1} lookup asset(s)", "LEO2A7V", num));
			ClearMissionConfigCaches();
		}
	}

	public static bool Register(UnitPrefabLookupScriptable lookup)
	{
		if (lookup == null || lookup.AllUnits == null)
		{
			return false;
		}
		string text = "LEO2A3";
		UnitPrefabLookupScriptable.UnitPrefabMetadata unitPrefabMetadata = null;
		bool flag = false;
		UnitPrefabLookupScriptable.UnitPrefabMetadata[] allUnits = lookup.AllUnits;
		foreach (UnitPrefabLookupScriptable.UnitPrefabMetadata unitPrefabMetadata2 in allUnits)
		{
			if (unitPrefabMetadata2 != null)
			{
				if (unitPrefabMetadata2.Name == "LEO2A7V")
				{
					flag = true;
				}
				else if (unitPrefabMetadata2.Name == text)
				{
					unitPrefabMetadata = unitPrefabMetadata2;
				}
			}
		}
		if (flag)
		{
			return false;
		}
		if (unitPrefabMetadata == null)
		{
			if (_sourceMissingWarned.Add(lookup.GetInstanceID()))
			{
				List<string> list = new List<string>();
				allUnits = lookup.AllUnits;
				foreach (UnitPrefabLookupScriptable.UnitPrefabMetadata unitPrefabMetadata3 in allUnits)
				{
					if (unitPrefabMetadata3 != null)
					{
						list.Add(unitPrefabMetadata3.Name);
					}
				}
				MelonLogger.Warning("[L2A7V] source unit '" + text + "' is not in this lookup asset (" + list.Count + " units): " + string.Join(", ", list));
			}
			return false;
		}
		UnitPrefabLookupScriptable.UnitPrefabMetadata item = new UnitPrefabLookupScriptable.UnitPrefabMetadata
		{
			Name = "LEO2A7V",
			FriendlyName = "Leopard 2A7V",
			PrefabReference = unitPrefabMetadata.PrefabReference,
			BaseAmmoClipsReference = unitPrefabMetadata.BaseAmmoClipsReference,
			UseDecalLayout = false,
			DecalLayout = unitPrefabMetadata.DecalLayout,
			Army = unitPrefabMetadata.Army,
			Class = unitPrefabMetadata.Class,
			AlternativeClasses = unitPrefabMetadata.AlternativeClasses,
			AllowInCustomizer = true
		};
		List<UnitPrefabLookupScriptable.UnitPrefabMetadata> list2 = new List<UnitPrefabLookupScriptable.UnitPrefabMetadata>(lookup.AllUnits) { item };
		lookup.AllUnits = list2.ToArray();
		ClearSpawnerCache(lookup);
		return true;
	}

	private static void ClearSpawnerCache(UnitPrefabLookupScriptable lookup)
	{
		UnitSpawner[] array = UnityEngine.Object.FindObjectsOfType<UnitSpawner>();
		foreach (UnitSpawner unitSpawner in array)
		{
			if (!(unitSpawner.PrefabLookup != lookup))
			{
				FieldInfo fieldInfo = AccessTools.Field(typeof(UnitSpawner), "_prefabLookupCached");
				if (fieldInfo != null)
				{
					fieldInfo.SetValue(unitSpawner, null);
				}
			}
		}
	}

	private static void ClearMissionConfigCaches()
	{
		Type type = AccessTools.TypeByName("MissionConfigMenuPanel");
		if (!(type == null))
		{
			FieldInfo fieldInfo = AccessTools.Field(type, "_unitKeysByClassCached");
			if (!(fieldInfo == null) && fieldInfo.GetValue(null) is IDictionary dictionary)
			{
				dictionary.Clear();
				MelonLogger.Msg("[L2A7V] cleared the mission customization unit key cache");
			}
		}
	}
}

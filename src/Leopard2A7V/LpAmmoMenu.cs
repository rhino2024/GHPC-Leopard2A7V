using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC.Mission;
using HarmonyLib;
using MelonLoader;

namespace Leopard2A7V;

public static class LpAmmoMenu
{
	private const string Tag = "[L2A7V] ";

	private static bool _announced;

	public static void Install(HarmonyLib.Harmony harmony)
	{
		Type type = AccessTools.TypeByName("MissionConfigMenuPanel");
		if (type == null)
		{
			MelonLogger.Warning("[L2A7V] the mission customiser panel is not in this build, so only its own ammunition rows, if any, will show for this vehicle");
			return;
		}
		MethodInfo methodInfo = AccessTools.Method(type, "GetAmmoSetsByUnit");
		if (methodInfo == null)
		{
			MelonLogger.Warning("[L2A7V] could not find the ammunition rows the mission customiser builds");
			return;
		}
		harmony.Patch(methodInfo, null, new HarmonyMethod(typeof(LpAmmoMenu), "DropRows"));
		MelonLogger.Msg("[L2A7V] hooked the mission customiser's ammunition rows");
	}

	private static void DropRows(string unitKey, ref List<AmmoLogisticsScriptable> __result, ref List<int> preferredIndices)
	{
		if (!(unitKey != "LEO2A7V") && __result != null && __result.Count != 0)
		{
			int count = __result.Count;
			__result = new List<AmmoLogisticsScriptable>();
			preferredIndices = new List<int>();
			if (!_announced)
			{
				_announced = true;
				MelonLogger.Msg("[L2A7V] the mission customiser now offers no ammunition choice for " + unitKey + " (" + count + " donor row(s) hidden); it loads its own list");
			}
		}
	}
}

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GHPC;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpArmor
{
	internal sealed class Zone
	{
		public float Ke;

		public float Ce;

		public string Source;
	}

	private static readonly Dictionary<string, Zone> Table = new Dictionary<string, Zone>(StringComparer.Ordinal)
	{
		{
			"upper glacis array",
			new Zone
			{
				Ke = 360f,
				Ce = 720f,
				Source = "hull_01 629mm, real 600/1200 frontal"
			}
		},
		{
			"lower glacis array",
			new Zone
			{
				Ke = 360f,
				Ce = 720f,
				Source = "hull_01 629mm, real 600/1200 frontal"
			}
		},
		{
			"lower front plate",
			new Zone
			{
				Ke = 70f,
				Ce = 70f,
				Source = "real 70mm steel nose plate"
			}
		},
		{
			"front left array",
			new Zone
			{
				Ke = 120f,
				Ce = 250f,
				Source = "hull_03 strake, real 120mm KE"
			}
		},
		{
			"front right array",
			new Zone
			{
				Ke = 120f,
				Ce = 250f,
				Source = "hull_03 strake, real 120mm KE"
			}
		},
		{
			"skirt left",
			new Zone
			{
				Ke = 120f,
				Ce = 250f,
				Source = "hull_05 strake, real 120mm KE"
			}
		},
		{
			"skirt right",
			new Zone
			{
				Ke = 120f,
				Ce = 250f,
				Source = "hull_05 strake, real 120mm KE"
			}
		},
		{
			"hull left upper",
			new Zone
			{
				Ke = 50f,
				Ce = 50f,
				Source = "real 50mm hull side"
			}
		},
		{
			"hull right upper",
			new Zone
			{
				Ke = 50f,
				Ce = 50f,
				Source = "real 50mm hull side"
			}
		},
		{
			"hull left lower",
			new Zone
			{
				Ke = 50f,
				Ce = 50f,
				Source = "real 50mm hull side"
			}
		},
		{
			"hull right lower",
			new Zone
			{
				Ke = 50f,
				Ce = 50f,
				Source = "real 50mm hull side"
			}
		},
		{
			"hull roof front",
			new Zone
			{
				Ke = 150f,
				Ce = 150f,
				Source = "hull_02 band, real 150mm deck"
			}
		},
		{
			"sponson left",
			new Zone
			{
				Ke = 40f,
				Ce = 40f,
				Source = "real 40mm sponson"
			}
		},
		{
			"sponson right",
			new Zone
			{
				Ke = 40f,
				Ce = 40f,
				Source = "real 40mm sponson"
			}
		},
		{
			"hull floor",
			new Zone
			{
				Ke = 30f,
				Ce = 30f,
				Source = "real 30mm floor"
			}
		},
		{
			"hull floor front",
			new Zone
			{
				Ke = 40f,
				Ce = 40f,
				Source = "real 40mm driver's floor"
			}
		},
		{
			"driver's hatch",
			new Zone
			{
				Ke = 20f,
				Ce = 20f,
				Source = "real 20mm hatch"
			}
		},
		{
			"hull rear",
			new Zone
			{
				Ke = 40f,
				Ce = 40f,
				Source = "real 40mm rear plate"
			}
		},
		{
			"engine deck",
			new Zone
			{
				Ke = 30f,
				Ce = 30f,
				Source = "real 30mm deck"
			}
		},
		{
			"rear grill",
			new Zone
			{
				Ke = 15f,
				Ce = 15f,
				Source = "real 15mm grill"
			}
		},
		{
			"cheek array left A",
			new Zone
			{
				Ke = 640f,
				Ce = 1120f,
				Source = "turret_03 1142mm, real 800/1400 frontal"
			}
		},
		{
			"cheek array right A",
			new Zone
			{
				Ke = 320f,
				Ce = 560f,
				Source = "half of turret_03, real 800/1400 frontal"
			}
		},
		{
			"cheek array right B",
			new Zone
			{
				Ke = 320f,
				Ce = 560f,
				Source = "half of turret_03, real 800/1400 frontal"
			}
		},
		{
			"left cheek plate",
			new Zone
			{
				Ke = 60f,
				Ce = 60f,
				Source = "real 60mm cheek face"
			}
		},
		{
			"right cheek plate",
			new Zone
			{
				Ke = 60f,
				Ce = 60f,
				Source = "real 60mm cheek face"
			}
		},
		{
			"front left plate",
			new Zone
			{
				Ke = 40f,
				Ce = 40f,
				Source = "real 40mm turret face"
			}
		},
		{
			"front right plate",
			new Zone
			{
				Ke = 40f,
				Ce = 40f,
				Source = "real 40mm turret face"
			}
		},
		{
			"mantlet",
			new Zone
			{
				Ke = 350f,
				Ce = 700f,
				Source = "turret_10 515mm, real 350/700"
			}
		},
		{
			"array left",
			new Zone
			{
				Ke = 260f,
				Ce = 340f,
				Source = "turret_04 flank, real 260mm KE"
			}
		},
		{
			"array right",
			new Zone
			{
				Ke = 260f,
				Ce = 340f,
				Source = "turret_04 flank, real 260mm KE"
			}
		},
		{
			"turret roof front",
			new Zone
			{
				Ke = 60f,
				Ce = 60f,
				Source = "real 60mm roof"
			}
		},
		{
			"turret roof mid",
			new Zone
			{
				Ke = 50f,
				Ce = 50f,
				Source = "real 50mm roof"
			}
		},
		{
			"commander's hatch",
			new Zone
			{
				Ke = 30f,
				Ce = 30f,
				Source = "real 30mm hatch"
			}
		},
		{
			"commander's sight",
			new Zone
			{
				Ke = 30f,
				Ce = 30f,
				Source = "real 30mm PERI housing"
			}
		},
		{
			"commander's sight superstructure",
			new Zone
			{
				Ke = 30f,
				Ce = 30f,
				Source = "real 30mm ring"
			}
		},
		{
			"loader's hatch",
			new Zone
			{
				Ke = 25f,
				Ce = 25f,
				Source = "real 25mm hatch"
			}
		},
		{
			"loader's hatch superstructure",
			new Zone
			{
				Ke = 20f,
				Ce = 20f,
				Source = "real 20mm ring"
			}
		},
		{
			"sight front armour",
			new Zone
			{
				Ke = 20f,
				Ce = 20f,
				Source = "real 20mm sight cover"
			}
		},
		{
			"sight channel surfaces",
			new Zone
			{
				Ke = 15f,
				Ce = 15f,
				Source = "real 15mm channel"
			}
		},
		{
			"ballistic glass",
			new Zone
			{
				Ke = 20f,
				Ce = 20f,
				Source = "real 20mm window"
			}
		},
		{
			"bustle armour",
			new Zone
			{
				Ke = 30f,
				Ce = 30f,
				Source = "real 30mm bustle wall"
			}
		},
		{
			"bustle rear",
			new Zone
			{
				Ke = 40f,
				Ce = 40f,
				Source = "real 40mm bustle rear"
			}
		},
		{
			"bustle rack inner",
			new Zone
			{
				Ke = 30f,
				Ce = 30f,
				Source = "real 30mm rack wall"
			}
		},
		{
			"bustle rack outter",
			new Zone
			{
				Ke = 30f,
				Ce = 30f,
				Source = "real 30mm rack wall"
			}
		},
		{
			"turret rear right",
			new Zone
			{
				Ke = 30f,
				Ce = 30f,
				Source = "real 30mm turret rear"
			}
		},
		{
			"turret rear right inner",
			new Zone
			{
				Ke = 30f,
				Ce = 30f,
				Source = "real 30mm turret rear"
			}
		},
		{
			"hydraulic compartment wall",
			new Zone
			{
				Ke = 30f,
				Ce = 30f,
				Source = "real 30mm bulkhead"
			}
		},
		{
			"hydraulic compartment roof",
			new Zone
			{
				Ke = 15f,
				Ce = 15f,
				Source = "real 15mm lid"
			}
		},
		{
			"hydraulic compartment inner wall",
			new Zone
			{
				Ke = 15f,
				Ce = 15f,
				Source = "real 15mm bulkhead"
			}
		}
	};

	private static bool _installed;

	private static readonly HashSet<string> _Attempted = new HashSet<string>(StringComparer.Ordinal);

	private static readonly HashSet<string> _UnmatchedReported = new HashSet<string>(StringComparer.Ordinal);

	private static readonly HashSet<string> _Reported = new HashSet<string>(StringComparer.Ordinal);

	private static readonly HashSet<string> _DumpedIdentities = new HashSet<string>(StringComparer.Ordinal);

	private const int Identities = 40;

	private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		{ "turret cheek composite", "cheek array left A" },
		{ "turret left side composite", "cheek array right A" },
		{ "turret right side composite", "cheek array right B" },
		{ "heavy skirt", "skirt left" },
		{ "upper front plate", "upper glacis array" },
		{ "hull front plate", "lower front plate" },
		{ "hull upper left", "hull left upper" },
		{ "hull upper right", "hull right upper" },
		{ "hull lower left", "hull left lower" },
		{ "hull lower right", "hull right lower" },
		{ "hull front roof", "hull roof front" }
	};

	private static Dictionary<string, string> _folded;

	public static void Install(HarmonyLib.Harmony harmony)
	{
		if (!_installed)
		{
			_installed = true;
			Patch(harmony, "GetNormalThickness", "NormalPostfix");
			Patch(harmony, "GetLosThickness", "LosPostfix");
			Patch(harmony, "SabotRha", "SabotPostfix", property: true);
			Patch(harmony, "HeatRha", "HeatPostfix", property: true);
			Patch(harmony, "SimpleRha", "SimplePostfix", property: true);
		}
	}

	private static void Patch(HarmonyLib.Harmony harmony, string member, string postfix, bool property = false)
	{
		MethodInfo methodInfo = (property ? AccessTools.PropertyGetter(typeof(VariableArmor), member) : AccessTools.Method(typeof(VariableArmor), member));
		if (methodInfo == null)
		{
			MelonLogger.Error("[L2A7V] armour hook target VariableArmor." + member + " not found; that layer keeps the donor value");
		}
		else
		{
			harmony.Patch(methodInfo, null, new HarmonyMethod(typeof(LpArmor), postfix));
		}
	}

	public static bool Apply(GameObject unitRoot)
	{
		if (!LpConfig.WtArmor || unitRoot == null)
		{
			return true;
		}
		if (_Reported.Contains(unitRoot.name))
		{
			return true;
		}
		if (_Attempted.Add(unitRoot.name))
		{
			MelonLogger.Msg("[L2A7V] armour pass attempt on " + unitRoot.name + ": " + unitRoot.GetComponentsInChildren<UniformArmor>(includeInactive: true).Length + " uniform + " + unitRoot.GetComponentsInChildren<VariableArmor>(includeInactive: true).Length + " raycast-measured pieces present, coefficient x" + LpConfig.ArmorScale.ToString("0.00"));
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		int num2 = 0;
		float armorScale = LpConfig.ArmorScale;
		UniformArmor[] componentsInChildren = unitRoot.GetComponentsInChildren<UniformArmor>(includeInactive: true);
		UniformArmor[] array = componentsInChildren;
		foreach (UniformArmor uniformArmor in array)
		{
			if (!uniformArmor.transform.name.StartsWith("L2A7V_OwnArmour", StringComparison.Ordinal) && TryMatch(uniformArmor.transform.name, uniformArmor.Name, out var zone, hashSet, out var via))
			{
				float primarySabotRha = zone.Ke * armorScale;
				float primaryHeatRha = zone.Ce * armorScale;
				stringBuilder.Append("\n[L2A7V]   uniform '").Append(uniformArmor.transform.name).Append("' [")
					.Append(via)
					.Append("] ")
					.Append(uniformArmor.PrimarySabotRha.ToString("0"))
					.Append('/')
					.Append(uniformArmor.PrimaryHeatRha.ToString("0"))
					.Append(" -> ")
					.Append(primarySabotRha.ToString("0"))
					.Append('/')
					.Append(primaryHeatRha.ToString("0"))
					.Append(" (")
					.Append(zone.Source)
					.Append(')');
				uniformArmor.PrimarySabotRha = primarySabotRha;
				uniformArmor.PrimaryHeatRha = primaryHeatRha;
				uniformArmor.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
				uniformArmor.AngleMatters = true;
				num++;
			}
		}
		VariableArmor[] componentsInChildren2 = unitRoot.GetComponentsInChildren<VariableArmor>(includeInactive: true);
		VariableArmor[] array2 = componentsInChildren2;
		foreach (VariableArmor variableArmor in array2)
		{
			if (!variableArmor.transform.name.StartsWith("L2A7V_OwnArmour", StringComparison.Ordinal) && TryMatch(variableArmor.transform.name, variableArmor.Name, out var zone2, hashSet, out var via2))
			{
				float ke = zone2.Ke * armorScale;
				float ce = zone2.Ce * armorScale;
				Mark(variableArmor.gameObject, ke, ce);
				stringBuilder.Append("\n[L2A7V]   measured '").Append(variableArmor.transform.name).Append("' [")
					.Append(via2)
					.Append("] raycast -> ")
					.Append(ke.ToString("0"))
					.Append('/')
					.Append(ce.ToString("0"))
					.Append(" (")
					.Append(zone2.Source)
					.Append(')');
				num2++;
			}
		}
		if (componentsInChildren.Length + componentsInChildren2.Length <= 0)
		{
			return false;
		}
		_Reported.Add(unitRoot.name);
		List<string> list = new List<string>();
		foreach (string key in Table.Keys)
		{
			if (!hashSet.Contains(key))
			{
				list.Add(key);
				if (_UnmatchedReported.Add(key))
				{
					MelonLogger.Warning("[L2A7V] armour zone '" + key + "' matched no piece on " + unitRoot.name);
				}
			}
		}
		MelonLogger.Msg("[L2A7V] protection applied to " + (num + num2) + " piece(s) (" + num + " stored, " + num2 + " measured) out of " + componentsInChildren.Length + " uniform + " + componentsInChildren2.Length + " raycast-measured, armour coefficient x" + armorScale.ToString("0.00") + stringBuilder);
		if (list.Count > 0 && _DumpedIdentities.Add(unitRoot.name))
		{
			DumpIdentities(componentsInChildren, componentsInChildren2);
		}
		return true;
	}

	private static void DumpIdentities(UniformArmor[] stored, VariableArmor[] raycast)
	{
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		int num2 = 0;
		foreach (UniformArmor uniformArmor in stored)
		{
			if (!IsCrewPiece(uniformArmor.ArmorType))
			{
				if (num >= 40)
				{
					num2++;
					continue;
				}
				num++;
				stringBuilder.Append("\n[L2A7V]   id uniform name='").Append(uniformArmor.transform.name).Append("' label='")
					.Append(uniformArmor.Name)
					.Append('\'');
			}
		}
		int num3 = 0;
		int num4 = 0;
		foreach (VariableArmor variableArmor in raycast)
		{
			if (!IsCrewPiece(variableArmor.ArmorType))
			{
				if (num3 >= 40)
				{
					num4++;
					continue;
				}
				num3++;
				stringBuilder.Append("\n[L2A7V]   id measured name='").Append(variableArmor.transform.name).Append("' label='")
					.Append(variableArmor.Name)
					.Append('\'');
			}
		}
		MelonLogger.Warning("[L2A7V] runtime armour identities (name and component label, " + 40 + " per kind, " + num2 + "+" + num4 + " more not shown) while zones are unmatched" + stringBuilder);
	}

	private static bool IsCrewPiece(ArmorCodexScriptable codex)
	{
		if (codex == null)
		{
			return false;
		}
		string name = codex.name;
		if (name != null)
		{
			if (!name.Contains("human") && !name.Contains("flesh"))
			{
				return name.Contains("bone");
			}
			return true;
		}
		return false;
	}

	private static void Mark(GameObject piece, float ke, float ce)
	{
		LpArmorZone lpArmorZone = piece.GetComponent<LpArmorZone>();
		if (lpArmorZone == null)
		{
			lpArmorZone = piece.AddComponent<LpArmorZone>();
		}
		lpArmorZone.Ke = ke;
		lpArmorZone.Ce = ce;
	}

	public static bool TryGetApplied(VariableArmor armor, out float ke, out float ce)
	{
		LpArmorZone lpArmorZone = ((armor == null) ? null : armor.GetComponent<LpArmorZone>());
		ke = ((lpArmorZone == null) ? 0f : lpArmorZone.Ke);
		ce = ((lpArmorZone == null) ? 0f : lpArmorZone.Ce);
		return lpArmorZone != null;
	}

	internal static bool TryZoneForXrayGroup(string groupName, out float ke, out float ce, out string zoneName)
	{
		string text = TokenOf(groupName);
		foreach (KeyValuePair<string, Zone> item in Table)
		{
			if (item.Value.Source != null && item.Value.Source.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				ke = item.Value.Ke;
				ce = item.Value.Ce;
				zoneName = item.Key;
				return true;
			}
		}
		if (text.StartsWith("turret_", StringComparison.OrdinalIgnoreCase))
		{
			ke = 300f;
			ce = 600f;
			zoneName = "turret composite (unlisted)";
			return true;
		}
		if (text.StartsWith("hull_", StringComparison.OrdinalIgnoreCase))
		{
			ke = 200f;
			ce = 400f;
			zoneName = "hull composite (unlisted)";
			return true;
		}
		if (text.StartsWith("firewall", StringComparison.OrdinalIgnoreCase))
		{
			ke = 30f;
			ce = 60f;
			zoneName = "firewall armour";
			return true;
		}
		if (text.StartsWith("spall_liner", StringComparison.OrdinalIgnoreCase))
		{
			ke = 8f;
			ce = 15f;
			zoneName = "spall liner";
			return true;
		}
		ke = (ce = 0f);
		zoneName = null;
		return false;
	}

	private static string TokenOf(string groupName)
	{
		string text = groupName;
		if (text.StartsWith("composite_armor_", StringComparison.OrdinalIgnoreCase))
		{
			text = text.Substring("composite_armor_".Length);
		}
		else if (text.StartsWith("firewall_armor_", StringComparison.OrdinalIgnoreCase))
		{
			text = "firewall_" + text.Substring("firewall_armor_".Length);
		}
		return text.Replace("_xray", string.Empty);
	}

	internal static bool TryResolve(string name, string label, HashSet<string> used, out Zone zone)
	{
		string via;
		return TryMatch(name, label, out zone, used, out via);
	}

	private static bool TryMatch(string name, string label, out Zone zone, HashSet<string> used, out string via)
	{
		zone = null;
		via = null;
		string text = null;
		if (!string.IsNullOrEmpty(name) && Table.TryGetValue(name, out zone))
		{
			text = name;
			via = "name";
		}
		else if (!string.IsNullOrEmpty(label) && Table.TryGetValue(label, out zone))
		{
			text = label;
			via = "label";
		}
		else
		{
			text = NormalizeLookup(name) ?? NormalizeLookup(label);
			if (text != null)
			{
				zone = Table[text];
				via = "folded";
			}
		}
		if (zone == null)
		{
			string text2 = NormalizeLookup(name) ?? NormalizeLookup(label);
			if (text2 != null && Aliases.TryGetValue(text2, out var value) && Table.TryGetValue(value, out zone))
			{
				text = value;
				via = "alias";
			}
		}
		if (text == null)
		{
			return false;
		}
		used.Add(text);
		return true;
	}

	private static string NormalizeLookup(string text)
	{
		string text2 = Fold(text);
		if (text2 == null)
		{
			return null;
		}
		if (_folded == null)
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (string key in Table.Keys)
			{
				string text3 = Fold(key);
				if (text3 != null && !dictionary.ContainsKey(text3))
				{
					dictionary.Add(text3, key);
				}
			}
			_folded = dictionary;
		}
		if (!_folded.TryGetValue(text2, out var value))
		{
			return null;
		}
		return value;
	}

	private static string Fold(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		foreach (char c in text)
		{
			if (char.IsLetterOrDigit(c))
			{
				stringBuilder.Append(char.ToLowerInvariant(c));
			}
			else if (stringBuilder.Length > 0 && stringBuilder[stringBuilder.Length - 1] != ' ')
			{
				stringBuilder.Append(' ');
			}
		}
		if (stringBuilder.Length > 0 && stringBuilder[stringBuilder.Length - 1] == ' ')
		{
			stringBuilder.Length--;
		}
		if (stringBuilder.Length != 0)
		{
			return stringBuilder.ToString();
		}
		return null;
	}

	public static void NormalPostfix(VariableArmor __instance, ResistanceType type, bool forcePhysicalDimensionsOnly, ref float __result)
	{
		if (!forcePhysicalDimensionsOnly)
		{
			LpArmorZone component = __instance.GetComponent<LpArmorZone>();
			if (!(component == null))
			{
				__result = ((type == ResistanceType.KE) ? component.Ke : component.Ce);
			}
		}
	}

	public static void LosPostfix(VariableArmor __instance, ResistanceType type, Vector3 surfaceNormal, Vector3 penetrationVector, bool forcePhysicalDimensionsOnly, ref float __result)
	{
		if (!forcePhysicalDimensionsOnly)
		{
			LpArmorZone component = __instance.GetComponent<LpArmorZone>();
			if (!(component == null))
			{
				float num = ((type == ResistanceType.KE) ? component.Ke : component.Ce);
				float num2 = 90f - Vector3.Angle(-surfaceNormal, penetrationVector);
				__result = num / Mathf.Sin(num2 * (Mathf.PI / 180f));
			}
		}
	}

	public static void SabotPostfix(VariableArmor __instance, ref float __result)
	{
		LpArmorZone component = __instance.GetComponent<LpArmorZone>();
		if (component != null)
		{
			__result = component.Ke;
		}
	}

	public static void HeatPostfix(VariableArmor __instance, ref float __result)
	{
		LpArmorZone component = __instance.GetComponent<LpArmorZone>();
		if (component != null)
		{
			__result = component.Ce;
		}
	}

	public static void SimplePostfix(VariableArmor __instance, ref float __result)
	{
		LpArmorZone component = __instance.GetComponent<LpArmorZone>();
		if (component != null)
		{
			__result = component.Ke;
		}
	}
}

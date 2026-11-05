using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GHPC;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpArmorLog
{
	private static readonly HashSet<int> Logged = new HashSet<int>();

	private static readonly FieldInfo VariableAverageRha = AccessTools.Field(typeof(VariableArmor), "AverageRha");

	private static bool _spoke;

	private const int PerKind = 24;

	public static void Apply(GameObject unitRoot)
	{
		if (unitRoot == null || _spoke)
		{
			return;
		}
		_spoke = true;
		if (Logged.Add(unitRoot.GetInstanceID()))
		{
			UniformArmor[] componentsInChildren = unitRoot.GetComponentsInChildren<UniformArmor>(includeInactive: true);
			VariableArmor[] componentsInChildren2 = unitRoot.GetComponentsInChildren<VariableArmor>(includeInactive: true);
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("[L2A7V] donor protection on ").Append(unitRoot.name).Append(": ")
				.Append(componentsInChildren.Length)
				.Append(" uniform + ")
				.Append(componentsInChildren2.Length)
				.Append(" raycast-measured pieces");
			int num = AppendUniform(stringBuilder, componentsInChildren);
			if (num < componentsInChildren.Length)
			{
				stringBuilder.Append("\n[L2A7V]   (").Append(componentsInChildren.Length - num).Append(" uniform pieces not shown)");
			}
			num = AppendMeasured(stringBuilder, componentsInChildren2);
			if (num < componentsInChildren2.Length)
			{
				stringBuilder.Append("\n[L2A7V]   (").Append(componentsInChildren2.Length - num).Append(" measured pieces not shown)");
			}
			MelonLogger.Msg(stringBuilder.ToString());
		}
	}

	private static int AppendUniform(StringBuilder sb, UniformArmor[] pieces)
	{
		int num = 0;
		foreach (UniformArmor uniformArmor in pieces)
		{
			if (num >= 24)
			{
				break;
			}
			num++;
			if (!IsCrew(uniformArmor.Name))
			{
				sb.Append("\n[L2A7V]   uniform '").Append(Shorten(uniformArmor.Name)).Append("' KE=")
					.Append(uniformArmor.SabotRha.ToString("0"))
					.Append(" CE=")
					.Append(uniformArmor.HeatRha.ToString("0"))
					.Append(" era=")
					.Append(uniformArmor.IsEra)
					.Append(" type=")
					.Append(CodexName(uniformArmor.ArmorType));
			}
		}
		return num;
	}

	private static int AppendMeasured(StringBuilder sb, VariableArmor[] pieces)
	{
		int num = 0;
		foreach (VariableArmor variableArmor in pieces)
		{
			if (num >= 24)
			{
				break;
			}
			num++;
			if (!IsCrew(variableArmor.Name))
			{
				if (LpArmor.TryGetApplied(variableArmor, out var ke, out var ce))
				{
					sb.Append("\n[L2A7V]   measured '").Append(Shorten(variableArmor.transform.name)).Append("' KE=")
						.Append(ke.ToString("0"))
						.Append(" CE=")
						.Append(ce.ToString("0"))
						.Append(" (x-ray)");
					continue;
				}
				float num2 = ((VariableAverageRha == null) ? float.NaN : ((float)VariableAverageRha.GetValue(variableArmor)));
				sb.Append("\n[L2A7V]   measured '").Append(Shorten(variableArmor.Name)).Append("' fallback RHA=")
					.Append(num2.ToString("0"))
					.Append(" type=")
					.Append(CodexName(variableArmor.ArmorType));
			}
		}
		return num;
	}

	private static bool IsCrew(string label)
	{
		if (string.IsNullOrEmpty(label))
		{
			return false;
		}
		string text = label.ToLowerInvariant();
		if (!text.Contains("hitzone"))
		{
			return text.Contains("swat:");
		}
		return true;
	}

	private static string Shorten(string label)
	{
		if (string.IsNullOrEmpty(label))
		{
			return "-";
		}
		if (label.Length > 40)
		{
			return label.Substring(0, 40);
		}
		return label;
	}

	private static string CodexName(ArmorCodexScriptable codex)
	{
		if (!(codex == null))
		{
			return codex.name;
		}
		return "-";
	}
}

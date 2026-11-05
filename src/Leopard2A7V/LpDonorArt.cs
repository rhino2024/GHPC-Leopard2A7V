using System;
using System.Collections.Generic;
using GHPC;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpDonorArt
{
	private static readonly Predicate<string> CrewArtName = (string name) => name.StartsWith("soldier_", StringComparison.OrdinalIgnoreCase);

	private static readonly Predicate<string> CrewFigureName = delegate(string name)
	{
		switch (name)
		{
		default:
			return name == "loader";
		case "driver":
		case "gunner":
		case "commander":
			return true;
		}
	};

	private static readonly HashSet<string> _crewReported = new HashSet<string>();

	private static bool _noMannequinsReported;

	private static bool _noInteriorReported;

	public static void Request(GameObject unitRoot)
	{
		if (!(unitRoot == null) && !(unitRoot.GetComponent<LpDonorArtWatch>() != null))
		{
			unitRoot.AddComponent<LpDonorArtWatch>();
			MelonLogger.Msg("[L2A7V] watching the donor art pass on " + unitRoot.name + " for the gun list, the armour table and the crew figures");
		}
	}

	public static bool HideDonorCrew(GameObject unitRoot)
	{
		if (unitRoot == null || !LpConfig.ShowInterior)
		{
			return true;
		}
		if (LpInterior.Failed)
		{
			if (!_noInteriorReported)
			{
				_noInteriorReported = true;
				MelonLogger.Warning("[L2A7V] the War Thunder compartment failed to load, so the game's own crew figures stay: there is nothing to stand in for them");
			}
			return true;
		}
		if (!LpInterior.Loaded)
		{
			return false;
		}
		if (LpInterior.CrewTriangles <= 0)
		{
			if (!_noMannequinsReported)
			{
				_noMannequinsReported = true;
				MelonLogger.Warning("[L2A7V] the imported compartment carries no crew mannequins (int_crew), so the game's own crew figures stay");
			}
			return true;
		}
		List<Renderer> list = new List<Renderer>();
		List<AarVisual> list2 = new List<AarVisual>();
		List<string> list3 = new List<string>();
		HashSet<AarVisual> hashSet = new HashSet<AarVisual>();
		Renderer[] componentsInChildren = unitRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren)
		{
			if (!(renderer == null) && !LpVisual.IsImportedArt(renderer.transform) && CrewArtName(renderer.name))
			{
				list.Add(renderer);
				AarVisual aarVisual = VisualAbove(renderer.transform);
				if (aarVisual != null && !LpVisual.IsImportedArt(aarVisual.transform) && hashSet.Add(aarVisual))
				{
					list2.Add(aarVisual);
				}
				string item = FigureName(renderer.transform);
				if (!list3.Contains(item))
				{
					list3.Add(item);
				}
			}
		}
		if (list.Count == 0)
		{
			AarVisual[] componentsInChildren2 = unitRoot.GetComponentsInChildren<AarVisual>(includeInactive: true);
			foreach (AarVisual aarVisual2 in componentsInChildren2)
			{
				if (aarVisual2 == null || LpVisual.IsImportedArt(aarVisual2.transform))
				{
					continue;
				}
				string obj = aarVisual2.transform.name.ToLowerInvariant();
				if (CrewFigureName(obj))
				{
					if (hashSet.Add(aarVisual2))
					{
						list2.Add(aarVisual2);
					}
					if (!list3.Contains(aarVisual2.transform.name))
					{
						list3.Add(aarVisual2.transform.name);
					}
				}
			}
		}
		if (list2.Count == 0 && list.Count == 0)
		{
			return false;
		}
		foreach (AarVisual item2 in list2)
		{
			item2.Hidden = true;
		}
		foreach (Renderer item3 in list)
		{
			item3.enabled = false;
		}
		LpCrewSwap lpCrewSwap = unitRoot.GetComponent<LpCrewSwap>();
		if (lpCrewSwap == null)
		{
			lpCrewSwap = unitRoot.AddComponent<LpCrewSwap>();
		}
		lpCrewSwap.Bind(list2, list);
		if (_crewReported.Add(unitRoot.name))
		{
			MelonLogger.Msg($"[L2A7V] x-ray crew replaced: the game's {list3.Count} figure(s) " + string.Format("({0}, {1} soldier renderer(s)) are ", string.Join(", ", list3), list.Count) + "kept out, and the War Thunder mannequins imported with the compartment " + $"(int_crew, {LpInterior.CrewTriangles} triangle(s)) are what the x-ray " + "draws; the hitzones and the anatomy models stay");
		}
		return true;
	}

	private static AarVisual VisualAbove(Transform node)
	{
		Transform transform = node;
		while (transform != null)
		{
			AarVisual component = transform.GetComponent<AarVisual>();
			if (component != null)
			{
				return component;
			}
			transform = transform.parent;
		}
		return null;
	}

	private static string FigureName(Transform node)
	{
		Transform transform = node;
		while (transform != null)
		{
			if (CrewFigureName(transform.name.ToLowerInvariant()))
			{
				return transform.name;
			}
			transform = transform.parent;
		}
		return node.name;
	}
}

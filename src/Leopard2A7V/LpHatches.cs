using System;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpHatches
{
	private const string Tag = "[L2A7V][hatches]";

	public static bool Bind(GameObject unitRoot)
	{
		if (unitRoot == null)
		{
			return true;
		}
		VehicleInfo vehicleInfo = LpFind.VehicleInfo(unitRoot);
		if (vehicleInfo == null)
		{
			return false;
		}
		Transform transform = ImportedRoot(unitRoot);
		if (transform == null)
		{
			return false;
		}
		int num = 0;
		num += TryBind(vehicleInfo.CommanderHatch, transform, "commander");
		num += TryBind(vehicleInfo.GunnerHatch, transform, "gunner");
		num += TryBind(vehicleInfo.DriverHatch, transform, "driver");
		num += TryBind(vehicleInfo.LoaderHatch, transform, "loader");
		if (vehicleInfo.OtherTurretHatches != null)
		{
			AnimatedPart[] otherTurretHatches = vehicleInfo.OtherTurretHatches;
			foreach (AnimatedPart part in otherTurretHatches)
			{
				num += TryBind(part, transform, "turret");
			}
		}
		MelonLogger.Msg("[L2A7V][hatches] bound " + num + " hatch part(s) to the 2A7V art; closed poses retained, no hinge animation claimed");
		if (num == 0)
		{
			DumpCandidates(transform);
		}
		return true;
	}

	private static int TryBind(AnimatedPart part, Transform art, string role)
	{
		if (part == null || part.Transform == null)
		{
			return 0;
		}
		string name = part.Transform.name;
		Transform transform = FindByName(art, name) ?? FindByHatchNumber(art, name) ?? FindByRole(art, role);
		if (transform == null)
		{
			return 0;
		}
		part.Transform = transform;
		if (part.StartTransform == null)
		{
			part.StartTransform = transform;
		}
		if (part.EndTransform == null)
		{
			part.EndTransform = transform;
		}
		MelonLogger.Msg("[L2A7V][hatches] " + role + " hatch '" + name + "' -> '" + transform.name + "' (kept shut)");
		return 1;
	}

	private static Transform MakeHinge(Transform lid, string role, AnimatedPart part)
	{
		LpData.Hatch hatch = LpData.HatchFor(lid.name, role);
		Bounds bounds = LocalBounds(lid);
		Vector3 axis = hatch?.Axis ?? Vector3.forward;
		float degrees = hatch?.Degrees ?? 95f;
		Vector3 position = ((hatch != null && hatch.HasHinge) ? hatch.Hinge : new Vector3(bounds.min.x, bounds.center.y, bounds.center.z));
		Vector3 position2 = lid.TransformPoint(position);
		GameObject gameObject = new GameObject("L2A7V_Hatch_" + lid.name);
		gameObject.transform.SetParent(lid.parent, worldPositionStays: false);
		gameObject.transform.position = position2;
		gameObject.transform.rotation = lid.rotation;
		lid.SetParent(gameObject.transform, worldPositionStays: true);
		gameObject.AddComponent<LpHatchMotion>().Begin(gameObject.transform, axis, degrees, (part == null) ? null : part.Controller, (part == null) ? null : part.FloatParam);
		return gameObject.transform;
	}

	private static Bounds LocalBounds(Transform node)
	{
		bool flag = false;
		Bounds result = new Bounds(Vector3.zero, Vector3.zero);
		MeshFilter[] componentsInChildren = node.GetComponentsInChildren<MeshFilter>(includeInactive: true);
		foreach (MeshFilter meshFilter in componentsInChildren)
		{
			if (!(meshFilter == null) && !(meshFilter.sharedMesh == null))
			{
				Bounds bounds = meshFilter.sharedMesh.bounds;
				Vector3 center = node.InverseTransformPoint(meshFilter.transform.TransformPoint(bounds.center));
				Vector3 vector = node.InverseTransformVector(meshFilter.transform.TransformVector(bounds.size));
				Bounds bounds2 = new Bounds(center, new Vector3(Mathf.Abs(vector.x), Mathf.Abs(vector.y), Mathf.Abs(vector.z)));
				if (!flag)
				{
					result = bounds2;
					flag = true;
				}
				else
				{
					result.Encapsulate(bounds2);
				}
			}
		}
		return result;
	}

	private static Transform FindByName(Transform art, string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		Transform[] componentsInChildren = art.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (transform != null && string.Equals(transform.name, name, StringComparison.OrdinalIgnoreCase))
			{
				return transform;
			}
		}
		return null;
	}

	private static Transform FindByHatchNumber(Transform art, string donorName)
	{
		string text = HatchNumber(donorName);
		if (text == null)
		{
			return null;
		}
		string b = "hatch_" + text;
		Transform[] componentsInChildren = art.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (transform != null && string.Equals(transform.name, b, StringComparison.OrdinalIgnoreCase))
			{
				return transform;
			}
		}
		return null;
	}

	private static string HatchNumber(string name)
	{
		int num = name.IndexOf("hatch", StringComparison.OrdinalIgnoreCase);
		if (num < 0)
		{
			return null;
		}
		int i;
		for (i = num + "hatch".Length; i < name.Length && (name[i] == '_' || name[i] == '-' || name[i] == ' '); i++)
		{
		}
		int num2 = i;
		for (; i < name.Length && char.IsDigit(name[i]); i++)
		{
		}
		if (i == num2)
		{
			return null;
		}
		return name.Substring(num2, i - num2).PadLeft(2, '0');
	}

	private static Transform FindByRole(Transform art, string role)
	{
		Transform[] componentsInChildren = art.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (!(transform == null))
			{
				string name = transform.name;
				if (name.IndexOf("hatch", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf(role, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return transform;
				}
			}
		}
		return null;
	}

	private static Transform ImportedRoot(GameObject unitRoot)
	{
		Transform[] componentsInChildren = unitRoot.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (transform != null && transform.name.Equals("Leopard2A7V_Visual", StringComparison.OrdinalIgnoreCase))
			{
				return transform;
			}
		}
		return null;
	}

	private static void DumpCandidates(Transform art)
	{
		int num = 0;
		Transform[] componentsInChildren = art.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (!(transform == null) && transform.name.IndexOf("hatch", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				MelonLogger.Msg("[L2A7V][hatches]   candidate '" + transform.name + "'");
				if (++num >= 12)
				{
					break;
				}
			}
		}
		if (num == 0)
		{
			MelonLogger.Msg("[L2A7V][hatches]   the imported model has no node with 'hatch' in its name");
		}
		else
		{
			MelonLogger.Msg("[L2A7V][hatches]   " + num + " candidate(s) listed");
		}
	}
}

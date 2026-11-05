using System;
using System.Reflection;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpModules
{
	private struct Target
	{
		internal string Field;

		internal Vector3 Centre;

		internal Vector3 Size;
	}

	private const string Tag = "[L2A7V][modules]";

	private static readonly Target[] Targets = new Target[3]
	{
		new Target
		{
			Field = "EngineHitZone",
			Centre = new Vector3(-2.331f, 1.093f, -0.014f),
			Size = new Vector3(2.838f, 1.036f, 2.079f)
		},
		new Target
		{
			Field = "TransmissionHitZone",
			Centre = new Vector3(-3.099f, 0.949f, -0.027f),
			Size = new Vector3(0.894f, 0.738f, 1.551f)
		},
		new Target
		{
			Field = "RadiatorHitZone",
			Centre = new Vector3(-3.171f, 1.506f, 0f),
			Size = new Vector3(1.014f, 0.309f, 2.027f)
		}
	};

	public static void Apply(GameObject unitRoot)
	{
		if (unitRoot == null)
		{
			return;
		}
		ChassisDamageManager componentInChildren = unitRoot.GetComponentInChildren<ChassisDamageManager>(includeInactive: true);
		if (componentInChildren == null)
		{
			MelonLogger.Msg("[L2A7V][modules] no ChassisDamageManager on " + unitRoot.name + "; hit zones left where the donor put them");
			return;
		}
		Transform transform = Space(unitRoot);
		if (transform == null)
		{
			return;
		}
		int num = 0;
		Target[] targets = Targets;
		for (int i = 0; i < targets.Length; i++)
		{
			Target target = targets[i];
			DestructibleComponent destructibleComponent = Field(componentInChildren, target.Field);
			if (!(destructibleComponent == null) && Move(transform, destructibleComponent, target))
			{
				num++;
			}
		}
		MelonLogger.Msg("[L2A7V][modules] moved " + num + " of " + Targets.Length + " hit zone(s) onto the 2A7V layout");
	}

	private static bool Move(Transform space, DestructibleComponent zone, Target target)
	{
		Transform transform = zone.transform;
		Vector3 vector = LpConfig.FromExport(target.Centre);
		Vector3 vector2 = LpConfig.FromExport(target.Size);
		if ((space.InverseTransformPoint(transform.position) - vector).magnitude < 0.05f)
		{
			return false;
		}
		if (transform.GetComponentInChildren<Renderer>(includeInactive: true) != null)
		{
			BoxCollider boxCollider = transform.GetComponent<BoxCollider>();
			if (boxCollider == null)
			{
				Collider[] components = transform.GetComponents<Collider>();
				for (int i = 0; i < components.Length; i++)
				{
					components[i].enabled = false;
				}
				boxCollider = transform.gameObject.AddComponent<BoxCollider>();
			}
			boxCollider.center = transform.InverseTransformPoint(space.TransformPoint(vector));
			Vector3 vector3 = transform.InverseTransformVector(space.TransformVector(vector2));
			boxCollider.size = new Vector3(Mathf.Abs(vector3.x), Mathf.Abs(vector3.y), Mathf.Abs(vector3.z));
			boxCollider.isTrigger = false;
			boxCollider.enabled = true;
			MelonLogger.Msg("[L2A7V][modules] moved the collider of '" + transform.name + "' (kept its art in place)");
			return true;
		}
		transform.position = space.TransformPoint(vector);
		Vector3 vector4 = space.TransformVector(vector2);
		vector4 = new Vector3(Mathf.Abs(vector4.x), Mathf.Abs(vector4.y), Mathf.Abs(vector4.z));
		BoxCollider boxCollider2 = transform.GetComponent<BoxCollider>();
		if (boxCollider2 == null)
		{
			Collider[] components = transform.GetComponents<Collider>();
			for (int i = 0; i < components.Length; i++)
			{
				components[i].enabled = false;
			}
			boxCollider2 = transform.gameObject.AddComponent<BoxCollider>();
		}
		boxCollider2.size = vector4;
		boxCollider2.isTrigger = false;
		boxCollider2.enabled = true;
		return true;
	}

	private static DestructibleComponent Field(ChassisDamageManager chassis, string name)
	{
		FieldInfo field = typeof(ChassisDamageManager).GetField(name);
		if (!(field == null))
		{
			return field.GetValue(chassis) as DestructibleComponent;
		}
		return null;
	}

	private static Transform Space(GameObject unitRoot)
	{
		Transform[] componentsInChildren = unitRoot.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (transform != null && transform.name.Equals("leo2_rig", StringComparison.OrdinalIgnoreCase))
			{
				return transform;
			}
		}
		return unitRoot.transform;
	}
}

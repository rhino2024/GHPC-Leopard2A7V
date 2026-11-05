using System;
using System.Reflection;
using GHPC;
using GHPC.Equipment;
using GHPC.Player;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpSmoke
{
	private const string Tag = "[L2A7V][smoke]";

	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

	private const float MuzzleOffset = 0.1f;

	private const float LeftFace = -1.56f;

	private const float RightFace = 1.58f;

	private const float LowerRow = 1.9f;

	private const float UpperRow = 2.3f;

	private static readonly float[] Columns = new float[4] { -0.02f, -0.21f, -0.4f, -0.59f };

	private static Type _playerInputType;

	private static PropertyInfo _currentPlayerUnitProperty;

	private static FieldInfo _currentPlayerUnitField;

	public static bool Apply(GameObject unitRoot)
	{
		if (unitRoot == null)
		{
			return true;
		}
		try
		{
			return ApplyUnsafe(unitRoot);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V][smoke] smoke setup will retry; donor launcher left as it is: " + ex.Message);
			return false;
		}
	}

	private static bool ApplyUnsafe(GameObject unitRoot)
	{
		VehicleSmokeManager componentInChildren = unitRoot.GetComponentInChildren<VehicleSmokeManager>(includeInactive: true);
		if (componentInChildren == null)
		{
			MelonLogger.Warning("[L2A7V][smoke] no VehicleSmokeManager on " + unitRoot.name + "; donor launcher setup deferred");
			return false;
		}
		FieldInfo field = componentInChildren.GetType().GetField("_hasSmokeLauncher", BindingFlags.Instance | BindingFlags.NonPublic);
		FieldInfo field2 = componentInChildren.GetType().GetField("_requiredActiveLauncherObject", BindingFlags.Instance | BindingFlags.NonPublic);
		FieldInfo field3 = componentInChildren.GetType().GetField("_smokePrefab", BindingFlags.Instance | BindingFlags.NonPublic);
		if (field != null && !(bool)field.GetValue(componentInChildren))
		{
			field.SetValue(componentInChildren, true);
			MelonLogger.Warning("[L2A7V][smoke] the donor reports no smoke launcher; switched the flag on so the donor's own launcher answers the key");
		}
		GameObject gameObject = ((field2 == null) ? null : (field2.GetValue(componentInChildren) as GameObject));
		if (gameObject != null && !gameObject.activeInHierarchy)
		{
			gameObject.SetActive(value: true);
			Renderer[] componentsInChildren = gameObject.GetComponentsInChildren<Renderer>(includeInactive: true);
			foreach (Renderer renderer in componentsInChildren)
			{
				if (renderer != null)
				{
					renderer.enabled = false;
				}
			}
			MelonLogger.Warning("[L2A7V][smoke] the launcher gate '" + gameObject.name + "' was inactive (that alone makes the whole launcher report itself missing); switched back on, donor art left off");
		}
		FieldInfo field4 = componentInChildren.GetType().GetField("_smokeSlots", BindingFlags.Instance | BindingFlags.NonPublic);
		Array array = ((field4 == null) ? null : (field4.GetValue(componentInChildren) as Array));
		int num = Reposition(componentInChildren, unitRoot, array);
		// Keep GHPC's input and initialization as the sole owner of firing.
		// The old secondary input component is excluded from this assembly.
		LpSmokeNativeInput.Ensure(unitRoot, componentInChildren);
		GameObject gameObject2 = ((field3 == null) ? null : (field3.GetValue(componentInChildren) as GameObject));
		MelonLogger.Msg("[L2A7V][smoke] launcher: slots=" + (array?.Length ?? 0) + " moved=" + num + " gate=" + ((gameObject == null) ? "-" : gameObject.name) + " prefab=" + ((gameObject2 == null) ? "-" : gameObject2.name) + " HasSmokeLauncher=" + componentInChildren.HasSmokeLauncher);
		return true;
	}

	private static int Reposition(VehicleSmokeManager manager, GameObject unitRoot, Array slots)
	{
		if (slots == null || slots.Length == 0)
		{
			return 0;
		}
		Transform transform = FindDeep(unitRoot.transform, "L2A7V_TurretPivot");
		if (transform == null)
		{
			return 0;
		}
		int num = 0;
		for (int i = 0; i < slots.Length && i < 16; i++)
		{
			object value = slots.GetValue(i);
			if (value != null)
			{
				FieldInfo field = value.GetType().GetField("SpawnLocation");
				Transform transform2 = ((field == null) ? null : (field.GetValue(value) as Transform));
				if (!(transform2 == null))
				{
					Vector3 vector = TubePosition(i);
					Vector3 position = transform.TransformPoint(vector - LpConfig.TurretAxis);
					transform2.SetParent(transform, worldPositionStays: true);
					transform2.position = position;
					num++;
				}
			}
		}
		return num;
	}

	private static Vector3 TubePosition(int i)
	{
		int num = i % 16 / 4;
		int num2 = i % 4;
		float z = Columns[num2];
		float y = ((num == 1 || num == 3) ? 2.3f : 1.9f);
		return new Vector3((num < 2) ? (-1.56f) : 1.58f, y, z);
	}

	internal static bool WirePlayerInput(VehicleSmokeManager manager, GameObject unitRoot)
	{
		PlayerInput instance = PlayerInput.Instance;
		if (instance == null || manager == null)
		{
			return false;
		}
		FieldInfo field = SmokeManagerField(instance);
		if (field == null)
		{
			return false;
		}
		if ((object)field.GetValue(instance) == manager)
		{
			return true;
		}
		Unit unit = CurrentPlayerUnit(instance);
		if (unit == null)
		{
			return false;
		}
		if (unit.gameObject != unitRoot)
		{
			return false;
		}
		if (unit.GetComponentInChildren<VehicleSmokeManager>(includeInactive: true) != manager)
		{
			return false;
		}
		field.SetValue(instance, manager);
		MelonLogger.Msg("[L2A7V][smoke] the player's smoke key now points at this vehicle's launcher");
		return true;
	}

	internal static void ReleasePlayerInput(VehicleSmokeManager manager, GameObject unitRoot)
	{
		PlayerInput instance = PlayerInput.Instance;
		FieldInfo field = ((instance == null) ? null : SmokeManagerField(instance));
		if (field == null || (object)field.GetValue(instance) != manager)
		{
			return;
		}
		Unit unit = CurrentPlayerUnit(instance);
		if (unit == null || unit.gameObject != unitRoot)
		{
			field.SetValue(instance, null);
		}
	}

	private static FieldInfo SmokeManagerField(PlayerInput input)
	{
		return input.GetType().GetField("_smokeLauncherManager", BindingFlags.Instance | BindingFlags.NonPublic);
	}

	internal static Unit CurrentPlayerUnit(PlayerInput input)
	{
		if (input == null)
		{
			return null;
		}
		Type type = input.GetType();
		if (_playerInputType != type)
		{
			_playerInputType = type;
			_currentPlayerUnitProperty = type.GetProperty("CurrentPlayerUnit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			_currentPlayerUnitField = type.GetField("CurrentPlayerUnit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		}
		try
		{
			PropertyInfo property = _currentPlayerUnitProperty;
			if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
			{
				return property.GetValue(input, null) as Unit;
			}
		}
		catch (Exception)
		{
		}
		try
		{
			FieldInfo field = _currentPlayerUnitField;
			return (field == null) ? null : (field.GetValue(input) as Unit);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static Transform FindDeep(Transform root, string name)
	{
		if (root == null)
		{
			return null;
		}
		if (root.name == name)
		{
			return root;
		}
		for (int i = 0; i < root.childCount; i++)
		{
			Transform transform = FindDeep(root.GetChild(i), name);
			if (transform != null)
			{
				return transform;
			}
		}
		return null;
	}
}

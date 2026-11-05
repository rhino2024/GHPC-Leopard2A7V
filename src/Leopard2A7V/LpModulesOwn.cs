using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Equipment;
using GHPC.PhysicsHelpers;
using GHPC.Utility;
using GHPC.Thermals;
using GHPC.Vehicle;
using GHPC.Weapons;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpModulesOwn
{
	private sealed class BuildState : MonoBehaviour
	{
		internal bool CrewLogged;
		internal bool FireControlArranged;
		internal string LastSummary;
		internal string LastWaiting;
	}

	private sealed class Spec
	{
		internal string Name;

		internal Vector3 Centre;

		internal Vector3 Size;

		internal float Health;

		internal float Threshold;
	}

	public class CookOffTimer : MonoBehaviour
	{
		private AmmoRack _rack;

		private float _remaining;

		private float _distance;

		private int _rackCount;

		internal void Begin(AmmoRack rack, float delay, float distance, int rackCount)
		{
			_rack = rack;
			_remaining = delay;
			_distance = distance;
			_rackCount = rackCount;
		}

		private void Update()
		{
			_remaining -= Time.deltaTime;
			if (!(_remaining > 0f))
			{
				if (_rack != null && !_rack.OnFire)
				{
					_rack.NotifyRackFire();
					MelonLogger.Msg("[L2A7V][modules] cook-off started in '" + _rack.GetFriendlyName() + "' (" + _distance.ToString("0.0") + " m from the hit, " + _rackCount + " rack(s) on the vehicle)");
				}
				UnityEngine.Object.Destroy(base.gameObject);
			}
		}
	}

	private const string Tag = "[L2A7V][modules]";

	private const float IgnitionDelay = 1.2f;

	private static readonly Spec[] Racks = new Spec[2]
	{
		new Spec
		{
			Name = "ammo_hull",
			Centre = new Vector3(1.835f, 1.089f, -0.352f),
			Size = new Vector3(0.848f, 0.706f, 1.122f),
			Health = 120f,
			Threshold = 25f
		},
		new Spec
		{
			Name = "ammo_turret",
			Centre = new Vector3(-1.373f, 2.151f, -0.644f),
			Size = new Vector3(0.56f, 0.523f, 0.798f),
			Health = 100f,
			Threshold = 25f
		}
	};

	public static bool Build(GameObject unitRoot)
	{
		if (unitRoot == null)
		{
			return true;
		}
		Transform transform = Space(unitRoot);
		if (transform == null)
		{
			return false;
		}
		ChassisDamageManager componentInChildren = unitRoot.GetComponentInChildren<ChassisDamageManager>(includeInactive: true);
		VehicleInfo vehicleInfo = LpFind.VehicleInfo(unitRoot);
		if (vehicleInfo == null || componentInChildren == null)
		{
			return false;
		}
		if (componentInChildren.EngineHitZone == null)
		{
			return false;
		}
		BuildState state = unitRoot.GetComponent<BuildState>();
		if (state == null)
		{
			state = unitRoot.AddComponent<BuildState>();
		}
		int layer = CodeUtils.LAYER_INDEX_RAYCAST_ONLY;
		int num = 0;
		bool ready = true;
		string waiting = null;
		Spec[] racks = Racks;
		foreach (Spec spec in racks)
		{
			string objectName = ((spec.Name == "ammo_hull") ? "hull spare rack" : "ready rack");
			AmmoRack rack = FindMappedRack(vehicleInfo, spec.Name, objectName);
			if (rack == null)
			{
				ready = false;
				continue;
			}
			Transform space2 = ModuleSpace(rack, transform);
			if (space2 == null)
			{
				waiting = "'" + objectName + "': no native RaycastColliderGroup ancestor";
				ready = false;
			}
			else if (!BuildOne(space2, rack, spec, layer))
			{
				waiting = "'" + objectName + "': damage-zone creation failed";
				ready = false;
			}
			else
			{
				num++;
			}
		}
		int num2 = ArmPowerpack(componentInChildren);
		int num3 = ArmGunAndFireControl(unitRoot);
		if (LpConfig.AddHeatSource)
		{
			AddEngineHeat(transform);
		}
		if (!state.CrewLogged)
		{
			LogCrew(unitRoot);
			state.CrewLogged = true;
		}
		string summary = "built " + num + " mapped ammunition zone(s), armed " + num2 + " powerpack zone(s) and " + num3 + " gun / fire control module(s)";
		if (state.LastSummary != summary)
		{
			MelonLogger.Msg("[L2A7V][modules] " + summary);
			state.LastSummary = summary;
		}
		if (!ready && state.LastWaiting != waiting)
		{
			MelonLogger.Warning("[L2A7V][modules] waiting for " + (waiting ?? "a unique named main-gun rack") + "; no nearest-rack, empty fallback or rig-parent substitution will be used");
			state.LastWaiting = waiting;
		}
		return ready;
	}

	private static bool BuildOne(Transform space, AmmoRack rack, Spec spec, int layer)
	{
		if (AlreadyThere(space, spec.Name))
		{
			return true;
		}
		GameObject go = new GameObject("L2A7V_Module " + spec.Name);
		// Keep the donor's LateFollow hierarchy intact. Authored coordinates are
		// measured from leo2_rig, not the translated/rotated ready-rack pivot.
		Transform rig = space.root.Find("leo2_rig");
		if (rig == null)
		{
			rig = Space(space.root.gameObject);
		}
		if (rig == null)
		{
			UnityEngine.Object.Destroy(go);
			return false;
		}
		go.SetActive(value: false);
		go.transform.SetParent(space, worldPositionStays: false);
		go.transform.position = rig.TransformPoint(LpConfig.FromExport(spec.Centre));
		go.transform.rotation = space.rotation;
		go.transform.localScale = Vector3.one;
		go.layer = layer;
		try
		{
			go.tag = "Penetrable";
		}
		catch (UnityException)
		{
		}
		BoxCollider boxCollider = go.AddComponent<BoxCollider>();
		boxCollider.size = LpConfig.FromExport(spec.Size);
		boxCollider.isTrigger = false;
		GHPC.Equipment.DestructibleComponent destructibleComponent = go.AddComponent<GHPC.Equipment.DestructibleComponent>();
		destructibleComponent.SetName("2A7V " + spec.Name);
		Configure(destructibleComponent, spec.Health, spec.Threshold);
		destructibleComponent.ComponentCollision = boxCollider;
		destructibleComponent.HealthChanged += delegate(IDestructible source, float percent)
		{
			MelonLogger.Msg("[L2A7V][modules] '" + spec.Name + "' health " + (percent * 100f).ToString("0") + "%");
		};
		destructibleComponent.Destroyed += delegate
		{
			CookOff(rack, spec.Name);
		};
		AarVisual aarVisual = go.AddComponent<AarVisual>();
		aarVisual.HideUntilAar = true;
		Material material = LpArmorOwn.XrayMaterial();
		if (material != null)
		{
			aarVisual.SwitchMaterials = true;
			aarVisual.AarMaterial = material;
		}
		go.SetActive(value: true);
		RaycastColliderGroup.AddNewColliderSetToParentGroup(go.transform);
		aarVisual.UpdateVisibility();
		MelonLogger.Msg("[L2A7V][modules] module '" + spec.Name + "' mapped to '" + rack.name + "' under '" + space.name + "' at " + go.transform.position.ToString("0.00") + " health " + spec.Health + " layer " + layer);
		return true;
	}

	private static AmmoRack FindMappedRack(VehicleInfo info, string zoneName, string objectName)
	{
		if (info.AmmoRacks == null)
		{
			return null;
		}
		AmmoRack found = null;
		AmmoRack.AmmoRackLocation expected = ((zoneName == "ammo_hull") ? AmmoRack.AmmoRackLocation.Hull : AmmoRack.AmmoRackLocation.Turret);
		foreach (AmmoRack rack in info.AmmoRacks)
		{
			if (rack == null || rack.Location != expected || !string.Equals(rack.name, objectName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if (found != null)
			{
				return null;
			}
			found = rack;
		}
		return found;
	}

	private static Transform ModuleSpace(AmmoRack rack, Transform rig)
	{
		Transform parent = rack.transform.parent;
		while (parent != null)
		{
			if (parent.GetComponent<RaycastColliderGroup>() != null)
			{
				// These nodes already belong to the donor's collider group.
				Transform transform = parent.Find("LEO2_aar_turret") ?? parent.Find("LEO2_aar_hull");
				if (transform == null)
				{
					transform = parent.Find("AAR") ?? parent;
				}
				return transform;
			}
			parent = parent.parent;
		}
		return null;
	}

	private static void CookOff(AmmoRack rack, string name)
	{
		MelonLogger.Msg("[L2A7V][modules] mapped ammunition zone '" + name + "' destroyed");
		if (rack == null)
		{
			MelonLogger.Warning("[L2A7V][modules] no explicit rack mapping; no fire will be invented");
			return;
		}
		if (rack.OnFire)
		{
			return;
		}
		if (rack.HasBlowoutPanels)
		{
			int damaged = 0;
			RackRound[] inventory = rack.RackInventory;
			if (inventory != null)
			{
				for (int i = 0; i < inventory.Length && damaged < 3; i++)
				{
					if (inventory[i] != null && !inventory[i].Damaged)
					{
						rack.DamageRoundBySlot(i);
						damaged++;
					}
				}
			}
			MelonLogger.Msg("[L2A7V][modules] '" + rack.GetFriendlyName() + "' has blowout panels; " + damaged + " occupied, undamaged round slot(s) processed (not a claim of crew protection)");
			return;
		}
		new GameObject("L2A7V_CookOffTimer").AddComponent<CookOffTimer>().Begin(rack, IgnitionDelay, 0f, 1);
	}

	private static bool AlreadyThere(Transform space, string name)
	{
		Transform[] componentsInChildren = space.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (transform != null && transform.name == "L2A7V_Module " + name)
			{
				GHPC.Equipment.DestructibleComponent component = transform.GetComponent<GHPC.Equipment.DestructibleComponent>();
				BoxCollider collider = transform.GetComponent<BoxCollider>();
				if (component != null && collider != null && component.ComponentCollision == collider && transform.gameObject.layer == CodeUtils.LAYER_INDEX_RAYCAST_ONLY)
				{
					RaycastColliderGroup.AddNewColliderSetToParentGroup(transform);
					return true;
				}
				// Destroy is deferred until end of frame. Hide the old object and
				// make it inactive so a retry in the same frame cannot adopt it.
				transform.name = "L2A7V_IncompleteModule " + name;
				transform.gameObject.SetActive(value: false);
				// Do not let an exception during construction leave a named object
				// that falsely makes every later retry look complete.
				MelonLogger.Warning("[L2A7V][modules] removing an incomplete '" + name + "' damage zone before retrying");
				UnityEngine.Object.Destroy(transform.gameObject);
				return false;
			}
		}
		return false;
	}


	private static int ArmPowerpack(ChassisDamageManager chassis)
	{
		if (chassis == null)
		{
			return 0;
		}
		int num = 0;
		if (Arm(chassis.EngineHitZone, 200f, 25f))
		{
			num++;
		}
		if (Arm(chassis.TransmissionHitZone, 160f, 25f))
		{
			num++;
		}
		if (Arm(chassis.RadiatorHitZone, 100f, 20f))
		{
			num++;
		}
		return num;
	}

	private static int ArmGunAndFireControl(GameObject unitRoot)
	{
		int num = 0;
		WeaponSystem[] componentsInChildren = unitRoot.GetComponentsInChildren<WeaponSystem>(includeInactive: true);
		foreach (WeaponSystem obj in componentsInChildren)
		{
			if (Arm(obj.BarrelHitZone, 120f, 25f))
			{
				num++;
			}
			if (Arm(obj.BreechHitZone, 120f, 25f))
			{
				num++;
			}
		}
		FireControlSystem componentInChildren = unitRoot.GetComponentInChildren<FireControlSystem>(includeInactive: true);
		BuildState state = unitRoot.GetComponent<BuildState>();
		if (componentInChildren != null)
		{
			if (Arm(componentInChildren.ComputerComponent, 80f, 20f))
			{
				num++;
			}
			if (Arm(componentInChildren.LaserComponent, 60f, 15f))
			{
				num++;
			}
			Transform transform = Turret(unitRoot);
			if (transform != null && !state.FireControlArranged)
			{
				MoveZone(componentInChildren.ComputerComponent, transform.position + transform.up * 0.35f, transform.rotation, new Vector3(0.45f, 0.3f, 0.45f));
				MoveZone(componentInChildren.LaserComponent, transform.position + transform.up * 0.15f + transform.right * 0.4f, transform.rotation, new Vector3(0.25f, 0.2f, 0.3f));
				MelonLogger.Msg("[L2A7V][modules] fire control: computer health " + ((componentInChildren.ComputerComponent == null) ? "n/a" : componentInChildren.ComputerComponent.Health.ToString("0")) + ", laser health " + ((componentInChildren.LaserComponent == null) ? "n/a" : componentInChildren.LaserComponent.Health.ToString("0")));
				state.FireControlArranged = true;
			}
		}
		return num;
	}

	private static void LogCrew(GameObject unitRoot)
	{
		int num = 0;
		MonoBehaviour[] componentsInChildren = unitRoot.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
		foreach (MonoBehaviour monoBehaviour in componentsInChildren)
		{
			if (!(monoBehaviour == null) && !(monoBehaviour.GetType().Name != "CrewMember"))
			{
				num++;
				MelonLogger.Msg("[L2A7V][modules] crew '" + monoBehaviour.name + "' at " + monoBehaviour.transform.position.ToString("0.00"));
			}
		}
		MelonLogger.Msg("[L2A7V][modules] " + num + " crew member(s) in the unit");
	}

	private static Transform Turret(GameObject unitRoot)
	{
		Transform[] componentsInChildren = unitRoot.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (transform != null && transform.name.Equals("turret", StringComparison.OrdinalIgnoreCase))
			{
				return transform;
			}
		}
		return null;
	}

	private static bool Arm(GHPC.Equipment.DestructibleComponent module, float health, float threshold)
	{
		if (module == null)
		{
			return false;
		}
		if (module.Health > 0f)
		{
			return false;
		}
		Configure(module, health, threshold);
		return true;
	}

	private static void Configure(Component component, float health, float threshold)
	{
		if (!(component == null))
		{
			Type type = component.GetType();
			SetFloat(type, component, "_health", health, BindingFlags.Instance | BindingFlags.NonPublic);
			SetFloat(type, component, "_fullHealth", health, BindingFlags.Instance | BindingFlags.NonPublic);
			SetFloat(type, component, "_damageThreshold", threshold, BindingFlags.Instance | BindingFlags.NonPublic);
			SetFloat(type, component, "_pressureTolerance", 1f, BindingFlags.Instance | BindingFlags.NonPublic);
			FieldInfo field = type.GetField("_children", BindingFlags.Instance | BindingFlags.NonPublic);
			if (field != null && field.GetValue(component) == null)
			{
				field.SetValue(component, Activator.CreateInstance(field.FieldType));
			}
			SetFloat(type, component, "Health", health, BindingFlags.Instance | BindingFlags.Public);
			SetFloat(type, component, "MinimumRhaTrigger", threshold, BindingFlags.Instance | BindingFlags.Public);
		}
	}

	private static void SetFloat(Type type, object target, string name, float value, BindingFlags flags)
	{
		FieldInfo field = type.GetField(name, flags);
		if (!(field == null) && !(field.FieldType != typeof(float)))
		{
			field.SetValue(target, value);
		}
	}

	private static void MoveZone(Component component, Vector3 position, Quaternion rotation, Vector3 size)
	{
		if (component == null)
		{
			return;
		}
		Transform transform = component.transform;
		BoxCollider existing = transform.GetComponent<BoxCollider>();
		if (existing != null && existing.enabled && (transform.position - position).sqrMagnitude < 0.000001f && (existing.size - size).sqrMagnitude < 0.000001f)
		{
			return;
		}
		transform.position = position;
		transform.rotation = rotation;
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
		boxCollider.size = size;
		boxCollider.isTrigger = false;
		boxCollider.enabled = true;
	}

	private static void AddEngineHeat(Transform space)
	{
		if (space.Find("L2A7V_Heat_Engine") != null)
		{
			return;
		}
		GameObject gameObject = new GameObject("L2A7V_Heat_Engine");
		gameObject.transform.SetParent(space, worldPositionStays: false);
		gameObject.transform.position = space.TransformPoint(LpConfig.FromExport(new Vector3(-2.331f, 1.093f, -0.014f)));
		gameObject.AddComponent<HeatSource>().Heat = 0.85f;
		MelonLogger.Msg("[L2A7V][modules] engine heat source at " + gameObject.transform.position.ToString("0.0"));
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
		return null;
	}
}

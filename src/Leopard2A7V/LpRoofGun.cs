using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Crew;
using GHPC.Equipment;
using GHPC.Equipment.Optics;
using GHPC.Player;
using GHPC.Utility;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using Reticle;
using UnityEngine;

namespace Leopard2A7V;

public static class LpRoofGun
{
	public const string WeaponName = "7.62 mm machine gun MG3 (AA)";

	private static readonly string[] YawParts = new string[1] { "mg_aa_mount_h" };

	private static readonly string[] PitchParts = new string[3] { "mg_aa_mount_v", "bone_mg_aa_v", "ammo" };

	private static readonly FieldInfo WeaponExclusiveItems = AccessTools.Field(typeof(WeaponSystem), "ExclusiveItems");

	private static readonly FieldInfo WeaponCodexField = AccessTools.Field(typeof(WeaponSystem), "CodexEntry");

	private static readonly FieldInfo SlotReparentTarget = AccessTools.Field(typeof(CameraSlot), "_reparentTarget");

	private static readonly FieldInfo DesignatedSlots = AccessTools.Field(typeof(Unit), "_designatedCameraSlots");

	private static readonly MethodInfo RescanCamSlots = AccessTools.Method(typeof(CameraManager), "RescanCamSlots");

	private static WeaponSystemCodexScriptable _codex;

	public static bool IsRoofGunPart(string objectName)
	{
		if (!Matches(YawParts, objectName))
		{
			return Matches(PitchParts, objectName);
		}
		return true;
	}

	public static void Apply(GameObject unitRoot, LpVisual.Assembly art, Vector3 ring)
	{
		if (!LpConfig.AddRoofGun || unitRoot == null || art == null || art.Turret == null || art.RoofGunYaw != null)
		{
			return;
		}
		WeaponsManager component = unitRoot.GetComponent<WeaponsManager>();
		Transform transform = FindCoaxScripts(unitRoot);
		if (component == null || transform == null)
		{
			MelonLogger.Warning("[L2A7V] the roof gun stays decoration: " + ((component == null) ? "the unit has no weapons manager" : "the donor has no coaxial machine gun to clone"));
			return;
		}
		Vector3 roofGunYaw = LpConfig.RoofGunYaw;
		Vector3 roofGunTrunnion = LpConfig.RoofGunTrunnion;
		Transform transform2 = LpVisual.MakePivot(art.Turret.parent, "L2A7V_RoofGunYaw", roofGunYaw - ring);
		transform2.gameObject.SetActive(value: false);
		Transform transform3 = LpVisual.MakePivot(transform2, "L2A7V_RoofGunPitch", roofGunTrunnion - roofGunYaw);
		art.RoofGunYaw = transform2;
		art.RoofGunPitch = transform3;
		GameObject gameObject = new GameObject("L2A7V_RoofGunFireControl");
		gameObject.transform.SetParent(transform3, worldPositionStays: false);
		GameObject gameObject2 = UnityEngine.Object.Instantiate(transform.gameObject, transform3, worldPositionStays: false);
		gameObject2.name = "L2A7V_RoofGunScripts";
		DetachReparenters(gameObject2);
		WeaponSystem componentInChildren = gameObject2.GetComponentInChildren<WeaponSystem>(includeInactive: true);
		if (componentInChildren == null)
		{
			MelonLogger.Warning("[L2A7V] the copied coaxial weapon is gone, the mount stays decoration");
			return;
		}
		AmmoFeed component2 = componentInChildren.GetComponent<AmmoFeed>();
		GHPC.Weapons.AmmoRack ammoRack = ((component2 != null) ? component2.ReadyRack : null);
		if (ammoRack == null)
		{
			MelonLogger.Warning("[L2A7V] the copied coaxial feed lost its belt rack, the mount stays decoration");
			return;
		}
		componentInChildren.gameObject.name = "MG3 (AA)";
		componentInChildren.transform.localPosition = LpConfig.RoofGunMuzzle - roofGunTrunnion;
		ammoRack.transform.localPosition = LpConfig.RoofGunRackPoint - roofGunTrunnion;
		ammoRack.Name = "Roof MG rack";
		componentInChildren.DamageName = "Roof MG";
		if (WeaponExclusiveItems != null)
		{
			WeaponExclusiveItems.SetValue(componentInChildren, new GameObject[0]);
		}
		AssignCodex(componentInChildren);
		AimablePlatform aimablePlatform = AddPlatform(transform2, "Roof MG traverse", EnumHelpers.Axis3.Y, 25f, 15f, Vector2.zero, reverseEuler: false);
		AimablePlatform aimablePlatform2 = AddPlatform(transform3, "Roof MG elevation", EnumHelpers.Axis3.X, 20f, 12f, new Vector2(-10f, 75f), reverseEuler: true);
		aimablePlatform2.ParentPlatform = aimablePlatform;
		aimablePlatform2.ProtectFromBadProjection = true;
		ConfigureFcs(gameObject, transform2, aimablePlatform, aimablePlatform2, componentInChildren);
		FireControlSystem component3 = gameObject.GetComponent<FireControlSystem>();
		MeshRenderer meshRenderer = LpVisual.AttachParts(transform2, "RoofGunMount", IsYawPart, roofGunYaw);
		MeshReceiverCheck(meshRenderer, "mount");
		art.RoofGunYawArt = ((meshRenderer != null) ? meshRenderer.transform : null);
		MeshRenderer meshRenderer2 = LpVisual.AttachParts(transform3, "RoofGunReceiver", IsPitchPart, roofGunTrunnion);
		MeshReceiverCheck(meshRenderer2, "receiver");
		art.RoofGunPitchArt = ((meshRenderer2 != null) ? meshRenderer2.transform : null);
		CameraSlot cameraSlot = BuildSight(transform3, FindDonorSight(unitRoot), componentInChildren, component3, LpConfig.RoofGunSight - roofGunTrunnion);
		transform2.gameObject.SetActive(value: true);
		gameObject2.transform.SetParent(transform3, worldPositionStays: false);
		gameObject2.transform.localPosition = Vector3.zero;
		gameObject2.transform.localRotation = Quaternion.identity;
		RefreshCaches(unitRoot, cameraSlot, AppendWeapon(component, componentInChildren, component3));
		LpRoofGunView.Attach(unitRoot, componentInChildren, cameraSlot);
		Vector3 position = componentInChildren.transform.position;
		MelonLogger.Msg("[L2A7V] roof gun armed: '7.62 mm machine gun MG3 (AA)' on a cloned coaxial MG3, " + string.Format("{0} rounds, sight {1}; ", ammoRack.ClipCapacity, (cameraSlot == null) ? "missing" : "ready") + $"muzzle at ({position.x:F2},{position.y:F2},{position.z:F2}) world, under " + ((gameObject2.transform.parent == null) ? "nothing" : gameObject2.transform.parent.name));
	}

	private static bool IsYawPart(ObjGeometry part)
	{
		return Matches(YawParts, part.ObjectName);
	}

	private static bool IsPitchPart(ObjGeometry part)
	{
		return Matches(PitchParts, part.ObjectName);
	}

	private static bool Matches(string[] names, string objectName)
	{
		if (string.IsNullOrEmpty(objectName))
		{
			return false;
		}
		foreach (string b in names)
		{
			if (string.Equals(objectName, b, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static void MeshReceiverCheck(MeshRenderer renderer, string what)
	{
		if (renderer == null)
		{
			MelonLogger.Warning("[L2A7V] the roof gun " + what + " has no geometry, it still aims and fires but cannot be seen");
		}
	}

	private static void DetachReparenters(GameObject clone)
	{
		Reparent[] componentsInChildren = clone.GetComponentsInChildren<Reparent>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			UnityEngine.Object.DestroyImmediate(componentsInChildren[i]);
		}
	}

	private static GameObject CloneSource(UsableOptic donor)
	{
		Transform transform = donor.transform;
		while (transform != null)
		{
			if (transform.GetComponentInChildren<CameraSlot>(includeInactive: true) != null)
			{
				return transform.gameObject;
			}
			transform = transform.parent;
		}
		return donor.gameObject;
	}

	private static CameraSlot ResolveSlot(GameObject clone, UsableOptic optic)
	{
		CameraSlot cameraSlot = ((optic != null) ? optic.slot : null);
		if (cameraSlot == null || !IsInside(clone, cameraSlot.transform))
		{
			cameraSlot = clone.GetComponentInChildren<CameraSlot>(includeInactive: true);
		}
		if (cameraSlot == null || !IsInside(clone, cameraSlot.transform))
		{
			return null;
		}
		if (optic != null && (object)optic.slot != cameraSlot)
		{
			optic.slot = cameraSlot;
		}
		return cameraSlot;
	}

	private static bool IsInside(GameObject root, Transform node)
	{
		if (node != null)
		{
			return node.IsChildOf(root.transform);
		}
		return false;
	}

	private static void StripWeaponLogic(GameObject clone)
	{
		FireControlSystem[] componentsInChildren = clone.GetComponentsInChildren<FireControlSystem>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			UnityEngine.Object.DestroyImmediate(componentsInChildren[i]);
		}
		MissileGuidanceUnit[] componentsInChildren2 = clone.GetComponentsInChildren<MissileGuidanceUnit>(includeInactive: true);
		for (int i = 0; i < componentsInChildren2.Length; i++)
		{
			UnityEngine.Object.DestroyImmediate(componentsInChildren2[i]);
		}
	}

	private static Transform FindCoaxScripts(GameObject unitRoot)
	{
		WeaponSystem[] componentsInChildren = unitRoot.GetComponentsInChildren<WeaponSystem>(includeInactive: true);
		foreach (WeaponSystem weaponSystem in componentsInChildren)
		{
			if (!LpVisual.IsImportedArt(weaponSystem.transform) && !(weaponSystem.Feed == null) && !(weaponSystem.Feed.ReadyRack == null))
			{
				string text = weaponSystem.gameObject.name.ToLowerInvariant();
				if (text.Contains("7.62") || text.Contains("coax"))
				{
					return weaponSystem.transform.parent;
				}
			}
		}
		return null;
	}

	private static UsableOptic FindDonorSight(GameObject unitRoot)
	{
		UsableOptic usableOptic = null;
		UsableOptic[] componentsInChildren = unitRoot.GetComponentsInChildren<UsableOptic>(includeInactive: true);
		foreach (UsableOptic usableOptic2 in componentsInChildren)
		{
			if (!LpVisual.IsImportedArt(usableOptic2.transform) && !(usableOptic2.slot == null) && !(usableOptic2.reticleMesh == null) && !usableOptic2.slot.IsExterior && usableOptic2.slot.VisionType == NightVisionType.None)
			{
				if (usableOptic2.transform.name.IndexOf("FERO", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return usableOptic2;
				}
				if (usableOptic == null)
				{
					usableOptic = usableOptic2;
				}
			}
		}
		return usableOptic;
	}

	private static AimablePlatform AddPlatform(Transform pivot, string name, EnumHelpers.Axis3 axis, float powered, float unpowered, Vector2 eulerLimits, bool reverseEuler)
	{
		AimablePlatform aimablePlatform = pivot.gameObject.AddComponent<AimablePlatform>();
		aimablePlatform.Name = name;
		aimablePlatform.Transform = pivot;
		aimablePlatform.Axis = axis;
		aimablePlatform.SpeedPowered = powered;
		aimablePlatform.SpeedUnpowered = unpowered;
		aimablePlatform.Stabilized = false;
		aimablePlatform.LocalEulerLimits = eulerLimits;
		aimablePlatform.ReverseLocalEuler = reverseEuler;
		aimablePlatform.HitZones = Array.Empty<GHPC.Equipment.DestructibleComponent>();
		return aimablePlatform;
	}

	private static void ConfigureFcs(GameObject node, Transform yaw, AimablePlatform yawPlatform, AimablePlatform pitchPlatform, WeaponSystem weapon)
	{
		FireControlSystem fireControlSystem = node.AddComponent<FireControlSystem>();
		fireControlSystem.ReferenceTransform = yaw;
		fireControlSystem.Mounts = new AimablePlatform[2] { yawPlatform, pitchPlatform };
		fireControlSystem.LinkedWeaponSystems = new WeaponSystem[1] { weapon };
		fireControlSystem.GatedAimablePlatforms = Array.Empty<AimablePlatform>();
		fireControlSystem.ManualModeTriggers = Array.Empty<FcsManualModeStartTrigger>();
		fireControlSystem.AutoModeTriggers = Array.Empty<FcsManualModeCancelTrigger>();
		fireControlSystem.MaxLaserRange = 0f;
		fireControlSystem.RegisteredRangeLimits = new Vector2(0f, 1600f);
		fireControlSystem.RangeStep = 100;
		fireControlSystem.FireGateAngle = 0f;
		fireControlSystem.CurrentStabMode = StabilizationMode.None;
		fireControlSystem.WeaponAuthoritative = false;
		fireControlSystem.ComputerNeedsPower = false;
		fireControlSystem.SuperelevateWeapon = false;
		fireControlSystem.SuperleadWeapon = false;
		fireControlSystem.DynamicLead = false;
		fireControlSystem.InertialCompensation = false;
		fireControlSystem.RecordTraverseRateBuffer = false;
		weapon.FCS = fireControlSystem;
	}

	private static CameraSlot BuildSight(Transform pitch, UsableOptic donor, WeaponSystem weapon, FireControlSystem fcs, Vector3 localPosition)
	{
		if (donor == null || fcs == null)
		{
			return null;
		}
		GameObject gameObject = UnityEngine.Object.Instantiate(CloneSource(donor), pitch, worldPositionStays: false);
		gameObject.name = "L2A7V_MG gunsight";
		DetachReparenters(gameObject);
		StripWeaponLogic(gameObject);
		gameObject.transform.localPosition = localPosition;
		UsableOptic componentInChildren = gameObject.GetComponentInChildren<UsableOptic>(includeInactive: true);
		CameraSlot cameraSlot = ResolveSlot(gameObject, componentInChildren);
		if (componentInChildren == null || cameraSlot == null || componentInChildren.reticleMesh == null)
		{
			MelonLogger.Warning("[L2A7V] the copied gunsight has no optic, slot or reticle of its own to rebuild, so the mount keeps no sight rather than editing the gunner's");
			UnityEngine.Object.Destroy(gameObject);
			return null;
		}
		componentInChildren.FCS = fcs;
		componentInChildren.Mounts = Array.Empty<AimablePlatform>();
		cameraSlot.LinkedNightSight = null;
		cameraSlot.HitZone = null;
		cameraSlot.SlotToUseOnDestroy = null;
		cameraSlot.ExclusiveItems = Array.Empty<GameObject>();
		cameraSlot.ZeroScaleItems = Array.Empty<Transform>();
		cameraSlot.OtherFovs = Array.Empty<float>();
		cameraSlot.ExclusiveWeapons = Array.Empty<WeaponSystem>();
		if (SlotReparentTarget != null)
		{
			SlotReparentTarget.SetValue(cameraSlot, null);
		}
		float defaultFov = cameraSlot.DefaultFov;
		cameraSlot.DefaultFov = Mathf.Clamp(20f, 1f, 60f);
		if (defaultFov > 0f && !Mathf.Approximately(defaultFov, cameraSlot.DefaultFov))
		{
			float num = Mathf.Tan(cameraSlot.DefaultFov * 0.5f * (Mathf.PI / 180f)) / Mathf.Tan(defaultFov * 0.5f * (Mathf.PI / 180f));
			Vector3 b = new Vector3(num, num, 1f);
			MeshRenderer[] componentsInChildren = gameObject.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
			foreach (MeshRenderer meshRenderer in componentsInChildren)
			{
				meshRenderer.transform.localScale = Vector3.Scale(meshRenderer.transform.localScale, b);
			}
		}
		ReticleMesh reticleMesh = componentInChildren.reticleMesh;
		reticleMesh.reticleSO = LpReticle.BuildRoofGunPlate(reticleMesh.reticleSO);
		SkinnedMeshRenderer component = reticleMesh.GetComponent<SkinnedMeshRenderer>();
		if (component != null)
		{
			component.sharedMesh = null;
		}
		gameObject.SetActive(value: true);
		return cameraSlot;
	}

	private static WeaponSystemInfo AppendWeapon(WeaponsManager manager, WeaponSystem weapon, FireControlSystem fcs)
	{
		WeaponSystemInfo weaponSystemInfo = new WeaponSystemInfo
		{
			Name = "7.62 mm machine gun MG3 (AA)",
			Role = WeaponSystemRole.RoofGun,
			Weapon = weapon,
			FCS = fcs
		};
		if (manager.Weapons == null)
		{
			manager.Weapons = new WeaponSystemInfo[1] { weaponSystemInfo };
			return weaponSystemInfo;
		}
		WeaponSystemInfo[] weapons = manager.Weapons;
		foreach (WeaponSystemInfo weaponSystemInfo2 in weapons)
		{
			if (weaponSystemInfo2 != null && weaponSystemInfo2.Role == WeaponSystemRole.RoofGun)
			{
				weaponSystemInfo2.Weapon = weapon;
				weaponSystemInfo2.FCS = fcs;
				weaponSystemInfo2.Name = "7.62 mm machine gun MG3 (AA)";
				return weaponSystemInfo2;
			}
		}
		WeaponSystemInfo[] array = new WeaponSystemInfo[manager.Weapons.Length + 1];
		Array.Copy(manager.Weapons, array, manager.Weapons.Length);
		array[manager.Weapons.Length] = weaponSystemInfo;
		manager.Weapons = array;
		return weaponSystemInfo;
	}

	private static void RefreshCaches(GameObject unitRoot, CameraSlot slot, WeaponSystemInfo info)
	{
		Unit component = unitRoot.GetComponent<Unit>();
		if (component == null)
		{
			return;
		}
		if (slot != null && DesignatedSlots != null)
		{
			CameraSlot[] array = DesignatedSlots.GetValue(component) as CameraSlot[];
			if (array == null || array.Length == 0)
			{
				array = LiveSlots(component);
			}
			if (array.Length == 0)
			{
				MelonLogger.Warning("[L2A7V] the unit reports no camera slot of its own, so the roof gun sight is not registered: an authored list of one would take every other view out of the cycle");
			}
			else if (Array.IndexOf(array, slot) < 0)
			{
				DesignatedSlots.SetValue(component, AddTo(array, slot));
			}
		}
		CrewManager component2 = unitRoot.GetComponent<CrewManager>();
		if (component2 != null && component2.CrewMembers != null)
		{
			CrewManager.CrewMember[] crewMembers = component2.CrewMembers;
			foreach (CrewManager.CrewMember crewMember in crewMembers)
			{
				if (crewMember != null && crewMember.Position == CrewPosition.Gunner)
				{
					List<WeaponSystemInfo> list = crewMember.Brain?.WeaponsModule?.Weapons;
					if (list != null && !list.Contains(info))
					{
						list.Add(info);
					}
				}
			}
		}
		PlayerInput instance = PlayerInput.Instance;
		CameraManager instance2 = CameraManager.Instance;
		if (!(instance == null) && !(instance2 == null) && !(RescanCamSlots == null) && (object)instance.CurrentPlayerUnit == component)
		{
			CameraSlot[] designatedCameraSlots = component.DesignatedCameraSlots;
			if (designatedCameraSlots != null && designatedCameraSlots.Length != 0)
			{
				RescanCamSlots.Invoke(instance2, new object[1] { designatedCameraSlots });
			}
		}
	}

	private static CameraSlot[] LiveSlots(Unit unit)
	{
		List<CameraSlot> list = new List<CameraSlot>();
		CameraSlot[] componentsInChildren = unit.GetComponentsInChildren<CameraSlot>(includeInactive: true);
		foreach (CameraSlot cameraSlot in componentsInChildren)
		{
			if (cameraSlot != null && !cameraSlot.DirectAttachOnly)
			{
				list.Add(cameraSlot);
			}
		}
		return list.ToArray();
	}

	private static CameraSlot[] AddTo(CameraSlot[] array, CameraSlot extra)
	{
		CameraSlot[] array2 = new CameraSlot[array.Length + 1];
		Array.Copy(array, array2, array.Length);
		array2[array.Length] = extra;
		return array2;
	}

	private static void AssignCodex(WeaponSystem weapon)
	{
		if (WeaponCodexField == null)
		{
			return;
		}
		WeaponSystemCodexScriptable weaponSystemCodexScriptable = WeaponCodexField.GetValue(weapon) as WeaponSystemCodexScriptable;
		if (!(weaponSystemCodexScriptable == null))
		{
			if (_codex == null)
			{
				_codex = ScriptableObject.CreateInstance<WeaponSystemCodexScriptable>();
				_codex.name = "L2A7V_MG3_AA";
				_codex.FriendlyName = "7.62 mm machine gun MG3 (AA)";
				_codex.CaliberMm = weaponSystemCodexScriptable.CaliberMm;
				_codex.Type = weaponSystemCodexScriptable.Type;
			}
			WeaponCodexField.SetValue(weapon, _codex);
		}
	}
}

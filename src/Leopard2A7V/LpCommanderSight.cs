using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Utility;
using GHPC.Weaponry;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using Reticle;
using UnityEngine;

namespace Leopard2A7V;

public static class LpCommanderSight
{
	internal const string CloneName = "L2A7V_CommanderSight";

	private const ReticleTree.Light.Type Lit = ReticleTree.Light.Type.NightIllumination;

	private const ReticleTree.Light.Type Powered = ReticleTree.Light.Type.Powered;

	internal const string OpticalPlateName = "L2A7V_CMDR";

	internal const string ThermalPlateName = "L2A7V_CMDR_TH";

	private static readonly RGB ThermalGreen = new RGB(0f, 0.99f, 0.11f);

	internal const float ZoomLow = 2f;

	internal const float ZoomHigh = 8f;

	internal const float CommanderFov = 20f;

	internal const float ThermalFov = 15.07f;

	private static readonly Vector3 Eye = new Vector3(0.27f, 2.78f, -0.33f);

	internal const float YawLimit = 40f;

	internal const float PitchLimit = 15f;

	internal const float Sensitivity = 0.9f;

	private static readonly FieldInfo SlotReparentTarget = AccessTools.Field(typeof(CameraSlot), "_reparentTarget");

	internal const float GlassVibrationBlur = 0.1f;

	internal const float GlassVibrationShake = 0.15f;

	private static ReticleSO _opticalPlate;

	private static ReticleSO _thermalPlate;

	internal const string PivotName = "L2A7V_CmdSightPivot";

	internal const string HeadArtName = "L2A7V_CmdSightPivot_Art";

	private const float EyeClearance = 0.08f;

	internal static ReticleSO OpticalPlate
	{
		get
		{
			if (_opticalPlate == null)
			{
				_opticalPlate = BuildPlate("L2A7V_CMDR", ReticleTree.Light.Type.NightIllumination, new RGB(0f, 0f, 0f, hdr: true), 20f);
			}
			return _opticalPlate;
		}
	}

	internal static ReticleSO ThermalPlate
	{
		get
		{
			if (_thermalPlate == null)
			{
				_thermalPlate = BuildPlate("L2A7V_CMDR_TH", ReticleTree.Light.Type.Powered, ThermalGreen, 20f);
			}
			return _thermalPlate;
		}
	}

	private static float FovFor(float magnification)
	{
		return 2f * Mathf.Atan(Mathf.Tan(0.0720821f) * 11f / Mathf.Max(1f, magnification)) * 57.29578f;
	}

	internal static Transform FindHead(GameObject unitRoot)
	{
		Transform[] componentsInChildren = unitRoot.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (transform != null && transform.name == "L2A7V_CmdSightPivot")
			{
				return transform;
			}
		}
		return null;
	}

	internal static Transform FindHeadArt(Transform head)
	{
		if (!(head == null))
		{
			return head.Find("L2A7V_CmdSightPivot_Art");
		}
		return null;
	}

	internal static float EyeBehind(Transform head)
	{
		Transform transform = FindHeadArt(head);
		MeshFilter meshFilter = ((transform == null) ? null : transform.GetComponent<MeshFilter>());
		return ((meshFilter != null && meshFilter.sharedMesh != null) ? meshFilter.sharedMesh.bounds.extents.z : 0.32f) + 0.08f;
	}

	internal static void Mount(Transform eye, Transform head)
	{
		eye.SetParent(head, worldPositionStays: false);
		eye.localPosition = new Vector3(0f, 0f, 0f - EyeBehind(head));
		eye.localRotation = Quaternion.identity;
		eye.localScale = Vector3.one;
	}

	private static Transform FindClone(GameObject unitRoot)
	{
		Transform[] componentsInChildren = unitRoot.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (transform != null && transform.name == "L2A7V_CommanderSight")
			{
				return transform;
			}
		}
		return null;
	}

	internal static bool IsCommanderSlot(Transform node)
	{
		int num = 0;
		while (node != null && num++ < 32)
		{
			if (node.name == "L2A7V_CmdSightPivot" || node.name == "L2A7V_CommanderSight")
			{
				return true;
			}
			node = node.parent;
		}
		return false;
	}

	internal static bool IsSightPart(string objectName)
	{
		if (objectName != null)
		{
			return objectName.IndexOf("optic_commander", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return false;
	}

	public static Transform AttachHead(Transform turretBone, Vector3 ring)
	{
		if (turretBone == null)
		{
			return null;
		}
		Bounds bounds = default(Bounds);
		bool flag = false;
		foreach (ObjGeometry geometry in ObjModel.Geometries)
		{
			if (geometry.Group == LpGroup.CommanderGear && IsSightPart(geometry.ObjectName))
			{
				if (!flag)
				{
					bounds = geometry.Bounds;
					flag = true;
				}
				else
				{
					bounds.Encapsulate(geometry.Bounds);
				}
			}
		}
		if (!flag)
		{
			MelonLogger.Msg("[L2A7V] the export carries no optic_commander part, so the sight device cannot turn");
			return null;
		}
		Vector3 center = bounds.center;
		Transform transform = LpVisual.MakePivot(turretBone, "L2A7V_CmdSightPivot", center - ring);
		LpVisual.AttachParts(transform, "L2A7V_CmdSightPivot_Art", (ObjGeometry part) => part.Group == LpGroup.CommanderGear && IsSightPart(part.ObjectName), center);
		return transform;
	}

	public static void Apply(GameObject unitRoot)
	{
		if (unitRoot == null || FindClone(unitRoot) != null)
		{
			return;
		}
		UsableOptic usableOptic = FindDonorOptic(unitRoot);
		if (usableOptic == null)
		{
			MelonLogger.Warning("[L2A7V] the commander's sight stays unbuilt: no interior eyepiece to copy");
			return;
		}
		GameObject gameObject = CloneSource(usableOptic);
		if (gameObject == null)
		{
			MelonLogger.Warning("[L2A7V] the commander's sight stays unbuilt: the eyepiece carries no CameraSlot");
			return;
		}
		GameObject gameObject2 = new GameObject("L2A7V_CommanderSight_staging");
		gameObject2.SetActive(value: false);
		GameObject gameObject3 = UnityEngine.Object.Instantiate(gameObject, gameObject2.transform, worldPositionStays: false);
		gameObject3.name = "L2A7V_CommanderSight";
		gameObject3.transform.localPosition = Vector3.zero;
		gameObject3.transform.localRotation = Quaternion.identity;
		gameObject3.transform.localScale = Vector3.one;
		Strip(gameObject3);
		MeshRenderer[] componentsInChildren = gameObject3.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
		foreach (MeshRenderer meshRenderer in componentsInChildren)
		{
			if (!(meshRenderer == null))
			{
				Material sharedMaterial = meshRenderer.sharedMaterial;
				Texture texture = ((sharedMaterial != null) ? sharedMaterial.mainTexture : null);
				string text = ((texture != null) ? texture.name.ToLowerInvariant() : string.Empty);
				string text2 = meshRenderer.transform.name.ToLowerInvariant();
				bool flag = text.Contains("gunsight") || text.Contains("scope") || text2.Contains("quad") || text2.Contains("scope") || text2.Contains("ring") || text2.Contains("bezel");
				MelonLogger.Msg("[L2A7V] commander eyepiece renderer '" + meshRenderer.transform.name + "' art='" + text + "' -> " + (flag ? "ring switched off" : "kept"));
				if (flag)
				{
					meshRenderer.enabled = false;
				}
			}
		}
		UsableOptic componentInChildren = gameObject3.GetComponentInChildren<UsableOptic>(includeInactive: true);
		CameraSlot cameraSlot = ResolveSlot(gameObject3, componentInChildren);
		ReticleMesh reticleMesh = ((componentInChildren != null) ? componentInChildren.reticleMesh : null);
		if (componentInChildren == null || cameraSlot == null || reticleMesh == null)
		{
			MelonLogger.Warning("[L2A7V] the copied eyepiece lost its optic, slot or reticle, so the commander keeps the head-out camera");
			UnityEngine.Object.Destroy(gameObject3);
			return;
		}
		MakePhysical(componentInChildren, cameraSlot);
		reticleMesh.reticleSO = OpticalPlate;
		SkinnedMeshRenderer component = reticleMesh.GetComponent<SkinnedMeshRenderer>();
		if (component != null)
		{
			component.sharedMesh = null;
		}
		reticleMesh.Load();
		reticleMesh.GetLight(ReticleTree.Light.Type.NightIllumination, out var value);
		reticleMesh.SetLight(ReticleTree.Light.Type.NightIllumination, (float.IsNaN(value) || value <= 0f) ? 0f : 1f);
		LpReticleLight.Attach(reticleMesh);
		Transform transform = FindHead(unitRoot);
		if (transform != null)
		{
			Mount(gameObject3.transform, transform);
		}
		else
		{
			gameObject3.transform.SetParent(unitRoot.transform, worldPositionStays: false);
			gameObject3.transform.localPosition = Eye;
		}
		gameObject3.transform.localRotation = Quaternion.identity;
		gameObject3.transform.localScale = Vector3.one;
		gameObject3.SetActive(value: true);
		UnityEngine.Object.Destroy(gameObject2);
		LpCommanderToggle lpCommanderToggle = unitRoot.AddComponent<LpCommanderToggle>();
		lpCommanderToggle.Slot = cameraSlot;
		lpCommanderToggle.Mesh = reticleMesh;
		lpCommanderToggle.Sight = ((transform != null) ? transform : gameObject3.transform);
		lpCommanderToggle.Clone = gameObject3.transform;
		lpCommanderToggle.Unit = unitRoot;
		lpCommanderToggle.Art = FindHeadArt(transform);
		lpCommanderToggle.Mounted = transform != null;
		lpCommanderToggle.Border = BorderTexture(256, 0.015f, 0.035f);
		MelonLogger.Msg($"[L2A7V] commander's sight built at {Eye} from '{gameObject.name}': " + $"{2f:0.#}x/{8f:0.#}x ({cameraSlot.DefaultFov:0.##}/{cameraSlot.OtherFovs[0]:0.##} deg), " + "plate " + reticleMesh.reticleSO.name + ", Z to enter and leave, mouse to turn the head, " + ((transform != null) ? $"mounted on the sight head {transform.name} at {gameObject3.transform.localPosition.z:0.##} m" : "the sight head is built later, so the eyepiece waits for it") + ", " + (LpCommanderThermal.Available() ? "T switches the imager on and off" : "and no thermal camera in this scene, so T has nothing to switch"));
	}

	private static UsableOptic FindDonorOptic(GameObject unitRoot)
	{
		UsableOptic usableOptic = null;
		UsableOptic[] componentsInChildren = unitRoot.GetComponentsInChildren<UsableOptic>(includeInactive: true);
		foreach (UsableOptic usableOptic2 in componentsInChildren)
		{
			if (!(usableOptic2 == null) && !(usableOptic2.slot == null) && !usableOptic2.slot.IsExterior && !LpVisual.IsImportedArt(usableOptic2.transform) && usableOptic2.slot.VisionType == NightVisionType.None && !(usableOptic2.reticleMesh == null) && !(usableOptic2.reticleMesh.reticleSO == null))
			{
				if (usableOptic2.reticleMesh.reticleSO.name == "LEO2A7V")
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

	private static GameObject CloneSource(UsableOptic donor)
	{
		Transform transform = donor.transform;
		int num = 0;
		while (transform != null && num < 8)
		{
			if (transform.GetComponent<CameraSlot>() != null)
			{
				return transform.gameObject;
			}
			num++;
			transform = transform.parent;
		}
		return null;
	}

	private static CameraSlot ResolveSlot(GameObject clone, UsableOptic optic)
	{
		CameraSlot cameraSlot = ((optic != null) ? optic.slot : null);
		if (cameraSlot != null && cameraSlot.transform.IsChildOf(clone.transform))
		{
			return cameraSlot;
		}
		return clone.GetComponentInChildren<CameraSlot>(includeInactive: true);
	}

	private static void Strip(GameObject clone)
	{
		Reparent[] componentsInChildren = clone.GetComponentsInChildren<Reparent>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			UnityEngine.Object.DestroyImmediate(componentsInChildren[i]);
		}
		FireControlSystem[] componentsInChildren2 = clone.GetComponentsInChildren<FireControlSystem>(includeInactive: true);
		for (int i = 0; i < componentsInChildren2.Length; i++)
		{
			UnityEngine.Object.DestroyImmediate(componentsInChildren2[i]);
		}
		MissileGuidanceUnit[] componentsInChildren3 = clone.GetComponentsInChildren<MissileGuidanceUnit>(includeInactive: true);
		for (int i = 0; i < componentsInChildren3.Length; i++)
		{
			UnityEngine.Object.DestroyImmediate(componentsInChildren3[i]);
		}
	}

	private static void MakePhysical(UsableOptic optic, CameraSlot slot)
	{
		optic.FCS = null;
		optic.RegisterAtStart = false;
		optic.Alignment = OpticAlignment.Fixed;
		optic.Mounts = Array.Empty<AimablePlatform>();
		optic.RotateAzimuth = false;
		optic.RotateElevation = false;
		optic.CantCorrect = false;
		optic.UseRotationForShake = false;
		optic.ForceHorizontalReticleAlign = false;
		optic.RangeText = null;
		optic.ReadyToFireObject = null;
		optic.UnreadyToFireObject = null;
		optic.FaultObject = null;
		optic.OverridingObject = null;
		optic.RegisteredAmmoTypes = Array.Empty<AmmoCodexScriptable>();
		slot.VisionType = NightVisionType.None;
		slot.DoMechanicalFLIR = false;
		slot.DefaultFov = 20f;
		slot.OtherFovs = new float[1] { 2f * Mathf.Atan(Mathf.Tan(0.17453292f) * 2f / 8f) * 57.29578f };
		slot.AllowFreeLook = false;
		slot.AllowFreeZoom = false;
		slot.DirectAttachOnly = true;
		slot.NightSightAtNightOnly = false;
		slot.LinkedNightSight = null;
		slot.HitZone = null;
		slot.SlotToUseOnDestroy = null;
		slot.ZeroScaleItems = Array.Empty<Transform>();
		slot.ExclusiveItems = Array.Empty<GameObject>();
		slot.ExclusiveWeapons = Array.Empty<WeaponSystem>();
		slot.BaseBlur = 0f;
		slot.VibrationBlurScale = 0.1f;
		slot.VibrationShakeMultiplier = 0f;
		if (SlotReparentTarget != null)
		{
			SlotReparentTarget.SetValue(slot, null);
		}
	}

	private static ReticleSO BuildPlate(string plateName, ReticleTree.Light.Type light, RGB colour, float fov)
	{
		ReticleTree.Angular angular = new ReticleTree.Angular(Vector2.zero, null, ReticleTree.GroupBase.Alignment.Boresight)
		{
			name = "CommanderScale",
			elements = new List<ReticleTree.TransformElement>()
		};
		float num = Mathf.Clamp(Mathf.Tan(fov * 0.5f * (Mathf.PI / 180f)) * 1000f, 20f, 900f);
		float num2 = Mathf.Clamp(num / 320f, 0.25f, 2.5f);
		Add(angular.elements, 0f - num, 0f, -2.5f, 0f, num2 * 0.8f, light);
		Add(angular.elements, 2.5f, 0f, num, 0f, num2 * 0.8f, light);
		float num3 = Mathf.Max(2f, num / 40f);
		for (float num4 = num3; num4 <= num; num4 += num3)
		{
			float num5 = ((Mathf.Abs(Mathf.Repeat(num4, num3 * 5f)) < 0.01f) ? 2.2f : 1.1f);
			Add(angular.elements, num4, 0f - num5, num4, num5, num2 * 0.6f, light);
			Add(angular.elements, 0f - num4, 0f - num5, 0f - num4, num5, num2 * 0.6f, light);
		}
		float b = ((Screen.height > 0) ? ((float)Screen.width / (float)Screen.height) : 1.7778f);
		float num6 = Mathf.Tan(fov * 0.5f * (Mathf.PI / 180f)) * 1000f / Mathf.Max(1f, b);
		for (float num7 = 2f; num7 <= num6; num7 += 2f)
		{
			float num8 = ((Mathf.Abs(Mathf.Repeat(num7, 10f)) < 0.01f) ? 3.2f : 1.8f);
			Add(angular.elements, 0f - num8, num7, num8, num7, num2 * 0.6f, light);
		}
		ReticleTree.FocalPlane item = new ReticleTree.FocalPlane(ReticleTree.FocalPlane.Type.First)
		{
			elements = new List<ReticleTree.TransformElement> { angular }
		};
		ReticleSO reticleSO = ScriptableObject.CreateInstance<ReticleSO>();
		reticleSO.name = plateName;
		reticleSO.hideFlags = HideFlags.DontUnloadUnusedAsset;
		reticleSO.planes = new List<ReticleTree.FocalPlane> { item };
		reticleSO.lights = new List<ReticleTree.Light>
		{
			new ReticleTree.Light
			{
				type = ReticleTree.Light.Type.NightIllumination,
				color = ((light == ReticleTree.Light.Type.NightIllumination) ? colour : new RGB(0f, 0f, 0f, hdr: true))
			},
			new ReticleTree.Light
			{
				type = ReticleTree.Light.Type.Powered,
				color = ((light == ReticleTree.Light.Type.Powered) ? colour : new RGB(1f, 1f, 1f))
			}
		};
		return reticleSO;
	}

	private static Texture2D BorderTexture(int size, float solid, float fade)
	{
		Texture2D texture2D = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
		texture2D.wrapMode = TextureWrapMode.Clamp;
		Color32[] array = new Color32[size * size];
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				float a = (float)Mathf.Min(j, size - 1 - j) / (float)(size - 1);
				float b = (float)Mathf.Min(i, size - 1 - i) / (float)(size - 1);
				float value = Mathf.Min(a, b);
				float num = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(solid, solid + fade, value));
				array[i * size + j] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(num * 255f));
			}
		}
		texture2D.SetPixels32(array);
		texture2D.Apply(updateMipmaps: false, makeNoLongerReadable: false);
		return texture2D;
	}

	private static void Add(List<ReticleTree.TransformElement> elements, float x1, float y1, float x2, float y2, float stroke, ReticleTree.Light.Type light)
	{
		float num = x2 - x1;
		float num2 = y2 - y1;
		float num3 = Mathf.Sqrt(num * num + num2 * num2);
		if (!(num3 < 0.01f))
		{
			elements.Add(new ReticleTree.Line(new Vector2((x1 + x2) * 0.5f, (y1 + y2) * 0.5f), Mathf.Atan2(num2, num) * 57.29578f, num3, stroke)
			{
				illumination = light
			});
		}
	}
}

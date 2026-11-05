using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GHPC;
using GHPC.Thermals;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V;

public static class LpVisual
{
	public sealed class Assembly
	{
		public Transform Hull;

		public Transform Turret;

		public Transform Gun;

		public Transform CommanderGear;

		public Transform CommanderSight;

		public Transform RoofGunYaw;

		public Transform RoofGunPitch;

		public Transform RoofGunYawArt;

		public Transform RoofGunPitchArt;

		public GameObject Marker;
	}

	public static Material TemplateMaterial;

	public static Material GlassTemplate;

	public static readonly Dictionary<string, List<int>> GroupMaterials = new Dictionary<string, List<int>>();

	public static readonly Dictionary<string, List<bool>> GroupThin = new Dictionary<string, List<bool>>();

	public const string CommanderGearNode = "CommanderGear";

	public const string GlassShaderName = "Standard (FLIR)";

	private const string RigRoot = "leo2_rig";

	private const string HullBone = "hull";

	private const string TurretBone = "turret";

	private const string MantletBone = "mantlet";

	private const string VehicleShaderName = "GHPC/VehicleShader";

	private static int _visualLayer = -1;

	private static readonly FieldInfo HeatBackingField = AccessTools.Field(typeof(HeatSource), "_heat");

	public static bool IsReady
	{
		get
		{
			if (ObjModel.Loaded)
			{
				return !ObjModel.Failed;
			}
			return false;
		}
	}

	public static Assembly Convert(GameObject unitRoot)
	{
		if (!IsReady)
		{
			return null;
		}
		if (FindDeep(unitRoot.transform, "Leopard2A7V_Visual") != null)
		{
			return null;
		}
		if (!CaptureTemplateMaterial(unitRoot))
		{
			return null;
		}
		CaptureGlassMaterial(unitRoot);
		int num = LpAssets.RepairShader(TemplateMaterial.shader);
		num += LpInterior.RepairShader(TemplateMaterial.shader);
		if (num > 0)
		{
			MelonLogger.Warning($"[L2A7V] re-pointed {num} cached material(s) onto " + TemplateMaterial.shader.name + ", the previous bundle copy had died");
		}
		Transform transform = unitRoot.transform;
		Transform transform2 = FindDeep(transform, "leo2_rig");
		Transform transform3 = ((transform2 == null) ? null : transform2.Find("hull"));
		Transform transform4 = ((transform3 == null) ? null : transform3.Find("turret"));
		Transform transform5 = ((transform4 == null) ? null : transform4.Find("mantlet"));
		if (transform4 == null || transform5 == null)
		{
			MelonLogger.Error("[L2A7V] the donor rig bones leo2_rig/hull/turret/mantlet are missing, so the imported art has nothing to ride");
			return null;
		}
		if (transform2 != null)
		{
			MelonLogger.Msg("[L2A7V] drive bones: turret " + Describe(transform4) + ", gun " + Describe(transform5));
		}
		Vector3 offset = LpConfig.Offset;
		Vector3 turretAxis = LpConfig.TurretAxis;
		Vector3 gunAxis = LpConfig.GunAxis;
		Assembly assembly = new Assembly();
		try
		{
			GameObject gameObject = new GameObject("Leopard2A7V_Visual");
			gameObject.transform.SetParent(transform, worldPositionStays: false);
			assembly.Marker = gameObject;
			Transform bone = ((transform3 != null) ? transform3 : transform);
			Vector3 localPosition = ((transform3 != null) ? transform3.InverseTransformPoint(transform.TransformPoint(offset)) : offset);
			assembly.Hull = MakePivot(bone, "L2A7V_HullPivot", localPosition);
			AttachGroup(assembly.Hull, "Hull", LpGroup.Hull, Vector3.zero);
			if (!LpConfig.UseDonorRunningGear)
			{
				LpRunningGear.Attach(assembly.Hull, transform);
			}
			assembly.Turret = MakePivot(transform4, "L2A7V_TurretPivot", Vector3.zero);
			AttachGroup(assembly.Turret, "Turret", LpGroup.Turret, turretAxis);
			assembly.CommanderGear = MakePivot(transform4, "L2A7V_CupolaPivot", Vector3.zero);
			AttachParts(assembly.CommanderGear, "CommanderGear", (ObjGeometry part) => part.Group == LpGroup.CommanderGear && !LpCommanderSight.IsSightPart(part.ObjectName) && (!LpConfig.AddRoofGun || !LpRoofGun.IsRoofGunPart(part.ObjectName)), turretAxis);
			assembly.CommanderSight = LpCommanderSight.AttachHead(transform4, turretAxis);
			assembly.Gun = MakePivot(transform5, "L2A7V_GunPivot", Vector3.zero);
			AttachGroup(assembly.Gun, "Gun", LpGroup.Gun, gunAxis);
			LpRoofGun.Apply(unitRoot, assembly, turretAxis);
		}
		catch (Exception ex)
		{
			MelonLogger.Error("[L2A7V] visual build failed: " + ex);
			if (assembly.Marker != null)
			{
				UnityEngine.Object.Destroy(assembly.Marker);
			}
			return null;
		}
		LpInterior.Attach(unitRoot, assembly);
		if (LpConfig.ShowInterior)
		{
			LpDonorInterior.Attach(unitRoot);
		}
		LpSightView.Attach(unitRoot, assembly);
		LpReticleOrder.Apply(unitRoot);
		if (unitRoot.GetComponent<LpReticleWatch>() == null)
		{
			unitRoot.AddComponent<LpReticleWatch>();
		}
		LpThermal.Apply(unitRoot);
		LpBaseRig.Apply(transform);
		LpDonorArt.Request(unitRoot);
		LpArmorLog.Apply(unitRoot);
		MelonLogger.Msg("[L2A7V] armour at conversion: " + unitRoot.GetComponentsInChildren<UniformArmor>(includeInactive: true).Length + " uniform + " + unitRoot.GetComponentsInChildren<VariableArmor>(includeInactive: true).Length + " measured on '" + unitRoot.name + "'");
		ApplyHeat(assembly);
		LpDiagnostics.DumpOnce(unitRoot);
		LpDonorArtWatch.SetConverted(unitRoot);
		return assembly;
	}

	public static Transform MakePivot(Transform bone, string name, Vector3 localPosition)
	{
		Transform transform = new GameObject(name).transform;
		transform.SetParent(bone, worldPositionStays: false);
		transform.localPosition = localPosition;
		transform.localRotation = Quaternion.identity;
		Vector3 lossyScale = bone.lossyScale;
		Vector3 vector = new Vector3(SafeReciprocal(lossyScale.x), SafeReciprocal(lossyScale.y), SafeReciprocal(lossyScale.z));
		if (vector != Vector3.one)
		{
			MelonLogger.Warning($"[L2A7V] {bone.name} is scaled {lossyScale}, {name} compensates with {vector}");
		}
		transform.localScale = vector;
		return transform;
	}

	private static float SafeReciprocal(float value)
	{
		if (!(Mathf.Abs(value) < 1E-05f))
		{
			return 1f / value;
		}
		return 1f;
	}

	private static bool IsLid(string partName)
	{
		if (!string.IsNullOrEmpty(partName))
		{
			return partName.StartsWith("hatch_", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static void AttachGroup(Transform parent, string name, LpGroup group, Vector3 anchor)
	{
		AttachParts(parent, name, (ObjGeometry part) => part.Group == group, anchor);
	}

	public static MeshRenderer AttachParts(Transform parent, string name, Predicate<ObjGeometry> select, Vector3 anchor)
	{
		return AttachMesh(parent, name, LpAssets.BuildWhere(select, name, anchor));
	}

	public static MeshRenderer AttachMesh(Transform parent, string name, Mesh mesh)
	{
		if (mesh == null)
		{
			MelonLogger.Warning("[L2A7V] no geometry for " + name);
			return null;
		}
		GameObject gameObject = new GameObject(name);
		gameObject.transform.SetParent(parent, worldPositionStays: false);
		gameObject.layer = ((_visualLayer >= 0) ? _visualLayer : parent.gameObject.layer);
		gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
		if (GroupMaterials.TryGetValue(name, out var value))
		{
			if (!GroupThin.TryGetValue(name, out var value2))
			{
				value2 = new List<bool>();
			}
			Material[] array = new Material[value.Count];
			for (int i = 0; i < value.Count; i++)
			{
				array[i] = LpAssets.GetMaterial(value[i], name, i < value2.Count && value2[i]);
				if (array[i] == null)
				{
					MelonLogger.Warning("[L2A7V] material unavailable for index " + value[i]);
				}
			}
			meshRenderer.sharedMaterials = array;
		}
		meshRenderer.shadowCastingMode = ShadowCastingMode.On;
		meshRenderer.receiveShadows = true;
		meshRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
		meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
		return meshRenderer;
	}

	private static bool CaptureTemplateMaterial(GameObject unitRoot)
	{
		if (TemplateMaterial != null && TemplateMaterial.shader != null && string.Equals(TemplateMaterial.shader.name, "GHPC/VehicleShader", StringComparison.Ordinal))
		{
			return true;
		}
		Renderer renderer = null;
		Material templateMaterial = null;
		int num = int.MinValue;
		Renderer[] componentsInChildren = unitRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer2 in componentsInChildren)
		{
			if (IsImportedArt(renderer2.transform))
			{
				continue;
			}
			Material material = null;
			Material[] sharedMaterials = renderer2.sharedMaterials;
			foreach (Material material2 in sharedMaterials)
			{
				if (material2 != null && material2.shader != null && material2.shader.name == "GHPC/VehicleShader")
				{
					material = material2;
					break;
				}
			}
			if (!(material == null))
			{
				string text = material.name.ToLowerInvariant();
				string text2 = PathOf(renderer2.transform).ToLowerInvariant();
				int num2 = 1;
				if (text2.Contains("leo2_mesh"))
				{
					num2 += 8;
				}
				if (text.StartsWith("leo2"))
				{
					num2 += 4;
				}
				if (text2.Contains("hull") || text2.Contains("turret"))
				{
					num2 += 2;
				}
				if (text.Contains("tricolour"))
				{
					num2++;
				}
				if (text.Contains("void") || text.Contains("see-through"))
				{
					num2 -= 12;
				}
				if (text2.Contains("interior") || text2.Contains("collider") || text2.Contains("hitzone"))
				{
					num2 -= 10;
				}
				if (text2.Contains("running gear") || text2.Contains("track"))
				{
					num2 -= 3;
				}
				if (num2 > num)
				{
					num = num2;
					renderer = renderer2;
					templateMaterial = material;
				}
			}
		}
		if (renderer == null)
		{
			Shader shader = Shader.Find("GHPC/VehicleShader");
			if (shader == null || TemplateMaterial == null)
			{
				MelonLogger.Error("[L2A7V] could not find a live GHPC/VehicleShader to clone, so this spawn is left as the donor");
				return false;
			}
			TemplateMaterial.shader = shader;
			MelonLogger.Warning("[L2A7V] no donor renderer reported GHPC/VehicleShader, revived the cached template through Shader.Find");
			return true;
		}
		TemplateMaterial = templateMaterial;
		_visualLayer = renderer.gameObject.layer;
		LpAssets.ConfigureFromTemplate(TemplateMaterial);
		MelonLogger.Msg("[L2A7V] shader template '" + TemplateMaterial.name + "' / " + $"'{TemplateMaterial.shader.name}' from {PathOf(renderer.transform)}, layer {_visualLayer}");
		return true;
	}

	private static void CaptureGlassMaterial(GameObject unitRoot)
	{
		if (GlassTemplate != null && GlassTemplate.shader != null && GlassTemplate.shader.name == "Standard (FLIR)")
		{
			return;
		}
		Material material = null;
		int num = int.MinValue;
		Renderer[] componentsInChildren = unitRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren)
		{
			if (IsImportedArt(renderer.transform))
			{
				continue;
			}
			Material[] sharedMaterials = renderer.sharedMaterials;
			foreach (Material material2 in sharedMaterials)
			{
				if (!(material2 == null) && !(material2.shader == null))
				{
					string text = material2.name.ToLowerInvariant();
					int num2 = 0;
					if (text.Contains("glass"))
					{
						num2 += 6;
					}
					if (text.Contains("fake glass"))
					{
						num2 += 4;
					}
					if (material2.shader.name == "Standard (FLIR)")
					{
						num2 += 8;
					}
					if (text.Contains("lamp"))
					{
						num2 -= 4;
					}
					if (text.Contains("see-through") || text.Contains("void"))
					{
						num2 -= 12;
					}
					if (num2 > num)
					{
						num = num2;
						material = material2;
					}
				}
			}
		}
		if (material != null && num > 0)
		{
			GlassTemplate = material;
			MelonLogger.Msg("[L2A7V] glass material '" + material.name + "' / '" + material.shader.name + "' " + $"from the donor, queue {material.renderQueue}");
		}
		else
		{
			GlassTemplate = null;
			MelonLogger.Warning("[L2A7V] the donor reported no glass material, the sight windows stay opaque");
		}
	}

	private static void ApplyHeat(Assembly art)
	{
		if (LpConfig.AddHeatSource && !(art.Hull == null))
		{
			HeatSource heatSource = art.Hull.gameObject.AddComponent<HeatSource>();
			if (HeatBackingField != null)
			{
				HeatBackingField.SetValue(heatSource, float.NaN);
			}
			heatSource.Heat = 0.85f;
		}
	}

	public static bool IsImportedArt(Transform transform)
	{
		Transform transform2 = transform;
		int num = 0;
		while (transform2 != null && num++ < 32)
		{
			if (transform2.name.StartsWith("L2A7V_", StringComparison.Ordinal))
			{
				return true;
			}
			transform2 = transform2.parent;
		}
		return false;
	}

	private static string Describe(Transform bone)
	{
		Vector3 position = bone.position;
		return $"{bone.name} at ({position.x:0.000},{position.y:0.000},{position.z:0.000}) scale {bone.lossyScale}";
	}

	private static string PathOf(Transform transform)
	{
		StringBuilder stringBuilder = new StringBuilder(transform.name);
		Transform parent = transform.parent;
		int num = 0;
		while (parent != null && num++ < 24)
		{
			stringBuilder.Insert(0, parent.name + "/");
			parent = parent.parent;
		}
		return stringBuilder.ToString();
	}

	private static Transform FindDeep(Transform root, string name)
	{
		if (root.name == name)
		{
			return root;
		}
		foreach (Transform item in root)
		{
			Transform transform = FindDeep(item, name);
			if (transform != null)
			{
				return transform;
			}
		}
		return null;
	}
}

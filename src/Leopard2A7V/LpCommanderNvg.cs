using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using HarmonyLib;
using MelonLoader;
using Reticle;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V;

public static class LpCommanderNvg
{
	private const string Tag = "[L2A7V][cmdr-sight]";

	private const string SlotNameFragment = "commander head";

	private static readonly FieldInfo DesignatedSlots = AccessTools.Field(typeof(Unit), "_designatedCameraSlots");

	internal static void Apply(GameObject unitRoot)
	{
		if (unitRoot == null || unitRoot.GetComponent<LpCommanderSightRig>() != null)
		{
			return;
		}
		CameraSlot cameraSlot = FindSlot(unitRoot);
		if (cameraSlot == null)
		{
			MelonLogger.Warning("[L2A7V][cmdr-sight] no commander head slot on " + unitRoot.name + "; no sight is built");
			return;
		}
		cameraSlot.NightSightAtNightOnly = false;
		CameraSlot cameraSlot2 = CloneView(cameraSlot, unitRoot);
		if (cameraSlot2 == null)
		{
			MelonLogger.Warning("[L2A7V][cmdr-sight] the commander head slot could not be copied, so the CITV cannot be built");
			return;
		}
		Transform transform = LpCommanderSight.FindHead(unitRoot);
		if (transform != null)
		{
			AttachToHead(cameraSlot2, transform);
		}
		cameraSlot2.AllowFreeLook = true;
		cameraSlot2.AllowFreeZoom = false;
		cameraSlot2.IsExterior = false;
		cameraSlot2.SpriteType = CameraSpriteManager.SpriteType.DefaultScope;
		cameraSlot2.DirectAttachOnly = true;
		cameraSlot2.fovAspect = true;
		cameraSlot2.DefaultFov = 20f;
		cameraSlot2.OtherFovs = new float[1] { 2f * Mathf.Atan(Mathf.Tan(0.17453292f) * 2f / 8f) * 57.29578f };
		cameraSlot2.VisionType = NightVisionType.None;
		cameraSlot2.DoMechanicalFLIR = false;
		cameraSlot2.OverrideFLIRResolution = false;
		cameraSlot2.CanToggleFlirPolarity = false;
		cameraSlot2.FLIRBlitMaterialOverride = null;
		cameraSlot2.BaseBlur = 0f;
		cameraSlot2.VibrationBlurScale = 0.1f;
		cameraSlot2.VibrationShakeMultiplier = 0f;
		CameraSlot cameraSlot3 = ThermalDonor(unitRoot, cameraSlot2);
		if (cameraSlot3 != null)
		{
			cameraSlot2.MechanicalScanWidth = cameraSlot3.MechanicalScanWidth;
			cameraSlot2.MechanicalScanHeight = cameraSlot3.MechanicalScanHeight;
			MelonLogger.Msg("[L2A7V][cmdr-sight] thermal mode takes the gunner's raster " + cameraSlot3.MechanicalScanWidth + "x" + cameraSlot3.MechanicalScanHeight + " and palette '" + ((cameraSlot3.FLIRBlitMaterialOverride == null) ? "<default>" : cameraSlot3.FLIRBlitMaterialOverride.name) + "'");
		}
		else
		{
			MelonLogger.Warning("[L2A7V][cmdr-sight] this unit carries no thermal channel to copy the imager settings from");
		}
		KeepOutOfTheCycle(unitRoot, cameraSlot2);
		LogComponents(cameraSlot2);
		cameraSlot2.FLIRBlitMaterialOverride = ((cameraSlot3 != null) ? cameraSlot3.FLIRBlitMaterialOverride : null);
		LpCitv.Attach(unitRoot, cameraSlot, cameraSlot2);
		LpCommanderSightRig lpCommanderSightRig = unitRoot.AddComponent<LpCommanderSightRig>();
		lpCommanderSightRig.Night = cameraSlot2;
		lpCommanderSightRig.Head = transform;
		lpCommanderSightRig.Commander = cameraSlot;
		MelonLogger.Msg("[L2A7V][citv] commander CITV: '" + PathOf(cameraSlot2.transform) + "' " + ((transform == null) ? "queued for the cupola sight" : "at the cupola sight") + ", 15/10/4 deg full-screen thermal, T enters from and returns to the commander, no scan, old Z sight disabled");
	}

	private static string Fields(float[] fields)
	{
		if (fields == null || fields.Length == 0)
		{
			return "no";
		}
		string[] array = new string[fields.Length];
		for (int i = 0; i < fields.Length; i++)
		{
			array[i] = fields[i].ToString("0.#");
		}
		return string.Join("/", array);
	}

	private static CameraSlot CloneView(CameraSlot source, GameObject unitRoot)
	{
		if (source == null)
		{
			return null;
		}
		Transform parent = ((source.transform.parent != null) ? source.transform.parent : unitRoot.transform);
		CameraSlot cameraSlot = UnityEngine.Object.Instantiate(source, parent, worldPositionStays: false);
		if (cameraSlot == null)
		{
			return null;
		}
		cameraSlot.name = "L2A7V_CmdSight";
		FieldInfo fieldInfo = AccessTools.Field(typeof(CameraSlot), "_reparentTarget");
		if (fieldInfo != null)
		{
			fieldInfo.SetValue(cameraSlot, null);
		}
		cameraSlot.transform.SetParent(parent, worldPositionStays: false);
		cameraSlot.transform.localPosition = source.transform.localPosition;
		cameraSlot.transform.localRotation = source.transform.localRotation;
		cameraSlot.transform.localScale = source.transform.localScale;
		CameraSlot[] componentsInChildren = cameraSlot.GetComponentsInChildren<CameraSlot>(includeInactive: true);
		foreach (CameraSlot cameraSlot2 in componentsInChildren)
		{
			if (cameraSlot2 != null && cameraSlot2 != cameraSlot)
			{
				UnityEngine.Object.DestroyImmediate(cameraSlot2.gameObject);
			}
		}
		cameraSlot.LinkedNightSight = null;
		cameraSlot.LinkedDaySight = null;
		cameraSlot.IsLinkedNightSight = false;
		return cameraSlot;
	}

	private static CameraSlot ThermalDonor(GameObject unitRoot, CameraSlot sight)
	{
		CameraSlot cameraSlot = null;
		CameraSlot[] componentsInChildren = unitRoot.GetComponentsInChildren<CameraSlot>(includeInactive: true);
		foreach (CameraSlot cameraSlot2 in componentsInChildren)
		{
			if (!(cameraSlot2 == null) && !(cameraSlot2 == sight) && cameraSlot2.VisionType == NightVisionType.Thermal)
			{
				if (cameraSlot2.DoMechanicalFLIR)
				{
					return cameraSlot2;
				}
				if (cameraSlot == null)
				{
					cameraSlot = cameraSlot2;
				}
			}
		}
		return cameraSlot;
	}

	internal static Material GrayPalette(Material donor)
	{
		if (donor == null)
		{
			return null;
		}
		Material material = new Material(donor);
		material.name = donor.name + " (gray)";
		Shader shader = material.shader;
		if (shader == null)
		{
			return material;
		}
		List<string> list = new List<string>();
		for (int i = 0; i < shader.GetPropertyCount(); i++)
		{
			if (shader.GetPropertyType(i) != 0)
			{
				continue;
			}
			string propertyName = shader.GetPropertyName(i);
			if (!string.IsNullOrEmpty(propertyName))
			{
				string text = propertyName.ToLowerInvariant();
				if (text.Contains("color") || text.Contains("tint"))
				{
					material.SetColor(propertyName, Color.white);
					list.Add(propertyName);
				}
			}
		}
		MelonLogger.Msg("[L2A7V][cmdr-sight] imager palette '" + donor.name + "' copied, white-stated: " + ((list.Count == 0) ? "no colour property, so the tint lives elsewhere" : string.Join(", ", list)));
		return material;
	}

	internal static void AttachToHead(CameraSlot sight, Transform head)
	{
		if (!(sight == null) && !(head == null))
		{
			Transform parent = ((head.parent != null) ? head.parent : head);
			sight.transform.SetParent(parent, worldPositionStays: true);
			sight.transform.position = head.position;
			sight.transform.localScale = Vector3.one;
		}
	}

	private static void KeepOutOfTheCycle(GameObject unitRoot, CameraSlot sight)
	{
		sight.DirectAttachOnly = true;
		if (DesignatedSlots == null)
		{
			return;
		}
		Unit component = LpFind.Unit(unitRoot);
		if (component == null || !(DesignatedSlots.GetValue(component) is CameraSlot[] array) || Array.IndexOf(array, sight) < 0)
		{
			return;
		}
		List<CameraSlot> list = new List<CameraSlot>(array.Length);
		CameraSlot[] array2 = array;
		foreach (CameraSlot cameraSlot in array2)
		{
			if (cameraSlot != null && cameraSlot != sight)
			{
				list.Add(cameraSlot);
			}
		}
		DesignatedSlots.SetValue(component, list.ToArray());
	}

	private static ReticleMesh FitCommanderPlate(CameraSlot sight, GameObject unitRoot)
	{
		ReticleMesh reticleMesh = sight.GetComponentInChildren<ReticleMesh>(includeInactive: true);
		if (reticleMesh == null)
		{
			reticleMesh = CloneDonorReticle(sight, unitRoot);
		}
		if (reticleMesh == null)
		{
			MelonLogger.Warning("[L2A7V][cmdr-sight] no reticle mesh on the copied view and none on the gunner's optics, so the commander's plate is not fitted");
			return null;
		}
		reticleMesh.reticleSO = LpCommanderSight.OpticalPlate;
		SkinnedMeshRenderer component = reticleMesh.GetComponent<SkinnedMeshRenderer>();
		if (component != null)
		{
			component.sharedMesh = null;
		}
		reticleMesh.Load();
		reticleMesh.GetLight(ReticleTree.Light.Type.NightIllumination, out var value);
		reticleMesh.SetLight(ReticleTree.Light.Type.NightIllumination, (float.IsNaN(value) || value <= 0f) ? 0f : 1f);
		ReportGraticule(reticleMesh);
		MelonLogger.Msg("[L2A7V][cmdr-sight] commander plate '" + reticleMesh.reticleSO.name + "' fitted at " + sight.DefaultFov.ToString("0.#") + " deg + " + Fields(sight.OtherFovs) + " deg");
		return reticleMesh;
	}

	private static void ReportGraticule(ReticleMesh mesh)
	{
		SkinnedMeshRenderer component = mesh.GetComponent<SkinnedMeshRenderer>();
		if (component == null)
		{
			MelonLogger.Warning("[L2A7V][cmdr-sight] the graticule has no SkinnedMeshRenderer");
			return;
		}
		Mesh sharedMesh = component.sharedMesh;
		MelonLogger.Msg("[L2A7V][cmdr-sight] graticule: active=" + mesh.gameObject.activeInHierarchy + " renderer=" + component.enabled + " post=" + (mesh.GetComponent<PostMeshComp>() != null) + " layer=" + mesh.gameObject.layer + " mesh=" + ((sharedMesh == null) ? "<none>" : (sharedMesh.name + " " + sharedMesh.vertexCount + " verts " + sharedMesh.subMeshCount + " submesh(es) bounds " + sharedMesh.bounds.center.ToString() + "/" + sharedMesh.bounds.extents.ToString())) + " materials=" + ((component.sharedMaterials != null) ? component.sharedMaterials.Length : 0) + " scale=" + mesh.transform.localScale.ToString());
	}

	private static ReticleMesh CloneDonorReticle(CameraSlot sight, GameObject unitRoot)
	{
		ReticleMesh reticleMesh = null;
		CameraSlot cameraSlot = null;
		UsableOptic[] componentsInChildren = unitRoot.GetComponentsInChildren<UsableOptic>(includeInactive: true);
		foreach (UsableOptic usableOptic in componentsInChildren)
		{
			if (!(usableOptic == null) && !(usableOptic.reticleMesh == null) && !(usableOptic.slot == null) && !(usableOptic.slot == sight))
			{
				bool flag = usableOptic.slot.VisionType == NightVisionType.Thermal;
				if (reticleMesh == null || flag)
				{
					reticleMesh = usableOptic.reticleMesh;
					cameraSlot = usableOptic.slot;
				}
				if (flag)
				{
					break;
				}
			}
		}
		if (reticleMesh == null || cameraSlot == null)
		{
			return null;
		}
		ReticleMesh reticleMesh2 = UnityEngine.Object.Instantiate(reticleMesh, sight.transform, worldPositionStays: false);
		reticleMesh2.name = "L2A7V_CmdReticle";
		reticleMesh2.transform.localPosition = Vector3.zero;
		reticleMesh2.transform.localRotation = Quaternion.identity;
		reticleMesh2.transform.localScale = Vector3.one;
		if (!reticleMesh2.gameObject.activeSelf)
		{
			reticleMesh2.gameObject.SetActive(value: true);
		}
		MelonLogger.Msg("[L2A7V][cmdr-sight] commander graticule carried over from '" + PathOf(reticleMesh.transform) + "', framed against " + sight.DefaultFov.ToString("0.#") + " deg");
		return reticleMesh2;
	}

	private static Renderer BuildGraticule(CameraSlot sight)
	{
		float num = Mathf.Tan(0.17453292f) * 1f;
		float b = ((Screen.height > 0) ? ((float)Screen.width / (float)Screen.height) : 1.7778f);
		float num2 = num / Mathf.Max(1f, b);
		List<Vector3> vertices = new List<Vector3>();
		List<int> triangles = new List<int>();
		Bar(0f - num, 0f, -0.004f, 0f, 0.0011f);
		Bar(0.004f, 0f, num, 0f, 0.0011f);
		for (float num3 = 0.002f; num3 <= num; num3 += 0.002f)
		{
			float num4 = ((Mathf.Abs(Mathf.Repeat(num3, 0.010000001f)) < 0.0005f) ? 0.0026f : 0.0014f);
			Bar(num3, 0f - num4, num3, num4, 0.00088f);
			Bar(0f - num3, 0f - num4, 0f - num3, num4, 0.00088f);
		}
		for (float num5 = 0.003f; num5 <= num2; num5 += 0.003f)
		{
			float num6 = ((Mathf.Abs(Mathf.Repeat(num5, 0.015f)) < 0.002f) ? 0.0032f : 0.0018f);
			Bar(0f - num6, num5, num6, num5, 0.00088f);
		}
		Mesh mesh = new Mesh();
		mesh.name = "L2A7V_CMDR_mesh";
		mesh.SetVertices(vertices);
		mesh.SetTriangles(triangles, 0);
		mesh.RecalculateBounds();
		GameObject gameObject = new GameObject("L2A7V_CmdGraticule");
		gameObject.transform.SetParent(sight.transform, worldPositionStays: false);
		gameObject.transform.localPosition = Vector3.zero;
		gameObject.transform.localRotation = Quaternion.identity;
		gameObject.transform.localScale = Vector3.one;
		gameObject.layer = sight.gameObject.layer;
		gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
		Shader shader = Shader.Find("Sprites/Default");
		if (shader == null)
		{
			shader = Shader.Find("Unlit/Color");
		}
		if (shader == null)
		{
			shader = Shader.Find("GUI/Text Shader");
		}
		Material material = new Material(shader);
		material.color = new Color(0f, 0f, 0f, 1f);
		material.renderQueue = 3999;
		meshRenderer.sharedMaterial = material;
		meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
		meshRenderer.receiveShadows = false;
		MelonLogger.Msg("[L2A7V][cmdr-sight] commander graticule drawn: " + vertices.Count + " verts over " + (Mathf.Atan(num / 1f) * 2f * 57.29578f).ToString("0.#") + " deg, shader " + ((shader == null) ? "<missing>" : shader.name) + ", layer " + gameObject.layer);
		return meshRenderer;
		void Bar(float x1, float y1, float x2, float y2, float width)
		{
			float num7 = x2 - x1;
			float num8 = y2 - y1;
			float num9 = Mathf.Sqrt(num7 * num7 + num8 * num8);
			if (!(num9 < 1E-06f))
			{
				float num10 = (0f - num8) / num9 * width * 0.5f;
				float num11 = num7 / num9 * width * 0.5f;
				int count = vertices.Count;
				vertices.Add(new Vector3(x1 + num10, y1 + num11, 1f));
				vertices.Add(new Vector3(x1 - num10, y1 - num11, 1f));
				vertices.Add(new Vector3(x2 - num10, y2 - num11, 1f));
				vertices.Add(new Vector3(x2 + num10, y2 + num11, 1f));
				triangles.Add(count);
				triangles.Add(count + 1);
				triangles.Add(count + 2);
				triangles.Add(count);
				triangles.Add(count + 2);
				triangles.Add(count + 3);
			}
		}
	}

	private static void LogComponents(CameraSlot sight)
	{
		List<string> list = new List<string>();
		Component[] componentsInChildren = sight.GetComponentsInChildren<Component>(includeInactive: true);
		foreach (Component component in componentsInChildren)
		{
			if (!(component == null))
			{
				list.Add(component.GetType().Name);
			}
		}
		list.Sort(StringComparer.Ordinal);
		MelonLogger.Msg("[L2A7V][cmdr-sight] sight components: " + string.Join(", ", list));
	}

	private static CameraSlot FindSlot(GameObject unitRoot)
	{
		CameraSlot cameraSlot = null;
		CameraSlot[] componentsInChildren = unitRoot.GetComponentsInChildren<CameraSlot>(includeInactive: true);
		foreach (CameraSlot cameraSlot2 in componentsInChildren)
		{
			if (!(cameraSlot2 == null) && cameraSlot2.IsExterior && cameraSlot2.AllowFreeLook)
			{
				if (cameraSlot2.transform.name.ToLowerInvariant().Contains("commander head"))
				{
					return cameraSlot2;
				}
				if (cameraSlot == null)
				{
					cameraSlot = cameraSlot2;
				}
			}
		}
		return cameraSlot;
	}

	private static string PathOf(Transform node)
	{
		if (node == null)
		{
			return "<null>";
		}
		string text = node.name;
		Transform parent = node.parent;
		while (parent != null)
		{
			text = parent.name + "/" + text;
			parent = parent.parent;
		}
		return text;
	}
}

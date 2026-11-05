using System;
using System.Collections.Generic;
using System.Text;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using MelonLoader;
using Reticle;
using UnityEngine;

namespace Leopard2A7V;

public static class LpBaseRig
{
	private static readonly string[] KeepFragments = new string[15]
	{
		"leo2_int", "m1ip_interior", "leo2_volumes", "collider", "hitzone", "interior", "wheel", "suspension", "sight open", "sight closed",
		"turret parent", "ammo door", "loader's hatch", "turret holes", "grass cutter"
	};

	private static readonly string[] WheelArtFragments = new string[1] { "running gear" };

	private static readonly string[] TrackArtFragments = new string[1] { "leo2_tracks" };

	public const int ImprintLayer = 24;

	private static readonly Dictionary<Mesh, Mesh> EmptiedMeshes = new Dictionary<Mesh, Mesh>();

	private static readonly HashSet<Transform> ProtectedTransforms = new HashSet<Transform>();

	private static readonly HashSet<string> _RigWarned = new HashSet<string>(StringComparer.Ordinal);

	private const int MaxProtectedRenderers = 8;

	internal static Renderer[] DonorSnapshot;

	internal static int CollapseSkinDetailBones(Transform unitRoot)
	{
		int num = 0;
		SkinnedMeshRenderer[] componentsInChildren = unitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
		foreach (SkinnedMeshRenderer skinnedMeshRenderer in componentsInChildren)
		{
			if (skinnedMeshRenderer == null || LpVisual.IsImportedArt(skinnedMeshRenderer.transform))
			{
				continue;
			}
			Transform[] bones = skinnedMeshRenderer.bones;
			if (bones == null)
			{
				continue;
			}
			Transform[] array = bones;
			foreach (Transform transform in array)
			{
				if (!(transform == null) && IsHullDetailBone(transform.name) && !(transform.localScale == Vector3.zero))
				{
					transform.localScale = Vector3.zero;
					num++;
				}
			}
		}
		return num;
	}

	internal static bool IsHullDetailBone(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}
		string text = name.ToLowerInvariant();
		if (!text.StartsWith("skirt_") && !text.StartsWith("fender_") && !text.StartsWith("flap") && !text.StartsWith("gill_") && !text.Contains("mudflap"))
		{
			return text.Contains("side_skirt");
		}
		return true;
	}

	public static void Apply(Transform unitRoot)
	{
		CollectProtected(unitRoot);
		int num = 0;
		int num2 = 0;
		Renderer[] componentsInChildren = unitRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren)
		{
			if (!LpVisual.IsImportedArt(renderer.transform) && !(renderer is ParticleSystemRenderer) && ShouldHide(renderer.transform, renderer.name))
			{
				StripGeometry(renderer);
				renderer.enabled = false;
				num++;
				string text = PathOf(renderer.transform).ToLowerInvariant();
				if (text.Contains("net") || text.Contains("camo") || text.Contains("marking"))
				{
					num2++;
				}
			}
		}
		MelonLogger.Msg($"[L2A7V] stripped the geometry of {num} donor renderer(s), " + $"{num2} of them netting or markings");
		int num3 = CollapseSkinDetailBones(unitRoot);
		if (num3 > 0)
		{
			MelonLogger.Msg($"[L2A7V] collapsed {num3} skinned donor bone(s) (skirts, fenders, grilles) " + "so the old hull cannot poke through");
		}
		int num4 = 0;
		componentsInChildren = unitRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer2 in componentsInChildren)
		{
			if (!(renderer2 == null) && renderer2.gameObject.layer == 24)
			{
				MeshFilter component = renderer2.GetComponent<MeshFilter>();
				if (component != null && component.sharedMesh != null)
				{
					num4++;
				}
			}
		}
		MelonLogger.Msg($"[L2A7V] {num4} track-imprint brush(es) kept on layer {24}, " + "the only layer the TVE trax camera renders");
	}

	internal static void StripGeometry(Renderer renderer)
	{
		MeshFilter component = renderer.GetComponent<MeshFilter>();
		if (component != null && component.sharedMesh != null)
		{
			component.sharedMesh = null;
		}
		SkinnedMeshRenderer skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
		if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
		{
			skinnedMeshRenderer.sharedMesh = EmptiedCopy(skinnedMeshRenderer.sharedMesh);
		}
	}

	private static Mesh EmptiedCopy(Mesh source)
	{
		if (EmptiedMeshes.TryGetValue(source, out var value) && value != null)
		{
			return value;
		}
		Mesh mesh = new Mesh
		{
			name = source.name + " (no faces)"
		};
		try
		{
			mesh.subMeshCount = source.subMeshCount;
			int[] triangles = new int[0];
			for (int i = 0; i < mesh.subMeshCount; i++)
			{
				mesh.SetTriangles(triangles, i);
			}
			mesh.bounds = source.bounds;
		}
		catch (Exception)
		{
			mesh.subMeshCount = 1;
			mesh.SetTriangles(new int[0], 0);
		}
		EmptiedMeshes[source] = mesh;
		return mesh;
	}

	public static bool ShouldHide(Transform node, string rendererName)
	{
		if (IsProtected(node))
		{
			return false;
		}
		string text = PathOf(node) + "/" + rendererName;
		string[] keepFragments = KeepFragments;
		foreach (string value in keepFragments)
		{
			if (text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return false;
			}
		}
		keepFragments = WheelArtFragments;
		foreach (string value2 in keepFragments)
		{
			if (text.IndexOf(value2, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return !LpConfig.UseDonorRunningGear;
			}
		}
		keepFragments = TrackArtFragments;
		foreach (string value3 in keepFragments)
		{
			if (text.IndexOf(value3, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				if (!LpConfig.UseDonorRunningGear && !LpConfig.UseDonorTrackArt)
				{
					return !LpTracks.DonorFallback;
				}
				return false;
			}
		}
		return true;
	}

	private static void CollectProtected(Transform unitRoot)
	{
		ProtectedTransforms.Clear();
		CameraSlot[] componentsInChildren = unitRoot.GetComponentsInChildren<CameraSlot>(includeInactive: true);
		foreach (CameraSlot cameraSlot in componentsInChildren)
		{
			if (cameraSlot.ZeroScaleItems != null)
			{
				Transform[] zeroScaleItems = cameraSlot.ZeroScaleItems;
				for (int j = 0; j < zeroScaleItems.Length; j++)
				{
					AddProtected(zeroScaleItems[j], unitRoot);
				}
			}
			if (cameraSlot.ExclusiveItems != null)
			{
				GameObject[] exclusiveItems = cameraSlot.ExclusiveItems;
				foreach (GameObject gameObject in exclusiveItems)
				{
					AddIfInside((gameObject != null) ? gameObject.transform : null, unitRoot);
				}
			}
			if (cameraSlot.LinkedNightSight != null)
			{
				AddProtected(cameraSlot.LinkedNightSight.transform, unitRoot);
			}
		}
		UsableOptic[] componentsInChildren2 = unitRoot.GetComponentsInChildren<UsableOptic>(includeInactive: true);
		foreach (UsableOptic usableOptic in componentsInChildren2)
		{
			AddProtected(usableOptic.Transform, unitRoot);
			AddProtected(usableOptic.LevelTransform, unitRoot);
			AddProtected((usableOptic.ReadyToFireObject != null) ? usableOptic.ReadyToFireObject.transform : null, unitRoot);
			AddProtected((usableOptic.UnreadyToFireObject != null) ? usableOptic.UnreadyToFireObject.transform : null, unitRoot);
			AddProtected((usableOptic.FaultObject != null) ? usableOptic.FaultObject.transform : null, unitRoot);
			AddProtected((usableOptic.OverridingObject != null) ? usableOptic.OverridingObject.transform : null, unitRoot);
		}
		ReticleMesh[] componentsInChildren3 = unitRoot.GetComponentsInChildren<ReticleMesh>(includeInactive: true);
		for (int i = 0; i < componentsInChildren3.Length; i++)
		{
			AddProtected(componentsInChildren3[i].transform, unitRoot);
		}
	}

	private static void AddProtected(Transform node, Transform unitRoot)
	{
		if (!IsInside(node, unitRoot))
		{
			return;
		}
		Renderer[] componentsInChildren = node.GetComponentsInChildren<Renderer>(includeInactive: true);
		int num = 0;
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			if (!LpVisual.IsImportedArt(componentsInChildren[i].transform))
			{
				num++;
			}
		}
		if (num > 8)
		{
			if (_RigWarned.Add(PathOf(node)))
			{
				MelonLogger.Warning("[L2A7V] a sight reference names " + PathOf(node) + ", which covers " + $"{num} donor renderer(s) - that is a rig bone, not sight art, " + "so its subtree still gets stripped");
			}
		}
		else
		{
			ProtectedTransforms.Add(node);
		}
	}

	private static bool IsInside(Transform node, Transform unitRoot)
	{
		if (node == null)
		{
			return false;
		}
		Transform transform = node;
		int num = 0;
		while (transform != null && num++ < 32)
		{
			if ((object)transform == unitRoot)
			{
				return true;
			}
			transform = transform.parent;
		}
		return false;
	}

	private static void AddIfInside(Transform node, Transform unitRoot)
	{
		if (IsInside(node, unitRoot))
		{
			ProtectedTransforms.Add(node);
		}
	}

	private static bool IsProtected(Transform node)
	{
		if (ProtectedTransforms.Count == 0)
		{
			return false;
		}
		Transform transform = node;
		int num = 0;
		while (transform != null && num++ < 32)
		{
			if (ProtectedTransforms.Contains(transform))
			{
				return true;
			}
			transform = transform.parent;
		}
		return false;
	}

	internal static void SnapshotDonor(Transform unitRoot)
	{
		if (unitRoot == null)
		{
			return;
		}
		List<Renderer> list = new List<Renderer>();
		Renderer[] componentsInChildren = unitRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren)
		{
			if (!(renderer == null) && !(renderer is ParticleSystemRenderer) && ShouldHide(renderer.transform, renderer.name) && renderer.gameObject.layer != 24 && !(renderer.GetComponentInParent<UsableOptic>() != null) && !(renderer.GetComponentInChildren<UniformArmor>(includeInactive: true) != null) && !(renderer.GetComponentInChildren(typeof(ReticleMesh), includeInactive: true) != null))
			{
				list.Add(renderer);
			}
		}
		DonorSnapshot = list.ToArray();
		MelonLogger.Msg("[L2A7V] donor snapshot: " + DonorSnapshot.Length + " renderer(s) before conversion");
	}

	internal static string PathOf(Transform node)
	{
		StringBuilder stringBuilder = new StringBuilder(node.name);
		Transform parent = node.parent;
		int num = 0;
		while (parent != null && num++ < 24)
		{
			stringBuilder.Insert(0, parent.name + "/");
			parent = parent.parent;
		}
		return stringBuilder.ToString();
	}
}

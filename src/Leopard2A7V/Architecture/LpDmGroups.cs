using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.PhysicsHelpers;
using GHPC.Utility;
using GHPC.Vehicle;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V.Architecture;

internal sealed class LpDmGroup
{
	internal string Name;
	internal Transform Bone;
	internal Transform Armour;
	internal Transform Aar;
}

internal static class LpDmGroups
{
	private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
	private static readonly FieldInfo FollowPos = typeof(LateFollow).GetField("_localPosShift", Any);
	private static readonly FieldInfo FollowRot = typeof(LateFollow).GetField("_localRotShift", Any);
	private static readonly FieldInfo GroupColliders = typeof(RaycastColliderGroup).GetField("childColliders", Any);

	internal static LpDmGroup Find(GameObject root, string name)
	{
		string token = name + " COLLIDERS";
		foreach (LateFollow follow in root.GetComponentsInChildren<LateFollow>(true))
		{
			if (follow == null || follow.FollowTarget == null || follow.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) continue;
			Transform t = follow.transform;
			string suffix = name == "HULL" ? "hull" : "turret";
			return new LpDmGroup
			{
				Name = name, Bone = follow.FollowTarget,
				Armour = t.Find("LEO2_armour_" + suffix) ?? t.Find("ARMOR") ?? t,
				Aar = t.Find("LEO2_aar_" + suffix) ?? t.Find("AAR") ?? t
			};
		}
		return null;
	}

	internal static Transform Rig(GameObject root)
	{
		foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
		{
			if (t != null && t.name.Equals("leo2_rig", StringComparison.OrdinalIgnoreCase)) return t;
		}
		return null;
	}

	// A collider group can have its own LateFollow offset. Do not parent to a
	// translated ready-rack pivot or apply the exported coordinate swap twice.
	internal static void PlaceOnBone(Transform piece, Transform bone, Transform target)
	{
		LateFollow follow = null;
		foreach (LateFollow f in target.GetComponentsInParent<LateFollow>(true))
		{
			if (f.FollowTarget == bone) { follow = f; break; }
		}
		if (follow == null)
		{
			piece.SetParent(bone, false);
			piece.localPosition = Vector3.zero;
			piece.localRotation = Quaternion.identity;
			piece.localScale = Vector3.one;
			piece.SetParent(target, true);
			return;
		}
		Vector3 shift = FollowPos == null ? Vector3.zero : (Vector3)FollowPos.GetValue(follow);
		Quaternion rotation = FollowRot == null ? Quaternion.identity : (Quaternion)FollowRot.GetValue(follow);
		Matrix4x4 relative = Matrix4x4.TRS(shift, rotation, Vector3.one) * follow.transform.worldToLocalMatrix * target.localToWorldMatrix;
		Matrix4x4 inverse = relative.inverse;
		piece.SetParent(target, false);
		piece.localPosition = inverse.MultiplyPoint3x4(Vector3.zero);
		piece.localRotation = inverse.rotation;
		piece.localScale = Vector3.one;
	}

	internal static Matrix4x4 PartToGroup(Transform group, Transform rig, Vector3 anchor)
	{
		return group.worldToLocalMatrix * rig.localToWorldMatrix * Matrix4x4.Translate(anchor);
	}

	internal static Vector3 Anchor(string group)
	{
		if (group == "TURRET") return LpConfig.TurretAxis;
		if (group == "GUN") return LpConfig.GunAxis;
		return Vector3.zero;
	}

	internal static List<Transform> UnitObjects(GameObject root)
	{
		List<Transform> objects = new List<Transform> { root.transform };
		HashSet<Transform> seen = new HashSet<Transform> { root.transform };
		// One bounded wiring pass, after donor initialization. No permanent
		// global scan is retained on the vehicle.
		foreach (LateFollow f in UnityEngine.Object.FindObjectsByType<LateFollow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
		{
			if (f == null || f.FollowTarget == null || !f.FollowTarget.IsChildOf(root.transform) || f.transform.IsChildOf(root.transform)) continue;
			if (seen.Add(f.transform)) objects.Add(f.transform);
		}
		return objects;
	}

	internal static void UnregisterCollider(Collider collider, List<Transform> objects)
	{
		if (collider == null || GroupColliders == null) return;
		foreach (Transform root in objects)
		{
			foreach (RaycastColliderGroup group in root.GetComponentsInChildren<RaycastColliderGroup>(true))
			{
				if (GroupColliders.GetValue(group) is List<Collider> list) list.Remove(collider);
			}
		}
	}

	internal static void StripArmour(GameObject piece, List<Transform> objects)
	{
		foreach (Collider c in piece.GetComponents<Collider>())
		{
			UnregisterCollider(c, objects);
			UnityEngine.Object.Destroy(c);
		}
		foreach (UniformArmor armor in piece.GetComponents<UniformArmor>()) UnityEngine.Object.Destroy(armor);
		foreach (VariableArmor armor in piece.GetComponents<VariableArmor>()) UnityEngine.Object.Destroy(armor);
		foreach (Renderer r in piece.GetComponents<Renderer>()) UnityEngine.Object.Destroy(r);
		foreach (AarVisual aar in piece.GetComponents<AarVisual>()) UnityEngine.Object.Destroy(aar);
		if (piece.transform.childCount == 0 && piece.GetComponent<GHPC.Equipment.DestructibleComponent>() == null) piece.SetActive(false);
	}

	internal static Mesh MeshFrom(LpXrayParts.Part part)
	{
		Mesh mesh = new Mesh { name = "L2A7V DM " + part.Name, indexFormat = part.Positions.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
		mesh.vertices = part.Positions;
		mesh.triangles = part.Triangles;
		mesh.RecalculateBounds();
		return mesh;
	}

	internal static Mesh MeshFrom(ObjGeometry part)
	{
		Mesh mesh = new Mesh { name = "L2A7V DM " + part.ObjectName, indexFormat = part.Positions.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
		mesh.vertices = part.Positions;
		mesh.triangles = part.Triangles;
		mesh.RecalculateBounds();
		return mesh;
	}

	internal static Mesh Transformed(Mesh source, Matrix4x4 transform)
	{
		Vector3[] vertices = source.vertices;
		for (int i = 0; i < vertices.Length; i++) vertices[i] = transform.MultiplyPoint3x4(vertices[i]);
		Mesh result = new Mesh
		{
			name = source.name + " collider-space", indexFormat = source.indexFormat,
			vertices = vertices, triangles = source.triangles
		};
		result.RecalculateBounds();
		return result;
	}

	internal static void AddXrayRenderer(GameObject host, Mesh mesh, Material material, int layer)
	{
		if (mesh == null || material == null) return;
		GameObject visual = new GameObject("x-ray");
		visual.layer = layer;
		visual.transform.SetParent(host.transform, false);
		visual.AddComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
		renderer.sharedMaterial = material;
		renderer.shadowCastingMode = ShadowCastingMode.Off;
		renderer.receiveShadows = false;
	}
}

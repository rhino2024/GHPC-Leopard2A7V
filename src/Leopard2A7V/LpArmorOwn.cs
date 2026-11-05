using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpArmorOwn
{
	private sealed class Stats
	{
		internal int Seen;

		internal int Resolved;

		internal int NoGeometry;

		internal int Built;

		internal int BuildFailed;
	}

	private sealed class ArmorPiece
	{
		internal Component Source;

		internal Collider Collider;

		internal Bounds Bounds;

		internal LpArmor.Zone Zone;

		internal string Name;

		internal bool IsTurret;

		internal bool IsGun;
	}

	private const string Tag = "[L2A7V][armour]";

	private const string Marker = "L2A7V_OwnArmour";

	private static readonly HashSet<string> _Built = new HashSet<string>(StringComparer.Ordinal);

	private static Material _xray;

	public static bool Build(GameObject unitRoot)
	{
		if (unitRoot == null)
		{
			return true;
		}
		Transform transform = FindPivot(unitRoot, "L2A7V_HullPivot") ?? FindBone(unitRoot, "hull");
		Transform transform2 = FindPivot(unitRoot, "L2A7V_TurretPivot") ?? FindBone(unitRoot, "turret");
		Transform transform3 = FindPivot(unitRoot, "L2A7V_GunPivot") ?? FindBone(unitRoot, "mantlet", "gun");
		if (transform == null)
		{
			MelonLogger.Warning("[L2A7V][armour] neither our hull pivot nor the donor's hull bone was found on " + unitRoot.name + "; the protection stays donor");
			return true;
		}
		Stats stats = new Stats();
		HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
		foreach (ArmorPiece item in Snapshot(unitRoot, used, stats))
		{
			Transform transform4 = (item.IsGun ? transform3 : (item.IsTurret ? transform2 : transform));
			if (transform4 == null)
			{
				transform4 = transform;
			}
			if (!BuildPiece(transform4, item))
			{
				stats.BuildFailed++;
				continue;
			}
			Disable(item);
			stats.Built++;
		}
		MelonLogger.Msg("[L2A7V][armour] snapshot: " + stats.Seen + " donor piece(s), " + stats.Resolved + " matched a zone, " + stats.NoGeometry + " had no geometry, " + stats.BuildFailed + " could not be boxed; built " + stats.Built + " of our own on " + unitRoot.name);
		if (stats.Built == 0 && _Built.Contains(unitRoot.name))
		{
			return true;
		}
		_Built.Add(unitRoot.name);
		return true;
	}

	private static List<ArmorPiece> Snapshot(GameObject unitRoot, HashSet<string> used, Stats stats)
	{
		List<ArmorPiece> list = new List<ArmorPiece>();
		UniformArmor[] componentsInChildren = unitRoot.GetComponentsInChildren<UniformArmor>(includeInactive: true);
		foreach (UniformArmor uniformArmor in componentsInChildren)
		{
			if (uniformArmor == null)
			{
				continue;
			}
			stats.Seen++;
			if (LpArmor.TryResolve(uniformArmor.transform.name, uniformArmor.Name, used, out var zone))
			{
				stats.Resolved++;
				if (!TryAdd(list, uniformArmor, uniformArmor.GetComponent<Collider>(), zone))
				{
					stats.NoGeometry++;
				}
			}
		}
		VariableArmor[] componentsInChildren2 = unitRoot.GetComponentsInChildren<VariableArmor>(includeInactive: true);
		foreach (VariableArmor variableArmor in componentsInChildren2)
		{
			if (variableArmor == null)
			{
				continue;
			}
			stats.Seen++;
			if (LpArmor.TryResolve(variableArmor.transform.name, variableArmor.Name, used, out var zone2))
			{
				stats.Resolved++;
				if (!TryAdd(list, variableArmor, variableArmor.GetComponent<Collider>(), zone2))
				{
					stats.NoGeometry++;
				}
			}
		}
		return list;
	}

	private static bool TryAdd(List<ArmorPiece> pieces, Component source, Collider collider, LpArmor.Zone zone)
	{
		if (!TryBounds(source, collider, out var bounds))
		{
			return false;
		}
		if (bounds.size.sqrMagnitude < 1E-06f)
		{
			return false;
		}
		string text = PathOf(source.transform);
		pieces.Add(new ArmorPiece
		{
			Source = source,
			Collider = collider,
			Bounds = bounds,
			Zone = zone,
			Name = source.name,
			IsTurret = (text.Contains("turret") || text.Contains("cupola") || text.Contains("peri")),
			IsGun = (text.Contains("gun") || text.Contains("mantlet") || text.Contains("barrel"))
		});
		return true;
	}

	private static bool TryBounds(Component source, Collider collider, out Bounds bounds)
	{
		if (collider != null && collider.bounds.size.sqrMagnitude > 1E-06f)
		{
			bounds = collider.bounds;
			return true;
		}
		Renderer renderer = source.GetComponent<Renderer>() ?? source.GetComponentInChildren<Renderer>(includeInactive: true);
		if (renderer != null && renderer.bounds.size.sqrMagnitude > 1E-06f)
		{
			bounds = renderer.bounds;
			return true;
		}
		MeshFilter meshFilter = source.GetComponent<MeshFilter>() ?? source.GetComponentInChildren<MeshFilter>(includeInactive: true);
		if (meshFilter != null && meshFilter.sharedMesh != null)
		{
			Bounds bounds2 = meshFilter.sharedMesh.bounds;
			Vector3 center = meshFilter.transform.TransformPoint(bounds2.center);
			Vector3 vector = meshFilter.transform.TransformVector(bounds2.size);
			bounds = new Bounds(center, new Vector3(Mathf.Abs(vector.x), Mathf.Abs(vector.y), Mathf.Abs(vector.z)));
			if (bounds.size.sqrMagnitude > 1E-06f)
			{
				return true;
			}
		}
		bounds = default(Bounds);
		return false;
	}

	private static bool BuildPiece(Transform bone, ArmorPiece piece)
	{
		Collider collider = ((piece.Collider != null) ? piece.Collider : piece.Source.GetComponent<Collider>());
		Vector3 localPosition = bone.InverseTransformPoint(piece.Bounds.center);
		Vector3 vector = bone.InverseTransformVector(piece.Bounds.size);
		vector = new Vector3(Mathf.Abs(vector.x), Mathf.Abs(vector.y), Mathf.Abs(vector.z));
		if (vector.x < 0.01f && vector.y < 0.01f && vector.z < 0.01f)
		{
			return false;
		}
		GameObject gameObject = new GameObject("L2A7V_OwnArmour " + piece.Source.name);
		gameObject.transform.SetParent(bone, worldPositionStays: false);
		gameObject.layer = piece.Source.gameObject.layer;
		gameObject.tag = piece.Source.gameObject.tag;
		if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
		{
			gameObject.transform.position = collider.transform.position;
			gameObject.transform.rotation = collider.transform.rotation;
			MeshCollider meshCollider2 = gameObject.AddComponent<MeshCollider>();
			meshCollider2.sharedMesh = meshCollider.sharedMesh;
			meshCollider2.convex = false;
		}
		else if (collider is BoxCollider boxCollider)
		{
			gameObject.transform.position = boxCollider.transform.TransformPoint(boxCollider.center);
			gameObject.transform.rotation = boxCollider.transform.rotation;
			gameObject.transform.localScale = boxCollider.transform.lossyScale;
			gameObject.AddComponent<BoxCollider>().size = boxCollider.size;
		}
		else if (collider is SphereCollider sphereCollider)
		{
			gameObject.transform.position = sphereCollider.transform.position;
			gameObject.AddComponent<SphereCollider>().radius = sphereCollider.radius;
		}
		else if (collider is CapsuleCollider capsuleCollider)
		{
			gameObject.transform.position = capsuleCollider.transform.position;
			gameObject.transform.rotation = capsuleCollider.transform.rotation;
			CapsuleCollider capsuleCollider2 = gameObject.AddComponent<CapsuleCollider>();
			capsuleCollider2.radius = capsuleCollider.radius;
			capsuleCollider2.height = capsuleCollider.height;
			capsuleCollider2.direction = capsuleCollider.direction;
		}
		else
		{
			gameObject.transform.localPosition = localPosition;
			gameObject.transform.localRotation = Quaternion.identity;
			gameObject.transform.localScale = Vector3.one;
			gameObject.AddComponent<BoxCollider>().size = vector;
		}
		float armorScale = LpConfig.ArmorScale;
		UniformArmor uniformArmor = gameObject.AddComponent<UniformArmor>();
		FieldInfo[] fields = typeof(UniformArmor).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (FieldInfo fieldInfo in fields)
		{
			if (fieldInfo.IsLiteral || fieldInfo.IsInitOnly)
			{
				continue;
			}
			switch (fieldInfo.Name)
			{
			case "DetonatedObjects":
			case "UndetonatedObjects":
			case "CenterPoint":
			case "PrimarySabotRha":
			case "PrimaryHeatRha":
			case "SecondarySabotRha":
			case "SecondaryHeatRha":
			case "ThicknessListed":
				continue;
			}
			try
			{
				fieldInfo.SetValue(uniformArmor, fieldInfo.GetValue(piece.Source as UniformArmor));
			}
			catch (Exception)
			{
			}
		}
		uniformArmor.PrimarySabotRha = piece.Zone.Ke * armorScale;
		uniformArmor.PrimaryHeatRha = piece.Zone.Ce * armorScale;
		uniformArmor.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
		uniformArmor.AngleMatters = true;
		uniformArmor.DetonatedObjects = Array.Empty<GameObject>();
		uniformArmor.UndetonatedObjects = Array.Empty<GameObject>();
		uniformArmor.CenterPoint = gameObject.transform;
		uniformArmor.SetName(piece.Name);
		Unit componentInParent = gameObject.GetComponentInParent<Unit>();
		if (componentInParent != null)
		{
			uniformArmor.Unit = componentInParent;
		}
		AarVisual aarVisual = gameObject.AddComponent<AarVisual>();
		aarVisual.HideUntilAar = true;
		Material material = XrayMaterial();
		if (material != null)
		{
			aarVisual.SwitchMaterials = true;
			aarVisual.AarMaterial = material;
			aarVisual.UpdateVisibility();
		}
		return true;
	}

	internal static Material XrayMaterial()
	{
		if (_xray != null)
		{
			return _xray;
		}
		AarVisual[] array = Resources.FindObjectsOfTypeAll<AarVisual>();
		foreach (AarVisual aarVisual in array)
		{
			if (!(aarVisual == null) && !(aarVisual.AarMaterial == null) && aarVisual.AarMaterial.name.ToLowerInvariant().Contains("see-through"))
			{
				_xray = aarVisual.AarMaterial;
				break;
			}
		}
		return _xray;
	}

	private static void Disable(ArmorPiece piece)
	{
		if (piece.Collider != null)
		{
			piece.Collider.enabled = false;
		}
		if (piece.Source is Behaviour behaviour)
		{
			behaviour.enabled = false;
		}
	}

	private static Transform FindPivot(GameObject unitRoot, string name)
	{
		Transform[] componentsInChildren = unitRoot.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (transform != null && transform.name == name)
			{
				return transform;
			}
		}
		return null;
	}

	private static Transform FindBone(GameObject unitRoot, params string[] names)
	{
		Transform[] componentsInChildren = unitRoot.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (transform == null)
			{
				continue;
			}
			string text = transform.name.ToLowerInvariant();
			foreach (string text2 in names)
			{
				if (text == text2)
				{
					return transform;
				}
			}
		}
		return null;
	}

	private static string PathOf(Transform node)
	{
		string text = ((node != null) ? node.name : string.Empty);
		Transform transform = ((node != null) ? node.parent : null);
		while (transform != null)
		{
			text = transform.name + "/" + text;
			transform = transform.parent;
		}
		return text.ToLowerInvariant();
	}
}

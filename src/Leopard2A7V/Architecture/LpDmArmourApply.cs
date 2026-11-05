using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Utility;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V.Architecture;

internal static class LpDmArmourApply
{
	private const string Prefix = "L2A7V_DM_Armour ";
	private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
	private static readonly FieldInfo ArmourName = typeof(UniformArmor).GetField("_name", Any);
	private static readonly FieldInfo ArmourType = typeof(UniformArmor).GetField("_armorType", Any);
	private static readonly Dictionary<string, ArmorCodexScriptable> Codices = new Dictionary<string, ArmorCodexScriptable>(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
	private static LpDmArmour _table;
	private static bool _dataAttempted;
	private static bool _dataFailed;
	private static readonly List<string> RetiredOnce = new List<string>();

	internal static bool Build(GameObject root)
	{
		if (root == null) return false;
		if (_dataFailed) return true;
		if (!_dataAttempted)
		{
			_dataAttempted = true;
			try { _table = LpDmArmour.Load(); }
			catch (Exception ex)
			{
				_dataFailed = true;
				MelonLogger.Error("[L2A7V][dm-armour] invalid data; donor and bugfix armour are left untouched: " + ex.Message);
				return true;
			}
		}
		Transform rig = LpDmGroups.Rig(root);
		if (rig == null || !LpXrayParts.Loaded || LpXrayParts.Failed || !ObjModel.Loaded || ObjModel.Failed) return false;
		if (root.GetComponent<LpDmArmourMarker>() != null) return true;
		Unit unit = LpFind.Unit(root);
		if (unit == null) return false;
		int built = 0;
		int skipped = 0;
		try
		{
			// Only the first successfully configured unit strips the explicitly
			// replaced donor plates. A failed spawn never removes donor protection.
			HashSet<string> retired = new HashSet<string>(StringComparer.Ordinal);
			foreach (LpDmArmour.Entry entry in _table.Entries)
			{
				foreach (string part in entry.Meshes)
				{
					string[] names = entry.Piece.Split('|');
					foreach (string name in names)
					{
						Mesh mesh = Source(part, name);
						if (mesh == null) { skipped++; continue; }
						string groupName = GroupFor(name, part);
						LpDmGroup group = LpDmGroups.Find(root, groupName);
						if (group == null || group.Armour == null) { skipped++; continue; }
						GameObject plate = Spawn(root, rig, group, entry, name, part, mesh, unit);
						if (plate == null) { skipped++; continue; }
						built++;
						foreach (string old in entry.Replace) retired.Add(old);
					}
				}
			}
			if (built == 0) return false;
			List<Transform> objects = LpDmGroups.UnitObjects(root);
			RetireExplicit(root, retired, objects);
			root.AddComponent<LpDmArmourMarker>().Built = built;
			MelonLogger.Msg("[L2A7V][dm-armour] built " + built + " data-driven plate(s), skipped " + skipped + ", explicit donor replacement count " + retired.Count + "; additive all-convex overlay disabled");
			return true;
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V][dm-armour] bounded build will retry: " + ex.Message);
			return false;
		}
	}

	private static string GroupFor(string name, string source)
	{
		string n = name.ToLowerInvariant();
		if (source == "visual") return n.StartsWith("ex_armor_body", StringComparison.Ordinal) || n.StartsWith("ex_armor_l_", StringComparison.Ordinal) || n.StartsWith("ex_armor_r_", StringComparison.Ordinal) ? "HULL" : "TURRET";
		if (n.StartsWith("gun_", StringComparison.Ordinal) || n.StartsWith("composite_armor_turret_10", StringComparison.Ordinal)) return "GUN";
		if (n.Contains("turret") || n.StartsWith("spall_liner_0", StringComparison.Ordinal) && !n.EndsWith("06_xray", StringComparison.Ordinal) && !n.EndsWith("07_xray", StringComparison.Ordinal) && !n.EndsWith("08_xray", StringComparison.Ordinal) && !n.EndsWith("09_xray", StringComparison.Ordinal) || n.StartsWith("firewall", StringComparison.Ordinal)) return "TURRET";
		return "HULL";
	}

	private static Mesh Source(string source, string name)
	{
		string key = source + ":" + name;
		if (MeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
		if (source == "xray")
		{
			foreach (LpXrayParts.Part part in LpXrayParts.Parts)
			{
				if (part.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return MeshCache[key] = LpDmGroups.MeshFrom(part);
			}
		}
		else
		{
			foreach (ObjGeometry part in ObjModel.Geometries)
			{
				if (part.ObjectName.Equals(name, StringComparison.OrdinalIgnoreCase)) return MeshCache[key] = LpDmGroups.MeshFrom(part);
			}
		}
		return null;
	}

	private static GameObject Spawn(GameObject root, Transform rig, LpDmGroup group, LpDmArmour.Entry entry, string name, string source, Mesh sourceMesh, Unit unit)
	{
		string objectName = Prefix + name;
		foreach (Transform child in group.Armour)
		{
			if (child.name == objectName && child.GetComponent<Collider>() != null) return child.gameObject;
		}
		GameObject go = new GameObject(objectName);
		go.SetActive(false);
		go.layer = CodeUtils.LAYER_INDEX_RAYCAST_ONLY;
		go.tag = "Penetrable";
		LpDmGroups.PlaceOnBone(go.transform, group.Bone, group.Armour);
		Vector3 anchor = source == "visual" && group.Name == "HULL" ? LpConfig.Offset : Vector3.zero;
		Matrix4x4 matrix = LpDmGroups.PartToGroup(go.transform, rig, anchor);
		Mesh mesh = LpDmGroups.Transformed(sourceMesh, matrix);
		try
		{
			MeshCollider collider = go.AddComponent<MeshCollider>();
			collider.sharedMesh = mesh;
			collider.convex = entry.Convex;
			collider.isTrigger = false;
			float scale = LpConfig.ArmorScale;
			if (entry.Variable)
			{
				VariableArmor armor = go.AddComponent<VariableArmor>();
				armor.SetName(entry.Name);
				armor.Unit = unit;
				SetPrivate(armor, "_armorType", Codex(entry, scale));
			}
			else
			{
				UniformArmor armor = go.AddComponent<UniformArmor>();
				armor.PrimarySabotRha = entry.Ke * scale;
				armor.PrimaryHeatRha = entry.Ce * scale;
				armor.SecondarySabotRha = entry.KeAfter * scale;
				armor.SecondaryHeatRha = entry.CeAfter * scale;
				armor.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
				armor.AngleMatters = entry.AngleMatters;
				armor.DetonatedObjects = Array.Empty<GameObject>();
				armor.UndetonatedObjects = Array.Empty<GameObject>();
				armor.CenterPoint = go.transform;
				armor.Unit = unit;
				armor.SetName(entry.Name);
				if (ArmourName != null) ArmourName.SetValue(armor, entry.Name);
				if (ArmourType != null) ArmourType.SetValue(armor, Codex(entry, scale));
			}
			AarVisual aar = go.AddComponent<AarVisual>();
			aar.HideUntilAar = true;
			Material xray = LpArmorOwn.XrayMaterial();
			aar.SwitchMaterials = xray != null;
			aar.AarMaterial = xray;
			LpDmGroups.AddXrayRenderer(go, mesh, xray, group.Aar.gameObject.layer);
			go.SetActive(true);
			RaycastColliderGroup.AddNewColliderSetToParentGroup(go.transform);
			aar.UpdateVisibility();
			return go;
		}
		catch (Exception ex)
		{
			go.name = "L2A7V_DM_Incomplete " + name;
			UnityEngine.Object.Destroy(go);
			MelonLogger.Warning("[L2A7V][dm-armour] '" + name + "' could not be built: " + ex.Message);
			return null;
		}
	}

	private static void RetireExplicit(GameObject root, HashSet<string> names, List<Transform> objects)
	{
		if (names.Count == 0) return;
		int count = 0;
		foreach (Transform obj in objects)
		{
			foreach (Transform t in obj.GetComponentsInChildren<Transform>(true))
			{
				if (t == null || t.name.StartsWith(Prefix, StringComparison.Ordinal) || !names.Contains(t.name)) continue;
				if (t.GetComponent<UniformArmor>() == null && t.GetComponent<VariableArmor>() == null) continue;
				LpDmGroups.StripArmour(t.gameObject, objects);
				count++;
				if (RetiredOnce.Count < 100 && !RetiredOnce.Contains(t.name)) RetiredOnce.Add(t.name);
			}
		}
		MelonLogger.Msg("[L2A7V][dm-armour] retired " + count + " explicitly listed donor plate(s); tracks, crew, running gear and unlisted donor plates preserved");
	}

	private static ArmorCodexScriptable Codex(LpDmArmour.Entry e, float scale)
	{
		string key = e.Material + "|" + (e.Variable ? (e.Ke * scale).ToString("R") + "/" + (e.Ce * scale).ToString("R") : "uniform");
		if (Codices.TryGetValue(key, out ArmorCodexScriptable codex) && codex != null) return codex;
		ArmorType type = new ArmorType
		{
			Name = "L2A7V " + e.Material.Replace('_', ' '), ThicknessSource = e.Variable ? ArmorType.RhaSource.Measured : ArmorType.RhaSource.Listed,
			RhaeMultiplierKe = e.Variable ? e.Ke * scale : 1f, RhaeMultiplierCe = e.Variable ? e.Ce * scale : 1f,
			CanRicochet = e.Material == "armour_steel" || e.Material == "composite",
			NormalizesHits = e.Material != "liner", CanShatterLongRods = e.Material == "armour_steel" || e.Material == "composite",
			HarderIsBetter = e.Material == "liner", BHN = e.Material == "armour_steel" ? 313f : e.Material == "liner" ? 2f : 100f,
			SpallAngleMultiplier = e.Material == "liner" ? 0.33f : e.Material == "composite" ? 0.5f : 1f,
			SpallPowerMultiplier = e.Material == "composite" ? 0.5f : e.Material == "liner" ? 0.9f : 1f,
			CrushThicknessModifier = e.Material == "liner" ? 0.05f : 1f
		};
		codex = ScriptableObject.CreateInstance<ArmorCodexScriptable>();
		codex.name = key;
		codex.ArmorType = type;
		UnityEngine.Object.DontDestroyOnLoad(codex);
		Codices[key] = codex;
		return codex;
	}

	private static void SetPrivate(object target, string field, object value)
	{
		FieldInfo f = target.GetType().GetField(field, Any);
		if (f != null) f.SetValue(target, value);
	}
}

internal sealed class LpDmArmourMarker : MonoBehaviour
{
	internal int Built;
}

using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.PhysicsHelpers;
using GHPC.Utility;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V;

public static class LpArmorOverlay
{
	private sealed class Zone
	{
		public string Name;

		public float Ke;

		public float Ce;

		public string[] Parts;

		public bool OnTurret;
	}

	private const string Tag = "[L2A7V][armour]";

	private const string Prefix = "L2A7V_Armor ";

	private static readonly Zone[] Zones = new Zone[4]
	{
		new Zone
		{
			Name = "turret front array",
			Ke = 880f,
			Ce = 1500f,
			OnTurret = true,
			Parts = new string[2] { "ex_armor_l_17", "ex_armor_r_17" }
		},
		new Zone
		{
			Name = "turret side array",
			Ke = 460f,
			Ce = 800f,
			OnTurret = true,
			Parts = new string[2] { "ex_armor_l_18", "ex_armor_r_18" }
		},
		new Zone
		{
			Name = "hull side array",
			Ke = 360f,
			Ce = 650f,
			OnTurret = false,
			Parts = new string[30]
			{
				"ex_armor_l_02", "ex_armor_r_02", "ex_armor_l_03", "ex_armor_r_03", "ex_armor_l_04", "ex_armor_r_04", "ex_armor_l_05", "ex_armor_r_05", "ex_armor_l_06", "ex_armor_r_06",
				"ex_armor_l_07", "ex_armor_r_07", "ex_armor_l_08", "ex_armor_r_08", "ex_armor_l_09", "ex_armor_r_09", "ex_armor_l_10", "ex_armor_r_10", "ex_armor_l_11", "ex_armor_r_11",
				"ex_armor_l_12", "ex_armor_r_12", "ex_armor_l_13", "ex_armor_r_13", "ex_armor_l_14", "ex_armor_r_14", "ex_armor_l_15", "ex_armor_r_15", "ex_armor_l_16", "ex_armor_r_16"
			}
		},
		new Zone
		{
			Name = "hull front glacis",
			Ke = 360f,
			Ce = 720f,
			OnTurret = false,
			Parts = new string[6] { "ex_armor_body_01", "ex_armor_body_03", "ex_armor_body_04", "ex_armor_body_05", "ex_armor_body_06", "ex_armor_body_07" }
		}
	};

	private static bool _spoke;

	private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>(StringComparer.Ordinal);

	private static readonly FieldInfo ArmorTypeField = typeof(UniformArmor).GetField("_armorType", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly Dictionary<string, ArmorCodexScriptable> CodexCache = new Dictionary<string, ArmorCodexScriptable>(StringComparer.OrdinalIgnoreCase);

	private static int _donorArmorLayer = -1;

	private static bool _layerSpoke;

	public static int Apply(GameObject unitRoot, Transform turretBone, Transform hullBone, Vector3 hullLocal)
	{
		if (!LpConfig.OverlayArmor || unitRoot == null || turretBone == null || hullBone == null)
		{
			return 0;
		}
		bool talk = !_spoke;
		_spoke = true;
		int layer = ArmorLayer(unitRoot);
		Unit unit = unitRoot.GetComponent<Unit>();
		if (unit == null)
		{
			unit = unitRoot.GetComponentInParent<Unit>();
		}
		float armorScale = LpConfig.ArmorScale;
		int num = ApplyVisible(turretBone, hullBone, hullLocal, layer, unit, armorScale, talk);
		int num2 = 0;
		if (LpConfig.OverlayXrayArmor)
		{
			num2 = ApplyXray(turretBone, hullBone, hullLocal, layer, unit, armorScale, talk);
		}
		int num3 = num + num2;
		if (num3 > 0)
		{
			MelonLogger.Msg("[L2A7V][armour] " + num3 + " additive plate collider(s) at x" + armorScale.ToString("0.00") + " (" + num + " visible module, " + num2 + " x-ray array); the donor's own plates are left exactly as they shipped");
		}
		return num3;
	}

	private static int ApplyVisible(Transform turretBone, Transform hullBone, Vector3 hullLocal, int layer, Unit unit, float scale, bool talk)
	{
		if (!ObjModel.Loaded)
		{
			return 0;
		}
		int num = 0;
		Zone[] zones = Zones;
		foreach (Zone zone in zones)
		{
			Transform transform = (zone.OnTurret ? turretBone : hullBone);
			if (transform == null)
			{
				continue;
			}
			Vector3 anchor = (zone.OnTurret ? LpConfig.TurretAxis : Vector3.zero);
			Vector3 localPosition = (zone.OnTurret ? Vector3.zero : hullLocal);
			HashSet<string> hashSet = new HashSet<string>(zone.Parts, StringComparer.OrdinalIgnoreCase);
			Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			int num2 = 0;
			Vector3 vector = Vector3.zero;
			foreach (ObjGeometry geometry in ObjModel.Geometries)
			{
				if (!hashSet.Contains(geometry.ObjectName))
				{
					continue;
				}
				Mesh mesh = Cached(geometry.ObjectName + "#" + geometry.MaterialIndex, geometry, anchor);
				if (!(mesh == null))
				{
					GameObject gameObject = Spawn(zone.Name, zone.Ke * scale, zone.Ce * scale, transform, localPosition, mesh, layer, unit);
					if (num2 == 0 && gameObject != null)
					{
						vector = gameObject.transform.position;
					}
					num2++;
					dictionary.TryGetValue(geometry.ObjectName, out var value);
					dictionary[geometry.ObjectName] = value + 1;
				}
			}
			if (num2 == 0)
			{
				MelonLogger.Warning("[L2A7V][armour] zone '" + zone.Name + "' has no geometry in the model");
				continue;
			}
			num += num2;
			if (!talk)
			{
				continue;
			}
			List<string> list = new List<string>();
			foreach (KeyValuePair<string, int> item in dictionary)
			{
				if (item.Value > 1)
				{
					list.Add(item.Key + " x" + item.Value);
				}
			}
			MelonLogger.Msg("[L2A7V][armour] '" + zone.Name + "': " + num2 + " plate collider(s), " + (zone.Ke * scale).ToString("0") + "/" + (zone.Ce * scale).ToString("0") + " mm KE/CE, first at world " + vector.ToString("0.0") + ((list.Count == 0) ? string.Empty : (" (split across materials, one piece each: " + string.Join(", ", list) + ")")));
		}
		return num;
	}

	private static bool TryXrayZone(string name, out float ke, out float ce, out bool onTurret)
	{
		ke = (ce = 0f);
		onTurret = false;
		string text = name.ToLowerInvariant();
		if (text.StartsWith("composite_armor_turret"))
		{
			onTurret = true;
			if (text.Contains("_10"))
			{
				ke = 350f;
				ce = 700f;
				return true;
			}
			if (text.Contains("_05") || text.Contains("_06") || text.Contains("_08"))
			{
				ke = 250f;
				ce = 400f;
				return true;
			}
			return false;
		}
		if (text.StartsWith("composite_armor_hull"))
		{
			if (text.Contains("_04"))
			{
				ke = 100f;
				ce = 100f;
				return true;
			}
			if (text.Contains("_02") || text.Contains("_03") || text.Contains("_05"))
			{
				ke = 50f;
				ce = 70f;
				return true;
			}
			return false;
		}
		if (text.StartsWith("spall_liner"))
		{
			ke = 15f;
			ce = 15f;
			onTurret = true;
			return true;
		}
		if (text.StartsWith("firewall_armor"))
		{
			ke = 30f;
			ce = 30f;
			onTurret = true;
			return true;
		}
		return false;
	}

	private static int ApplyXray(Transform turretBone, Transform hullBone, Vector3 hullLocal, int layer, Unit unit, float scale, bool talk)
	{
		if (!LpXrayParts.Loaded || LpXrayParts.Failed)
		{
			return 0;
		}
		int num = 0;
		foreach (LpXrayParts.Part part in LpXrayParts.Parts)
		{
			string name = part.Name;
			string text = name.ToLowerInvariant();
			if ((!text.StartsWith("composite_armor") && !text.StartsWith("spall_liner") && !text.StartsWith("firewall_armor")) || !TryXrayZone(name, out var ke, out var ce, out var onTurret))
			{
				continue;
			}
			if (text.StartsWith("spall_liner"))
			{
				onTurret = part.Center.y > 1.7f;
			}
			Transform transform = (onTurret ? turretBone : hullBone);
			if (!(transform == null))
			{
				Vector3 anchor = (onTurret ? LpConfig.TurretAxis : Vector3.zero);
				Vector3 localPosition = (onTurret ? Vector3.zero : hullLocal);
				Mesh mesh = Cached("x:" + name, part, anchor);
				if (!(mesh == null) && Spawn(name, ke * scale, ce * scale, transform, localPosition, mesh, layer, unit) != null)
				{
					num++;
				}
			}
		}
		if (num > 0 && talk)
		{
			MelonLogger.Msg("[L2A7V][armour] x-ray arrays: " + num + " extra collider(s) (mantlet, turret rear, hull deck, firewall, spall liners)");
		}
		return num;
	}

	private static Mesh Cached(string key, ObjGeometry geometry, Vector3 anchor)
	{
		if (MeshCache.TryGetValue(key, out var value) && value != null)
		{
			return value;
		}
		value = BuildMesh(geometry, anchor);
		if (value != null)
		{
			MeshCache[key] = value;
		}
		return value;
	}

	private static Mesh Cached(string key, LpXrayParts.Part part, Vector3 anchor)
	{
		if (MeshCache.TryGetValue(key, out var value) && value != null)
		{
			return value;
		}
		value = BuildMesh(part, anchor);
		if (value != null)
		{
			MeshCache[key] = value;
		}
		return value;
	}

	private static GameObject Spawn(string zoneName, float ke, float ce, Transform bone, Vector3 localPosition, Mesh mesh, int layer, Unit unit)
	{
		GameObject gameObject = new GameObject("L2A7V_Armor " + zoneName);
		gameObject.transform.SetParent(bone, worldPositionStays: false);
		gameObject.transform.localPosition = localPosition;
		gameObject.transform.localRotation = Quaternion.identity;
		gameObject.transform.localScale = Vector3.one;
		gameObject.layer = layer;
		try
		{
			gameObject.tag = "Penetrable";
		}
		catch (UnityException)
		{
		}
		MeshCollider meshCollider = gameObject.AddComponent<MeshCollider>();
		bool flag = false;
		try
		{
			meshCollider.sharedMesh = mesh;
			meshCollider.convex = true;
			flag = meshCollider.sharedMesh != null;
		}
		catch (Exception ex2)
		{
			MelonLogger.Warning("[L2A7V][armour] convex cook failed for " + mesh.name + ": " + ex2.Message);
		}
		if (!flag)
		{
			UnityEngine.Object.Destroy(meshCollider);
			BoxCollider boxCollider = gameObject.AddComponent<BoxCollider>();
			boxCollider.center = mesh.bounds.center;
			boxCollider.size = mesh.bounds.size;
		}
		UniformArmor uniformArmor = gameObject.AddComponent<UniformArmor>();
		uniformArmor.PrimarySabotRha = ke;
		uniformArmor.PrimaryHeatRha = ce;
		uniformArmor.SecondarySabotRha = ke;
		uniformArmor.SecondaryHeatRha = ce;
		uniformArmor.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
		uniformArmor.AngleMatters = true;
		uniformArmor.DetonatedObjects = Array.Empty<GameObject>();
		uniformArmor.UndetonatedObjects = Array.Empty<GameObject>();
		uniformArmor.CenterPoint = gameObject.transform;
		uniformArmor.SetName(zoneName + " (" + mesh.name + ")");
		ArmorCodexScriptable armorCodexScriptable = CodexFor(zoneName);
		if (armorCodexScriptable != null && ArmorTypeField != null)
		{
			ArmorTypeField.SetValue(uniformArmor, armorCodexScriptable);
		}
		if (unit != null)
		{
			uniformArmor.Unit = unit;
		}
		if (layer == CodeUtils.LAYER_INDEX_RAYCAST_ONLY)
		{
			RaycastColliderGroup.AddNewColliderSetToParentGroup(gameObject.transform);
		}
		return gameObject;
	}

	private static ArmorCodexScriptable CodexFor(string zoneName)
	{
		string text = CodexFragmentFor(zoneName);
		if (text == null)
		{
			return null;
		}
		if (CodexCache.TryGetValue(text, out var value))
		{
			return value;
		}
		ArmorCodexScriptable armorCodexScriptable = null;
		ArmorCodexScriptable[] array = Resources.FindObjectsOfTypeAll<ArmorCodexScriptable>();
		foreach (ArmorCodexScriptable armorCodexScriptable2 in array)
		{
			if (!(armorCodexScriptable2 == null) && armorCodexScriptable2.ArmorType != null && armorCodexScriptable2.ArmorType.Name != null && armorCodexScriptable2.ArmorType.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				armorCodexScriptable = armorCodexScriptable2;
				break;
			}
		}
		CodexCache[text] = armorCodexScriptable;
		return armorCodexScriptable;
	}

	private static string CodexFragmentFor(string zoneName)
	{
		string text = zoneName.ToLowerInvariant();
		if (text.Contains("turret front"))
		{
			return "turret front";
		}
		if (text.Contains("turret side"))
		{
			return "turret side";
		}
		if (text.Contains("mantlet"))
		{
			return "mantlet";
		}
		if (text.Contains("spall"))
		{
			return "spall";
		}
		if (text.Contains("firewall"))
		{
			return "steel";
		}
		if (text.Contains("hull") || text.Contains("rear") || text.Contains("roof"))
		{
			return "hull array";
		}
		return null;
	}

	private static int ArmorLayer(GameObject unitRoot)
	{
		UniformArmor[] componentsInChildren = unitRoot.GetComponentsInChildren<UniformArmor>(includeInactive: true);
		foreach (UniformArmor uniformArmor in componentsInChildren)
		{
			if (!(uniformArmor == null) && !(uniformArmor.GetComponent<Collider>() == null))
			{
				int layer = uniformArmor.gameObject.layer;
				if (!_layerSpoke || layer != _donorArmorLayer)
				{
					_layerSpoke = true;
					MelonLogger.Msg("[L2A7V][armour] armour layer " + layer + " (read from this unit's donor plate)");
				}
				_donorArmorLayer = layer;
				return layer;
			}
		}
		if (_donorArmorLayer >= 0 && _donorArmorLayer != CodeUtils.LAYER_INDEX_RAYCAST_ONLY)
		{
			MelonLogger.Warning("[L2A7V][armour] donor layer differs from Raycast Only on this unit; overlay left unchanged pending ballistic validation");
			return _donorArmorLayer;
		}
		MelonLogger.Warning("[L2A7V][armour] no donor armour collider found; using the game's Raycast Only layer");
		return CodeUtils.LAYER_INDEX_RAYCAST_ONLY;
	}

	private static Mesh BuildMesh(ObjGeometry geometry, Vector3 anchor)
	{
		if (geometry.Positions.Length < 3 || geometry.Triangles.Length < 3)
		{
			return null;
		}
		Vector3[] array = new Vector3[geometry.Positions.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = geometry.Positions[i] - anchor;
		}
		return BuildMesh(geometry.ObjectName, array, geometry.Triangles);
	}

	private static Mesh BuildMesh(LpXrayParts.Part part, Vector3 anchor)
	{
		if (part.Positions.Length < 3 || part.Triangles.Length < 3)
		{
			return null;
		}
		Vector3[] array = new Vector3[part.Positions.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = part.Positions[i] - anchor;
		}
		return BuildMesh(part.Name, array, part.Triangles);
	}

	private static Mesh BuildMesh(string name, Vector3[] vertices, int[] triangles)
	{
		Mesh mesh = new Mesh();
		mesh.name = name;
		mesh.indexFormat = ((vertices.Length > 65000) ? IndexFormat.UInt32 : IndexFormat.UInt16);
		mesh.vertices = vertices;
		mesh.triangles = triangles;
		mesh.RecalculateBounds();
		return mesh;
	}
}

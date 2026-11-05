using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using GHPC;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V;

public static class LpXrayArmor
{
	private sealed class Group
	{
		internal string Name;

		internal Mesh Mesh;
	}

	private struct Codex
	{
		internal string Name;

		internal ArmorCodexScriptable Type;
	}

	internal const string Tag = "[L2A7V][armour]";

	private const string TagOld = "[L2A7V][armour]";

	private const string FileName = "l2a7v_armor.bin";

	private const string Marker = "L2A7V_XrayArmor";

	private const int LayeredArrayCount = 3;

	private const float MaxLayerSpacing = 0.15f;

	private const float StrikingFaceCosine = 0.5f;

	private const float EdgeRha = 40f;

	private static int _logged;

	private static readonly List<Bounds> OurBounds = new List<Bounds>();

	private static List<Group> _groups;

	private static readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);

	private static Codex[] DonorCodex = new Codex[0];

	private const string BundleFile = "l2a7v_armor.bundle";

	private const string PrefabName = "L2A7V_armor";

	private static AssetBundle _bundle;

	internal static string ModFolder
	{
		get
		{
			string text = Path.Combine(MelonEnvironment.ModsDirectory, "Leopard2A7V");
			if (!Directory.Exists(text))
			{
				return MelonEnvironment.ModsDirectory;
			}
			return text;
		}
	}

	internal static bool TryGetGroups(List<KeyValuePair<string, Mesh>> into)
	{
		List<Group> list = Load();
		if (list == null)
		{
			return false;
		}
		foreach (Group item in list)
		{
			if (item != null && item.Mesh != null)
			{
				into.Add(new KeyValuePair<string, Mesh>(item.Name, item.Mesh));
			}
		}
		return into.Count > 0;
	}

	public static bool Build(GameObject unitRoot, LpVisual.Assembly art = null)
	{
		if (unitRoot == null)
		{
			return false;
		}
		List<Group> list = Load();
		if (list == null || list.Count == 0)
		{
			return false;
		}
		Transform transform = ((art != null) ? art.Hull : FindBone(unitRoot, "hull"));
		Transform transform2 = ((art != null) ? art.Turret : FindBone(unitRoot, "turret"));
		Transform transform3 = ((art != null) ? art.Gun : FindBone(unitRoot, "Rh-120_L/44"));
		if (transform == null)
		{
			MelonLogger.Warning("[L2A7V][armour] no hull bone on " + unitRoot.name + "; the donor armour stays");
			return false;
		}
		Unit unit = LpFind.Unit(unitRoot);
		Transform transform4 = ((unit != null) ? unit.transform : unitRoot.transform);
		Vector3 vector = transform.InverseTransformPoint(transform4.TransformPoint(LpConfig.Offset));
		Vector3 zero = Vector3.zero;
		Vector3 zero2 = Vector3.zero;
		MelonLogger.Msg("[L2A7V][armour] anchors: hull " + vector.ToString("0.00") + " turret " + zero.ToString("0.00") + " gun " + zero2.ToString("0.00"));
		OurBounds.Clear();
		DonorCodex = CollectCodex(unitRoot);
		int num = 0;
		int num2 = 0;
		foreach (Group item in list)
		{
			bool num3 = GroupOnGun(item.Name);
			bool flag = !num3 && item.Name.IndexOf("turret", StringComparison.OrdinalIgnoreCase) >= 0;
			Transform bone = ((num3 && transform3 != null) ? transform3 : ((flag && transform2 != null) ? transform2 : transform));
			Vector3 anchor = ((num3 && transform3 != null) ? zero2 : ((flag && transform2 != null) ? zero : vector));
			int layers;
			float ke2;
			float ce2;
			if (!LpArmor.TryZoneForXrayGroup(item.Name, out var ke, out var ce, out var zoneName))
			{
				if (!LpData.TryArmour(item.Name, out ke, out ce, out layers))
				{
					num2++;
					continue;
				}
				zoneName = item.Name;
			}
			else if (LpData.TryArmour(item.Name, out ke2, out ce2, out layers))
			{
				ke = ke2;
				ce = ce2;
			}
			int layers2 = ((!item.Name.StartsWith("composite_armor_", StringComparison.OrdinalIgnoreCase)) ? 1 : 3);
			if (LpData.TryArmour(item.Name, out var _, out var _, out var layers3) && layers3 > 0)
			{
				layers2 = layers3;
			}
			if (BuildOne(bone, anchor, item, ke, ce, zoneName, layers2, null))
			{
				num++;
			}
		}
		int num4 = ((num > 0) ? RetireDonorArmour(unitRoot) : 0);
		if (art != null)
		{
			CheckAgainstArt(art);
		}
		MelonLogger.Msg("[L2A7V][armour] built " + num + " piece(s) from the baked armour" + ((num2 > 0) ? (", " + num2 + " group(s) had no zone entry") : string.Empty) + ((num4 > 0) ? (", retired " + num4 + " donor plate(s)") : string.Empty));
		return num > 0;
	}

	private static bool GroupOnGun(string name)
	{
		if (name.IndexOf("breech", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("barrel", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("_gun", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return name.StartsWith("gun_", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static void CheckAgainstArt(LpVisual.Assembly art)
	{
		Bounds bounds = ArtBounds(art.Hull);
		Bounds bounds2 = ArtBounds(art.Turret);
		int num = 0;
		foreach (Bounds ourBound in OurBounds)
		{
			bool num2 = bounds.size != Vector3.zero && bounds.Intersects(ourBound);
			bool flag = bounds2.size != Vector3.zero && bounds2.Intersects(ourBound);
			if (!(num2 || flag))
			{
				num++;
				MelonLogger.Warning("[L2A7V][armour] plate outside the model: " + ourBound.center.ToString("0.00"));
			}
		}
		MelonLogger.Msg("[L2A7V][armour] plates outside the visible model: " + num + " of " + OurBounds.Count);
	}

	private static Bounds ArtBounds(Transform pivot)
	{
		if (pivot == null)
		{
			return new Bounds(Vector3.zero, Vector3.zero);
		}
		bool flag = false;
		Bounds result = new Bounds(pivot.position, Vector3.zero);
		Renderer[] componentsInChildren = pivot.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren)
		{
			if (!(renderer == null))
			{
				if (!flag)
				{
					result = renderer.bounds;
					flag = true;
				}
				else
				{
					result.Encapsulate(renderer.bounds);
				}
			}
		}
		if (!flag)
		{
			return new Bounds(pivot.position, Vector3.zero);
		}
		return result;
	}

	private static int RetireDonorArmour(GameObject unitRoot)
	{
		int num = 0;
		int num2 = 0;
		List<string> list = new List<string>();
		UniformArmor[] componentsInChildren = unitRoot.GetComponentsInChildren<UniformArmor>(includeInactive: true);
		foreach (UniformArmor uniformArmor in componentsInChildren)
		{
			if (uniformArmor == null || uniformArmor.gameObject.name.StartsWith("L2A7V_XrayArmor", StringComparison.Ordinal))
			{
				continue;
			}
			bool num3 = LpData.IsVanillaOff(uniformArmor.name);
			bool flag = uniformArmor.name.IndexOf("array", StringComparison.OrdinalIgnoreCase) >= 0;
			if (!num3 && !flag && (IsProtected(uniformArmor.name, uniformArmor.ArmorType) || !Covered(uniformArmor)))
			{
				num2++;
				list.Add(uniformArmor.name + "|" + uniformArmor.transform.position.ToString("0.00"));
				continue;
			}
			Collider[] componentsInChildren2 = uniformArmor.GetComponentsInChildren<Collider>(includeInactive: true);
			foreach (Collider collider in componentsInChildren2)
			{
				if (collider != null)
				{
					collider.enabled = false;
				}
			}
			uniformArmor.enabled = false;
			num++;
		}
		VariableArmor[] componentsInChildren3 = unitRoot.GetComponentsInChildren<VariableArmor>(includeInactive: true);
		foreach (VariableArmor variableArmor in componentsInChildren3)
		{
			if (variableArmor == null)
			{
				continue;
			}
			Collider componentInChildren = variableArmor.GetComponentInChildren<Collider>(includeInactive: true);
			if (componentInChildren == null || IsProtected(variableArmor.name, variableArmor.ArmorType))
			{
				continue;
			}
			bool flag2 = LpData.IsVanillaOff(variableArmor.name);
			bool flag3 = false;
			foreach (Bounds ourBound in OurBounds)
			{
				ourBound.Expand(1.2f);
				if (ourBound.Intersects(componentInChildren.bounds))
				{
					flag3 = true;
					break;
				}
			}
			if (flag2 || flag3)
			{
				componentInChildren.enabled = false;
				variableArmor.enabled = false;
				num++;
			}
		}
		if (list.Count > 0)
		{
			MelonLogger.Msg("[L2A7V][armour] kept " + num2 + " donor plate(s) outside the x-ray coverage");
			DumpKept(list);
		}
		return num;
	}

	private static bool IsProtected(string name, ArmorCodexScriptable codex)
	{
		string text = ((codex != null && codex.name != null) ? codex.name.ToLowerInvariant() : string.Empty);
		if (text.Contains("human") || text.Contains("flesh") || text.Contains("bone") || text.Contains("track"))
		{
			return true;
		}
		string text2 = ((name == null) ? string.Empty : name.ToLowerInvariant());
		if (!text2.Contains("track") && !text2.Contains("wheel") && !text2.Contains("suspension") && !text2.Contains("crew") && !text2.Contains("fuel"))
		{
			return text2.Contains("human");
		}
		return true;
	}

	private static void DumpKept(List<string> names)
	{
		try
		{
			File.WriteAllLines(Path.Combine(ModFolder, "donor_plates.txt"), names);
			MelonLogger.Msg("[L2A7V][armour] wrote " + names.Count + " kept plate name(s) to donor_plates.txt");
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V][armour] could not write donor_plates.txt: " + ex.Message);
		}
	}

	private static bool Covered(UniformArmor plate)
	{
		Bounds bounds = BoundsOf(plate);
		if (bounds.size == Vector3.zero)
		{
			return false;
		}
		foreach (Bounds ourBound in OurBounds)
		{
			ourBound.Expand(1.2f);
			if (ourBound.Intersects(bounds))
			{
				return true;
			}
		}
		return false;
	}

	private static Bounds BoundsOf(UniformArmor plate)
	{
		Collider componentInChildren = plate.GetComponentInChildren<Collider>(includeInactive: true);
		if (componentInChildren == null)
		{
			return new Bounds(plate.transform.position, Vector3.zero);
		}
		return componentInChildren.bounds;
	}

	private static Codex[] CollectCodex(GameObject unitRoot)
	{
		List<Codex> list = new List<Codex>();
		int num = 0;
		int num2 = 0;
		UniformArmor[] componentsInChildren = unitRoot.GetComponentsInChildren<UniformArmor>(includeInactive: true);
		foreach (UniformArmor uniformArmor in componentsInChildren)
		{
			if (uniformArmor == null)
			{
				continue;
			}
			num++;
			if (uniformArmor.ArmorType == null)
			{
				continue;
			}
			num2++;
			string name = uniformArmor.ArmorType.name;
			if (string.IsNullOrEmpty(name))
			{
				name = uniformArmor.name;
			}
			bool flag = false;
			foreach (Codex item in list)
			{
				if (item.Name == name)
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				list.Add(new Codex
				{
					Name = name,
					Type = uniformArmor.ArmorType
				});
			}
		}
		VariableArmor[] componentsInChildren2 = unitRoot.GetComponentsInChildren<VariableArmor>(includeInactive: true);
		foreach (VariableArmor variableArmor in componentsInChildren2)
		{
			if (variableArmor == null)
			{
				continue;
			}
			num++;
			if (variableArmor.ArmorType == null)
			{
				continue;
			}
			num2++;
			string name2 = variableArmor.ArmorType.name;
			if (string.IsNullOrEmpty(name2))
			{
				name2 = variableArmor.name;
			}
			bool flag2 = false;
			foreach (Codex item2 in list)
			{
				if (item2.Name == name2)
				{
					flag2 = true;
					break;
				}
			}
			if (!flag2)
			{
				list.Add(new Codex
				{
					Name = name2,
					Type = variableArmor.ArmorType
				});
			}
		}
		MelonLogger.Msg("[L2A7V][armour] donor pieces with a codex: " + num2 + " of " + num);
		if (list.Count > 0)
		{
			MelonLogger.Msg("[L2A7V][armour] donor armour codex: " + list.Count + " type(s) available to copy");
		}
		foreach (Codex item3 in list)
		{
			MelonLogger.Msg("[L2A7V][armour]   " + item3.Name);
		}
		return list.ToArray();
	}

	private static ArmorCodexScriptable CodexFor(string groupName, Vector3 facing)
	{
		string text = groupName.ToLowerInvariant();
		if (text.Contains("track"))
		{
			return Match("tank tracks");
		}
		if (text.Contains("spall_liner") || text.Contains("liner"))
		{
			return Match("composite skirts");
		}
		if (text.Contains("firewall"))
		{
			return Match("US gun steel");
		}
		if (text.Contains("mantlet") || text.Contains("breech") || text.Contains("barrel"))
		{
			return Match("Leo 2 mantlet array");
		}
		if (text.Contains("turret"))
		{
			ArmorCodexScriptable armorCodexScriptable = Match((Mathf.Abs(facing.z) >= Mathf.Abs(facing.x)) ? "turret front array B tech" : "turret side array B tech");
			if (armorCodexScriptable != null)
			{
				return armorCodexScriptable;
			}
		}
		if (text.Contains("hull"))
		{
			return Match("Leo 2 hull array");
		}
		return CodexForStation(groupName);
	}

	private static ArmorCodexScriptable CodexForStation(string groupName)
	{
		string text = LpData.CodexFor(groupName);
		if (!string.IsNullOrEmpty(text))
		{
			ArmorCodexScriptable armorCodexScriptable = Match(text);
			if (armorCodexScriptable != null)
			{
				return armorCodexScriptable;
			}
		}
		string text2 = KindOf(groupName);
		if (text2 != null)
		{
			ArmorCodexScriptable armorCodexScriptable2 = Match(text2);
			if (armorCodexScriptable2 != null)
			{
				return armorCodexScriptable2;
			}
		}
		string text3 = ((groupName.IndexOf("turret", StringComparison.OrdinalIgnoreCase) >= 0) ? "turret" : ((groupName.IndexOf("hull", StringComparison.OrdinalIgnoreCase) >= 0) ? "hull" : null));
		if (text3 != null)
		{
			ArmorCodexScriptable armorCodexScriptable3 = Match(text3);
			if (armorCodexScriptable3 != null)
			{
				return armorCodexScriptable3;
			}
		}
		if (DonorCodex.Length == 0)
		{
			return null;
		}
		return DonorCodex[0].Type;
	}

	private static string KindOf(string groupName)
	{
		string text = groupName.ToLowerInvariant();
		if (text.StartsWith("spall_liner") || text.Contains("liner"))
		{
			return "spall";
		}
		if (text.StartsWith("firewall"))
		{
			return "steel";
		}
		if (text.Contains("composite") || text.Contains("array"))
		{
			return "array";
		}
		if (text.Contains("track"))
		{
			return "track";
		}
		return null;
	}

	private static ArmorCodexScriptable Match(string fragment)
	{
		Codex[] donorCodex = DonorCodex;
		for (int i = 0; i < donorCodex.Length; i++)
		{
			Codex codex = donorCodex[i];
			if (codex.Name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return codex.Type;
			}
		}
		return null;
	}

	private static void SetPrivate(object target, string field, object value)
	{
		if (target != null && value != null)
		{
			FieldInfo field2 = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
			if (field2 != null)
			{
				field2.SetValue(target, value);
			}
		}
	}

	private static bool BuildOne(Transform bone, Vector3 anchor, Group group, float ke, float ce, string zoneName, int layers, ArmorCodexScriptable codex)
	{
		if (bone == null || group.Mesh == null)
		{
			return false;
		}
		Mesh mesh = group.Mesh;
		Vector3 vector = PlateAxis(mesh);
		float num = ExtentAlong(mesh, vector);
		float num2 = ((layers > 1) ? Mathf.Min(num / (float)layers, 0.15f) : 0f);
		if (codex == null)
		{
			codex = CodexFor(group.Name, vector);
		}
		Mesh mesh2 = null;
		Mesh mesh3 = null;
		if (layers > 1 && ke > 60f)
		{
			mesh2 = Select(mesh, vector, facing: true);
			mesh3 = Select(mesh, vector, facing: false);
		}
		Mesh mesh4 = ((mesh2 != null) ? mesh2 : mesh);
		for (int i = 0; i < layers; i++)
		{
			float num3 = ((layers > 1) ? (((float)i - (float)(layers - 1) * 0.5f) * num2) : 0f);
			BuildLayer(bone, anchor, group, mesh4, ke / (float)layers, ce / (float)layers, codex, (layers > 1) ? (zoneName + " layer " + (i + 1) + "/" + layers) : zoneName, vector * num3, vector);
		}
		if (mesh3 != null)
		{
			BuildLayer(bone, anchor, group, mesh3, 40f, 40f, codex, zoneName + " rim", Vector3.zero, vector);
		}
		return true;
	}

	private static Mesh Select(Mesh source, Vector3 axis, bool facing)
	{
		Vector3[] vertices = source.vertices;
		int[] triangles = source.triangles;
		List<int> list = new List<int>(triangles.Length);
		for (int i = 0; i + 2 < triangles.Length; i += 3)
		{
			Vector3 vector = vertices[triangles[i]];
			Vector3 vector2 = vertices[triangles[i + 1]];
			Vector3 vector3 = Vector3.Cross(rhs: vertices[triangles[i + 2]] - vector, lhs: vector2 - vector);
			if (!(vector3.sqrMagnitude < 1E-12f) && Mathf.Abs(Vector3.Dot(vector3.normalized, axis)) >= 0.5f == facing)
			{
				list.Add(triangles[i]);
				list.Add(triangles[i + 1]);
				list.Add(triangles[i + 2]);
			}
		}
		if (list.Count == 0)
		{
			return null;
		}
		Mesh mesh = new Mesh
		{
			name = source.name + (facing ? " face" : " rim")
		};
		if (vertices.Length > 65000)
		{
			mesh.indexFormat = IndexFormat.UInt32;
		}
		mesh.vertices = vertices;
		mesh.triangles = list.ToArray();
		mesh.RecalculateBounds();
		return mesh;
	}

	private static void BuildLayer(Transform bone, Vector3 anchor, Group group, Mesh mesh, float ke, float ce, ArmorCodexScriptable codex, string zoneName, Vector3 offset, Vector3 facing)
	{
		GameObject go = new GameObject("L2A7V_XrayArmor " + group.Name + " " + zoneName);
		go.transform.SetParent(bone, worldPositionStays: false);
		go.transform.localPosition = anchor + offset;
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;
		go.layer = 8;
		go.tag = "Penetrable";
		MeshCollider meshCollider = go.AddComponent<MeshCollider>();
		meshCollider.sharedMesh = mesh;
		meshCollider.convex = false;
		OurBounds.Add(new Bounds(go.transform.TransformPoint(mesh.bounds.center), Abs(go.transform.TransformVector(mesh.bounds.size))));
		float armorScale = LpConfig.ArmorScale;
		UniformArmor uniformArmor = go.AddComponent<UniformArmor>();
		uniformArmor.PrimarySabotRha = ke * armorScale;
		uniformArmor.PrimaryHeatRha = ce * armorScale;
		uniformArmor.SecondarySabotRha = ke * armorScale;
		uniformArmor.SecondaryHeatRha = ce * armorScale;
		uniformArmor.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
		uniformArmor.AngleMatters = true;
		uniformArmor.DetonatedObjects = Array.Empty<GameObject>();
		uniformArmor.UndetonatedObjects = Array.Empty<GameObject>();
		uniformArmor.Punctured += delegate
		{
			MelonLogger.Msg("[L2A7V][armour] PIERCED: '" + zoneName + "' (" + group.Name + ") at " + go.transform.position.ToString("0.0"));
		};
		MelonLogger.Msg("[L2A7V][armour] piece '" + group.Name + "' -> codex '" + ((codex == null) ? "none" : codex.name) + "'");
		uniformArmor.CenterPoint = go.transform;
		uniformArmor.SetName(zoneName);
		if (codex != null)
		{
			SetPrivate(uniformArmor, "_armorType", codex);
		}
		Unit componentInParent = go.GetComponentInParent<Unit>();
		if (componentInParent != null)
		{
			uniformArmor.Unit = componentInParent;
		}
		else
		{
			MelonLogger.Warning("[L2A7V][armour] piece '" + group.Name + "' has no Unit; hits may not be attributed");
		}
		if (_logged < 6)
		{
			_logged++;
			MelonLogger.Msg("[L2A7V][armour] piece '" + group.Name + "' centre " + go.transform.position.ToString("0.00") + " size " + mesh.bounds.size.ToString("0.00") + " facing " + facing.ToString("0.00") + " layer " + go.layer + " unit " + (uniformArmor.Unit != null));
		}
		AarVisual aarVisual = go.AddComponent<AarVisual>();
		aarVisual.HideUntilAar = true;
		Material material = LpArmorOwn.XrayMaterial();
		if (material != null)
		{
			aarVisual.SwitchMaterials = true;
			aarVisual.AarMaterial = material;
			aarVisual.UpdateVisibility();
		}
	}

	private static Vector3 PlateAxis(Mesh mesh)
	{
		Vector3[] vertices = mesh.vertices;
		int[] triangles = mesh.triangles;
		double[,] array = new double[3, 3];
		for (int i = 0; i + 2 < triangles.Length; i += 3)
		{
			Vector3 vector = vertices[triangles[i]];
			Vector3 vector2 = vertices[triangles[i + 1]];
			Vector3 vector3 = vertices[triangles[i + 2]];
			Vector3 vector4 = Vector3.Cross(vector2 - vector, vector3 - vector);
			float magnitude = vector4.magnitude;
			if (!(magnitude < 1E-09f))
			{
				Vector3 vector5 = vector4 / magnitude;
				array[0, 0] += magnitude * vector5.x * vector5.x;
				array[0, 1] += magnitude * vector5.x * vector5.y;
				array[0, 2] += magnitude * vector5.x * vector5.z;
				array[1, 1] += magnitude * vector5.y * vector5.y;
				array[1, 2] += magnitude * vector5.y * vector5.z;
				array[2, 2] += magnitude * vector5.z * vector5.z;
			}
		}
		array[1, 0] = array[0, 1];
		array[2, 0] = array[0, 2];
		array[2, 1] = array[1, 2];
		int num = 0;
		if (array[1, 1] > array[num, num])
		{
			num = 1;
		}
		if (array[2, 2] > array[num, num])
		{
			num = 2;
		}
		double[] array2 = new double[3];
		array2[num] = 1.0;
		for (int j = 0; j < 64; j++)
		{
			double[] array3 = new double[3];
			for (int k = 0; k < 3; k++)
			{
				array3[k] = array[k, 0] * array2[0] + array[k, 1] * array2[1] + array[k, 2] * array2[2];
			}
			double num2 = Math.Sqrt(array3[0] * array3[0] + array3[1] * array3[1] + array3[2] * array3[2]);
			if (num2 < 1E-12)
			{
				break;
			}
			for (int l = 0; l < 3; l++)
			{
				array2[l] = array3[l] / num2;
			}
		}
		Vector3 vector6 = new Vector3((float)array2[0], (float)array2[1], (float)array2[2]);
		if (vector6.sqrMagnitude < 1E-06f)
		{
			return Vector3.up;
		}
		return vector6.normalized;
	}

	private static Vector3 Abs(Vector3 value)
	{
		return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
	}

	private static float ExtentAlong(Mesh mesh, Vector3 normal)
	{
		Vector3[] vertices = mesh.vertices;
		if (vertices.Length == 0)
		{
			return 0f;
		}
		float num = float.MaxValue;
		float num2 = float.MinValue;
		Vector3[] array = vertices;
		for (int i = 0; i < array.Length; i++)
		{
			float num3 = Vector3.Dot(array[i], normal);
			if (num3 < num)
			{
				num = num3;
			}
			if (num3 > num2)
			{
				num2 = num3;
			}
		}
		return Mathf.Max(0.02f, num2 - num);
	}

	private static Mesh Cache(string name, Mesh source)
	{
		if (_meshes.TryGetValue(name, out var value) && value != null)
		{
			return value;
		}
		if (source == null)
		{
			return null;
		}
		if (!source.isReadable)
		{
			MelonLogger.Warning("[L2A7V][armour] the bundle mesh '" + name + "' is not readable, so it keeps the export's axes and its plate sits ninety degrees off");
			_meshes[name] = source;
			return source;
		}
		return Cache(name, source.vertices, source.triangles);
	}

	private static Mesh Cache(string name, Vector3[] source, int[] triangles)
	{
		if (_meshes.TryGetValue(name, out var value) && value != null)
		{
			return value;
		}
		float num = ((name.IndexOf("turret", StringComparison.OrdinalIgnoreCase) >= 0) ? LpConfig.TurretAxis.z : 0f);
		Vector3[] array = new Vector3[source.Length];
		for (int i = 0; i < source.Length; i++)
		{
			Vector3 vector = source[i];
			array[i] = new Vector3(vector.z + num, vector.y, vector.x - num);
		}
		int[] array2 = new int[triangles.Length];
		for (int j = 0; j + 2 < triangles.Length; j += 3)
		{
			array2[j] = triangles[j];
			array2[j + 1] = triangles[j + 2];
			array2[j + 2] = triangles[j + 1];
		}
		Mesh mesh = new Mesh
		{
			name = name
		};
		if (array.Length > 65000)
		{
			mesh.indexFormat = IndexFormat.UInt32;
		}
		mesh.vertices = array;
		mesh.triangles = array2;
		mesh.RecalculateBounds();
		_meshes[name] = mesh;
		return mesh;
	}

	private static List<Group> Load()
	{
		if (_groups != null)
		{
			return _groups;
		}
		_groups = new List<Group>();
		if (TryLoadBundle())
		{
			return _groups;
		}
		Stream stream = null;
		string path = Path.Combine(ModFolder, "l2a7v_armor.bin");
		if (File.Exists(path))
		{
			stream = File.OpenRead(path);
		}
		else
		{
			stream = typeof(LpXrayArmor).Assembly.GetManifestResourceStream("armor.bin");
			if (stream == null)
			{
				MelonLogger.Warning("[L2A7V][armour] no baked armour inside the DLL and no l2a7v_armor.bin beside it; the armour falls back to the donor pieces");
				return _groups;
			}
		}
		try
		{
			using BinaryReader binaryReader = new BinaryReader(stream);
			byte[] array = binaryReader.ReadBytes(8);
			if (array.Length != 8 || array[0] != 71 || array[1] != 72)
			{
				MelonLogger.Warning("[L2A7V][armour] l2a7v_armor.bin is not a baked armour file; the donor armour stays");
				return _groups;
			}
			int num = binaryReader.ReadInt32();
			for (int i = 0; i < num; i++)
			{
				int count = binaryReader.ReadInt32();
				string @string = Encoding.UTF8.GetString(binaryReader.ReadBytes(count));
				int num2 = binaryReader.ReadInt32();
				Vector3[] array2 = new Vector3[num2];
				for (int j = 0; j < num2; j++)
				{
					array2[j] = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
				}
				int num3 = binaryReader.ReadInt32();
				int[] array3 = new int[num3];
				for (int k = 0; k < num3; k++)
				{
					array3[k] = binaryReader.ReadInt32();
				}
				_groups.Add(new Group
				{
					Name = @string,
					Mesh = Cache(@string, array2, array3)
				});
			}
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V][armour] could not read l2a7v_armor.bin: " + ex.Message);
			_groups.Clear();
			return _groups;
		}
		MelonLogger.Msg("[L2A7V][armour] loaded " + _groups.Count + " armour group(s) from l2a7v_armor.bin, re-framed into the unit's axes");
		return _groups;
	}

	private static bool TryLoadBundle()
	{
		if (_bundle == null)
		{
			string path = Path.Combine(ModFolder, "l2a7v_armor.bundle");
			if (!File.Exists(path))
			{
				return false;
			}
			_bundle = AssetBundle.LoadFromFile(path);
			if (_bundle == null)
			{
				MelonLogger.Warning("[L2A7V][armour] l2a7v_armor.bundle exists but could not be loaded; using the baked file");
				return false;
			}
		}
		GameObject gameObject = _bundle.LoadAsset<GameObject>("L2A7V_armor");
		if (gameObject == null)
		{
			MelonLogger.Warning("[L2A7V][armour] the bundle holds no L2A7V_armor prefab; using the baked file");
			return false;
		}
		int num = 0;
		foreach (Transform item in gameObject.transform)
		{
			MeshFilter component = item.GetComponent<MeshFilter>();
			if (!(component == null) && !(component.sharedMesh == null))
			{
				_groups.Add(new Group
				{
					Name = item.name,
					Mesh = Cache(item.name, component.sharedMesh)
				});
				num++;
			}
		}
		if (num == 0)
		{
			MelonLogger.Warning("[L2A7V][armour] the prefab holds no armour meshes; using the baked file");
			return false;
		}
		MelonLogger.Msg("[L2A7V][armour] loaded " + num + " armour mesh(es) from l2a7v_armor.bundle, re-framed into the unit's axes");
		return true;
	}

	private static Transform FindColliderRoot(GameObject unitRoot, string kind)
	{
		Transform[] componentsInChildren = unitRoot.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (!(transform == null))
			{
				string text = transform.name.ToLowerInvariant();
				if (text.IndexOf("collider", StringComparison.Ordinal) >= 0 && text.IndexOf(kind, StringComparison.Ordinal) >= 0)
				{
					return transform;
				}
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
}

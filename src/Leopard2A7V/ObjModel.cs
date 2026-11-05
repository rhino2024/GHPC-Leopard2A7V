using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class ObjModel
{
	private sealed class PartBuild
	{
		public string ObjectName;

		public int MaterialIndex;

		public readonly List<Vector3> Positions = new List<Vector3>();

		public readonly List<Vector2> Uvs = new List<Vector2>();

		public readonly List<Vector3> Normals = new List<Vector3>();

		public readonly List<int> Triangles = new List<int>();

		public readonly Dictionary<Vector3Int, int> VertexMap = new Dictionary<Vector3Int, int>();
	}

	private sealed class ObjParser
	{
		private readonly List<Vector3> _positions = new List<Vector3>();

		private readonly List<Vector2> _uvs = new List<Vector2>();

		private readonly List<Vector3> _normals = new List<Vector3>();

		private readonly Dictionary<string, int> _materialMap;

		private readonly List<PartBuild> _parts = new List<PartBuild>();

		private readonly HashSet<string> _skipped = new HashSet<string>();

		private PartBuild _active;

		private string _objectName;

		private int _currentMaterial;

		public List<PartBuild> Parts => _parts;

		public HashSet<string> SkippedMaterials => _skipped;

		public ObjParser(Dictionary<string, int> materialMap)
		{
			_materialMap = materialMap;
		}

		public void Parse(string objPath)
		{
			using StreamReader streamReader = new StreamReader(objPath);
			string text;
			while ((text = streamReader.ReadLine()) != null)
			{
				if (text.Length < 3 || text[0] == '#')
				{
					continue;
				}
				char c = text[1];
				if (text[0] == 'v')
				{
					switch (c)
					{
					case ' ':
						ReadVertex(text);
						continue;
					case 't':
						if (text[2] == ' ')
						{
							ReadUv(text);
							continue;
						}
						break;
					}
					if (c == 'n' && text[2] == ' ')
					{
						ReadNormal(text);
					}
				}
				else if (text[0] == 'f' && c == ' ')
				{
					ReadFace(text);
				}
				else if ((text[0] == 'o' || text[0] == 'g') && c == ' ')
				{
					_objectName = text.Substring(2).Trim();
					_active = null;
				}
				else if (text[0] == 'u' && text.StartsWith("usemtl"))
				{
					SetMaterial(text.Substring(6).Trim());
					_active = null;
				}
			}
		}

		private void SetMaterial(string name)
		{
			if (_materialMap.TryGetValue(name, out var value))
			{
				_currentMaterial = value;
				return;
			}
			_currentMaterial = -1;
			_active = null;
			_skipped.Add(name);
		}

		private void ReadVertex(string line)
		{
			string[] array = Tokens(line);
			if (array.Length >= 3)
			{
				float z = ParseFloat(array[0]);
				float y = ParseFloat(array[1]);
				float x = ParseFloat(array[2]);
				_positions.Add(new Vector3(x, y, z));
			}
		}

		private void ReadUv(string line)
		{
			string[] array = Tokens(line);
			float x = ((array.Length != 0) ? ParseFloat(array[0]) : 0f);
			float y = ((array.Length > 1) ? ParseFloat(array[1]) : 0f);
			_uvs.Add(new Vector2(x, y));
		}

		private void ReadNormal(string line)
		{
			string[] array = Tokens(line);
			if (array.Length < 3)
			{
				_normals.Add(Vector3.zero);
			}
			else
			{
				_normals.Add(new Vector3(ParseFloat(array[2]), ParseFloat(array[1]), ParseFloat(array[0])));
			}
		}

		private void ReadFace(string line)
		{
			if (_currentMaterial >= 0)
			{
				if (string.IsNullOrEmpty(_objectName))
				{
					_objectName = "mesh";
				}
				EnsureActive(_currentMaterial);
				string[] array = Tokens(line);
				int[] array2 = new int[array.Length];
				int[] array3 = new int[array.Length];
				int[] array4 = new int[array.Length];
				for (int i = 0; i < array.Length; i++)
				{
					string[] array5 = array[i].Split(new char[1] { '/' });
					array2[i] = IndexOf(array5, 0, _positions.Count);
					array3[i] = ((array5.Length > 1 && array5[1].Length > 0) ? IndexOf(array5, 1, _uvs.Count) : (-1));
					array4[i] = ((array5.Length > 2 && array5[2].Length > 0) ? IndexOf(array5, 2, _normals.Count) : (-1));
				}
				for (int j = 2; j < array.Length; j++)
				{
					AddCorner(_active, array2[0], array3[0], array4[0]);
					AddCorner(_active, array2[j], array3[j], array4[j]);
					AddCorner(_active, array2[j - 1], array3[j - 1], array4[j - 1]);
				}
			}
		}

		private void AddCorner(PartBuild part, int vertexId, int uvId, int normalId)
		{
			Vector3Int key = new Vector3Int(vertexId, uvId, normalId);
			if (!part.VertexMap.TryGetValue(key, out var value))
			{
				value = part.Positions.Count;
				part.VertexMap.Add(key, value);
				part.Positions.Add(_positions[Mathf.Clamp(vertexId, 0, _positions.Count - 1)]);
				part.Uvs.Add((uvId >= 0 && uvId < _uvs.Count) ? _uvs[uvId] : Vector2.zero);
				part.Normals.Add((normalId >= 0 && normalId < _normals.Count) ? _normals[normalId] : Vector3.zero);
			}
			part.Triangles.Add(value);
		}

		private void EnsureActive(int materialIndex)
		{
			if (_active == null || _active.MaterialIndex != materialIndex || !string.Equals(_active.ObjectName, _objectName, StringComparison.Ordinal))
			{
				_active = new PartBuild
				{
					ObjectName = _objectName,
					MaterialIndex = materialIndex
				};
				_parts.Add(_active);
			}
		}

		private static string[] Tokens(string line)
		{
			return line.Substring((line[1] == ' ') ? 2 : 3).Split(new char[2] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		}

		private static int IndexOf(string[] components, int slot, int count)
		{
			if (components.Length <= slot)
			{
				return 0;
			}
			if (!int.TryParse(components[slot], NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			{
				return 0;
			}
			if (result >= 0)
			{
				return result - 1;
			}
			return count + result;
		}
	}

	public static volatile bool Loaded;

	public static volatile bool Failed;

	public static string Error;

	public static readonly List<MtlDef> Materials = new List<MtlDef>();

	public static readonly List<ObjGeometry> Geometries = new List<ObjGeometry>();

	private static readonly string[] RunningGearKeys = new string[3] { "track_", "wheel_", "suspension_" };

	private static readonly string[] GunKeys = new string[3] { "gun_barrel", "bone_gun", "turret_cap" };

	private static readonly string[] CommanderGearKeys = new string[5] { "optic_commander", "mg_aa", "bone_mg_aa", "bone_turret_01", "ammo" };

	private static readonly string[] TurretKeys = new string[1] { "bone_turret" };

	public static int SmoothShadedParts;

	public static int FlatShadedParts;

	public static float FacetMeanDegrees;

	public static float FacetOverFivePercent;

	private static int _facetSamples;

	private static float _facetDegrees;

	private static int _facetOverFive;

	public static void Preload(string directory)
	{
		Task.Run(delegate
		{
			try
			{
				ParseDirectory(directory);
				Loaded = true;
			}
			catch (Exception ex)
			{
				Error = ex.Message;
				Failed = true;
				MelonLogger.Error("[L2A7V] model parse failed: " + ex);
			}
		});
	}

	public static LpGroup GroupOf(string objectName, string materialName)
	{
		string text = objectName.ToLowerInvariant();
		string[] runningGearKeys = RunningGearKeys;
		foreach (string value in runningGearKeys)
		{
			if (text.Contains(value))
			{
				return LpGroup.RunningGear;
			}
		}
		runningGearKeys = GunKeys;
		foreach (string value2 in runningGearKeys)
		{
			if (text.Contains(value2))
			{
				return LpGroup.Gun;
			}
		}
		runningGearKeys = CommanderGearKeys;
		foreach (string value3 in runningGearKeys)
		{
			if (text.Contains(value3))
			{
				return LpGroup.CommanderGear;
			}
		}
		runningGearKeys = TurretKeys;
		foreach (string value4 in runningGearKeys)
		{
			if (text.Contains(value4))
			{
				return LpGroup.Turret;
			}
		}
		string text2 = ((materialName == null) ? string.Empty : materialName.ToLowerInvariant());
		if (text2.Contains("turret_ger_camo"))
		{
			return LpGroup.Turret;
		}
		if (text2.Contains("gun_ger_camo"))
		{
			return LpGroup.Gun;
		}
		return LpGroup.Hull;
	}

	private static float ParseFloat(string token)
	{
		if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return 0f;
		}
		return result;
	}

	private static void ParseDirectory(string directory)
	{
		string text = Path.Combine(directory, "Leopard2A7V.obj");
		if (!File.Exists(text))
		{
			throw new FileNotFoundException("Leopard2A7V.obj missing", text);
		}
		Dictionary<string, int> materialMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		SmoothShadedParts = 0;
		FlatShadedParts = 0;
		_facetSamples = 0;
		_facetDegrees = 0f;
		_facetOverFive = 0;
		ParseMtl(Path.Combine(directory, "Leopard2A7V.mtl"), directory, materialMap);
		if (Materials.Count == 0)
		{
			throw new FileNotFoundException("Leopard2A7V.mtl has no materials", directory);
		}
		ObjParser objParser = new ObjParser(materialMap);
		objParser.Parse(text);
		foreach (PartBuild part in objParser.Parts)
		{
			if (part.Triangles.Count >= 3)
			{
				ObjGeometry objGeometry = new ObjGeometry
				{
					ObjectName = part.ObjectName,
					MaterialIndex = part.MaterialIndex,
					Group = GroupOf(part.ObjectName, Materials[part.MaterialIndex].Name),
					Positions = part.Positions.ToArray(),
					Uvs = part.Uvs.ToArray(),
					Normals = part.Normals.ToArray(),
					Triangles = part.Triangles.ToArray()
				};
				BuildNormalsAndTangents(objGeometry);
				Geometries.Add(objGeometry);
			}
		}
		int num = 0;
		foreach (ObjGeometry geometry in Geometries)
		{
			num += geometry.Positions.Length;
		}
		FacetMeanDegrees = ((_facetSamples > 0) ? (_facetDegrees / (float)_facetSamples) : 0f);
		FacetOverFivePercent = ((_facetSamples > 0) ? (100f * (float)_facetOverFive / (float)_facetSamples) : 0f);
		MelonLogger.Msg($"[L2A7V] parsed {Geometries.Count} parts / {num} vertices / " + $"{Materials.Count} materials from {directory}; " + $"{SmoothShadedParts} part(s) shaded on the file's normals, {FlatShadedParts} on derived ones, " + $"triangle vs shading normal {FacetMeanDegrees:0.0} deg mean / " + $"{FacetOverFivePercent:0} pc over 5 deg on {_facetSamples} samples");
		if (objParser.SkippedMaterials.Count > 0)
		{
			MelonLogger.Msg("[L2A7V] dropped the segments of the untextured materials: " + string.Join(", ", objParser.SkippedMaterials));
		}
		foreach (MtlDef material in Materials)
		{
			if (material.AlbedoPath == null)
			{
				MelonLogger.Warning("[L2A7V] material '" + material.Name + "' has no albedo map");
			}
		}
		Dictionary<LpGroup, int[]> dictionary = new Dictionary<LpGroup, int[]>();
		foreach (ObjGeometry geometry2 in Geometries)
		{
			if (!dictionary.TryGetValue(geometry2.Group, out var value))
			{
				value = (dictionary[geometry2.Group] = new int[2]);
			}
			value[0] += geometry2.Positions.Length;
			value[1] += geometry2.Triangles.Length / 3;
		}
		foreach (KeyValuePair<LpGroup, int[]> item in dictionary)
		{
			MelonLogger.Msg($"[L2A7V] group {item.Key}: {item.Value[0]} vertices / {item.Value[1]} triangles");
		}
	}

	private static void ParseMtl(string mtlPath, string directory, Dictionary<string, int> materialMap)
	{
		if (!File.Exists(mtlPath))
		{
			return;
		}
		using StreamReader streamReader = new StreamReader(mtlPath);
		MtlDef mtlDef = null;
		string text;
		while ((text = streamReader.ReadLine()) != null)
		{
			if (text.Length == 0 || text[0] == '#')
			{
				continue;
			}
			int num = IndexOfSeparator(text);
			if (num < 0)
			{
				continue;
			}
			string text2 = text.Substring(0, num).ToLowerInvariant();
			string text3 = text.Substring(num + 1).Trim();
			switch (text2)
			{
			case "newmtl":
				mtlDef = new MtlDef
				{
					Name = text3
				};
				if (!materialMap.ContainsKey(text3))
				{
					materialMap.Add(text3, Materials.Count);
				}
				Materials.Add(mtlDef);
				break;
			case "kd":
				if (mtlDef != null)
				{
					mtlDef.Diffuse = ParseColor(text3);
				}
				break;
			case "map_kd":
				if (mtlDef != null)
				{
					mtlDef.AlbedoPath = Resolve(directory, text3);
				}
				break;
			case "map_normal":
			case "bump":
				if (mtlDef != null)
				{
					mtlDef.NormalPath = Resolve(directory, text3);
				}
				break;
			case "map_ao":
				if (mtlDef != null)
				{
					mtlDef.OcclusionPath = Resolve(directory, text3);
				}
				break;
			case "map_ks":
				if (mtlDef != null)
				{
					mtlDef.GlossPath = Resolve(directory, text3);
				}
				break;
			}
		}
	}

	private static int IndexOfSeparator(string line)
	{
		for (int i = 0; i < line.Length; i++)
		{
			if (line[i] == ' ' || line[i] == '\t')
			{
				return i;
			}
		}
		return -1;
	}

	private static Color ParseColor(string rest)
	{
		string[] array = rest.Split(new char[2] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length < 3)
		{
			return Color.white;
		}
		return new Color(ParseFloat(array[0]), ParseFloat(array[1]), ParseFloat(array[2]), 1f);
	}

	private static string Resolve(string directory, string relativePath)
	{
		string text = Path.Combine(directory, relativePath.Trim(new char[1] { '"' }).Trim().Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(text))
		{
			return null;
		}
		return text;
	}

	private static void BuildNormalsAndTangents(ObjGeometry geometry)
	{
		int num = geometry.Positions.Length;
		bool flag = HasCompleteNormals(geometry.Normals, num);
		if (flag)
		{
			SmoothShadedParts++;
		}
		else
		{
			FlatShadedParts++;
		}
		Vector3[] array = (flag ? geometry.Normals : new Vector3[num]);
		Vector4[] array2 = new Vector4[num];
		Bounds bounds = new Bounds(geometry.Positions[0], Vector3.zero);
		for (int i = 0; i + 2 < geometry.Triangles.Length; i += 3)
		{
			int num2 = geometry.Triangles[i];
			int num3 = geometry.Triangles[i + 1];
			int num4 = geometry.Triangles[i + 2];
			Vector3 vector = geometry.Positions[num2];
			Vector3 vector2 = geometry.Positions[num3];
			Vector3 vector3 = geometry.Positions[num4];
			bounds.Encapsulate(vector);
			bounds.Encapsulate(vector2);
			bounds.Encapsulate(vector3);
			Vector3 vector4 = vector2 - vector;
			Vector3 vector5 = vector3 - vector;
			Vector3 vector6 = Vector3.Cross(vector4, vector5);
			if (vector6.sqrMagnitude > 1E-12f)
			{
				vector6.Normalize();
			}
			else
			{
				vector6 = Vector3.up;
			}
			if (flag)
			{
				if (array[num2].sqrMagnitude < 1E-12f)
				{
					array[num2] = vector6;
				}
				if (array[num3].sqrMagnitude < 1E-12f)
				{
					array[num3] = vector6;
				}
				if (array[num4].sqrMagnitude < 1E-12f)
				{
					array[num4] = vector6;
				}
			}
			else
			{
				array[num2] += vector6;
				array[num3] += vector6;
				array[num4] += vector6;
			}
			if ((i & 0x3F) == 0)
			{
				Vector3 vector7 = array[num2];
				float num5 = ((vector7.sqrMagnitude > 1E-12f) ? Vector3.Angle(vector6, vector7.normalized) : 0f);
				_facetSamples++;
				_facetDegrees += num5;
				if (num5 > 5f)
				{
					_facetOverFive++;
				}
			}
			float num6 = geometry.Uvs[num3].x - geometry.Uvs[num2].x;
			float num7 = geometry.Uvs[num3].y - geometry.Uvs[num2].y;
			float num8 = geometry.Uvs[num4].x - geometry.Uvs[num2].x;
			float num9 = geometry.Uvs[num4].y - geometry.Uvs[num2].y;
			float num10 = num6 * num9 - num8 * num7;
			if (Mathf.Abs(num10) > 1E-10f)
			{
				float num11 = 1f / num10;
				Vector3 rhs = (vector4 * num9 - vector5 * num7) * num11;
				Vector3 rhs2 = (vector5 * num6 - vector4 * num8) * num11;
				float w = ((Vector3.Dot(Vector3.Cross(vector6, rhs), rhs2) < 0f) ? (-1f) : 1f);
				Vector4 vector8 = new Vector4(rhs.x, rhs.y, rhs.z, w);
				array2[num2] += vector8;
				array2[num3] += vector8;
				array2[num4] += vector8;
			}
		}
		for (int j = 0; j < num; j++)
		{
			Vector3 vector9 = array[j];
			if (vector9.sqrMagnitude < 1E-12f)
			{
				vector9 = Vector3.up;
			}
			vector9.Normalize();
			array[j] = vector9;
			Vector4 vector10 = array2[j];
			Vector3 vector11 = ((vector10.sqrMagnitude < 1E-12f) ? Perpendicular(vector9) : (new Vector3(vector10.x, vector10.y, vector10.z) - vector9 * Vector3.Dot(vector9, new Vector3(vector10.x, vector10.y, vector10.z))));
			if (vector11.sqrMagnitude < 1E-08f)
			{
				vector11 = Perpendicular(vector9);
			}
			else
			{
				vector11.Normalize();
			}
			array2[j] = new Vector4(vector11.x, vector11.y, vector11.z, (vector10.w < 0f) ? (-1f) : 1f);
		}
		geometry.Normals = array;
		geometry.Tangents = array2;
		geometry.Bounds = bounds;
	}

	private static bool HasCompleteNormals(Vector3[] normals, int vertexCount)
	{
		if (normals == null || normals.Length != vertexCount || vertexCount == 0)
		{
			return false;
		}
		int num = 0;
		for (int i = 0; i < normals.Length; i++)
		{
			if (normals[i].sqrMagnitude < 1E-08f)
			{
				num++;
			}
		}
		return num <= vertexCount / 500;
	}

	private static Vector3 Perpendicular(Vector3 normal)
	{
		return Vector3.Cross((Mathf.Abs(normal.y) < 0.9f) ? Vector3.up : Vector3.right, normal).normalized;
	}
}

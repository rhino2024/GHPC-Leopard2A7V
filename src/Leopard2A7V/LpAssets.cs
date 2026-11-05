using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V;

public static class LpAssets
{
	private enum Pack
	{
		None,
		Normal
	}

	private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

	private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

	private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();

	private static NormalPackMode _normalMode = NormalPackMode.Raw;

	private static bool _normalModeDetected;

	private static int _maxTextureSize = 1024;

	private static Material _glassMaterial;

	private static Texture2D _neutralPaintMask;

	private static Texture2D _neutralSmoothness;

	private static bool _skinWarned;

	public static IEnumerable<KeyValuePair<string, Material>> CreatedMaterials => Materials;

	public static IEnumerable<Mesh> CreatedMeshes => Meshes.Values;

	private static Texture2D NeutralPaintMask
	{
		get
		{
			if (_neutralPaintMask == null)
			{
				_neutralPaintMask = FlatTexture(Level(255), 161, 0, byte.MaxValue);
			}
			return _neutralPaintMask;
		}
	}

	private static Texture2D NeutralSmoothness
	{
		get
		{
			if (_neutralSmoothness == null)
			{
				_neutralSmoothness = FlatTexture(0, 0, 0, Level(63));
			}
			return _neutralSmoothness;
		}
	}

	public static void ConfigureFromTemplate(Material template)
	{
		if (_normalModeDetected || template == null)
		{
			return;
		}
		_normalModeDetected = true;
		if (!template.HasProperty("_Normal"))
		{
			return;
		}
		Texture2D texture2D = template.GetTexture("_Normal") as Texture2D;
		if (!(texture2D == null))
		{
			string text = texture2D.format.ToString();
			MelonLogger.Msg("[L2A7V] vanilla normal map format: " + text);
			if (text.IndexOf("BC5", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				_normalMode = NormalPackMode.RedGreen;
			}
			else if (text.IndexOf("DXT5", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("BC7", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("BC3", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				_normalMode = NormalPackMode.AlphaGreen;
			}
			else
			{
				_normalMode = NormalPackMode.Raw;
			}
			MelonLogger.Msg("[L2A7V] normal maps packed as: " + _normalMode);
		}
	}

	public static void SetMaxTextureSize(int value)
	{
		_maxTextureSize = Mathf.Clamp(value, 256, 4096);
	}

	public static int RepairShader(Shader live)
	{
		if (live == null)
		{
			return 0;
		}
		int num = 0;
		foreach (Material value in Materials.Values)
		{
			if (!(value == null) && !(value.shader == live))
			{
				value.shader = live;
				num++;
			}
		}
		return num;
	}

	public static Material GetMaterial(int materialIndex, string groupKey, bool pane)
	{
		if (IsGlass(materialIndex) && (pane || groupKey == "CommanderGear"))
		{
			Material glassMaterial = GetGlassMaterial();
			if (glassMaterial != null)
			{
				return glassMaterial;
			}
		}
		string key = groupKey + "|" + materialIndex;
		if (Materials.TryGetValue(key, out var value))
		{
			return value;
		}
		Material templateMaterial = LpVisual.TemplateMaterial;
		if (templateMaterial == null)
		{
			return null;
		}
		Material material = Clone(materialIndex, templateMaterial, neutralMaps: true);
		Materials[key] = material;
		return material;
	}

	private static bool IsGlass(int materialIndex)
	{
		if (materialIndex < 0 || materialIndex >= ObjModel.Materials.Count)
		{
			return false;
		}
		string name = ObjModel.Materials[materialIndex].Name;
		if (name != null)
		{
			return name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return false;
	}

	private static Material GetGlassMaterial()
	{
		Material glassTemplate = LpVisual.GlassTemplate;
		if (glassTemplate == null)
		{
			return null;
		}
		if (_glassMaterial == null)
		{
			_glassMaterial = new Material(glassTemplate)
			{
				name = "L2A7V glass"
			};
			MelonLogger.Msg("[L2A7V] using '" + glassTemplate.name + "' for the glass panes, " + $"queue {_glassMaterial.renderQueue}");
		}
		else if (_glassMaterial.shader != glassTemplate.shader)
		{
			_glassMaterial.shader = glassTemplate.shader;
			MelonLogger.Warning("[L2A7V] re-pointed the sight glass at the live " + glassTemplate.shader.name);
		}
		return _glassMaterial;
	}

	private static Material Clone(int materialIndex, Material template, bool neutralMaps)
	{
		MtlDef mtlDef = ((materialIndex >= 0 && materialIndex < ObjModel.Materials.Count) ? ObjModel.Materials[materialIndex] : null);
		Material material = new Material(template)
		{
			name = template.name + " (L2A7V " + materialIndex + ")"
		};
		bool flag = true;
		bool flag2 = false;
		bool flag3 = false;
		if (mtlDef != null)
		{
			if (mtlDef.AlbedoPath != null)
			{
				Texture2D texture2D = LoadTexture(mtlDef.AlbedoPath, linear: false, Pack.None, isAlbedo: true);
				if (texture2D != null)
				{
					material.SetTexture("_Albedo", texture2D);
				}
			}
			if (mtlDef.NormalPath != null)
			{
				Texture2D texture2D2 = LoadTexture(mtlDef.NormalPath, linear: true, Pack.Normal);
				if (texture2D2 != null)
				{
					material.SetTexture("_Normal", texture2D2);
				}
			}
			if (flag && mtlDef.OcclusionPath != null)
			{
				Texture2D texture2D3 = LoadTexture(mtlDef.OcclusionPath, linear: true, Pack.None);
				if (texture2D3 != null)
				{
					SetIfPresent(material, "_Occlusion", texture2D3);
					flag2 = true;
				}
			}
			if (mtlDef.GlossPath != null)
			{
				Texture2D texture2D4 = LoadTexture(mtlDef.GlossPath, linear: true, Pack.None);
				if (texture2D4 != null)
				{
					SetIfPresent(material, "_Smoothness", texture2D4);
					flag3 = true;
				}
			}
		}
		if (!flag2)
		{
			SetIfPresent(material, "_Occlusion", Texture2D.whiteTexture);
		}
		SetIfPresent(material, "_OcclusionStrength", 1f);
		if (neutralMaps && !flag3)
		{
			SetIfPresent(material, "_Smoothness", NeutralSmoothness);
		}
		if (neutralMaps)
		{
			SetIfPresent(material, "_PaintMask", NeutralPaintMask);
		}
		SetIfPresent(material, "_hackcamoUVs", 0f);
		if (material.shaderKeywords != null && material.shaderKeywords.Length != 0)
		{
			material.DisableKeyword("_HACKCAMOUVS_ON");
		}
		SetIfPresent(material, "_CamoAmount", 0f);
		SetIfPresent(material, "_ScorchAmount", 0f);
		SetIfPresent(material, "_snow", 0f);
		SetIfPresent(material, "_dirt", 0f);
		MelonLogger.Msg(string.Format("[L2A7V] material {0} '{1}' ", materialIndex, mtlDef?.Name ?? "none") + "albedo=" + FileNameOf(mtlDef?.AlbedoPath) + " normal=" + FileNameOf(mtlDef?.NormalPath) + " ao=" + FileNameOf(mtlDef?.OcclusionPath) + " gloss=" + FileNameOf(mtlDef?.GlossPath) + " " + $"neutral={neutralMaps}");
		return material;
	}

	private static string FileNameOf(string path)
	{
		if (!string.IsNullOrEmpty(path))
		{
			return Path.GetFileName(path);
		}
		return "<none>";
	}

	private static void SetIfPresent(Material material, string property, Texture value)
	{
		if (value != null && material.HasProperty(property))
		{
			material.SetTexture(property, value);
		}
	}

	private static void SetIfPresent(Material material, string property, float value)
	{
		if (material.HasProperty(property))
		{
			material.SetFloat(property, value);
		}
	}

	private static byte Level(int value)
	{
		return (byte)Mathf.Clamp(value, 0, 255);
	}

	private static Texture2D FlatTexture(byte r, byte g, byte b, byte a)
	{
		Texture2D texture2D = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true)
		{
			name = "L2A7V_neutral"
		};
		Color32[] array = new Color32[4];
		for (int i = 0; i < 4; i++)
		{
			array[i] = new Color32(r, g, b, a);
		}
		texture2D.SetPixels32(array);
		texture2D.Apply(updateMipmaps: false);
		return texture2D;
	}

	public static Material CreateFlatMaterial(string name, Color32 albedo, byte gloss)
	{
		Material templateMaterial = LpVisual.TemplateMaterial;
		if (templateMaterial == null)
		{
			return null;
		}
		Material material = new Material(templateMaterial)
		{
			name = name
		};
		Texture2D texture2D = FlatTexture(albedo.r, albedo.g, albedo.b, albedo.a);
		texture2D.name = name + " albedo";
		if (material.HasProperty("_Albedo"))
		{
			material.SetTexture("_Albedo", texture2D);
		}
		Texture2D texture2D2 = FlatTexture(0, 0, 0, gloss);
		texture2D2.name = name + " smoothness";
		SetIfPresent(material, "_Smoothness", texture2D2);
		SetIfPresent(material, "_Occlusion", Texture2D.whiteTexture);
		SetIfPresent(material, "_OcclusionStrength", 1f);
		SetIfPresent(material, "_PaintMask", NeutralPaintMask);
		Color32[] array = new Color32[4];
		for (int i = 0; i < 4; i++)
		{
			array[i] = new Color32(127, 127, byte.MaxValue, byte.MaxValue);
		}
		Texture2D texture2D3 = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true, linear: false)
		{
			name = name + " normal"
		};
		texture2D3.SetPixels32(PackNormals(array));
		texture2D3.Apply(updateMipmaps: true);
		SetIfPresent(material, "_Normal", texture2D3);
		SetIfPresent(material, "_hackcamoUVs", 0f);
		if (material.shaderKeywords != null && material.shaderKeywords.Length != 0)
		{
			material.DisableKeyword("_HACKCAMOUVS_ON");
		}
		SetIfPresent(material, "_CamoAmount", 0f);
		SetIfPresent(material, "_ScorchAmount", 0f);
		SetIfPresent(material, "_snow", 0f);
		SetIfPresent(material, "_dirt", 0f);
		return material;
	}

	public static Mesh BuildMesh(LpGroup group, string cacheKey, Vector3 anchor)
	{
		return BuildWhere((ObjGeometry part) => part.Group == group, cacheKey, anchor);
	}

	public static Mesh BuildWhere(Predicate<ObjGeometry> select, string cacheKey, Vector3 anchor)
	{
		if (Meshes.TryGetValue(cacheKey, out var value))
		{
			return value;
		}
		List<Vector3> list = new List<Vector3>();
		List<Vector2> list2 = new List<Vector2>();
		List<Vector3> list3 = new List<Vector3>();
		List<Vector4> list4 = new List<Vector4>();
		Dictionary<int, List<int>> dictionary = new Dictionary<int, List<int>>();
		List<int> list5 = new List<int>();
		Dictionary<int, float> dictionary2 = new Dictionary<int, float>();
		foreach (ObjGeometry geometry in ObjModel.Geometries)
		{
			if (select(geometry))
			{
				int count = list.Count;
				for (int i = 0; i < geometry.Positions.Length; i++)
				{
					list.Add(geometry.Positions[i] - anchor);
					list2.Add(geometry.Uvs[i]);
					list3.Add(geometry.Normals[i]);
					list4.Add(geometry.Tangents[i]);
				}
				float num = Mathf.Min(geometry.Bounds.size.x, Mathf.Min(geometry.Bounds.size.y, geometry.Bounds.size.z));
				if (dictionary2.TryGetValue(geometry.MaterialIndex, out var value2))
				{
					dictionary2[geometry.MaterialIndex] = Mathf.Min(value2, num);
				}
				else
				{
					dictionary2[geometry.MaterialIndex] = num;
				}
				if (!dictionary.TryGetValue(geometry.MaterialIndex, out var value3))
				{
					value3 = new List<int>();
					dictionary.Add(geometry.MaterialIndex, value3);
					list5.Add(geometry.MaterialIndex);
				}
				for (int j = 0; j < geometry.Triangles.Length; j++)
				{
					value3.Add(geometry.Triangles[j] + count);
				}
			}
		}
		if (list.Count == 0)
		{
			return null;
		}
		Mesh mesh = new Mesh
		{
			name = "L2A7V_" + cacheKey,
			indexFormat = IndexFormat.UInt32
		};
		mesh.SetVertices(list);
		mesh.SetUVs(0, list2);
		mesh.SetNormals(list3);
		mesh.SetTangents(list4);
		mesh.subMeshCount = list5.Count;
		for (int k = 0; k < list5.Count; k++)
		{
			mesh.SetTriangles(dictionary[list5[k]], k);
		}
		mesh.RecalculateBounds();
		mesh.UploadMeshData(markNoLongerReadable: true);
		LpVisual.GroupMaterials[cacheKey] = list5;
		List<bool> list6 = new List<bool>(list5.Count);
		foreach (int item in list5)
		{
			float num2 = (dictionary2.ContainsKey(item) ? dictionary2[item] : 0f);
			list6.Add(num2 <= 0.06f);
		}
		LpVisual.GroupThin[cacheKey] = list6;
		Meshes[cacheKey] = mesh;
		return mesh;
	}

	private static string ResolveSkin(string path)
	{
		string skinName = LpConfig.SkinName;
		if (skinName.Length == 0)
		{
			return path;
		}
		string fileName = Path.GetFileName(path);
		string[] array = ((!LpConfig.UhdTextures) ? new string[1] { "skins" } : new string[2] { "skins_uhq", "skins" });
		foreach (string text in array)
		{
			string text2 = Path.Combine(LpConfig.AssetDir, text, skinName, fileName);
			if (File.Exists(text2))
			{
				MelonLogger.Msg("[L2A7V] scheme '" + skinName + "': " + text + "/" + skinName + "/" + fileName);
				return text2;
			}
		}
		if (!_skinWarned && !Directory.Exists(Path.Combine(LpConfig.AssetDir, "skins", skinName)))
		{
			_skinWarned = true;
			MelonLogger.Warning("[L2A7V] paint scheme '" + skinName + "' has no folder under " + Path.Combine(LpConfig.AssetDir, "skins") + "; keeping the factory scheme");
		}
		return path;
	}

	private static string ResolveResolution(string path)
	{
		if (!LpConfig.UhdTextures)
		{
			return path;
		}
		string directoryName = Path.GetDirectoryName(path);
		if (directoryName == null || !string.Equals(Path.GetFileName(directoryName), "textures", StringComparison.OrdinalIgnoreCase))
		{
			return path;
		}
		string text = Path.Combine(Path.GetDirectoryName(directoryName), "textures_uhq", Path.GetFileName(path));
		if (!File.Exists(text))
		{
			return path;
		}
		return text;
	}

	private static Texture2D LoadTexture(string path, bool linear, Pack pack, bool isAlbedo = false)
	{
		if (isAlbedo)
		{
			path = ResolveSkin(path);
		}
		path = ResolveResolution(path);
		string key = path + "|" + pack.ToString() + ((pack == Pack.Normal) ? ("|" + _normalMode) : string.Empty) + "|" + _maxTextureSize + "|" + ((!isAlbedo) ? (-1) : 0);
		if (Textures.TryGetValue(key, out var value))
		{
			return value;
		}
		try
		{
			byte[] data = File.ReadAllBytes(path);
			Texture2D texture2D = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true);
			if (!texture2D.LoadImage(data))
			{
				UnityEngine.Object.Destroy(texture2D);
				return null;
			}
			int outWidth = texture2D.width;
			int outHeight = texture2D.height;
			Color32[] array = Resample(texture2D.GetPixels32(), outWidth, outHeight, Mathf.Min(outWidth, _maxTextureSize), Mathf.Min(outHeight, _maxTextureSize), out outWidth, out outHeight);
			UnityEngine.Object.Destroy(texture2D);
			if (pack == Pack.Normal)
			{
				array = PackNormals(array);
			}
			if (isAlbedo)
			{
				array = MatchAlbedoLuminance(array, path);
			}
			Texture2D texture2D2 = new Texture2D(outWidth, outHeight, TextureFormat.RGBA32, mipChain: true, linear)
			{
				name = Path.GetFileNameWithoutExtension(path),
				filterMode = FilterMode.Trilinear,
				anisoLevel = 4,
				wrapMode = TextureWrapMode.Repeat
			};
			texture2D2.SetPixels32(array);
			texture2D2.Apply(updateMipmaps: true);
			Textures[key] = texture2D2;
			return texture2D2;
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V] texture load failed for " + path + ": " + ex.Message);
			return null;
		}
	}

	private static Color32[] MatchAlbedoLuminance(Color32[] source, string path)
	{
		int num = 0;
		if (num <= 0)
		{
			return source;
		}
		long num2 = 0L;
		for (int i = 0; i < source.Length; i++)
		{
			num2 += source[i].r + source[i].g + source[i].b;
		}
		float num3 = (float)num2 / ((float)source.Length * 3f);
		if (num3 < 1f)
		{
			return source;
		}
		float num4 = Mathf.Clamp((float)num / num3, 0.55f, 1f);
		if (num4 > 0.995f)
		{
			return source;
		}
		for (int j = 0; j < source.Length; j++)
		{
			Color32 color = source[j];
			source[j] = new Color32((byte)Mathf.Clamp((float)(int)color.r * num4, 0f, 255f), (byte)Mathf.Clamp((float)(int)color.g * num4, 0f, 255f), (byte)Mathf.Clamp((float)(int)color.b * num4, 0f, 255f), color.a);
		}
		MelonLogger.Msg("[L2A7V] albedo " + Path.GetFileName(path) + " luminance " + $"{num3:F0} -> {num3 * num4:F0} (x{num4:F2})");
		return source;
	}

	private static Color32[] PackNormals(Color32[] source)
	{
		if (_normalMode == NormalPackMode.Raw)
		{
			return source;
		}
		Color32[] array = new Color32[source.Length];
		for (int i = 0; i < source.Length; i++)
		{
			Color32 color = source[i];
			byte r = color.r;
			byte g = color.g;
			if (_normalMode == NormalPackMode.RedGreen)
			{
				array[i] = new Color32(r, g, 128, byte.MaxValue);
			}
			else
			{
				array[i] = new Color32(byte.MaxValue, g, 127, r);
			}
		}
		return array;
	}

	private static Color32[] Resample(Color32[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, out int outWidth, out int outHeight)
	{
		outWidth = targetWidth;
		outHeight = targetHeight;
		if (targetWidth == sourceWidth && targetHeight == sourceHeight)
		{
			return source;
		}
		Color32[] array = new Color32[targetWidth * targetHeight];
		float num = (float)sourceWidth / (float)targetWidth;
		float num2 = (float)sourceHeight / (float)targetHeight;
		for (int i = 0; i < targetHeight; i++)
		{
			int num3 = Mathf.Clamp((int)((float)i * num2), 0, sourceHeight - 1);
			int a = Mathf.Max(num3 + 1, Mathf.CeilToInt((float)(i + 1) * num2));
			a = Mathf.Min(a, sourceHeight);
			for (int j = 0; j < targetWidth; j++)
			{
				int num4 = Mathf.Clamp((int)((float)j * num), 0, sourceWidth - 1);
				int a2 = Mathf.Max(num4 + 1, Mathf.CeilToInt((float)(j + 1) * num));
				a2 = Mathf.Min(a2, sourceWidth);
				int num5 = 0;
				int num6 = 0;
				int num7 = 0;
				int num8 = 0;
				int num9 = 0;
				for (int k = num3; k < a; k++)
				{
					int num10 = k * sourceWidth;
					for (int l = num4; l < a2; l++)
					{
						Color32 color = source[num10 + l];
						num5 += color.r;
						num6 += color.g;
						num7 += color.b;
						num8 += color.a;
						num9++;
					}
				}
				array[i * targetWidth + j] = new Color32((byte)(num5 / num9), (byte)(num6 / num9), (byte)(num7 / num9), (byte)(num8 / num9));
			}
		}
		return array;
	}
}

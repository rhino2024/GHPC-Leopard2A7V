using System;
using System.Collections.Generic;
using GHPC;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V;

public static class LpInterior
{
	private sealed class Built
	{
		public Mesh Mesh;

		public List<string> Classes;
	}

	private static readonly Dictionary<string, Color32> Palette = new Dictionary<string, Color32>
	{
		{
			"int_armor",
			new Color32(172, 160, 118, byte.MaxValue)
		},
		{
			"int_liner",
			new Color32(140, 152, 148, byte.MaxValue)
		},
		{
			"int_firewall",
			new Color32(122, 84, 62, byte.MaxValue)
		},
		{
			"int_ammo",
			new Color32(198, 166, 72, byte.MaxValue)
		},
		{
			"int_engine",
			new Color32(78, 82, 96, byte.MaxValue)
		},
		{
			"int_fcs",
			new Color32(96, 124, 66, byte.MaxValue)
		},
		{
			"int_metal",
			new Color32(156, 158, 166, byte.MaxValue)
		},
		{
			"int_crew",
			new Color32(184, 158, 116, byte.MaxValue)
		}
	};

	private static readonly Dictionary<string, byte> Gloss = new Dictionary<string, byte>
	{
		{ "int_armor", 28 },
		{ "int_liner", 12 },
		{ "int_firewall", 20 },
		{ "int_ammo", 30 },
		{ "int_engine", 30 },
		{ "int_fcs", 44 },
		{ "int_metal", 38 },
		{ "int_crew", 14 }
	};

	private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

	private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();

	private static readonly Dictionary<string, List<string>> Submeshes = new Dictionary<string, List<string>>();

	public static int CrewTriangles { get; private set; }

	public static bool Loaded => LpXrayParts.Loaded;

	public static bool Failed => LpXrayParts.Failed;

	public static string Error => LpXrayParts.Error;

	public static void Preload(string directory)
	{
		LpXrayParts.Preload(directory);
	}

	public static int Attach(GameObject unitRoot, LpVisual.Assembly art)
	{
		if (!LpConfig.ShowInterior || art == null)
		{
			return 0;
		}
		if (!LpXrayParts.Loaded || LpXrayParts.Failed)
		{
			return 0;
		}
		if (CrewTriangles == 0)
		{
			int num = 0;
			foreach (LpXrayParts.Part part in LpXrayParts.Parts)
			{
				if (part.Class == "int_crew")
				{
					num += part.Triangles.Length / 3;
				}
			}
			CrewTriangles = num;
		}
		int num2 = 0;
		num2 += AttachBone("Hull", art.Hull, Vector3.zero);
		num2 += AttachBone("Turret", art.Turret, LpConfig.TurretAxis);
		num2 += AttachBone("Gun", art.Gun, LpConfig.GunAxis);
		if (num2 > 0)
		{
			MelonLogger.Msg("[L2A7V] x-ray compartment attached: " + num2 + " triangle(s) on the hull, turret and gun pivots, drawn only in the after-action x-ray view");
		}
		return num2;
	}

	private static int AttachBone(string bone, Transform pivot, Vector3 anchor)
	{
		if (pivot == null)
		{
			return 0;
		}
		if (pivot.Find("L2A7V_Interior_" + bone) != null)
		{
			return 0;
		}
		Built built = Build(bone, anchor);
		if (built == null || built.Mesh == null)
		{
			return 0;
		}
		GameObject gameObject = new GameObject("L2A7V_Interior_" + bone);
		gameObject.SetActive(value: false);
		gameObject.transform.SetParent(pivot, worldPositionStays: false);
		gameObject.layer = pivot.gameObject.layer;
		gameObject.AddComponent<MeshFilter>().sharedMesh = built.Mesh;
		MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
		Material[] array = new Material[built.Classes.Count];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = GetMaterial(built.Classes[i]);
		}
		meshRenderer.sharedMaterials = array;
		meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
		meshRenderer.receiveShadows = true;
		meshRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
		meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
		gameObject.AddComponent<AarVisual>().RenderMode = AarVisual.AarVisualRenderMode.XrayOnly;
		gameObject.SetActive(value: true);
		if (built.Mesh.vertexCount <= 0)
		{
			return 0;
		}
		return CountTriangles(built.Mesh);
	}

	private static int CountTriangles(Mesh mesh)
	{
		int num = 0;
		for (int i = 0; i < mesh.subMeshCount; i++)
		{
			num += (int)mesh.GetIndexCount(i) / 3;
		}
		return num;
	}

	private static Built Build(string bone, Vector3 anchor)
	{
		if (Meshes.TryGetValue(bone, out var value))
		{
			return new Built
			{
				Mesh = value,
				Classes = Submeshes[bone]
			};
		}
		List<Vector3> list = new List<Vector3>();
		List<Vector3> list2 = new List<Vector3>();
		Dictionary<string, List<int>> dictionary = new Dictionary<string, List<int>>();
		List<string> list3 = new List<string>();
		foreach (LpXrayParts.Part part in LpXrayParts.Parts)
		{
			if (string.Equals(part.Bone, bone, StringComparison.OrdinalIgnoreCase))
			{
				string text = (Palette.ContainsKey(part.Class) ? part.Class : "int_metal");
				if (!dictionary.TryGetValue(text, out var value2))
				{
					value2 = new List<int>();
					dictionary.Add(text, value2);
					list3.Add(text);
				}
				int count = list.Count;
				for (int i = 0; i < part.Positions.Length; i++)
				{
					list.Add(part.Positions[i] - anchor);
					list2.Add(part.Normals[i]);
				}
				for (int j = 0; j < part.Triangles.Length; j++)
				{
					value2.Add(part.Triangles[j] + count);
				}
			}
		}
		if (list.Count == 0 || list3.Count == 0)
		{
			return null;
		}
		Mesh mesh = new Mesh
		{
			name = "L2A7V_Interior_" + bone,
			indexFormat = ((list.Count > 65000) ? IndexFormat.UInt32 : IndexFormat.UInt16)
		};
		mesh.SetVertices(list);
		mesh.SetNormals(list2);
		mesh.subMeshCount = list3.Count;
		for (int k = 0; k < list3.Count; k++)
		{
			mesh.SetTriangles(dictionary[list3[k]], k);
		}
		mesh.RecalculateBounds();
		mesh.UploadMeshData(markNoLongerReadable: true);
		Meshes[bone] = mesh;
		Submeshes[bone] = list3;
		return new Built
		{
			Mesh = mesh,
			Classes = list3
		};
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

	private static Material GetMaterial(string cls)
	{
		if (Materials.TryGetValue(cls, out var value) && value != null)
		{
			if (value.shader == null || value.shader.name == "Hidden/InternalErrorShader")
			{
				value.shader = LpVisual.TemplateMaterial.shader;
			}
			return value;
		}
		if (!Palette.TryGetValue(cls, out var value2))
		{
			value2 = new Color32(156, 158, 166, byte.MaxValue);
		}
		if (!Gloss.TryGetValue(cls, out var value3))
		{
			value3 = 30;
		}
		value = LpAssets.CreateFlatMaterial("L2A7V " + cls, value2, value3);
		Materials[cls] = value;
		return value;
	}
}

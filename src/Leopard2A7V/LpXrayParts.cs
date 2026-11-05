using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpXrayParts
{
	public sealed class Part
	{
		public string Name;

		public string Bone;

		public string Class;

		public Vector3[] Positions;

		public Vector3[] Normals;

		public int[] Triangles;

		public Vector3 Center;

		public Vector3 Size;
	}

	private sealed class Builder
	{
		public string Name;

		public readonly List<Vector3> Positions = new List<Vector3>();

		public readonly List<Vector3> Normals = new List<Vector3>();

		public readonly List<int> Triangles = new List<int>();

		public readonly Dictionary<long, int> Map = new Dictionary<long, int>();
	}

	private const string Tag = "[L2A7V]";

	public static volatile bool Loaded;

	public static volatile bool Failed;

	public static string Error;

	public static readonly List<Part> Parts = new List<Part>();

	public static int Triangles
	{
		get
		{
			int num = 0;
			foreach (Part part in Parts)
			{
				num += part.Triangles.Length / 3;
			}
			return num;
		}
	}

	public static void Preload(string directory)
	{
		string path = Path.Combine(directory, "Interior.obj");
		if (!File.Exists(path))
		{
			Failed = true;
			MelonLogger.Msg("[L2A7V] no Interior.obj beside the art; the x-ray armour and compartment stay out");
			return;
		}
		Task.Run(delegate
		{
			try
			{
				Parse(path);
				Loaded = true;
			}
			catch (Exception ex)
			{
				Error = ex.Message;
				Failed = true;
				MelonLogger.Error("[L2A7V] x-ray parse failed: " + ex);
			}
		});
	}

	private static void Parse(string path)
	{
		List<Vector3> list = new List<Vector3>();
		List<Vector3> list2 = new List<Vector3>();
		List<Builder> list3 = new List<Builder>();
		Builder builder = null;
		using (StreamReader streamReader = new StreamReader(path))
		{
			string text;
			while ((text = streamReader.ReadLine()) != null)
			{
				if (text.Length < 3 || text[0] == '#')
				{
					continue;
				}
				char c = text[1];
				if (text[0] == 'v' && c == ' ')
				{
					list.Add(ReadVector(text));
				}
				else if (text[0] == 'v' && c == 'n' && text[2] == ' ')
				{
					list2.Add(ReadVector(text));
				}
				else if ((text[0] == 'g' || text[0] == 'o') && c == ' ')
				{
					builder = new Builder
					{
						Name = text.Substring(2).Trim()
					};
					list3.Add(builder);
				}
				else if (text[0] == 'f' && c == ' ')
				{
					if (builder == null)
					{
						builder = new Builder
						{
							Name = "xray"
						};
						list3.Add(builder);
					}
					ReadFace(text, builder, list, list2);
				}
			}
		}
		foreach (Builder item in list3)
		{
			if (item.Triangles.Count >= 3)
			{
				Part part = new Part
				{
					Name = item.Name,
					Positions = item.Positions.ToArray(),
					Normals = item.Normals.ToArray(),
					Triangles = item.Triangles.ToArray()
				};
				Measure(part);
				Classify(part);
				Parts.Add(part);
			}
		}
		int num = 0;
		int num2 = 0;
		foreach (Part part2 in Parts)
		{
			if (part2.Class == "int_armor")
			{
				num += part2.Triangles.Length / 3;
			}
			else
			{
				num2 += part2.Triangles.Length / 3;
			}
		}
		MelonLogger.Msg("[L2A7V] x-ray parsed: " + Parts.Count + " module(s) / " + Triangles + " triangles (" + num + " armour shell, " + num2 + " compartment) from " + path);
	}

	private static Vector3 ReadVector(string line)
	{
		string[] tokens = line.Split(new char[2] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		return new Vector3(z: ParseFloat(tokens, 1), y: ParseFloat(tokens, 2), x: ParseFloat(tokens, 3));
	}

	private static float ParseFloat(string[] tokens, int index)
	{
		if (index >= tokens.Length)
		{
			return 0f;
		}
		if (!float.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return 0f;
		}
		return result;
	}

	private static void ReadFace(string line, Builder builder, List<Vector3> positions, List<Vector3> normals)
	{
		string[] array = line.Substring(2).Split(new char[2] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length >= 3)
		{
			int[] array2 = new int[array.Length];
			for (int i = 0; i < array.Length; i++)
			{
				string[] array3 = array[i].Split(new char[1] { '/' });
				int vertex = IndexOf(array3, 0, positions.Count);
				int normal = ((array3.Length > 2 && array3[2].Length > 0) ? IndexOf(array3, 2, normals.Count) : (-1));
				array2[i] = AddCorner(builder, vertex, normal, positions, normals);
			}
			for (int j = 2; j < array2.Length; j++)
			{
				builder.Triangles.Add(array2[0]);
				builder.Triangles.Add(array2[j]);
				builder.Triangles.Add(array2[j - 1]);
			}
		}
	}

	private static int AddCorner(Builder builder, int vertex, int normal, List<Vector3> positions, List<Vector3> normals)
	{
		long key = ((long)vertex << 32) ^ (uint)normal;
		if (builder.Map.TryGetValue(key, out var value))
		{
			return value;
		}
		value = builder.Positions.Count;
		builder.Map[key] = value;
		builder.Positions.Add(positions[Mathf.Clamp(vertex, 0, positions.Count - 1)]);
		builder.Normals.Add((normal >= 0 && normal < normals.Count) ? normals[normal] : Vector3.zero);
		return value;
	}

	private static int IndexOf(string[] parts, int slot, int count)
	{
		if (parts.Length <= slot)
		{
			return 0;
		}
		if (!int.TryParse(parts[slot], NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return 0;
		}
		if (result >= 0)
		{
			return result - 1;
		}
		return count + result;
	}

	private static void Measure(Part part)
	{
		Vector3 vector = part.Positions[0];
		Vector3 vector2 = vector;
		for (int i = 1; i < part.Positions.Length; i++)
		{
			vector = Vector3.Min(vector, part.Positions[i]);
			vector2 = Vector3.Max(vector2, part.Positions[i]);
		}
		part.Center = (vector + vector2) * 0.5f;
		part.Size = vector2 - vector;
	}

	private static void Classify(Part part)
	{
		string name = part.Name.ToLowerInvariant();
		part.Bone = BoneFor(name);
		part.Class = ClassFor(name);
	}

	private static string BoneFor(string name)
	{
		if (name.Contains("cannon_breech") || name.Contains("gun_barrel_01"))
		{
			return "Gun";
		}
		if (name.Contains("gun_barrel_02") || name.Contains("optic_gun"))
		{
			return "Turret";
		}
		if (name.Contains("turret") || name.Contains("commander") || name.Contains("gunner") || name.Contains("loader") || name.Contains("fire_control") || name.Contains("radio") || name.Contains("drive_turret"))
		{
			return "Turret";
		}
		return "Hull";
	}

	private static string ClassFor(string name)
	{
		if (name.StartsWith("composite_armor") || name.StartsWith("spall_liner") || name.StartsWith("firewall_armor"))
		{
			return "int_armor";
		}
		if (name.StartsWith("ammo"))
		{
			return "int_ammo";
		}
		switch (name)
		{
		case "driver_xray":
		case "gunner_xray":
		case "loader_xray":
		case "commander_xray":
			return "int_crew";
		default:
			if (name.Contains("engine") || name.Contains("transmission") || name.Contains("radiator") || name.Contains("power_system"))
			{
				return "int_engine";
			}
			if (name.Contains("fire_control") || name.Contains("radio") || name.Contains("optic") || name.Contains("panoramic") || name.Contains("driver_controls"))
			{
				return "int_fcs";
			}
			return "int_metal";
		}
	}
}

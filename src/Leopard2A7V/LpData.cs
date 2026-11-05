using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpData
{
	internal sealed class Armour
	{
		internal float Ke;

		internal float Ce;

		internal int Layers;
	}

	internal sealed class Module
	{
		internal string Name;

		internal Vector3 Centre;

		internal Vector3 Size;

		internal float Health;

		internal float Threshold;
	}

	internal sealed class Hatch
	{
		internal string Group;

		internal string Role;

		internal Vector3 Axis = Vector3.up;

		internal float Degrees = 95f;

		internal Vector3 Hinge;

		internal bool HasHinge;
	}

	internal struct Role
	{
		internal Color Albedo;

		internal int Gloss;
	}

	private const string Tag = "[L2A7V][data]";

	private const string FileName = "L2A7V_data.txt";

	private static bool _loaded;

	internal static readonly Dictionary<string, Armour> Armours = new Dictionary<string, Armour>(StringComparer.OrdinalIgnoreCase);

	internal static readonly List<string> VanillaOff = new List<string>();

	internal static readonly Dictionary<string, string> CodexOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	internal static readonly List<Hatch> Hatches = new List<Hatch>();

	internal static readonly List<Module> Modules = new List<Module>();

	internal static readonly Dictionary<string, Role> InteriorRoles = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);

	internal static void Load()
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		string path = Path.Combine(LpXrayArmor.ModFolder, "L2A7V_data.txt");
		if (!File.Exists(path))
		{
			MelonLogger.Msg("[L2A7V][data] no L2A7V_data.txt; using the built-in numbers");
			return;
		}
		int num = 0;
		int num2 = 0;
		try
		{
			string[] array = File.ReadAllLines(path);
			for (int i = 0; i < array.Length; i++)
			{
				string text = array[i].Trim();
				if (text.Length == 0 || text[0] == '#')
				{
					continue;
				}
				num2++;
				string[] array2 = text.Split(new char[1] { '|' });
				string text2 = array2[0].Trim().ToLowerInvariant();
				try
				{
					switch (text2)
					{
					case "armour":
						ReadArmour(array2);
						break;
					case "vanilla_off":
						ReadVanillaOff(array2);
						break;
					case "hatch":
						ReadHatch(array2);
						break;
					case "codex":
						ReadCodex(array2);
						break;
					case "module":
						ReadModule(array2);
						break;
					case "interior_role":
						ReadRole(array2);
						break;
					default:
						num++;
						break;
					}
				}
				catch (Exception)
				{
					num++;
				}
			}
		}
		catch (Exception ex2)
		{
			MelonLogger.Warning("[L2A7V][data] could not read L2A7V_data.txt: " + ex2.Message);
			return;
		}
		MelonLogger.Msg("[L2A7V][data] loaded " + Armours.Count + " armour override(s), " + Modules.Count + " module(s), " + VanillaOff.Count + " donor plate name(s) and " + Hatches.Count + " hatch hinge(s) from L2A7V_data.txt" + ((num > 0) ? (" (" + num + " line(s) ignored of " + num2 + ")") : string.Empty));
	}

	private static void ReadArmour(string[] f)
	{
		if (f.Length >= 5)
		{
			Armour value = new Armour
			{
				Ke = Float(f[2]),
				Ce = Float(f[3]),
				Layers = Mathf.Clamp(Mathf.RoundToInt(Float(f[4])), 1, 6)
			};
			Armours[f[1].Trim()] = value;
		}
	}

	private static void ReadRole(string[] f)
	{
		if (f.Length >= 3)
		{
			string[] array = f[2].Split(new char[1] { ',' });
			if (array.Length == 3)
			{
				InteriorRoles[f[1].Trim()] = new Role
				{
					Albedo = new Color(Float(array[0]), Float(array[1]), Float(array[2]), 1f),
					Gloss = ((f.Length > 3) ? Mathf.RoundToInt(Float(f[3])) : 32)
				};
			}
		}
	}

	private static void ReadModule(string[] f)
	{
		if (f.Length >= 6)
		{
			string[] array = f[2].Split(new char[1] { ',' });
			string[] array2 = f[3].Split(new char[1] { ',' });
			if (array.Length == 3 && array2.Length == 3)
			{
				Modules.Add(new Module
				{
					Name = f[1].Trim(),
					Centre = new Vector3(Float(array[0]), Float(array[1]), Float(array[2])),
					Size = new Vector3(Float(array2[0]), Float(array2[1]), Float(array2[2])),
					Health = Float(f[4]),
					Threshold = ((f.Length > 5) ? Float(f[5]) : 25f)
				});
			}
		}
	}

	private static void ReadCodex(string[] f)
	{
		if (f.Length >= 3)
		{
			CodexOverrides[f[1].Trim()] = f[2].Trim();
		}
	}

	private static void ReadVanillaOff(string[] f)
	{
		if (f.Length >= 2)
		{
			string text = f[1].Trim();
			if (text.Length > 0)
			{
				VanillaOff.Add(text);
			}
		}
	}

	private static void ReadHatch(string[] f)
	{
		if (f.Length < 4)
		{
			return;
		}
		Hatch hatch = new Hatch
		{
			Group = f[1].Trim(),
			Role = f[2].Trim()
		};
		string[] array = f[3].Split(new char[1] { ',' });
		if (array.Length == 3)
		{
			Vector3 vector = new Vector3(Float(array[0]), Float(array[1]), Float(array[2]));
			if (vector.sqrMagnitude > 0.0001f)
			{
				hatch.Axis = vector.normalized;
			}
		}
		if (f.Length > 4)
		{
			hatch.Degrees = Float(f[4]);
		}
		if (f.Length > 5)
		{
			string[] array2 = f[5].Split(new char[1] { ',' });
			if (array2.Length == 3)
			{
				hatch.Hinge = new Vector3(Float(array2[0]), Float(array2[1]), Float(array2[2]));
				hatch.HasHinge = true;
			}
		}
		Hatches.Add(hatch);
	}

	private static float Float(string text)
	{
		return float.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
	}

	internal static bool TryArmour(string groupName, out float ke, out float ce, out int layers)
	{
		Load();
		if (Armours.TryGetValue(groupName, out var value))
		{
			ke = value.Ke;
			ce = value.Ce;
			layers = value.Layers;
			return true;
		}
		ke = (ce = 0f);
		layers = 0;
		return false;
	}

	internal static string CodexFor(string groupName)
	{
		Load();
		if (!CodexOverrides.TryGetValue(groupName, out var value))
		{
			return null;
		}
		return value;
	}

	internal static bool TryRole(string name, out Color albedo, out int gloss)
	{
		Load();
		if (InteriorRoles.TryGetValue(name, out var value))
		{
			albedo = value.Albedo;
			gloss = value.Gloss;
			return true;
		}
		albedo = Color.white;
		gloss = 32;
		return false;
	}

	internal static bool IsVanillaOff(string plateName)
	{
		Load();
		foreach (string item in VanillaOff)
		{
			if (plateName.IndexOf(item, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	internal static Hatch HatchFor(string groupName, string role)
	{
		Load();
		foreach (Hatch hatch in Hatches)
		{
			if (hatch.Group.Equals(groupName, StringComparison.OrdinalIgnoreCase))
			{
				return hatch;
			}
			if (!string.IsNullOrEmpty(role) && hatch.Role.Equals(role, StringComparison.OrdinalIgnoreCase) && hatch.Group.Length == 0)
			{
				return hatch;
			}
		}
		return null;
	}
}

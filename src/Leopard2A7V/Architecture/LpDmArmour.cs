using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using MelonLoader;

namespace Leopard2A7V.Architecture;

// The schema is this mod's own data-driven damage model; all values and
// geometry below belong to the Leopard 2A7V. No T90 protection is imported.
internal sealed class LpDmArmour
{
	internal sealed class Entry
	{
		internal string Piece;
		internal string Mode;
		internal string Name;
		internal string Material;
		internal string Source;
		internal string[] Meshes;
		internal string[] Replace;
		internal float Ke;
		internal float Ce;
		internal float KeAfter;
		internal float CeAfter;
		internal bool AngleMatters;
		internal bool Variable => Mode == "geometry";
		internal bool Convex => Mode == "composite";
		internal bool Drawn => Mode == "drawn";
	}

	private const string Header = "Piece\tMode\tKe\tCe\tKeAfter\tCeAfter\tAngleMatters\tMaterial\tMeshes\tName\tReplace\tSource";
	private const string Data = Header + "\n" +
		"composite_armor_turret_03_xray\tcomposite\t880\t1500\t880\t1500\t1\tcomposite\txray\tL2A7V turret front array\tcheek array left A;cheek array right A;cheek array right B;front left plate;front right plate;left cheek plate;right cheek plate\tExisting L2A7V frontal candidate, not a certified protection claim\n" +
		"composite_armor_turret_01_xray\tcomposite\t460\t800\t460\t800\t1\tcomposite\txray\tL2A7V turret flank array\tarray left;array right\tExisting L2A7V flank candidate\n" +
		"composite_armor_turret_02_xray\tcomposite\t460\t800\t460\t800\t1\tcomposite\txray\tL2A7V turret side array\tarray left;array right\tExisting L2A7V side candidate\n" +
		"composite_armor_turret_10_xray\tgeometry\t0.68\t1.36\t0\t0\t1\tcomposite\txray\tL2A7V mantlet\tmantlet;gun rotor;mantlet recess faces\tGeometry-measured mantlet coefficient\n" +
		"composite_armor_hull_01_xray\tcomposite\t360\t720\t360\t720\t1\tcomposite\txray\tL2A7V upper glacis\tupper glacis array;upper front plate\tExisting L2A7V glacis candidate\n" +
		"composite_armor_hull_02_xray\tcomposite\t50\t70\t50\t70\t1\tcomposite\txray\tL2A7V front hull side\tfront left array;front right array;hull roof front\tExisting L2A7V internal hull array\n" +
		"composite_armor_hull_03_xray\tcomposite\t50\t70\t50\t70\t1\tcomposite\txray\tL2A7V front hull side\tfront left array;front right array;hull roof front\tExisting L2A7V internal hull array\n" +
		"composite_armor_hull_04_xray\tgeometry\t0.15\t0.15\t0\t0\t1\tarmour_steel\txray\tL2A7V engine firewall\tfirewall\tGeometry-measured steel bulkhead\n" +
		"composite_armor_hull_05_xray\tcomposite\t50\t70\t50\t70\t1\tcomposite\txray\tL2A7V front hull side\tfront left array;front right array;hull roof front\tExisting L2A7V internal hull array\n" +
		"spall_liner_01_xray\tcomposite\t8\t15\t8\t15\t0\tliner\txray\tL2A7V turret liner 01\t\tExisting L2A7V liner candidate\n" +
		"spall_liner_02_xray\tcomposite\t8\t15\t8\t15\t0\tliner\txray\tL2A7V turret liner 02\t\tExisting L2A7V liner candidate\n" +
		"spall_liner_03_xray\tcomposite\t8\t15\t8\t15\t0\tliner\txray\tL2A7V turret liner 03\t\tExisting L2A7V liner candidate\n" +
		"spall_liner_04_xray\tcomposite\t8\t15\t8\t15\t0\tliner\txray\tL2A7V turret liner 04\t\tExisting L2A7V liner candidate\n" +
		"spall_liner_05_xray\tcomposite\t8\t15\t8\t15\t0\tliner\txray\tL2A7V bustle liner\t\tExisting L2A7V liner candidate\n" +
		"spall_liner_06_xray\tcomposite\t8\t15\t8\t15\t0\tliner\txray\tL2A7V hull liner 06\t\tExisting L2A7V liner candidate\n" +
		"spall_liner_07_xray\tcomposite\t8\t15\t8\t15\t0\tliner\txray\tL2A7V hull liner 07\t\tExisting L2A7V liner candidate\n" +
		"spall_liner_08_xray\tcomposite\t8\t15\t8\t15\t0\tliner\txray\tL2A7V hull liner 08\t\tExisting L2A7V liner candidate\n" +
		"spall_liner_09_xray\tcomposite\t8\t15\t8\t15\t0\tliner\txray\tL2A7V driver liner\t\tExisting L2A7V liner candidate\n" +
		"firewall_armor_01_xray\tgeometry\t0.15\t0.15\t0\t0\t1\tarmour_steel\txray\tL2A7V turret firewall\t\tGeometry-measured steel bulkhead\n" +
		"firewall_armor_02_xray\tgeometry\t0.15\t0.15\t0\t0\t1\tarmour_steel\txray\tL2A7V turret firewall edge\t\tGeometry-measured steel bulkhead\n" +
		"ex_armor_l_17|ex_armor_r_17\tcomposite\t880\t1500\t880\t1500\t1\tcomposite\tvisual\tL2A7V external turret front wedges\t\tExisting external visible geometry, shared protection value with front array\n" +
		"ex_armor_l_18|ex_armor_r_18\tcomposite\t460\t800\t460\t800\t1\tcomposite\tvisual\tL2A7V external turret side array\t\tExisting external visible geometry\n" +
		"ex_armor_l_02|ex_armor_r_02|ex_armor_l_03|ex_armor_r_03|ex_armor_l_04|ex_armor_r_04|ex_armor_l_05|ex_armor_r_05|ex_armor_l_06|ex_armor_r_06|ex_armor_l_07|ex_armor_r_07|ex_armor_l_08|ex_armor_r_08|ex_armor_l_09|ex_armor_r_09|ex_armor_l_10|ex_armor_r_10|ex_armor_l_11|ex_armor_r_11|ex_armor_l_12|ex_armor_r_12|ex_armor_l_13|ex_armor_r_13|ex_armor_l_14|ex_armor_r_14|ex_armor_l_15|ex_armor_r_15|ex_armor_l_16|ex_armor_r_16\tcomposite\t360\t650\t360\t650\t1\tcomposite\tvisual\tL2A7V external hull side array\t\tExisting external visible geometry\n" +
		"ex_armor_body_01|ex_armor_body_03|ex_armor_body_04|ex_armor_body_05|ex_armor_body_06|ex_armor_body_07\tcomposite\t360\t720\t360\t720\t1\tcomposite\tvisual\tL2A7V external glacis plates\t\tExisting external visible geometry; inspect for intentional overlap in ballistic tests";

	internal readonly List<Entry> Entries = new List<Entry>();
	private readonly Dictionary<string, Entry> _byPiece = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

	internal Entry this[string piece]
	{
		get
		{
			Entry entry;
			return _byPiece.TryGetValue(piece ?? string.Empty, out entry) ? entry : null;
		}
	}

	internal static LpDmArmour Load()
	{
		string text = null;
		try
		{
			string path = Path.Combine(LpConfig.AssetDir, "Architecture", "l2a7v_dm_armour.tsv");
			if (File.Exists(path))
			{
				text = File.ReadAllText(path, Encoding.UTF8);
			}
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V][dm-armour] external table failed, embedded defaults retained: " + ex.Message);
		}
		return Parse(text ?? Data);
	}

	internal static LpDmArmour Parse(string text)
	{
		LpDmArmour table = new LpDmArmour();
		string[] lines = text.Split('\n');
		for (int line = 0; line < lines.Length; line++)
		{
			string row = lines[line].TrimEnd('\r');
			if (row.Length == 0 || row.StartsWith("#", StringComparison.Ordinal) || row.StartsWith("Piece\t", StringComparison.Ordinal))
			{
				continue;
			}
			string[] f = row.Split('\t');
			if (f.Length < 12)
			{
				throw new FormatException("armour table line " + (line + 1) + " has " + f.Length + " of 12 fields");
			}
			Entry entry = new Entry
			{
				Piece = f[0].Trim(), Mode = f[1].Trim(), Ke = Number(f[2]), Ce = Number(f[3]),
				KeAfter = Number(f[4]), CeAfter = Number(f[5]), AngleMatters = f[6].Trim() == "1",
				Material = f[7].Trim(), Meshes = f[8].Trim().ToLowerInvariant().Split('|'),
				Name = f[9].Trim(), Replace = f[10].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), Source = f[11].Trim()
			};
			if (entry.Mode != "geometry" && entry.Mode != "composite" && entry.Mode != "drawn")
			{
				throw new FormatException("armour table line " + (line + 1) + " has unsupported mode " + entry.Mode);
			}
			table.Entries.Add(entry);
			foreach (string piece in entry.Piece.Split('|'))
			{
				table._byPiece[piece] = entry;
			}
		}
		return table;
	}

	private static float Number(string text)
	{
		return float.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
	}

	internal static string DefaultText => Data;
}

internal sealed class LpModuleSpec
{
	internal sealed class Hook
	{
		internal string Name;
		internal string Target;
	}

	internal sealed class Module
	{
		internal string Id;
		internal string Name;
		internal string Group;
		internal string Status;
		internal string Mesh;
		internal float Health;
		internal float Threshold;
		internal float Pressure;
		internal float Armour;
		internal string Rack;
		internal bool NoXray;
		internal readonly List<Hook> Hooks = new List<Hook>();
		internal readonly List<string> Replaces = new List<string>();
	}

	private const string Data =
		"id|name|group|mesh|health|threshold|pressure|armour|rack|hooks|replaces|status\n" +
		"engine|Leopard 2A7V engine|HULL|engine_xray|200|25|1|5|-|ChassisDamageManager.EngineHitZone|motor|Engine destroyed\n" +
		"transmission|Leopard 2A7V transmission|HULL|transmission_xray|160|25|1|5|-|ChassisDamageManager.TransmissionHitZone|transmission|Transmission destroyed\n" +
		"radiator|Leopard 2A7V radiator|HULL|radiator_xray|100|20|0.5|3|-|ChassisDamageManager.RadiatorHitZone|radiator|Radiator destroyed\n" +
		"breech|Leopard 2A7V cannon breech|GUN|cannon_breech_xray|120|25|1|5|-|WeaponSystem.BreechHitZone:main|gun breech|Main gun breech destroyed\n" +
		"barrel|Leopard 2A7V barrel|GUN|gun_barrel_01_xray|120|25|1|5|-|WeaponSystem.BarrelHitZone:main|gun barrel|Main gun barrel destroyed\n" +
		"traverse|Leopard 2A7V traverse drive|TURRET|drive_turret_h_xray|100|20|1|3|-|AimablePlatform.HitZones:turret|traverse drive|Turret traverse drive destroyed\n" +
		"elevation|Leopard 2A7V elevation drive|GUN|drive_turret_v_xray|100|20|1|3|-|AimablePlatform.HitZones:gun|elevation drive|Gun elevation drive destroyed\n" +
		"computer|Leopard 2A7V fire control computer|TURRET|fire_control_system_xray|80|20|0.5|2|-|FireControlSystem.ComputerComponent:main|ballistic computer control box|Ballistic computer destroyed\n" +
		"gunner_sight|Leopard 2A7V gunner sight|TURRET|optic_gun_xray|60|15|0.5|2|-|CameraSlot.HitZone:gunner_day;FireControlSystem.LaserComponent:main|gunsight head;gunsight lower|Gunner sight and laser destroyed\n" +
		"commander_sight|Leopard 2A7V commander panoramic sight|TURRET|commander_panoramic_sight_xray|60|15|0.5|2|-|CameraSlot.HitZone:commander_head;CameraSlot.HitZone:LpCitv|PERI computer;TC sight|Commander sight destroyed\n" +
		"ammo_hull|Leopard 2A7V hull ammunition rack|HULL|ammo_rack_body_xray|120|25|0.5|3|hull spare rack:Hull||front ammo rack|Hull ammunition rack destroyed\n" +
		"ammo_turret|Leopard 2A7V ready ammunition rack|TURRET|ammo_rack_turret_xray|100|25|0.5|3|ready rack:Turret||bustle rack inner;bustle rack outter|Ready ammunition rack destroyed\n";

	internal readonly List<Module> Modules = new List<Module>();

	internal static LpModuleSpec Load()
	{
		string text = null;
		try
		{
			string path = Path.Combine(LpConfig.AssetDir, "Architecture", "l2a7v_modules.tsv");
			if (File.Exists(path)) text = File.ReadAllText(path, Encoding.UTF8);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V][dm-modules] external table failed, embedded defaults retained: " + ex.Message);
		}
		return Parse(text ?? Data);
	}

	internal static LpModuleSpec Parse(string text)
	{
		LpModuleSpec spec = new LpModuleSpec();
		string[] lines = text.Split('\n');
		for (int line = 0; line < lines.Length; line++)
		{
			string row = lines[line].TrimEnd('\r');
			if (row.Length == 0 || row.StartsWith("#", StringComparison.Ordinal) || row.StartsWith("id|", StringComparison.Ordinal)) continue;
			string[] f = row.Split('|');
			if (f.Length < 12) throw new FormatException("module table line " + (line + 1) + " has " + f.Length + " of 12 fields");
			Module m = new Module
			{
				Id = f[0].Trim(), Name = f[1].Trim(), Group = f[2].Trim().ToUpperInvariant(), Mesh = f[3].Trim(),
				Health = Float(f[4]), Threshold = Float(f[5]), Pressure = Float(f[6]), Armour = Float(f[7]), Rack = f[8].Trim(),
				Status = f[11].Trim().Replace("\\n", "\n")
			};
			if (m.Group != "HULL" && m.Group != "TURRET" && m.Group != "GUN") throw new FormatException("module " + m.Id + " has invalid group " + m.Group);
			foreach (string hook in f[9].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				int colon = hook.IndexOf(':');
				m.Hooks.Add(new Hook { Name = colon < 0 ? hook : hook.Substring(0, colon), Target = colon < 0 ? null : hook.Substring(colon + 1) });
			}
			m.Replaces.AddRange(f[10].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
			spec.Modules.Add(m);
		}
		return spec;
	}

	private static float Float(string text) => float.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
	internal static string DefaultText => Data;
}

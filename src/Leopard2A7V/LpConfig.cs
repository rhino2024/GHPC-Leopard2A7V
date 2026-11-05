using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace Leopard2A7V;

public static class LpConfig
{
	public static MelonPreferences_Category Category;

	public static MelonPreferences_Entry<string> Skin;

	public static MelonPreferences_Entry<string> TextureResolution;

	public static MelonPreferences_Entry<float> ArmorCoefficient;

	public static MelonPreferences_Entry<string> Ammo;

	public const string UnitKey = "LEO2A7V";

	public const string VisualRootName = "Leopard2A7V_Visual";

	public const string FriendlyName = "Leopard 2A7V";

	public const string SourceUnitKey = "LEO2A3";

	public static readonly bool UseTemplate = false;

	public static readonly bool ConvertSourceUnit = false;

	public static readonly bool UseDonorRunningGear = true;

	public static readonly bool UseDonorTrackArt = true;

	public static readonly bool ShowInterior = false;

	// Active damage model: data-defined L2A7V armour and Interior.obj modules.
	// Legacy overlay / approximate retirement paths are retained only as source history.
	public static readonly bool DataDrivenArchitecture = true;
	public static readonly bool WtArmor = false;

	public static readonly bool OverlayArmor = true;

	public static readonly bool OverlayXrayArmor = true;

	public static readonly bool AddHeatSource = false;

	public static readonly bool ReplaceDayReticle = true;

	public static readonly bool AddRoofGun = true;

	public static readonly bool LimitMainGunElevation = true;

	public const float GunElevationDegrees = 20f;

	public const float GunDepressionDegrees = -9f;

	public const float RoofGunSightFov = 20f;

	public static readonly Vector2Int ThermalSize = new Vector2Int(1280, 640);

	public const float TrackTreadsPerMetre = 0.746f;

	public const int AlbedoLuminance = 0;

	public const int PaintMaskLevel = 255;

	public const int SmoothnessLevel = 63;

	public const bool NeutralMaps = true;

	public const bool UseDetailMaps = true;

	public static readonly Vector3 Offset = new Vector3(0f, 0f, 0.336f);

	public static readonly Vector3 TurretAxis = new Vector3(0f, 1.62f, 0.403f);

	public static readonly Vector3 GunAxis = new Vector3(0.003f, 1.969f, 1.369f);

	public static readonly Vector3 CommanderView = Vector3.zero;

	public static readonly Vector3 RoofGunYaw = new Vector3(-1.02f, 2.63f, -0.33f);

	public static readonly Vector3 RoofGunTrunnion = new Vector3(-0.96f, 2.8f, -0.3f);

	public static readonly Vector3 RoofGunMuzzle = new Vector3(-0.91f, 2.85f, 0.52f);

	public static readonly Vector3 RoofGunSight = new Vector3(-0.9f, 2.92f, -0.6f);

	public static readonly Vector3 RoofGunRackPoint = new Vector3(-1.05f, 2.83f, -0.25f);

	public static float ArmorScale
	{
		get
		{
			float num = ((ArmorCoefficient == null) ? 1f : ArmorCoefficient.Value);
			if (float.IsNaN(num) || num <= 0f)
			{
				return 1f;
			}
			return Mathf.Clamp(num, 0.3f, 3f);
		}
	}

	public static int MaxTextureSize
	{
		get
		{
			if (!UhdTextures)
			{
				return 2048;
			}
			return 4096;
		}
	}

	public static string AssetDir => Path.Combine(MelonEnvironment.ModsDirectory, "Leopard2A7V");

	public static string ResolutionName
	{
		get
		{
			string text = ((TextureResolution == null) ? null : TextureResolution.Value);
			if (string.IsNullOrEmpty(text))
			{
				return "2k";
			}
			switch (text.Trim().ToLowerInvariant())
			{
			default:
				return "2k";
			case "4k":
			case "uhq":
			case "4096":
				return "4k";
			}
		}
	}

	public static bool UhdTextures => ResolutionName == "4k";

	public static bool CarriesDm73 => AmmoChoice == "dm73";

	internal static string AmmoChoice => NormalisedChoice(Ammo, "dm73", "dm53", "dm73");

	public static string SkinName
	{
		get
		{
			string text = ((Skin == null) ? null : Skin.Value);
			if (string.IsNullOrEmpty(text))
			{
				return string.Empty;
			}
			text = text.Trim().ToLowerInvariant();
			switch (text)
			{
			case "factory":
			case "default":
			case "none":
			case "stock":
			case "bronzegruen":
				return string.Empty;
			default:
				return text;
			}
		}
	}

	public static Vector3 FromExport(Vector3 export)
	{
		return new Vector3(export.z, export.y, export.x);
	}

	internal static string NormalisedChoice(MelonPreferences_Entry<string> entry, string fallback, params string[] allowed)
	{
		string text = ((entry == null) ? null : (entry.Value ?? string.Empty).Trim().ToLowerInvariant());
		if (string.IsNullOrEmpty(text))
		{
			return fallback;
		}
		foreach (string text2 in allowed)
		{
			if (text == text2)
			{
				return text2;
			}
		}
		return fallback;
	}

	public static void Create()
	{
		Category = MelonPreferences.CreateCategory("Leopard2A7V", "Leopard 2A7V tank model swap");
		Skin = Category.CreateEntry("Skin", "", "Paint scheme: factory, nato, desert, winter, parade");
		TextureResolution = Category.CreateEntry("TextureResolution", "2k", "Texture detail: 2k or 4k. 4k is sharper up close but needs far more video memory and load time");
		ArmorCoefficient = Category.CreateEntry("ArmorCoefficient", 1f, "Armour strength multiplier over the L2A7V candidate values (1 = unvalidated authoring baseline, 0.3-3; not measured real-world protection)");
		Ammo = Category.CreateEntry("Ammo", "dm73", "APFSDS round: dm53 (1750 m/s) or dm73 (1780 m/s)");
		MelonPreferences.Save();
	}
}

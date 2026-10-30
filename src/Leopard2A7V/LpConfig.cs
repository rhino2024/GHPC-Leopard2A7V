using System.Globalization;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    public static class LpConfig
    {
        public static MelonPreferences_Category Category;

        public static MelonPreferences_Entry<bool> Enabled;
        public static MelonPreferences_Entry<bool> ConvertSourceUnit;
        public static MelonPreferences_Entry<bool> UseDonorRunningGear;
        public static MelonPreferences_Entry<bool> UseDonorTrackArt;
        public static MelonPreferences_Entry<float> TrackTreadsPerMetre;
        public static MelonPreferences_Entry<bool> ApplyArmor;
        public static MelonPreferences_Entry<bool> LogArmor;
        public static MelonPreferences_Entry<string> SourceUnitKey;
        public static MelonPreferences_Entry<string> FriendlyName;
        public static MelonPreferences_Entry<string> VisualOffset;
        public static MelonPreferences_Entry<string> ModelTurretAxis;
        public static MelonPreferences_Entry<string> ModelGunAxis;
        public static MelonPreferences_Entry<string> CommanderViewOffset;
        public static MelonPreferences_Entry<int> MaxTextureSize;
        public static MelonPreferences_Entry<int> AlbedoLuminance;
        public static MelonPreferences_Entry<int> PaintMaskLevel;
        public static MelonPreferences_Entry<int> SmoothnessLevel;
        public static MelonPreferences_Entry<bool> NeutralMaps;
        public static MelonPreferences_Entry<bool> UseDetailMaps;
        public static MelonPreferences_Entry<bool> AddHeatSource;
        public static MelonPreferences_Entry<bool> ReplaceDayReticle;
        public static MelonPreferences_Entry<bool> LimitMainGunElevation;
        public static MelonPreferences_Entry<float> GunElevationDegrees;
        public static MelonPreferences_Entry<float> GunDepressionDegrees;
        public static MelonPreferences_Entry<bool> AddRoofGun;
        public static MelonPreferences_Entry<string> RoofGunYawPoint;
        public static MelonPreferences_Entry<string> RoofGunTrunnionPoint;
        public static MelonPreferences_Entry<string> RoofGunMuzzlePoint;
        public static MelonPreferences_Entry<string> RoofGunSightPoint;
        public static MelonPreferences_Entry<string> RoofGunRack;
        public static MelonPreferences_Entry<float> RoofGunSightFov;
        public static MelonPreferences_Entry<bool> DumpMaterials;

        public const string UnitKey = "LEO2A7V";
        public const string VisualRootName = "Leopard2A7V_Visual";

        public static string AssetDir =>
            System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.ModsDirectory, "Leopard2A7V");

        public static void Create()
        {
            Category = MelonPreferences.CreateCategory("Leopard2A7V", "Leopard 2A7V tank model swap");

            Enabled = Category.CreateEntry("Enabled", true, "Turns the mod off without uninstalling it");
            SourceUnitKey = Category.CreateEntry("SourceUnitKey", "LEO2A3",
                "Vanilla unit the 2A7V is cloned from; LEO2A3 is the only one that works");
            FriendlyName = Category.CreateEntry("FriendlyName", "Leopard 2A7V", "Name this vehicle shows in the menus");
            ConvertSourceUnit = Category.CreateEntry("ConvertSourceUnit", false,
                "Also restyle the stock LEO2A3s, not just the new entry");
            UseDonorRunningGear = Category.CreateEntry("UseDonorRunningGear", false,
                "Keep the donor's wheels and tracks. Off builds the model's own road wheels on the donor bones; the return rollers and arms still move with the hull");
            UseDonorTrackArt = Category.CreateEntry("UseDonorTrackArt", true,
                "Keep the donor's own track loops. Only this setting shows a complete, rolling track, because the model's track sheet cannot scroll");
            TrackTreadsPerMetre = Category.CreateEntry("TrackTreadsPerMetre", 0.746f,
                "Tread repeats per metre when the model's own track is used; ignored while the donor track art is on");
            ApplyArmor = Category.CreateEntry("ApplyArmor", false,
                "Overwrite the protection values. Leave this off: the donor's layered armour is more accurate than any flat numbers this mod can set");
            LogArmor = Category.CreateEntry("LogArmor", true, "Print every armour layer to the log at load");
            VisualOffset = Category.CreateEntry("VisualOffset", "0,0,0.336",
                "Whole vehicle offset in metres in the game's unit frame (+X right, +Y up, +Z forward)");
            ModelTurretAxis = Category.CreateEntry("ModelTurretAxis", "0,1.620,0.403",
                "Turret traverse axis in model space, read from the model's bone table");
            ModelGunAxis = Category.CreateEntry("ModelGunAxis", "0.003,1.969,1.369",
                "Gun elevation axis in model space; its x is what keeps the mantlet sitting in the turret window");
            CommanderViewOffset = Category.CreateEntry("CommanderViewOffset", "0,0,0",
                "Nudges the commander's camera in unit-frame metres; only needed if the cupola clips his view");
            MaxTextureSize = Category.CreateEntry("MaxTextureSize", 2048,
                "Largest texture size loaded from disk; drop to 1024 to save memory");
            AlbedoLuminance = Category.CreateEntry("AlbedoLuminance", 0,
                "Pulls the imported albedos toward this mean grey; 0 keeps them as authored");
            PaintMaskLevel = Category.CreateEntry("PaintMaskLevel", 255,
                "Red channel of the neutral paint mask, 0-255; 255 reads as fully painted, 0 as bare metal");
            SmoothnessLevel = Category.CreateEntry("SmoothnessLevel", 63, "Base gloss 0-255 for materials without a gloss map");
            NeutralMaps = Category.CreateEntry("NeutralMaps", true,
                "Use flat neutral maps instead of the donor's paint mask and gloss; off keeps the donor's own UV-baked ones");
            UseDetailMaps = Category.CreateEntry("UseDetailMaps", true,
                "Bind the model's own ambient-occlusion bakes, which is what puts shading into panel seams and hatches");
            AddHeatSource = Category.CreateEntry("AddHeatSource", false, "Give the vehicle a heat signature source");
            ReplaceDayReticle = Category.CreateEntry("ReplaceDayReticle", true,
                "Draw the 2A7V graticule in the gunner's daylight sight; the fire control still moves the aiming mark");
            LimitMainGunElevation = Category.CreateEntry("LimitMainGunElevation", true,
                "Apply the gun arc below. GHPC treats an equal pair of limits as unlimited travel, so this is the switch that actually stops the barrel");
            GunElevationDegrees = Category.CreateEntry("GunElevationDegrees", 20f,
                "Highest elevation above horizontal, in degrees");
            GunDepressionDegrees = Category.CreateEntry("GunDepressionDegrees", -9f,
                "Lowest depression below horizontal, in degrees; negative");
            AddRoofGun = Category.CreateEntry("AddRoofGun", true,
                "Make the roof MG a real weapon with its own mount, fire control and sight; off leaves it as decoration");
            RoofGunYawPoint = Category.CreateEntry("RoofGunYawPoint", "-1.02,2.63,-0.33",
                "Traverse axis of the roof mount, in the same model space as the turret axis");
            RoofGunTrunnionPoint = Category.CreateEntry("RoofGunTrunnion", "-0.96,2.80,-0.30",
                "Elevation axis of the roof MG, in model space");
            RoofGunMuzzlePoint = Category.CreateEntry("RoofGunMuzzle", "-0.91,2.85,0.52",
                "Muzzle of the roof MG, where rounds and the flash spawn");
            RoofGunSightPoint = Category.CreateEntry("RoofGunSight", "-0.90,2.92,-0.60",
                "Eyepiece of the roof MG sight, in model space");
            RoofGunRack = Category.CreateEntry("RoofGunRack", "-1.05,2.83,-0.25", "Belt box of the roof MG, in model space");
            RoofGunSightFov = Category.CreateEntry("RoofGunSightFov", 20f,
                "Horizontal field of view of the roof MG sight, in degrees; wider tracks aircraft more easily");
            DumpMaterials = Category.CreateEntry("DumpMaterials", true,
                "Print the full material and renderer state once after the vehicle is built");

            MelonPreferences.Save();
        }

        /// <summary>Model space placement of the whole art relative to the unit frame.</summary>
        public static Vector3 Offset => ParseVector3(VisualOffset.Value, new Vector3(0, 0, 0.336f));

        /// <summary>Model space turret traverse axis, which the donor's TURRET bone is moved onto.</summary>
        public static Vector3 TurretAxis => ParseVector3(ModelTurretAxis.Value, new Vector3(0, 1.620f, 0.403f));

        /// <summary>Model space gun elevation axis, which the donor's GUN bone is moved onto.</summary>
        public static Vector3 GunAxis => ParseVector3(ModelGunAxis.Value, new Vector3(0.003f, 1.969f, 1.369f));

        /// <summary>Unit frame nudge for the commander's camera slot.</summary>
        public static Vector3 CommanderView => ParseVector3(CommanderViewOffset.Value, Vector3.zero);

        /// <summary>Model space traverse axis of the commander's machine gun mount.</summary>
        public static Vector3 RoofGunYaw => ParseVector3(RoofGunYawPoint.Value, new Vector3(-1.02f, 2.63f, -0.33f));

        /// <summary>Model space elevation axis of the commander's machine gun.</summary>
        public static Vector3 RoofGunTrunnion => ParseVector3(RoofGunTrunnionPoint.Value, new Vector3(-0.96f, 2.80f, -0.30f));

        /// <summary>Model space muzzle of the commander's machine gun.</summary>
        public static Vector3 RoofGunMuzzle => ParseVector3(RoofGunMuzzlePoint.Value, new Vector3(-0.91f, 2.85f, 0.52f));

        /// <summary>Model space eyepiece of the commander's machine gun sight.</summary>
        public static Vector3 RoofGunSight => ParseVector3(RoofGunSightPoint.Value, new Vector3(-0.90f, 2.92f, -0.60f));

        /// <summary>Model space belt box of the commander's machine gun.</summary>
        public static Vector3 RoofGunRackPoint => ParseVector3(RoofGunRack.Value, new Vector3(-1.05f, 2.83f, -0.25f));

        public static Vector3 ParseVector3(string value, Vector3 fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            string[] parts = value.Split(',');
            if (parts.Length != 3) return fallback;

            var components = new float[3];
            for (int i = 0; i < 3; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out components[i]))
                    return fallback;
            }
            return new Vector3(components[0], components[1], components[2]);
        }
    }
}

using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// What the player can switch on and off. Everything else this vehicle needs is a measured fact about the
    /// imported model - where its turret ring is, where the roof MG's muzzle is, how the paint maps are baked -
    /// and those belong in the source beside the measurement that produced them, not in a config file where a
    /// stray edit silently misaligns the art.
    /// </summary>
    public static class LpConfig
    {
        public static MelonPreferences_Category Category;

        public static MelonPreferences_Entry<bool> Enabled;
        public static MelonPreferences_Entry<bool> ConvertSourceUnit;
        public static MelonPreferences_Entry<bool> UseDonorRunningGear;
        public static MelonPreferences_Entry<bool> UseDonorTrackArt;
        public static MelonPreferences_Entry<bool> ShowInterior;
        public static MelonPreferences_Entry<bool> WtArmor;
        public static MelonPreferences_Entry<string> FriendlyName;
        public static MelonPreferences_Entry<string> Skin;
        public static MelonPreferences_Entry<bool> AddHeatSource;
        public static MelonPreferences_Entry<bool> ReplaceDayReticle;
        public static MelonPreferences_Entry<string> ThermalResolution;
        public static MelonPreferences_Entry<bool> LimitMainGunElevation;
        public static MelonPreferences_Entry<float> GunElevationDegrees;
        public static MelonPreferences_Entry<float> GunDepressionDegrees;
        public static MelonPreferences_Entry<bool> AddRoofGun;
        public static MelonPreferences_Entry<float> RoofGunSightFov;

        public const string UnitKey = "LEO2A7V";
        public const string VisualRootName = "Leopard2A7V_Visual";

        /// <summary>
        /// The vanilla unit this vehicle is cloned from. Its prefab, rig, armour types and sight plumbing are
        /// what the game authored against, and LEO2A3 is the only donor whose gunner daylight channel is a
        /// periscope the 2A7V's own optic can be fitted into.
        /// </summary>
        public const string SourceUnitKey = "LEO2A3";

        /// <summary>
        /// Tread repeats per metre for the model's own track sheet, which only matters while
        /// UseDonorTrackArt is off: lp_track_uv.py reads the exported loop's v against forward as one period
        /// per 0.746 m.
        /// </summary>
        public const float TrackTreadsPerMetre = 0.746f;

        /// <summary>Largest texture loaded from disk; the imported set and the donor's both ship at 2048.</summary>
        public const int MaxTextureSize = 2048;

        /// <summary>
        /// Target mean grey for the imported albedos. 0 keeps them as authored, which is right because
        /// lp_texpass.py already re-centred them on the donor's own means during the conversion.
        /// </summary>
        public const int AlbedoLuminance = 0;

        /// <summary>Red channel of the neutral paint mask: 255 reads as fully painted, 0 as bare metal.</summary>
        public const int PaintMaskLevel = 255;

        /// <summary>Base gloss for material groups that ship with no gloss bake; the donor's painted armour measures 94.</summary>
        public const int SmoothnessLevel = 63;

        /// <summary>
        /// Use flat neutral mask and gloss maps rather than the donor's, which are UV-baked for its layout.
        /// </summary>
        public const bool NeutralMaps = true;

        /// <summary>Bind the model's own ambient-occlusion bakes, which is what shades panel seams and hatches.</summary>
        public const bool UseDetailMaps = true;

        /// <summary>Whole-vehicle placement of the art in the game's unit frame, measured against the donor's ground contact.</summary>
        public static readonly Vector3 Offset = new Vector3(0f, 0f, 0.336f);

        /// <summary>Turret traverse axis in model space, read from the model's bone table.</summary>
        public static readonly Vector3 TurretAxis = new Vector3(0f, 1.620f, 0.403f);

        /// <summary>
        /// Gun elevation axis in model space; its x is what keeps the mantlet sitting in the turret window.
        /// </summary>
        public static readonly Vector3 GunAxis = new Vector3(0.003f, 1.969f, 1.369f);

        /// <summary>Unit frame nudge for the commander's camera, on top of the cupola point the slot is moved to.</summary>
        public static readonly Vector3 CommanderView = Vector3.zero;

        /// <summary>Model space traverse axis of the commander's machine gun mount, from the imported mg_aa_mount_h box.</summary>
        public static readonly Vector3 RoofGunYaw = new Vector3(-1.02f, 2.63f, -0.33f);

        /// <summary>Model space elevation axis of the commander's machine gun, on its bore line.</summary>
        public static readonly Vector3 RoofGunTrunnion = new Vector3(-0.96f, 2.80f, -0.30f);

        /// <summary>Model space muzzle of the commander's machine gun, where rounds and the flash spawn.</summary>
        public static readonly Vector3 RoofGunMuzzle = new Vector3(-0.91f, 2.85f, 0.52f);

        /// <summary>Model space eyepiece of the commander's machine gun sight.</summary>
        public static readonly Vector3 RoofGunSight = new Vector3(-0.90f, 2.92f, -0.60f);

        /// <summary>Model space belt box of the commander's machine gun.</summary>
        public static readonly Vector3 RoofGunRackPoint = new Vector3(-1.05f, 2.83f, -0.25f);

        public static string AssetDir =>
            System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.ModsDirectory, "Leopard2A7V");

        public static void Create()
        {
            Category = MelonPreferences.CreateCategory("Leopard2A7V", "Leopard 2A7V tank model swap");

            Enabled = Category.CreateEntry("Enabled", true, "Turns the mod off without uninstalling it");
            FriendlyName = Category.CreateEntry("FriendlyName", "Leopard 2A7V", "Name this vehicle shows in the menus");
            ConvertSourceUnit = Category.CreateEntry("ConvertSourceUnit", false,
                "Also restyle the stock LEO2A3s, not just the new entry");
            UseDonorRunningGear = Category.CreateEntry("UseDonorRunningGear", false,
                "Keep the donor's wheels and tracks. Off builds the model's own road wheels on the donor bones");
            UseDonorTrackArt = Category.CreateEntry("UseDonorTrackArt", true,
                "Keep the donor's track loops. Only this shows a complete, rolling track");

            // The imported compartment is the War Thunder x-ray interior (Interior.obj), parsed by LpInterior
            // and hung on the art's own pivots with the same anchors the exterior groups use, so the engine
            // sits behind the firewall and the carousel under the turret floor without a second offset.
            ShowInterior = Category.CreateEntry("ShowInterior", true,
                "Draw the crew compartment inside the hull. It follows the game's own x-ray view");

            // Composite depths come from that x-ray model's armour modules, scaled by the donor's own
            // material factor; off keeps the LEO2A3's measured armour. LpArmorLog reports every layer at load.
            WtArmor = Category.CreateEntry("WtArmor", true,
                "Set the armour thickness from this model's geometry instead of the donor's");

            Skin = Category.CreateEntry("Skin", "",
                "Paint scheme: nato, desert, winter, parade. Empty keeps the factory scheme");
            AddHeatSource = Category.CreateEntry("AddHeatSource", false, "Give the vehicle a heat signature source");
            ReplaceDayReticle = Category.CreateEntry("ReplaceDayReticle", true,
                "Draw the 2A7V graticule in the gunner's daylight sight");

            // The size the gunner thermal renders at. FLIRCamera draws into a target of exactly this size and
            // stretches it over the screen, and the vertical field is derived from the target's aspect, so keep
            // the donor's 2:1 ratio. The donor authors 480x240, which is coarse at full screen.
            ThermalResolution = Category.CreateEntry("ThermalResolution", "1280,640",
                "The gunner's thermal picture as \"width,height\" in pixels; keep the ratio 2:1");
            LimitMainGunElevation = Category.CreateEntry("LimitMainGunElevation", true,
                "Apply the gun arc below; this is the switch that actually stops the barrel");
            GunElevationDegrees = Category.CreateEntry("GunElevationDegrees", 20f,
                "Highest elevation above horizontal, in degrees");
            GunDepressionDegrees = Category.CreateEntry("GunDepressionDegrees", -9f,
                "Lowest depression below horizontal, in degrees; negative");
            AddRoofGun = Category.CreateEntry("AddRoofGun", true,
                "Make the roof MG a real weapon with its own mount, fire control and sight");
            RoofGunSightFov = Category.CreateEntry("RoofGunSightFov", 20f,
                "Horizontal field of view of the roof MG sight, in degrees; wider tracks aircraft more easily");

            MelonPreferences.Save();
        }

        /// <summary>Selected paint scheme as a folder name, or empty for the factory scheme.</summary>
        public static string SkinName
        {
            get
            {
                string value = Skin == null ? null : Skin.Value;
                return string.IsNullOrEmpty(value) ? string.Empty : value.Trim().ToLowerInvariant();
            }
        }

        /// <summary>
        /// The gunner thermal's render target size. A blank or unusable entry falls back to the donor's own
        /// 480 x 240, which is what the game authors its imagers at.
        /// </summary>
        public static Vector2Int ThermalSize => ParseResolution(ThermalResolution.Value, new Vector2Int(480, 240));

        public static Vector2Int ParseResolution(string value, Vector2Int fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            string[] parts = value.Split(',');
            if (parts.Length != 2) return fallback;
            if (!int.TryParse(parts[0].Trim(), out int width) || !int.TryParse(parts[1].Trim(), out int height))
                return fallback;
            return new Vector2Int(Mathf.Clamp(width, 32, 3840), Mathf.Clamp(height, 32, 2160));
        }
    }
}

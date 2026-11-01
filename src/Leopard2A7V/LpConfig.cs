using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// What the player chooses is the paint scheme the vehicle wears, how much texture detail to load, and one
    /// balance dial over the protection; everything else this vehicle needs is a measured fact about the
    /// imported model - where its turret ring is, where the roof MG's muzzle is, how the paint maps are baked,
    /// which of the donor's parts to keep - and those belong in the source beside the measurement that
    /// produced them. A config file of thirty switches invites a stray edit that silently misaligns the art or
    /// strips the interior, so only Skin, TextureResolution and ArmorCoefficient are written to
    /// MelonPreferences and the rest are the values this vehicle is authored and tested at.
    /// </summary>
    public static class LpConfig
    {
        public static MelonPreferences_Category Category;

        public static MelonPreferences_Entry<string> Skin;

        public static MelonPreferences_Entry<string> TextureResolution;

        /// <summary>
        /// Scale over every RHAe figure LpArmor writes, for players who want the vehicle softer or tougher than
        /// the real-data table. 1 keeps the published protection; the value is clamped so a typo cannot make the
        /// vehicle invulnerable or unarmoured.
        /// </summary>
        public static MelonPreferences_Entry<float> ArmorCoefficient;

        public static float ArmorScale
        {
            get
            {
                float value = ArmorCoefficient == null ? 1f : ArmorCoefficient.Value;
                if (float.IsNaN(value) || value <= 0f) return 1f;
                return Mathf.Clamp(value, 0.3f, 3f);
            }
        }

        public const string UnitKey = "LEO2A7V";
        public const string VisualRootName = "Leopard2A7V_Visual";

        /// <summary>Name this vehicle shows in the menus.</summary>
        public const string FriendlyName = "Leopard 2A7V";

        /// <summary>
        /// The vanilla unit this vehicle is cloned from. Its prefab, rig, armour types and sight plumbing are
        /// what the game authored against, and LEO2A3 is the only donor whose gunner daylight channel is a
        /// periscope the 2A7V's own optic can be fitted into.
        /// </summary>
        public const string SourceUnitKey = "LEO2A3";

        /// <summary>
        /// Off: only this mod's own entry is restyled and the stock LEO2A3s stay as the game made them, so a
        /// scenario can field both.
        /// </summary>
        public static readonly bool ConvertSourceUnit = false;

        /// <summary>
        /// Off builds this model's own road wheels, idlers and sprockets on the donor's suspension bones, which
        /// is the geometry the exported hull actually has.
        /// </summary>
        public static readonly bool UseDonorRunningGear = false;

        /// <summary>
        /// On keeps the donor's track loops. The 2A7V's own exported loop is authored too short to reach
        /// around its hull, so this is the only setting that shows a complete, rolling track.
        /// </summary>
        public static readonly bool UseDonorTrackArt = true;

        /// <summary>
        /// Draw the crew compartment inside the hull. The imported piece is the War Thunder x-ray interior
        /// (Interior.obj), parsed by LpInterior and hung on the art's own pivots with the same anchors the
        /// exterior groups use, so the engine sits behind the firewall and the carousel under the turret floor
        /// without a second offset. It follows the game's own x-ray view.
        /// </summary>
        public static readonly bool ShowInterior = true;

        /// <summary>
        /// Set the protection from this model's geometry rather than the LEO2A3's: composite depths come from
        /// that x-ray model's armour modules, scaled by the donor's own material factor. LpArmorLog reports
        /// every layer at load.
        /// </summary>
        public static readonly bool WtArmor = true;

        /// <summary>
        /// Attach a heat signature source to the imported visual. Off because the donor's own source stays on
        /// the unit, and two of them read as a brighter target than the 2A7V is.
        /// </summary>
        public static readonly bool AddHeatSource = false;

        /// <summary>Draw the 2A7V's own graticule in the gunner's daylight sight instead of the donor's.</summary>
        public static readonly bool ReplaceDayReticle = true;

        /// <summary>
        /// Make the roof MG a real weapon with its own mount, fire control and sight rather than decoration.
        /// </summary>
        public static readonly bool AddRoofGun = true;

        /// <summary>
        /// Apply the gun arc below; this is the switch that actually stops the barrel. GHPC clamps with
        /// AimablePlatform.LocalEulerLimits and treats x == y, a pair of zeros included, as unlimited travel,
        /// so re-stating the arc is what guarantees the barrel cannot run past the mount.
        /// </summary>
        public static readonly bool LimitMainGunElevation = true;

        /// <summary>Highest elevation above horizontal, in degrees - the 2A7V's arc as War Thunder models it.</summary>
        public const float GunElevationDegrees = 20f;

        /// <summary>Lowest depression below horizontal, in degrees; negative.</summary>
        public const float GunDepressionDegrees = -9f;

        /// <summary>
        /// Horizontal field of view of the roof MG sight, in degrees. The sight has no zoom, so this one figure
        /// sets both the picture and how the graticule's width is scaled.
        /// </summary>
        public const float RoofGunSightFov = 20f;

        /// <summary>
        /// The gunner thermal's render target size. FLIRCamera draws into a target of exactly this size and
        /// stretches it over the screen, and the vertical field is derived from the target's aspect, so the
        /// donor's 2:1 ratio is kept. The donor authors 480 x 240, which is coarse at full screen.
        /// </summary>
        public static readonly Vector2Int ThermalSize = new Vector2Int(1280, 640);

        /// <summary>
        /// Tread repeats per metre for the model's own track sheet, which only matters while
        /// UseDonorTrackArt is off: lp_track_uv.py reads the exported loop's v against forward as one period
        /// per 0.746 m.
        /// </summary>
        public const float TrackTreadsPerMetre = 0.746f;

        /// <summary>
        /// Largest texture loaded from disk. The 2K kit - what ships in textures/ - is 2048; the 4K kit in
        /// textures_uhq/ is 4096 square, which is also Unity's ceiling here, so the switch is the kit and the cap
        /// together. A 4K sheet is 64 MB decoded RGBA32 before mips, about sixteen times the 2K one's footprint,
        /// which is why 2K stays the default.
        /// </summary>
        public static int MaxTextureSize => UhdTextures ? 4096 : 2048;

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

        /// <summary>
        /// "2k" or "4k", whatever the player typed: anything that is not plainly the 4K kit falls back to the 2K
        /// one, so a typo costs detail instead of a magenta vehicle.
        /// </summary>
        public static string ResolutionName
        {
            get
            {
                string value = TextureResolution == null ? null : TextureResolution.Value;
                if (string.IsNullOrEmpty(value)) return "2k";
                value = value.Trim().ToLowerInvariant();
                return value == "4k" || value == "uhq" || value == "4096" ? "4k" : "2k";
            }
        }

        /// <summary>True when the 4K kit under textures_uhq/ is the one to bind.</summary>
        public static bool UhdTextures => ResolutionName == "4k";

        public static void Create()
        {
            Category = MelonPreferences.CreateCategory("Leopard2A7V", "Leopard 2A7V tank model swap");

            Skin = Category.CreateEntry("Skin", "",
                "Paint scheme: factory, nato, desert, winter, parade");

            TextureResolution = Category.CreateEntry("TextureResolution", "2k",
                "Texture detail: 2k or 4k. 4k is sharper up close but needs far more video memory and load time");

            ArmorCoefficient = Category.CreateEntry("ArmorCoefficient", 1.2f,
                "Armour strength multiplier over every zone (1 = real-data protection, 0.3-3)");

            MelonPreferences.Save();
        }

        /// <summary>
        /// Selected paint scheme as a folder name, or empty for the factory scheme. The manager offers the schemes
        /// as a list, so "factory" is accepted as the word for "no scheme folder" rather than making the player
        /// leave the field empty.
        /// </summary>
        public static string SkinName
        {
            get
            {
                string value = Skin == null ? null : Skin.Value;
                if (string.IsNullOrEmpty(value)) return string.Empty;
                value = value.Trim().ToLowerInvariant();
                if (value == "factory" || value == "default" || value == "none" || value == "stock" ||
                    value == "bronzegruen")
                    return string.Empty;
                return value;
            }
        }
    }
}

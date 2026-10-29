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

            Enabled = Category.CreateEntry("Enabled", true, "Mod enabled");
            SourceUnitKey = Category.CreateEntry("SourceUnitKey", "LEO2A3",
                "Vanilla unit key whose prefab, ammo and rig are reused");
            FriendlyName = Category.CreateEntry("FriendlyName", "Leopard 2A7V",
                "Name shown in the in-game UI");
            ConvertSourceUnit = Category.CreateEntry("ConvertSourceUnit", false,
                "Also restyle plain LEO2A3 spawns (useful when the new entry is missing from the UI)");
            UseDonorRunningGear = Category.CreateEntry("UseDonorRunningGear", false,
                "Keep the donor's own wheels and track art. Off builds the imported road wheels, idlers and " +
                "drive sprockets as separate parts and rides each one on the donor's matching bone, so they " +
                "turn with the suspension; the return rollers and arms travel with the hull, which is what " +
                "the donor's own skin does with them, and the loops are the other switch's business");
            UseDonorTrackArt = Category.CreateEntry("UseDonorTrackArt", true,
                "Keep the donor's own track loops and leave the imported ones out. This is the default and the " +
                "only setting that actually shows a complete, moving tread: the donor's LEO2_tracks loops are " +
                "skinned to the track bones the physics turns and UV'd face by face into its D570P sheet, which " +
                "measures as one link's ground face split into about ten u-columns in no particular order " +
                "across the width. That mapping belongs to the donor's own triangles - re-UV any other mesh onto " +
                "the same sheet and each face stretches between columns of unrelated parts, which is the flat " +
                "brown-black band this mod used to show. On is also the only way to keep tracks at all, because " +
                "the imported War Thunder sheet cannot scroll either: its ground faces scatter over u " +
                "0.014-1.000 and v -4.77..4.23 with no repeat direction whatsoever. Off is a dead end, kept " +
                "only as a knob for whoever bakes a genuine repeating tread strip first");
            TrackTreadsPerMetre = Category.CreateEntry("TrackTreadsPerMetre", 0.746f,
                "Only read when UseDonorTrackArt is off, which is the only case where this mod lays out a sheet " +
                "of its own. Repeats of the tread pattern per metre of run; 0.746 is the donor's own measured " +
                "gradient along the bottom run, where eight pads of its 64-pixel pattern period cross one " +
                "metre at a link pitch of about 0.18 m");
            ApplyArmor = Category.CreateEntry("ApplyArmor", false,
                "Override the protection values. Off keeps the donor's layout, which is what should be " +
                "used here: the LEO2A3 models its composite armor as raycast thickness arrays (upper " +
                "glacis array, turret cheek composite, lower glacis array) rather than as flat RHA " +
                "numbers, so overwriting the few uniform plates that do carry values would only make the " +
                "vehicle less accurate than the vanilla tank");
            LogArmor = Category.CreateEntry("LogArmor", true, "Print every armor layer to the log");
            VisualOffset = Category.CreateEntry("VisualOffset", "0,0,0.336",
                "Model space offset for the whole vehicle in meters, in the GHPC unit frame (+X starboard, " +
                "+Y up, +Z forward). The 2.7 cm of the imported art's own frame that this leaves over is " +
                "nothing: the value is the least-squares fit of the seven imported road-wheel centres onto " +
                "the donor's, which agree to 0.27-0.39 m with a mean of 0.336 m");
            ModelTurretAxis = Category.CreateEntry("ModelTurretAxis", "0,1.620,0.403",
                "Turret traverse axis in model space, read off the model's own bone table (bone_turret). " +
                "The imported turret is hung on the rig bone that actually turns, leo2_rig/hull/turret, and " +
                "this point is placed exactly on it, so the turret spins in place instead of orbiting the " +
                "ring. The donor's ring is 7.7 cm forward of and 3.2 cm above the model's own, which this " +
                "correction absorbs at the hidden ring joint rather than in the visible hull");
            ModelGunAxis = Category.CreateEntry("ModelGunAxis", "0.003,1.969,1.369",
                "Gun elevation axis in model space. Ring to trunnion is 0.966 m in the model against 0.964 m " +
                "between the donor's leo2_rig/hull/turret and its mantlet bone, so both land on their donor " +
                "counterparts with the same shift. The lateral figure is not the model's own gun centreline: " +
                "those two bones differ by 0.0032 m in x and nothing else, and because elevation turns about " +
                "X, an anchor's x only ever slides the gun sideways - so the two anchors may differ by that " +
                "3.2 mm and no more, which is what keeps the source art's own mantlet-in-window fit. The " +
                "model's barrel axis is itself 0.024 m to port of its turret's symmetry plane; centring the " +
                "barrel on the donor's axis instead would correct War Thunder's art and open a gap on the " +
                "other side");
            CommanderViewOffset = Category.CreateEntry("CommanderViewOffset", "0,0,0",
                "Moves the commander's camera slot in unit frame meters, where +X is starboard and +Z is " +
                "forward. The donor puts him at (0.64, 2.88, 0.20) while this model's cupola sight occupies " +
                "(0.05-0.50, 2.59-2.98, -0.56--0.10), i.e. to port of and behind him, so 0,0,0 is fine. " +
                "Raise it if the PERI housing still clips his view");
            MaxTextureSize = Category.CreateEntry("MaxTextureSize", 2048,
                "Largest texture size loaded from disk. The imported body, turret and gun maps are 2048 " +
                "and the occlusion bakes 512; drop to 1024 if the extra memory is not worth it");
            AlbedoLuminance = Category.CreateEntry("AlbedoLuminance", 0,
                "Target mean colour of the imported albedos, applied at load. Leave at 0 (keep as authored): " +
                "lp_texpass.py already re-centres the painted maps on the donor LEO2_co means of 42/42/35, " +
                "which every vanilla vehicle map sits inside. Set it if you drop a raw War Thunder map in " +
                "without running the pass");
            PaintMaskLevel = Category.CreateEntry("PaintMaskLevel", 255,
                "Red 0-255 of the neutral paint mask, where 255 reads as fully painted. 0 makes the hull read as bare metal");
            SmoothnessLevel = Category.CreateEntry("SmoothnessLevel", 63,
                "Base gloss 0-255 for the materials without a map_ks bake. The donor's LEO2_sm alpha measures " +
                "63, and the painted body, turret, gun and MG mount now carry their own gloss maps");
            NeutralMaps = Category.CreateEntry("NeutralMaps", true,
                "Replace the vanilla paint mask slot with a flat neutral texture, and the gloss slot too. " +
                "Off keeps the donor's own maps, which are UV-baked for its layout");
            UseDetailMaps = Category.CreateEntry("UseDetailMaps", true,
                "Bind the model's own ambient occlusion bakes. This is what puts the shading in panel seams, " +
                "bolts and hatches; off leaves the paint flatly lit however close the camera gets");
            AddHeatSource = Category.CreateEntry("AddHeatSource", false,
                "Attach a heat signature source to the imported visual");
            ReplaceDayReticle = Category.CreateEntry("ReplaceDayReticle", true,
                "Rebuild the gunner's daylight reticle as the Leopard 2A7V plate. The donor's FERO Z18 " +
                "picture is kept as the light source and as the VerticalRange wiring, so the FCS still moves " +
                "the aiming mark with the superelevation of the loaded round");
            LimitMainGunElevation = Category.CreateEntry("LimitMainGunElevation", true,
                "Re-state the gun arc on the platform that poses the mantlet. GHPC clamps with " +
                "AimablePlatform.LocalEulerLimits and treats x == y - a pair of zeros included - as unlimited " +
                "travel, so this is what guarantees the barrel cannot run past the mount however the donor " +
                "arrived");
            GunElevationDegrees = Category.CreateEntry("GunElevationDegrees", 20f,
                "Highest angle above horizontal the gun may reach. +20 degrees is what War Thunder's page for " +
                "the 2A7V writes on its 120 mm Rh120 L/55 A1 block ('Vertical guidance -9 / 20'), which is the " +
                "figure this mod follows. The donor LEO2A3 prefab happens to carry the same +20, but the " +
                "donor is the LEO2A3 and not this vehicle, so the number is stated here rather than inherited");
            GunDepressionDegrees = Category.CreateEntry("GunDepressionDegrees", -9f,
                "Lowest angle below horizontal, negative. -9 degrees, same War Thunder block. It is one degree " +
                "shallower than the donor's own -10, and that single degree is the whole point of restating " +
                "the arc: the mount inherited from an LEO2A3 would otherwise let the barrel dig one degree " +
                "deeper than this vehicle can");
            AddRoofGun = Category.CreateEntry("AddRoofGun", true,
                "Make the imported commander's machine gun a real weapon: its own traverse and elevation " +
                "platforms, a roof-gun WeaponSystem cloned from the donor's coaxial MG3 (so the same 1200 rpm " +
                "sound and 7.62 mm ammunition), its own fire control system, a gunsight the player can cycle " +
                "into, and a third entry in Next Weapon. Off leaves it as decoration only");
            RoofGunYawPoint = Category.CreateEntry("RoofGunYawPoint", "-1.02,2.63,-0.33",
                "Model space vertical traverse axis of the mount, in the same frame as ModelTurretAxis. Taken " +
                "from the imported mg_aa_mount_h box x[-1.18,-0.87] y[2.63,2.84] z[-0.45,-0.21]: centred " +
                "across the mount, at its base, midway along its travel");
            RoofGunTrunnionPoint = Category.CreateEntry("RoofGunTrunnion", "-0.96,2.80,-0.30",
                "Model space elevation axis. The cradle (mg_aa_mount_v) spans x[-1.02,-0.90] y[2.68,2.85] and " +
                "the gun body (bone_mg_aa_v) y[2.69,2.92], so the pin sits low in the cradle and 3.8 cm below " +
                "the bore line of 2.838, which the muzzle entry carries instead");
            RoofGunMuzzlePoint = Category.CreateEntry("RoofGunMuzzle", "-0.91,2.85,0.52",
                "Model space muzzle, where the live round and the flash spawn. The imported gun body " +
                "(bone_mg_aa_v) ends 4.5 cm aft of this point, and its muzzle ring measures -0.911 across, " +
                "y 2.819..2.858 about a bore line of 2.838; the old 2.80 sat under the barrel's underside, so " +
                "the burst read as firing from beneath the gun");
            RoofGunSightPoint = Category.CreateEntry("RoofGunSight", "-0.90,2.92,-0.60",
                "Model space eyepiece, near the rear of the receiver and at the height of the sight ring");
            RoofGunRack = Category.CreateEntry("RoofGunRack", "-1.05,2.83,-0.25",
                "Model space belt box, the centre of the imported ammo part");
            RoofGunSightFov = Category.CreateEntry("RoofGunSightFov", 20f,
                "Horizontal degrees for the roof gun sight. The donor's daylight eyepiece is cloned for its " +
                "post-process wiring and re-scaled to this, because an anti-aircraft gun needs a wide field. " +
                "The plate's line and letter widths grow with this number to stay as legible as at 10 degrees");
            DumpMaterials = Category.CreateEntry("DumpMaterials", true,
                "Print the full material and renderer state once after the visual is built");

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

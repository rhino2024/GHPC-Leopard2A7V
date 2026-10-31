using System;
using System.Collections.Generic;
using GHPC.Equipment.Optics;
using MelonLoader;
using Reticle;
using TMPro;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Replaces the gunner's optical sight plate with the Leopard 2A7V's own EMES-15 sight picture, taken
    /// from the War Thunder user sight for the vehicle and converted offline by
    /// ModSources/tools/wt_sight_to_cs.py: the four corner brackets and the centre cross with its flanking
    /// range marks, in true mrad. The whole picture is fixed in that file, so the plate hangs off the
    /// boresight and the FCS compensates by laying the gun rather than by walking a mark on the glass.
    ///
    /// The target is the optical channel, not the physical one. The donor gunner has two daylight eyepieces:
    /// `---TURRET SCRIPTS---/GPS/Optic`, the EMES-15 primary, five degrees and wired to the fire control
    /// system, and `---MAIN GUN SCRIPTS---/Rh-120_L/44/FERO Z18`, a ten degree telescope with no FCS in its
    /// ancestry. Tools/optic_reticle_map.py reads the plates off the bundle: GPS/Optic carries "EMES-15",
    /// FERO Z18 carries "FERO Z18", whose plate is a stadia tree in metres with a VerticalRange group that
    /// walks with the dialled range - the physical sight has no rangefinder, so its glass carries the
    /// ranging. The optical plate is a single Boresight group, which is why a static conversion belongs
    /// there and why putting it on the FERO leaves the sight the player actually aims through untouched.
    ///
    /// The donor's illumination type is kept. The plate's elements use NightIllumination, exactly like the
    /// FERO and T-72 plates, and the donor's own light definition is carried over: with its daytime value
    /// of zero the cached colour is opaque black, which is the black glass-etched reticle the 2A7V has,
    /// and the game's illumination key and night handling still turn it the old red.
    /// </summary>
    public static class LpReticle
    {
        private const string DonorPlateName = "EMES-15";
        private const string PlateName = "LEO2A7V";
        private const string RoofGunPlateName = "LEO2A7V_AA";

        /// <summary>
        /// The field one pixel of the War Thunder artwork was calibrated against: 1920 pixels across ten
        /// degrees is about 0.09 mrad. The EMES-15 channel opens at half that, so the converted stroke of
        /// 0.108 mrad lands at 2.4 pixels there, next to the 0.098 mrad the donor draws its own plate with.
        /// </summary>
        private const float DonorPlateFov = 10f;

        private const float MinorLine = 0.098f;
        private const float ScaleLine = 0.08f;
        private const float LabelSize = 9.5f;
        private const float TitleSize = 12f;

        /// <summary>
        /// Stroke width for the imported sight. A War Thunder sight draws its lines a pixel or two wide
        /// and asks for 1.2x of that; one pixel of a 10 degree field is about 0.09 mrad.
        /// </summary>
        private const float WtStroke = 0.09f * 1.2f * (DonorPlateFov / 10f);

        internal const ReticleTree.Light.Type Lit = ReticleTree.Light.Type.NightIllumination;

        private static TMP_FontAsset _font;
        private static bool _fontMissing;
        private static ReticleSO _plate;
        private static ReticleSO _roofGunPlate;

        /// <summary>
        /// How much wider the roof gun sight is than the 10 degrees the donor plate was drawn for. The
        /// geometry is in mrad and therefore fixed, but a line of 0.08 mrad covers half as many pixels at
        /// 20 degrees as it does at 10, so the anti-aircraft plate's widths and letters grow with the field.
        /// </summary>
        private static float _roofGunPixelScale = 1f;

        public static void Apply(Transform unitRoot)
        {
            if (!LpConfig.ReplaceDayReticle || unitRoot == null) return;

            foreach (UsableOptic optic in unitRoot.GetComponentsInChildren<UsableOptic>(true))
            {
                ReticleMesh mesh = optic.reticleMesh;
                if (mesh == null || mesh.reticleSO == null) continue;
                if (mesh.reticleSO.name != DonorPlateName) continue;
                if (ReferenceEquals(mesh.reticleSO, _plate)) return;

                if (_plate == null && !BuildPlate(mesh.reticleSO)) return;

                mesh.reticleSO = _plate;

                // Load() keeps the mesh it built the first time, so it has to be dropped to rebuild.
                SkinnedMeshRenderer renderer = mesh.GetComponent<SkinnedMeshRenderer>();
                if (renderer != null) renderer.sharedMesh = null;
                mesh.Load();

                // CacheLights() fills the light colour array before Load() hands the plate's own light
                // list to the mesh, so the new slot keeps the donor's value until it is pushed. Black is
                // a colour and not a hole here, so zero is the wanted daytime value.
                mesh.GetLight(Lit, out float value);
                mesh.SetLight(Lit, float.IsNaN(value) || value <= 0f ? 0f : 1f);

                MelonLogger.Msg("[L2A7V] gunner day reticle replaced with the 2A7V plate on " +
                                Describe(optic.transform, unitRoot));
                return;
            }
        }

        private static bool BuildPlate(ReticleSO donor)
        {
            FindFont();

            var plane = new ReticleTree.FocalPlane(ReticleTree.FocalPlane.Type.First)
            {
                elements = new List<ReticleTree.TransformElement>()
            };
            plane.elements.Add(WtSight());
            if (LpSightData.Moving.Length > 0) plane.elements.Add(WtBallistic());

            var plate = ScriptableObject.CreateInstance<ReticleSO>();
            plate.name = PlateName;
            plate.hideFlags = HideFlags.DontUnloadUnusedAsset;
            plate.planes = new List<ReticleTree.FocalPlane> { plane };
            // Reusing the donor's light objects keeps the sight's brightness range and night handling,
            // and gives the elements a slot to draw from; without one they are transparent.
            plate.lights = donor != null && donor.lights != null
                ? new List<ReticleTree.Light>(donor.lights)
                : new List<ReticleTree.Light>();
            if (plate.lights.Count == 0)
                plate.lights.Add(new ReticleTree.Light { type = Lit, color = new RGB(4f, 0.335f, 0f) });

            _plate = plate;
            return true;
        }

        /// <summary>
        /// The plate for the commander's machine gun sight. One element unit is one mrad here, exactly like
        /// every other plate, and the subtense does not depend on the sight's field of view, so the scales are
        /// laid out for the width the round actually needs: 7.62 mm is useful to about 40 mrad of holdover.
        /// What the field of view does change is how many pixels a given mrad covers, so the widths and letter
        /// sizes grow with it below and the plate stays as legible as the 10 degree one it was drawn against.
        /// </summary>
        public static ReticleSO BuildRoofGunPlate(ReticleSO donor)
        {
            if (_roofGunPlate != null) return _roofGunPlate;
            FindFont();
            _roofGunPixelScale = Mathf.Max(1f, LpConfig.RoofGunSightFov / DonorPlateFov);

            var plane = new ReticleTree.FocalPlane(ReticleTree.FocalPlane.Type.First)
            {
                elements = new List<ReticleTree.TransformElement>()
            };
            plane.elements.Add(RoofGunScales());
            plane.elements.Add(RoofGunAimMark());

            var plate = ScriptableObject.CreateInstance<ReticleSO>();
            plate.name = RoofGunPlateName;
            plate.hideFlags = HideFlags.DontUnloadUnusedAsset;
            plate.planes = new List<ReticleTree.FocalPlane> { plane };
            plate.lights = donor != null && donor.lights != null
                ? new List<ReticleTree.Light>(donor.lights)
                : new List<ReticleTree.Light>();
            if (plate.lights.Count == 0)
                plate.lights.Add(new ReticleTree.Light { type = Lit, color = new RGB(4f, 0.335f, 0f) });

            _roofGunPlate = plate;
            return plate;
        }

        /// <summary>Fixed geometry: the mil cross, the tick scale and the range brackets.</summary>
        private static ReticleTree.Angular RoofGunScales()
        {
            var group = new ReticleTree.Angular(Vector2.zero, null, ReticleTree.GroupBase.Alignment.Boresight);
            group.name = "Scales";

            // Arms with a 2 mrad gap so the boresight stays readable, out to +/-40 mrad.
            group.elements.Add(AALine(new Vector2(21f, 0f), 0f, 38f, ScaleLine));
            group.elements.Add(AALine(new Vector2(-21f, 0f), 0f, 38f, ScaleLine));
            group.elements.Add(AALine(new Vector2(0f, 21f), 90f, 38f, ScaleLine));
            group.elements.Add(AALine(new Vector2(0f, -21f), 90f, 38f, ScaleLine));

            for (int mil = 5; mil <= 40; mil += 5)
            {
                float tick = mil % 10 == 0 ? 2.4f : 1.2f;
                group.elements.Add(AALine(new Vector2(mil, 0f), 90f, tick, ScaleLine));
                group.elements.Add(AALine(new Vector2(-mil, 0f), 90f, tick, ScaleLine));
                group.elements.Add(AALine(new Vector2(0f, -mil), 0f, tick, ScaleLine));
                if (mil % 20 != 0) continue;
                AAMark(group.elements, new Vector2(mil, -3.6f), mil.ToString());
                AAMark(group.elements, new Vector2(-mil, -3.6f), mil.ToString());
            }

            // Stadiametric brackets for a 2.5 m target: the pair straddling a range is as wide as that
            // target measures at it, so the gunner ranges by fitting the vehicle between them.
            int[] metres = { 400, 600, 800, 1200 };
            for (int i = 0; i < metres.Length; i++)
            {
                float half = 1250f / metres[i];
                float depth = -(10f + i * 7.5f);
                group.elements.Add(AALine(new Vector2(-half, depth), 90f, 2.4f, ScaleLine));
                group.elements.Add(AALine(new Vector2(half, depth), 90f, 2.4f, ScaleLine));
                AAMark(group.elements, new Vector2(half + 3.4f, depth), (metres[i] / 100f).ToString("0"));
            }

            AddMark(group.elements, new Vector2(-46f, 40f), "MG3", TextAlignmentOptions.Left,
                LabelSizeFor(TitleSize));
            return group;
        }

        /// <summary>The aiming ring, in a VerticalRange group so it walks with the dialled range.</summary>
        private static ReticleTree.Angular RoofGunAimMark()
        {
            var group = new ReticleTree.Angular(Vector2.zero, null, ReticleTree.GroupBase.Alignment.VerticalRange);
            group.name = "Aim";
            group.elements.Add(AACircle(Vector2.zero, 3.2f, MinorLine, 40));
            group.elements.Add(AALine(new Vector2(0f, 4.6f), 90f, 1.4f, MinorLine));
            return group;
        }

        /// <summary>A line whose width, but not its length or placement, grows with the sight's field.</summary>
        private static ReticleTree.Line AALine(Vector2 position, float degrees, float length, float thickness)
        {
            return Line(position, degrees, length, thickness * _roofGunPixelScale);
        }

        private static ReticleTree.Circle AACircle(Vector2 position, float radius, float thickness, int segments)
        {
            return Circle(position, radius, thickness * _roofGunPixelScale, segments);
        }

        private static void AAMark(List<ReticleTree.TransformElement> elements, Vector2 position, string label)
        {
            AddMark(elements, position, label, TextAlignmentOptions.Center, LabelSizeFor(LabelSize));
        }

        private static float LabelSizeFor(float points)
        {
            return points * _roofGunPixelScale;
        }

        /// <summary>
        /// The EMES-15 picture, exactly as the War Thunder 2A7V user sight draws it: the four corner
        /// brackets and the centre cross with its flanking range marks. The table is generated offline from
        /// that file by ModSources/tools/wt_sight_to_cs.py, already in mrad and already flipped into the
        /// frame GHPC uses (War Thunder's y grows downwards, this one's grows up). Every mark in this sight
        /// is fixed in the file - the ballistic solution moves the gun, not the glass - so the whole picture
        /// hangs off the boresight.
        /// </summary>
        private static ReticleTree.Angular WtSight()
        {
            var group = new ReticleTree.Angular(Vector2.zero, null, ReticleTree.GroupBase.Alignment.Boresight);
            group.name = "WtSight";
            AddSegments(group.elements, LpSightData.Fixed);
            MelonLogger.Msg($"[L2A7V] reticle plate: {LpSightData.Fixed.Length / 4} fixed and " +
                            $"{LpSightData.Moving.Length / 4} moving segment(s) from the War Thunder EMES-15 sight");
            return group;
        }

        /// <summary>
        /// The marks the sight file tags `move: Yes`. The 2A7V file has none, but the group is kept so a
        /// sight with a moving range ladder converts without touching this code.
        /// </summary>
        private static ReticleTree.Angular WtBallistic()
        {
            var group = new ReticleTree.Angular(Vector2.zero, null, ReticleTree.GroupBase.Alignment.VerticalRange);
            group.name = "WtBallistic";
            AddSegments(group.elements, LpSightData.Moving);
            return group;
        }

        /// <summary>
        /// One line per segment, length and rotation taken straight from the end points.
        /// </summary>
        private static void AddSegments(List<ReticleTree.TransformElement> elements, float[] segments)
        {
            for (int i = 0; i + 3 < segments.Length; i += 4)
            {
                float dx = segments[i + 2] - segments[i];
                float dy = segments[i + 3] - segments[i + 1];
                float length = (float)Math.Sqrt(dx * dx + dy * dy);
                if (length < 0.01f) continue;
                elements.Add(Line(
                    new Vector2((segments[i] + segments[i + 2]) * 0.5f, (segments[i + 1] + segments[i + 3]) * 0.5f),
                    (float)(Math.Atan2(dy, dx) * (180.0 / Math.PI)), length, WtStroke));
            }
        }

        internal static ReticleTree.Line Line(Vector2 position, float degrees, float length, float thickness)
        {
            return new ReticleTree.Line(position, degrees, length, thickness) { illumination = Lit };
        }

        internal static ReticleTree.Circle Circle(Vector2 position, float radius, float thickness, int segments)
        {
            return new ReticleTree.Circle(position, 0f, radius, thickness, segments) { illumination = Lit };
        }

        /// <summary>
        /// ReticleMesh.Load() aborts the whole plate when a single text element has no font, so labels
        /// are only added once the atlas has been found.
        /// </summary>
        internal static void AddMark(List<ReticleTree.TransformElement> elements, Vector2 position,
            string label, TextAlignmentOptions alignment = TextAlignmentOptions.Center, float size = LabelSize)
        {
            if (_font == null) return;
            elements.Add(new ReticleTree.Text(position, 0f, label, size, alignment)
            {
                font = _font,
                illumination = Lit
            });
        }

        internal static void FindFont()
        {
            if (_font != null || _fontMissing) return;
            foreach (TMP_FontAsset asset in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (asset == null || asset.name.IndexOf("Reticle", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                // AddMark's guard runs when the plate is built, but the reference is re-read at mesh
                // generation by ReticleTree.Text.GetMaterial(), which throws "Fontless" and leaves the
                // plate with no numbers. A bundle-loaded font can be unloaded in between, so pin it.
                asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                _font = asset;
                MelonLogger.Msg("[L2A7V] reticle font '" + asset.name + "'");
                return;
            }
            _fontMissing = true;
            MelonLogger.Warning("[L2A7V] reticle font asset not found, the 2A7V plate will have no numbers");
        }

        private static string Describe(Transform node, Transform unitRoot)
        {
            var names = new List<string>();
            while (node != null && node != unitRoot)
            {
                names.Add(node.name);
                node = node.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }
    }
}

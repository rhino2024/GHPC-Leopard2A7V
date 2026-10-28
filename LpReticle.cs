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
    /// Replaces the FERO Z18 plate the donor gunner's daylight optic ships with by one laid out for the
    /// Leopard 2A7V. War Thunder's 2A7V sight is the same EMES-15 family graticule the game already
    /// carries on the "GPS/Optic" mesh, so the fixed geometry here (0.098 mrad lines, the side brackets,
    /// the centre ring, the mil scales) is measured from that plate rather than invented, and the moving
    /// part is a VerticalRange group like the FERO's so the FCS still walks the aiming mark with the
    /// superelevation of whatever round is loaded.
    ///
    /// The donor's illumination type is kept. The plate's elements use NightIllumination, exactly like the
    /// FERO and T-72 plates, and the donor's own light definition is carried over: with its daytime value
    /// of zero the cached colour is opaque black, which is the black glass-etched reticle the 2A7V has,
    /// and the game's illumination key and night handling still turn it the old red.
    /// </summary>
    public static class LpReticle
    {
        private const string DonorPlateName = "FERO Z18";
        private const string PlateName = "LEO2A7V";
        private const string RoofGunPlateName = "LEO2A7V_AA";

        /// <summary>The horizontal field the donor's daylight plate, and therefore its widths, are drawn for.</summary>
        private const float DonorPlateFov = 10f;

        private const float MainLine = 0.12f;
        private const float MinorLine = 0.098f;
        private const float ScaleLine = 0.08f;
        private const float MinorTick = 0.55f;
        private const float MajorTick = 1.15f;
        private const float LabelSize = 9.5f;
        private const float TitleSize = 12f;

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
            if (!LpConfig.ReplaceDayReticle.Value || unitRoot == null) return;

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
            plane.elements.Add(Graticule());
            plane.elements.Add(Index());

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
            _roofGunPixelScale = Mathf.Max(1f, LpConfig.RoofGunSightFov.Value / DonorPlateFov);

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
        /// The aiming graticule, in the same VerticalRange group the FERO uses so ReticleMesh.Update
        /// offsets it by the ballistic computer's superelevation for the loaded round.
        /// </summary>
        private static ReticleTree.Angular Graticule()
        {
            var group = new ReticleTree.Angular(Vector2.zero, null, ReticleTree.GroupBase.Alignment.VerticalRange);
            group.name = "Graticule";

            // Main cross with a 1 mrad central gap and the small centre ring of the EMES-15 picture. The
            // arms stop inside the bracket so the two do not read as one shape.
            group.elements.Add(Line(new Vector2(1.2f, 0f), 0f, 1.4f, MainLine));
            group.elements.Add(Line(new Vector2(-1.2f, 0f), 0f, 1.4f, MainLine));
            group.elements.Add(Line(new Vector2(0f, 1.2f), 90f, 1.4f, MainLine));
            group.elements.Add(Line(new Vector2(0f, -1.2f), 90f, 1.4f, MainLine));
            group.elements.Add(Circle(Vector2.zero, 0.22f, MinorLine, 32));

            // The bracket the EMES-15 plate puts around the cross, at its measured offsets.
            group.elements.Add(Line(new Vector2(-4.9087f, 1.9635f), 0f, 4.9087f, MinorLine));
            group.elements.Add(Line(new Vector2(-4.9087f, -1.9635f), 0f, 4.9087f, MinorLine));
            group.elements.Add(Line(new Vector2(4.9087f, 1.9635f), 0f, 4.9087f, MinorLine));
            group.elements.Add(Line(new Vector2(4.9087f, -1.9635f), 0f, 4.9087f, MinorLine));
            group.elements.Add(Line(new Vector2(7.3631f, 0f), 90f, 0.7f, MinorLine));
            group.elements.Add(Line(new Vector2(-7.3631f, 0f), 90f, 0.7f, MinorLine));

            HorizontalScale(group);
            VerticalScale(group);
            return group;
        }

        /// <summary>Lead scale: NATO mils to 10 either side, numbered every five.</summary>
        private static void HorizontalScale(ReticleTree.Angular group)
        {
            group.elements.Add(Line(new Vector2(6.925f, 0f), 0f, 6.25f, ScaleLine));
            group.elements.Add(Line(new Vector2(-6.925f, 0f), 0f, 6.25f, ScaleLine));

            for (int mil = 4; mil <= 10; mil++)
            {
                float tick = mil % 5 == 0 ? MajorTick : MinorTick;
                group.elements.Add(Line(new Vector2(mil, 0f), 90f, tick, ScaleLine));
                group.elements.Add(Line(new Vector2(-mil, 0f), 90f, tick, ScaleLine));
                if (mil % 5 != 0) continue;
                AddMark(group.elements, new Vector2(mil, 2.6f), mil.ToString());
                AddMark(group.elements, new Vector2(-mil, 2.6f), mil.ToString());
            }
        }

        /// <summary>Holdover scale below the cross, 10 mils deep.</summary>
        private static void VerticalScale(ReticleTree.Angular group)
        {
            group.elements.Add(Line(new Vector2(0f, -6.925f), 90f, 6.25f, ScaleLine));

            for (int mil = 4; mil <= 10; mil++)
            {
                float tick = mil % 5 == 0 ? MajorTick : MinorTick;
                group.elements.Add(Line(new Vector2(0f, -mil), 0f, tick, ScaleLine));
                if (mil % 5 == 0)
                    AddMark(group.elements, new Vector2(2.5f, -mil), mil.ToString());
            }
        }

        /// <summary>Fixed marking: which round the ballistic mark is following.</summary>
        private static ReticleTree.Angular Index()
        {
            var group = new ReticleTree.Angular(Vector2.zero, null, ReticleTree.GroupBase.Alignment.Boresight);
            group.name = "Index";
            AddMark(group.elements, new Vector2(-15.5f, 12.5f), "DM53", TextAlignmentOptions.Left, TitleSize);
            return group;
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

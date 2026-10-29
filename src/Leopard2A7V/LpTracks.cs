using System.Collections.Generic;
using MelonLoader;
using NWH.VehiclePhysics;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Builds the imported track loops as their own scrolling mesh. This path is disabled by default and is
    /// kept only as a knob for a future bake, because both sheets it could draw from have been measured and
    /// neither can work:
    ///
    /// The donor's `D570P` sheet is not a repeating tread material. lp_donor_tread.py reads its ground-facing
    /// triangles and finds the pattern does repeat along v - one pad every 64 px, eight per tile, 0.746 tiles
    /// per metre of run - but across the width it is a part atlas: a single link's ground face is split into
    /// roughly ten u-columns (0.004, 0.007, 0.032, 0.052, 0.124, 0.144, 0.194, 0.221, 0.318, 0.385) laid out
    /// side by side in no spatial order. That mapping only makes sense triangle by triangle on the donor's own
    /// mesh. Re-laying any other mesh by (arc, width) stretches each face between unrelated columns, which is
    /// exactly the flat brown-black band lp_track_preview.py reproduces offline; lp_donor_unroll.py confirms
    /// the point by showing the donor's own UVs unroll to the same mush once they are re-binned by width.
    ///
    /// The War Thunder sheet is worse for this purpose: ger_leopard_2a4_track scatters its ground faces over
    /// u 0.014-1.000 and v -4.766-4.233 with zero clusters (lp_tread_cell.py), so there is no direction along
    /// which the tread repeats and offsetting the texture moves random pads instead of the run.
    ///
    /// What the vehicle draws now is the donor's own `LEO2A3/LEO2_tracks/TRACK_L|R`: 3198-vert skinned meshes
    /// bound to the `track_l_1..7` bones the physics turns, which is why LpBaseRig keeps them visible.
    ///
    /// Only the loop's own vertices are rewritten here, so the return rollers and suspension arms keep their
    /// atlas UVs in L2A7V_RunningGearStatic. Each side gets its own mesh and its own material clone, which is
    /// what lets the two tracks slide at different speeds while turning.
    ///
    /// The scroll is driven from LpTrackDriver rather than by handing our renderer to NWH's Tracks component:
    /// Tracks.Initialize caches leftTrackRenderer.material during the vehicle's own startup, which happens
    /// before the background model parse has let us build anything, so a renderer assigned afterwards would
    /// simply never be read.
    /// </summary>
    public static class LpTracks
    {
        private const string Tag = "[L2A7V][track]";

        /// <summary>
        /// Tread repeats per metre of run, from the config. The same number has to scale both the UVs and the
        /// per-frame offset, since the offset is in whole texture tiles - one tile of scroll always has to move
        /// exactly one pad's worth of track - which is why this is read through one place and not inlined.
        /// </summary>
        private static float TreadsPerMetre => Mathf.Clamp(LpConfig.TrackTreadsPerMetre.Value, 0.05f, 20f);

        /// <summary>
        /// Where this pass lays the pattern across the track's own width. These bounds are Challenger2's, taken
        /// from a sheet whose tread really does occupy one narrow band; measured against the donor's D570P the
        /// same constants are the bug - that sheet spreads a link's ground face over u 0-0.451 in ten unordered
        /// columns, so squeezing our whole width into 0.005-0.073 samples a single column of bare metal and
        /// reads as the flat brown-black strip the user reported.
        /// </summary>
        private const float TreadStripMinU = 0.005f;

        private const float TreadStripMaxU = 0.073f;

        /// <summary>What the last attempt did, for the running gear pass to report.</summary>
        public static string Status { get; private set; } = "not attempted";

        /// <summary>
        /// True when our own loops could not be produced. The hide rule reads it and lets the donor's animated
        /// art stay, so a failed build costs a track of the wrong pattern rather than the vehicle's whole
        /// running gear.
        /// </summary>
        public static bool DonorFallback { get; private set; }

        public static void Attach(Transform hullPivot, Transform unitRoot)
        {
            DonorFallback = false;
            Status = "not built";

            VehicleController controller = unitRoot.GetComponent<VehicleController>();
            if (controller == null) controller = unitRoot.GetComponentInChildren<VehicleController>(true);
            Tracks native = controller == null ? null : controller.tracks;
            if (native == null || !native.trackedVehicle)
            {
                Status = KeepDonorArt("the donor has no track controller to read a speed from");
                return;
            }

            LpTrackDriver driver = unitRoot.gameObject.GetComponent<LpTrackDriver>();
            if (driver == null) driver = unitRoot.gameObject.AddComponent<LpTrackDriver>();
            driver.SetNative(native);

            MeshRenderer left = BuildSide(hullPivot, "track_l", "L2A7V_TrackLeft",
                native.leftTrackRenderer, driver, true);
            MeshRenderer right = BuildSide(hullPivot, "track_r", "L2A7V_TrackRight",
                native.rightTrackRenderer, driver, false);
            int built = (left == null ? 0 : 1) + (right == null ? 0 : 1);
            if (built == 2)
            {
                Status = "both loops rebuilt and scrolled on the donor's sheet";
                return;
            }

            // One side alone is worse than either answer: the other track would be missing entirely, and the
            // donor's art - which the hide pass has already taken out - cannot be half-reinstated. Undo the
            // attempt with the node still in one piece, because the hide pass skips anything named L2A7V_* and
            // a node left until the end of the frame would draw untextured geometry for a whole load.
            if (left != null) UnityEngine.Object.DestroyImmediate(left.gameObject);
            if (right != null) UnityEngine.Object.DestroyImmediate(right.gameObject);
            driver.SetSide(true, null, 0f);
            driver.SetSide(false, null, 0f);
            Status = KeepDonorArt($"only {built} of 2 loops could be built");
        }

        private static string KeepDonorArt(string reason)
        {
            DonorFallback = true;
            MelonLogger.Warning(Tag + " " + reason + ", so UseDonorTrackArt's donor art is kept instead");
            return reason + ", donor art kept";
        }

        /// <summary>Builds one side, or returns null for anything that has to be left to the donor's art.</summary>
        private static MeshRenderer BuildSide(Transform hullPivot, string objectName,
            string nodeName, Renderer donor, LpTrackDriver driver, bool isLeft)
        {
            Loop loop = Measure(objectName);
            if (loop == null)
            {
                MelonLogger.Warning(Tag + $" the export has no '{objectName}' object");
                return null;
            }

            Material material = TreadMaterial(donor, nodeName);
            if (material == null) return null;

            Mesh mesh = BuildMesh(loop, nodeName);
            if (mesh == null) return null;

            MeshRenderer renderer = LpVisual.AttachMesh(hullPivot, nodeName, mesh);
            if (renderer == null) return null;
            renderer.sharedMaterials = new[] { material };

            driver.SetSide(isLeft, material, loop.ScrollUnitsPerMetre);
            MelonLogger.Msg(Tag + $" {objectName} -> {nodeName}: run {loop.Length:0.00} m, loop height " +
                            $"{loop.Height:0.00} m, perimeter {loop.Perimeter:0.00} m, " +
                            $"{loop.Repeats:0} tread repeat(s) ({TreadsPerMetre:0.###}/m), " +
                            $"sheet '{SheetNameOf(donor)}'");
            return renderer;
        }

        /// <summary>
        /// Clone of the donor's track material, with its own offset so a side can scroll alone. The donor's sheet
        /// is a part atlas rather than a repeating tread, so this only produces coherent motion for a future
        /// layout that is one.
        /// </summary>
        private static Material TreadMaterial(Renderer donor, string nodeName)
        {
            if (donor == null || donor.sharedMaterial == null)
            {
                MelonLogger.Error(Tag + $" the donor's track renderer is gone, so {nodeName} has no tread sheet " +
                                  "to scroll; turn UseDonorTrackArt on instead");
                return null;
            }

            var material = new Material(donor.sharedMaterial) { name = "L2A7V seamless tread " + nodeName };
            // The repeats live in the rebuilt UVs, so the sheet itself must not tile again on top of them.
            material.mainTextureScale = Vector2.one;
            material.mainTextureOffset = Vector2.zero;
            SetIfPresent(material, "_Cutoff", 0f);
            SetIfPresent(material, "_AlbedoMult", 0.8f);
            SetIfPresent(material, "_GlossMapScale", 0.1f);
            SetIfPresent(material, "_SpecularHighlights", 0f);
            SetIfPresent(material, "_GlossyReflections", 0f);
            return material;
        }

        private static void SetIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static string SheetNameOf(Renderer donor)
        {
            if (donor == null || donor.sharedMaterial == null) return "<none>";
            Texture sheet = donor.sharedMaterial.mainTexture;
            return sheet == null ? "<no main texture>" : sheet.name + ' ' + sheet.width + "x" + sheet.height;
        }

        /// <summary>The loop as a stadium, which is the shape the donor's own skin approximates.</summary>
        private sealed class Loop
        {
            public List<ObjGeometry> Parts;
            public Vector3 Min;
            public Vector3 Max;
            public float CentreY;
            public float Rear;
            public float Front;
            public float Radius;
            public float Height;
            public float Length;
            public float Perimeter;
            public float Repeats;

            /// <summary>Texture tiles per metre of run: the same factor the UVs are scaled by, so the NWH
            /// offset in tiles becomes metres of track.</summary>
            public float ScrollUnitsPerMetre => Perimeter > 0.01f ? Repeats / Perimeter : TreadsPerMetre;

            public float Arc(Vector3 p) =>
                LoopDistance(p.y, p.z, CentreY, Rear, Front, Radius) / Perimeter;
        }

        private static Loop Measure(string objectName)
        {
            var loop = new Loop
            {
                Parts = new List<ObjGeometry>(),
                Min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue),
                Max = new Vector3(float.MinValue, float.MinValue, float.MinValue),
            };

            foreach (ObjGeometry part in ObjModel.Geometries)
            {
                if (part.Group != LpGroup.RunningGear) continue;
                if (!string.Equals(part.ObjectName, objectName, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                loop.Parts.Add(part);
                for (int i = 0; i < part.Positions.Length; i++)
                {
                    loop.Min = Vector3.Min(loop.Min, part.Positions[i]);
                    loop.Max = Vector3.Max(loop.Max, part.Positions[i]);
                }
            }
            if (loop.Parts.Count == 0) return null;

            loop.Height = loop.Max.y - loop.Min.y;
            loop.Length = loop.Max.z - loop.Min.z;
            loop.CentreY = (loop.Min.y + loop.Max.y) * 0.5f;
            // The arcs are as tall as the loop, so their centres sit one radius in from either end.
            loop.Radius = Mathf.Max(0.1f, loop.Height * 0.5f);
            loop.Rear = loop.Min.z + loop.Radius;
            loop.Front = loop.Max.z - loop.Radius;
            loop.Perimeter = 2f * (loop.Front - loop.Rear) + Mathf.PI * 2f * loop.Radius;
            if (loop.Front <= loop.Rear || loop.Perimeter < 1f)
            {
                MelonLogger.Warning(Tag + $" '{objectName}' is not a loop: run {loop.Length:0.00} m against a " +
                                  $"loop height of {loop.Height:0.00} m");
                return null;
            }
            loop.Repeats = Mathf.Max(1f, Mathf.Round(loop.Perimeter * TreadsPerMetre));
            return loop;
        }

        /// <summary>
        /// Distance travelled along the loop, in metres, from the top of the front arc: down and around the
        /// front wheel, back along the bottom run, up and around the rear one, then forward along the top.
        /// Increasing distance is therefore the direction the track material itself travels when the vehicle
        /// drives forward, which is what makes the sign of the scroll come out right against NWH's negative
        /// forward velocity.
        /// </summary>
        public static float LoopDistance(float y, float z, float centreY, float rear, float front, float radius)
        {
            if (z > front)
                return radius * (Mathf.PI * 0.5f - Mathf.Atan2(y - centreY, z - front));
            if (z < rear)
                return Mathf.PI * radius + front - rear + radius * (Mathf.Atan2(y - centreY, rear - z) + Mathf.PI * 0.5f);
            if (y < centreY)
                return Mathf.PI * radius + front - z;
            return Mathf.PI * 2f * radius + front - rear + z - rear;
        }

        private static Mesh BuildMesh(Loop loop, string cacheKey)
        {
            string key = cacheKey + "|" + loop.ScrollUnitsPerMetre.ToString("0.####");
            Mesh cached;
            if (LoopMeshes.TryGetValue(key, out cached) && cached != null) return cached;

            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float width = loop.Max.x - loop.Min.x;

            foreach (ObjGeometry part in loop.Parts)
            {
                int offset = positions.Count;
                for (int i = 0; i < part.Positions.Length; i++)
                {
                    Vector3 p = part.Positions[i];
                    positions.Add(p);
                    normals.Add(part.Normals[i]);
                    tangents.Add(part.Tangents[i]);
                }

                var arc = new float[part.Positions.Length];
                for (int i = 0; i < arc.Length; i++) arc[i] = loop.Arc(part.Positions[i]);

                // One arc position per triangle so a face that crosses the seam can be pushed over it instead
                // of being stretched back through the whole run.
                for (int t = 0; t + 2 < part.Triangles.Length; t += 3)
                {
                    float low = 1f, high = 0f;
                    for (int c = 0; c < 3; c++)
                    {
                        float value = arc[part.Triangles[t + c]];
                        if (value < low) low = value;
                        if (value > high) high = value;
                    }

                    for (int c = 0; c < 3; c++)
                    {
                        int source = part.Triangles[t + c];
                        float value = arc[source];
                        if (high - low > 0.5f && value < 0.5f) value += 1f;

                        float across = width > 1e-4f
                            ? Mathf.InverseLerp(loop.Min.x, loop.Max.x, positions[offset + source].x)
                            : 0.5f;
                        uvs.Add(new Vector2(Mathf.Lerp(TreadStripMinU, TreadStripMaxU, across),
                                            value * loop.Repeats));
                        triangles.Add(offset + source);
                    }
                }
            }

            if (triangles.Count < 3) return null;

            var mesh = new Mesh
            {
                name = "L2A7V_" + cacheKey,
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 1;
            mesh.SetTriangles(triangles, 0);
            // The atlas tangents pointed at the old UVs; the new ones have to be derived from the arc layout.
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            LoopMeshes[key] = mesh;
            return mesh;
        }

        /// <summary>Loop meshes as re-UV'd, per side and per repeats-per-metre it was laid out with.</summary>
        private static readonly Dictionary<string, Mesh> LoopMeshes = new Dictionary<string, Mesh>();
    }

    /// <summary>
    /// Slides both tread sheets along the run at the speed the donor's physics already computes. NWH's own
    /// Tracks.UpdateVisual does the same for the donor's art, but it latched that material during the
    /// vehicle's startup, before this mod had any geometry to hand it over, so the loops need their own driver.
    /// </summary>
    public class LpTrackDriver : MonoBehaviour
    {
        private Tracks _native;
        private Material _left;
        private Material _right;
        private float _leftUnits;
        private float _rightUnits;
        private float _leftPhase;
        private float _rightPhase;
        private bool _logged;

        public void SetNative(Tracks native) => _native = native;

        public void SetSide(bool isLeft, Material material, float scrollUnitsPerMetre)
        {
            if (isLeft)
            {
                _left = material;
                _leftUnits = scrollUnitsPerMetre;
            }
            else
            {
                _right = material;
                _rightUnits = scrollUnitsPerMetre;
            }
        }

        private void LateUpdate()
        {
            if (_native == null || (_left == null && _right == null)) return;
            float dt = Time.deltaTime;
            if (dt <= 0f || dt > 1f) return;

            if (_left != null)
            {
                _leftPhase = Advance(_leftPhase, _native.LeftTrackVelocity, _leftUnits, dt);
                _left.mainTextureOffset = new Vector2(0f, _leftPhase);
            }
            if (_right != null)
            {
                _rightPhase = Advance(_rightPhase, _native.RightTrackVelocity, _rightUnits, dt);
                _right.mainTextureOffset = new Vector2(0f, _rightPhase);
            }

            if (_logged) return;
            _logged = true;
            MelonLogger.Msg($"[L2A7V][track] driver running, track velocity " +
                            $"{_native.LeftTrackVelocity:0.00}/{_native.RightTrackVelocity:0.00} m/s at " +
                            $"{_native.wheelEnlargementCoefficient:0.#}x wheel size");
        }

        /// <summary>
        /// Offset in texture tiles, wrapped. NWH's track velocity is negative when the vehicle drives forward,
        /// and the mesh's v grows in the direction the material travels, so the two signs already agree and
        /// this only has to accumulate.
        /// </summary>
        private static float Advance(float phase, float velocity, float units, float dt)
        {
            if (float.IsNaN(velocity) || float.IsInfinity(velocity)) return phase;
            float next = phase + velocity * dt * units;
            return next - Mathf.Floor(next);
        }
    }
}

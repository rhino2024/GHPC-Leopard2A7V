using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V
{
    /// <summary>
    /// Prints the exact render state of the donor's vehicle material next to the cloned ones, so a visual
    /// defect can be traced to a single shader property instead of guesswork. A flat or wrongly lit panel
    /// has always come down to one of these lines.
    /// </summary>
    public static class LpDiagnostics
    {
        private const string Tag = "[L2A7V][diag]";

        private static readonly string[] FloatProps =
        {
            "_Mode", "_Surface", "_Cull", "_ZTest", "_ZWrite", "_SrcBlend", "_DstBlend",
            "_AlphaToMask", "_Cutoff", "_XrayAlpha", "_Glossiness", "_Metallic", "_GlossMapScale",
            "_SmoothnessTextureChannel", "_SpecularHighlights", "_ParallaxMapping",
            "_OcclusionStrength", "_DetailMaskChannel", "_DetailScale", "_BumpScale",
            "_CamoAmount", "_PaintMaskPower", "_paintmaskcontrast", "_snow", "_dirt",
            "_ScorchAmount", "_heat", "_FLIR_Emission", "_hackcamoUVs",
        };

        private static readonly string[] TextureProps =
        {
            "_Albedo", "_Normal", "_Occlusion", "_PaintMask", "_Smoothness", "_CamoLayer",
            "_noise1", "_ScorchAlbedo", "_DetailAlbedoMOTexel",
        };

        private static bool _dumped;

        public static void DumpOnce(GameObject unitRoot)
        {
            if (_dumped || !LpConfig.DumpMaterials.Value) return;
            _dumped = true;
            Dump(unitRoot);
        }

        public static void Dump(GameObject unitRoot)
        {
            if (unitRoot == null) return;
            var sb = new StringBuilder();
            sb.AppendLine("---- materials ----");
            Describe(sb, "template", LpVisual.TemplateMaterial);
            foreach (KeyValuePair<string, Material> entry in LpAssets.CreatedMaterials)
                Describe(sb, "clone[" + entry.Key + "]", entry.Value);

            sb.AppendLine("---- imported renderers ----");
            int ours = 0;
            foreach (Renderer renderer in unitRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!LpVisual.IsImportedArt(renderer.transform)) continue;
                ours++;
                Describe(sb, renderer);
            }

            sb.AppendLine("---- donor track drive ----");
            DescribeTrackDrive(sb, unitRoot);

            sb.AppendLine("---- donor renderers the hide rule covers but did not strip ----");
            int leftovers = 0;
            int expected = 0;
            foreach (Renderer renderer in unitRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (LpVisual.IsImportedArt(renderer.transform)) continue;
                if (renderer is ParticleSystemRenderer) continue;
                // One decision, shared with the strip pass: a renderer the rule means to hide is harmless once
                // it owns no mesh, so only the ones that still carry geometry are a fault.
                if (!LpBaseRig.ShouldHide(renderer.transform, renderer.name))
                {
                    expected++;
                    continue;
                }
                if (!HasGeometry(renderer)) continue;
                if (leftovers++ < 60)
                    sb.AppendLine(Tag + " still modelled " + PathOf(renderer.transform) + " mat " +
                                  MaterialSummary(renderer) + " enabled=" + renderer.enabled +
                                  " active=" + renderer.gameObject.activeInHierarchy);
            }
            sb.AppendLine(Tag + " " + ours + " imported renderer(s), " + expected +
                          " donor renderer(s) kept on purpose, " + leftovers +
                          " donor renderer(s) that still own a mesh");

            MelonLogger.Msg(sb.ToString());
        }

        private static void Describe(StringBuilder sb, string label, Material material)
        {
            if (material == null)
            {
                sb.AppendLine(Tag + " " + label + ": <null>");
                return;
            }

            sb.AppendLine(Tag + " " + label + " name='" + material.name + "' shader='" +
                          (material.shader == null ? "<null>" : material.shader.name) +
                          "' queue=" + material.renderQueue + " pass=" + material.passCount +
                          " kw='" + string.Join(",", material.shaderKeywords) + "'");
            sb.AppendLine(Tag + "   floats: " + FloatSummary(material));
            sb.AppendLine(Tag + "   color: " + (material.HasProperty("_Color")
                ? ColorToString(material.GetColor("_Color")) : "-"));
            sb.AppendLine(Tag + "   texes: " + TextureSummary(material));
        }

        private static void Describe(StringBuilder sb, Renderer renderer)
        {
            Mesh mesh = renderer.GetComponent<MeshFilter>() == null
                ? null : renderer.GetComponent<MeshFilter>().sharedMesh;
            sb.Append(Tag).Append(" OURS ").Append(renderer.name)
              .Append(" enabled=").Append(renderer.enabled)
              .Append(" active=").Append(renderer.gameObject.activeInHierarchy)
              .Append(" layer=").Append(renderer.gameObject.layer)
              .Append(" shadows=").Append(renderer.shadowCastingMode)
              .Append(" receiveShadows=").Append(renderer.receiveShadows)
              .Append(" probe=").Append(renderer.lightProbeUsage)
              .Append(" scale=").Append(renderer.transform.lossyScale)
              .Append(" bounds=").Append(BoundsToString(renderer.bounds))
              .AppendLine();

            if (mesh != null)
            {
                sb.AppendLine(Tag + "   mesh " + mesh.name + " verts=" + mesh.vertexCount +
                              " submeshes=" + mesh.subMeshCount +
                              " mats=" + renderer.sharedMaterials.Length +
                              " attrs=" + AttributeSummary(mesh));
            }
            sb.AppendLine(Tag + "   mats " + MaterialSummary(renderer));

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            string summary = PropertyBlockSummary(block);
            if (summary.Length > 0) sb.AppendLine(Tag + "   propblock " + summary);
        }

        /// <summary>
        /// What the donor animates its own tracks with, and at what scale. LpTracks borrows the donor's tread
        /// sheet, so the sheet's own repeats per metre is the number that says whether our pads come out the
        /// same size as the vanilla tank's - and it is only readable here, not from the export.
        /// </summary>
        private static void DescribeTrackDrive(StringBuilder sb, GameObject unitRoot)
        {
            var controller = unitRoot.GetComponent<NWH.VehiclePhysics.VehicleController>()
                ?? unitRoot.GetComponentInChildren<NWH.VehiclePhysics.VehicleController>(true);
            NWH.VehiclePhysics.Tracks tracks = controller == null ? null : controller.tracks;
            if (tracks == null)
            {
                sb.AppendLine(Tag + " no NWH track controller, so nothing in the game scrolls a track here");
                return;
            }

            sb.AppendLine(Tag + $" trackedVehicle={tracks.trackedVehicle} uvDirection={tracks.uvDirection} " +
                          $"wheelSize={tracks.wheelEnlargementCoefficient:0.##} ourLoops='{LpTracks.Status}'");
            DescribeTrackSide(sb, "left", tracks.leftTrackRenderer);
            DescribeTrackSide(sb, "right", tracks.rightTrackRenderer);
        }

        private static void DescribeTrackSide(StringBuilder sb, string label, Renderer renderer)
        {
            if (renderer == null)
            {
                sb.AppendLine(Tag + $" {label} donor loop: <null renderer>");
                return;
            }

            Material material = null;
            foreach (Material candidate in renderer.sharedMaterials)
                if (candidate != null) { material = candidate; break; }

            var builder = new StringBuilder(Tag + " " + label + " donor loop " + PathOf(renderer.transform) +
                                            " mat '" + (material == null ? "<none>" : material.name) + "'");
            Texture sheet = material == null ? null : material.mainTexture;
            if (sheet == null) builder.Append(" sheet=<none>");
            else
            {
                Vector2 scale = material.mainTextureScale;
                builder.Append($" sheet={sheet.name} {sheet.width}x{sheet.height} wrap={sheet.wrapMode} " +
                               $"scale({scale.x:0.###},{scale.y:0.###})");
            }
            builder.Append(" run/uv:");
            AppendLoopScale(builder, renderer, material == null ? Vector2.one : material.mainTextureScale);
            sb.AppendLine(builder.ToString());
        }

        /// <summary>
        /// The donor loop's stadium perimeter and the UV span laid over it, which together are its repeats per
        /// metre. The material's own tiling multiplies into that span, so both are needed.
        /// </summary>
        private static void AppendLoopScale(StringBuilder sb, Renderer renderer, Vector2 scale)
        {
            Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
                : renderer.GetComponent<MeshFilter>() == null ? null : renderer.GetComponent<MeshFilter>().sharedMesh;
            if (mesh == null) { sb.Append(" uv=<no mesh>"); return; }

            Vector2[] uvs;
            try
            {
                uvs = mesh.uv;
            }
            catch (Exception ex)
            {
                sb.Append(" uv=<unreadable: " + ex.GetType().Name + ">");
                return;
            }
            if (uvs == null || uvs.Length == 0) { sb.Append(" uv=<none>"); return; }

            float lowY = float.MaxValue, highY = float.MinValue, lowX = float.MaxValue, highX = float.MinValue;
            for (int i = 0; i < uvs.Length; i++)
            {
                if (uvs[i].y < lowY) lowY = uvs[i].y;
                if (uvs[i].y > highY) highY = uvs[i].y;
                if (uvs[i].x < lowX) lowX = uvs[i].x;
                if (uvs[i].x > highX) highX = uvs[i].x;
            }

            Bounds box = mesh.bounds;
            float radius = Mathf.Max(0.1f, box.extents.y);
            float perimeter = 2f * Mathf.Max(0f, box.size.z - 2f * radius) + Mathf.PI * 2f * radius;
            if (perimeter < 0.5f) { sb.Append(" uv" + Range(lowX, highX) + "/" + Range(lowY, highY)); return; }

            sb.Append($" run={box.size.z:0.00} m perimeter={perimeter:0.00} m" +
                      " uv" + Range(lowX, highX) + "/" + Range(lowY, highY) +
                      $" scale({scale.x:0.###},{scale.y:0.###})" +
                      $" -> {(highY - lowY) * scale.y / perimeter:0.###} repeat(s) per metre along y, " +
                      $"{(highX - lowX) * scale.x / Mathf.Max(0.01f, box.size.x):0.###} per metre across");
        }

        private static string Range(float low, float high) => $"({low:0.##}..{high:0.##})";

        /// <summary>
        /// A renderer only counts as a leftover while it still owns geometry: the hide pass clears the shared mesh,
        /// and GHPC cannot make an empty renderer draw again.
        /// </summary>
        private static bool HasGeometry(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skin) return DrawsTriangles(skin.sharedMesh);
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null && DrawsTriangles(filter.sharedMesh);
        }

        /// <summary>
        /// A skinned mesh cannot be set to null while the donor's Animator still updates its bones - Unity
        /// faults inside the skinning job - so the strip pass gives it a copy with empty index buffers
        /// instead. Draw-wise that is the same as no mesh, and the log has to agree with the picture.
        /// </summary>
        private static bool DrawsTriangles(Mesh mesh)
        {
            if (mesh == null) return false;
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetIndexCount(i) > 0) return true;
            return false;
        }

        private static string MaterialSummary(Renderer renderer)
        {
            var sb = new StringBuilder();
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                sb.Append(materials[i] == null ? "<null>" : materials[i].name).Append(i + 1 < materials.Length ? "," : "");
            return sb.ToString();
        }

        private static string FloatSummary(Material material)
        {
            var sb = new StringBuilder();
            foreach (string property in FloatProps)
            {
                if (!material.HasProperty(property)) continue;
                sb.Append(property).Append('=').Append(material.GetFloat(property).ToString("0.###")).Append(' ');
            }
            return sb.ToString();
        }

        private static string TextureSummary(Material material)
        {
            var sb = new StringBuilder();
            foreach (string property in TextureProps)
            {
                if (!material.HasProperty(property)) continue;
                Texture texture = material.GetTexture(property);
                sb.Append(property).Append('=');
                if (texture == null) sb.Append("<none>");
                else
                {
                    var texture2D = texture as Texture2D;
                    sb.Append(texture.name).Append('(').Append(texture.width).Append('x').Append(texture.height);
                    if (texture2D != null)
                        sb.Append(',').Append(texture2D.format).Append(',').Append(texture2D.graphicsFormat);
                    sb.Append(',').Append(texture.wrapMode).Append(')');
                }
                sb.Append(' ');
            }
            return sb.ToString();
        }

        private static string AttributeSummary(Mesh mesh)
        {
            var sb = new StringBuilder();
            foreach (VertexAttributeDescriptor descriptor in mesh.GetVertexAttributes())
            {
                if (descriptor.dimension == 0) continue;
                sb.Append(descriptor.attribute).Append('x').Append(descriptor.dimension).Append(',');
            }
            return sb.ToString();
        }

        private static string PropertyBlockSummary(MaterialPropertyBlock block)
        {
            var sb = new StringBuilder();
            foreach (string property in FloatProps)
            {
                float value = block.GetFloat(property);
                if (Mathf.Abs(value) < 1e-5f) continue;
                sb.Append(property).Append('=').Append(value.ToString("0.###")).Append(' ');
            }
            return sb.ToString();
        }

        private static string ColorToString(Color color) =>
            $"({color.r:0.##},{color.g:0.##},{color.b:0.##},{color.a:0.##})";

        private static string BoundsToString(Bounds bounds) =>
            $"c({bounds.center.x:0.##},{bounds.center.y:0.##},{bounds.center.z:0.##})" +
            $"s({bounds.size.x:0.##},{bounds.size.y:0.##},{bounds.size.z:0.##})";

        private static string PathOf(Transform transform)
        {
            var builder = new StringBuilder(transform.name);
            Transform parent = transform.parent;
            int guard = 0;
            while (parent != null && guard++ < 24)
            {
                builder.Insert(0, parent.name + "/");
                parent = parent.parent;
            }
            return builder.ToString();
        }
    }
}

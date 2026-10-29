using MelonLoader;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Leopard2A7V
{
    public enum NormalPackMode
    {
        Raw = 0,
        RedGreen = 1,
        AlphaGreen = 2
    }

    /// <summary>
    /// Builds Unity meshes, textures and materials from the parsed Wavefront data.
    /// </summary>
    public static class LpAssets
    {
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();

        private static NormalPackMode _normalMode = NormalPackMode.Raw;
        private static bool _normalModeDetected;
        private static int _maxTextureSize = 1024;

        public static void ConfigureFromTemplate(Material template)
        {
            if (_normalModeDetected || template == null) return;
            _normalModeDetected = true;
            if (!template.HasProperty("_Normal")) return;
            Texture reference = template.GetTexture("_Normal");
            var texture2D = reference as Texture2D;
            if (texture2D == null) return;

            string format = texture2D.format.ToString();
            MelonLogger.Msg("[L2A7V] vanilla normal map format: " + format);
            // BC5 is the only container that stores x in red; every other compressed format Unity uses
            // for normal maps here keeps the DXT5nm swizzle (x in alpha, y in green), which is what the
            // vehicle shader unpacks.
            if (format.IndexOf("BC5", StringComparison.OrdinalIgnoreCase) >= 0)
                _normalMode = NormalPackMode.RedGreen;
            else if (format.IndexOf("DXT5", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     format.IndexOf("BC7", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     format.IndexOf("BC3", StringComparison.OrdinalIgnoreCase) >= 0)
                _normalMode = NormalPackMode.AlphaGreen;
            else
                _normalMode = NormalPackMode.Raw;
            MelonLogger.Msg("[L2A7V] normal maps packed as: " + _normalMode);
        }

        public static void SetMaxTextureSize(int value)
        {
            _maxTextureSize = Mathf.Clamp(value, 256, 4096);
        }

        public static IEnumerable<KeyValuePair<string, Material>> CreatedMaterials => Materials;
        public static IEnumerable<Mesh> CreatedMeshes => Meshes.Values;

        /// <summary>
        /// Re-points every cached runtime material at the shader the current mission loaded. GHPC releases the
        /// vehicles bundle when a mission ends, and our Materials - which live in a static cache and in the
        /// cloned template under DontDestroyOnLoad - outlive it while their shader reference does not. Repairing
        /// in place is what makes the fix reach the already-spawned vehicles too: their renderers were handed
        /// these same Material objects, so the next frame draws them with the revived shader.
        /// </summary>
        public static int RepairShader(Shader live)
        {
            if (live == null) return 0;
            int repaired = 0;
            foreach (Material material in Materials.Values)
            {
                if (material == null || material.shader == live) continue;
                material.shader = live;
                repaired++;
            }
            return repaired;
        }

        public static Material GetMaterial(int materialIndex, string groupKey)
        {
            string cacheKey = groupKey + "|" + materialIndex;
            Material cached;
            if (Materials.TryGetValue(cacheKey, out cached)) return cached;

            var template = LpVisual.TemplateMaterial;
            if (template == null) return null;

            Material material = Clone(materialIndex, template, LpConfig.NeutralMaps.Value);
            Materials[cacheKey] = material;
            return material;
        }

        private static Material Clone(int materialIndex, Material template, bool neutralMaps)
        {
            MtlDef definition = materialIndex >= 0 && materialIndex < ObjModel.Materials.Count
                ? ObjModel.Materials[materialIndex]
                : null;

            var material = new Material(template) { name = template.name + " (L2A7V " + materialIndex + ")" };
            bool detailMaps = LpConfig.UseDetailMaps.Value;
            bool hasOcclusion = false;
            bool hasGloss = false;
            if (definition != null)
            {
                if (definition.AlbedoPath != null)
                {
                    Texture2D albedo = LoadTexture(definition.AlbedoPath, false, Pack.None, true);
                    if (albedo != null) material.SetTexture("_Albedo", albedo);
                }
                if (definition.NormalPath != null)
                {
                    Texture2D normal = LoadTexture(definition.NormalPath, true, Pack.Normal);
                    if (normal != null) material.SetTexture("_Normal", normal);
                }
                if (detailMaps && definition.OcclusionPath != null)
                {
                    // lp_texpass.py already re-centres these bakes on LEO2_ao's mean offline; no runtime shift.
                    Texture2D occlusion = LoadTexture(definition.OcclusionPath, true, Pack.None);
                    if (occlusion != null)
                    {
                        SetIfPresent(material, "_Occlusion", occlusion);
                        hasOcclusion = true;
                    }
                }
                if (definition.GlossPath != null)
                {
                    // Authored the way LEO2_sm is: RGB near zero, gloss in alpha.
                    Texture2D gloss = LoadTexture(definition.GlossPath, true, Pack.None);
                    if (gloss != null)
                    {
                        SetIfPresent(material, "_Smoothness", gloss);
                        hasGloss = true;
                    }
                }
            }

            if (!hasOcclusion) SetIfPresent(material, "_Occlusion", Texture2D.whiteTexture);
            SetIfPresent(material, "_OcclusionStrength", 1f);

            if (neutralMaps && !hasGloss) SetIfPresent(material, "_Smoothness", NeutralSmoothness);
            if (neutralMaps) SetIfPresent(material, "_PaintMask", NeutralPaintMask);

            // GHPC's paint system leaves these switched on for camouflaged hulls, which makes the shader
            // look for a camo layer our imported UV layout has no relationship to.
            SetIfPresent(material, "_hackcamoUVs", 0f);
            if (material.shaderKeywords != null && material.shaderKeywords.Length > 0)
                material.DisableKeyword("_HACKCAMOUVS_ON");
            SetIfPresent(material, "_CamoAmount", 0f);
            SetIfPresent(material, "_ScorchAmount", 0f);
            SetIfPresent(material, "_snow", 0f);
            SetIfPresent(material, "_dirt", 0f);

            MelonLogger.Msg($"[L2A7V] material {materialIndex} '{definition?.Name ?? "none"}' " +
                            $"albedo={FileNameOf(definition?.AlbedoPath)} normal={FileNameOf(definition?.NormalPath)} " +
                            $"ao={FileNameOf(definition?.OcclusionPath)} gloss={FileNameOf(definition?.GlossPath)} " +
                            $"neutral={neutralMaps}");
            return material;
        }

        private static string FileNameOf(string path) =>
            string.IsNullOrEmpty(path) ? "<none>" : Path.GetFileName(path);

        private static void SetIfPresent(Material material, string property, Texture value)
        {
            if (value != null && material.HasProperty(property)) material.SetTexture(property, value);
        }

        private static void SetIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static Texture2D _neutralPaintMask, _neutralSmoothness;

        private static Texture2D NeutralPaintMask
        {
            get
            {
                if (_neutralPaintMask == null)
                {
                    // R is "paint present"; the T-90A mod authors a flat 161 in G across its whole mask,
                    // so that is the value that reads as an unworn factory finish.
                    _neutralPaintMask = FlatTexture(Level(LpConfig.PaintMaskLevel.Value), 161, 0, 255);
                }
                return _neutralPaintMask;
            }
        }

        private static Texture2D NeutralSmoothness
        {
            get
            {
                if (_neutralSmoothness == null)
                    _neutralSmoothness = FlatTexture(0, 0, 0, Level(LpConfig.SmoothnessLevel.Value));
                return _neutralSmoothness;
            }
        }

        private static byte Level(int value) => (byte)Mathf.Clamp(value, 0, 255);

        private static Texture2D FlatTexture(byte r, byte g, byte b, byte a)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { name = "L2A7V_neutral" };
            var pixels = new Color32[4];
            for (int i = 0; i < 4; i++) pixels[i] = new Color32(r, g, b, a);
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        public static Mesh BuildMesh(LpGroup group, string cacheKey, Vector3 anchor)
        {
            return BuildWhere(part => part.Group == group, cacheKey, anchor);
        }

        /// <summary>
        /// Builds one mesh from whatever geometry the selector accepts, so a caller can split a group into
        /// as many nodes as it needs - one per road wheel, for instance, to let each wheel ride its own bone.
        /// </summary>
        public static Mesh BuildWhere(System.Predicate<ObjGeometry> select, string cacheKey, Vector3 anchor)
        {
            Mesh cached;
            if (Meshes.TryGetValue(cacheKey, out cached)) return cached;

            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var submeshTriangles = new Dictionary<int, List<int>>();
            var materialOrder = new List<int>();

            foreach (ObjGeometry part in ObjModel.Geometries)
            {
                if (!select(part)) continue;

                int offset = positions.Count;
                for (int i = 0; i < part.Positions.Length; i++)
                {
                    positions.Add(part.Positions[i] - anchor);
                    uvs.Add(part.Uvs[i]);
                    normals.Add(part.Normals[i]);
                    tangents.Add(part.Tangents[i]);
                }

                List<int> triangles;
                if (!submeshTriangles.TryGetValue(part.MaterialIndex, out triangles))
                {
                    triangles = new List<int>();
                    submeshTriangles.Add(part.MaterialIndex, triangles);
                    materialOrder.Add(part.MaterialIndex);
                }
                for (int i = 0; i < part.Triangles.Length; i++)
                    triangles.Add(part.Triangles[i] + offset);
            }

            if (positions.Count == 0) return null;

            var mesh = new Mesh { name = "L2A7V_" + cacheKey, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.subMeshCount = materialOrder.Count;
            for (int i = 0; i < materialOrder.Count; i++)
                mesh.SetTriangles(submeshTriangles[materialOrder[i]], i);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);

            LpVisual.GroupMaterials[cacheKey] = materialOrder;
            Meshes[cacheKey] = mesh;
            return mesh;
        }

        private enum Pack
        {
            None,
            Normal
        }

        private static Texture2D LoadTexture(string path, bool linear, Pack pack, bool isAlbedo = false)
        {
            string key = path + "|" + pack + (pack == Pack.Normal ? "|" + _normalMode : string.Empty)
                + "|" + _maxTextureSize
                + "|" + (isAlbedo ? LpConfig.AlbedoLuminance.Value : -1);
            Texture2D cached;
            if (Textures.TryGetValue(key, out cached)) return cached;

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (!ImageConversion.LoadImage(source, bytes))
                {
                    UnityEngine.Object.Destroy(source);
                    return null;
                }

                int width = source.width;
                int height = source.height;
                Color32[] pixels = Resample(source.GetPixels32(), width, height,
                    Mathf.Min(width, _maxTextureSize), Mathf.Min(height, _maxTextureSize), out width, out height);
                UnityEngine.Object.Destroy(source);

                if (pack == Pack.Normal) pixels = PackNormals(pixels);
                if (isAlbedo) pixels = MatchAlbedoLuminance(pixels, path);

                var result = new Texture2D(width, height, TextureFormat.RGBA32, true, linear)
                {
                    name = Path.GetFileNameWithoutExtension(path),
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4,
                    wrapMode = TextureWrapMode.Repeat
                };
                result.SetPixels32(pixels);
                result.Apply(true);
                Textures[key] = result;
                return result;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[L2A7V] texture load failed for " + path + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Optional fallback that pulls a raw imported albedo down into GHPC's brightness band: the donor's
        /// LEO2_co measures 42/42/35 per channel, where an unprocessed War Thunder diffuse sits at 101/90/80,
        /// which reads as a milky film once GHPC's exposure is applied. Scaling the stored sRGB bytes is
        /// equivalent to scaling linear reflectance. The shipped maps already come out of lp_texpass.py at
        /// the donor tone, so AlbedoLuminance defaults to 0 and this only runs for hand-dropped textures.
        /// </summary>
        private static Color32[] MatchAlbedoLuminance(Color32[] source, string path)
        {
            int target = LpConfig.AlbedoLuminance.Value;
            if (target <= 0) return source;

            long sum = 0;
            for (int i = 0; i < source.Length; i++)
                sum += source[i].r + source[i].g + source[i].b;
            float current = sum / (source.Length * 3f);
            if (current < 1f) return source;

            float factor = Mathf.Clamp(target / current, 0.55f, 1f);
            if (factor > 0.995f) return source;

            for (int i = 0; i < source.Length; i++)
            {
                Color32 pixel = source[i];
                source[i] = new Color32(
                    (byte)Mathf.Clamp(pixel.r * factor, 0f, 255f),
                    (byte)Mathf.Clamp(pixel.g * factor, 0f, 255f),
                    (byte)Mathf.Clamp(pixel.b * factor, 0f, 255f),
                    pixel.a);
            }
            MelonLogger.Msg($"[L2A7V] albedo {Path.GetFileName(path)} luminance " +
                            $"{current:F0} -> {current * factor:F0} (x{factor:F2})");
            return source;
        }

        private static Color32[] PackNormals(Color32[] source)
        {
            if (_normalMode == NormalPackMode.Raw) return source;
            var result = new Color32[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Color32 pixel = source[i];
                byte x = pixel.r;
                byte y = pixel.g;
                if (_normalMode == NormalPackMode.RedGreen)
                    result[i] = new Color32(x, y, 128, 255);
                else
                    result[i] = new Color32(255, y, 127, x);
            }
            return result;
        }

        private static Color32[] Resample(Color32[] source, int sourceWidth, int sourceHeight,
            int targetWidth, int targetHeight, out int outWidth, out int outHeight)
        {
            outWidth = targetWidth;
            outHeight = targetHeight;
            if (targetWidth == sourceWidth && targetHeight == sourceHeight) return source;

            var result = new Color32[targetWidth * targetHeight];
            float stepX = (float)sourceWidth / targetWidth;
            float stepY = (float)sourceHeight / targetHeight;
            for (int y = 0; y < targetHeight; y++)
            {
                int yStart = Mathf.Clamp((int)(y * stepY), 0, sourceHeight - 1);
                int yEnd = Mathf.Max(yStart + 1, Mathf.CeilToInt((y + 1) * stepY));
                yEnd = Mathf.Min(yEnd, sourceHeight);
                for (int x = 0; x < targetWidth; x++)
                {
                    int xStart = Mathf.Clamp((int)(x * stepX), 0, sourceWidth - 1);
                    int xEnd = Mathf.Max(xStart + 1, Mathf.CeilToInt((x + 1) * stepX));
                    xEnd = Mathf.Min(xEnd, sourceWidth);

                    int rSum = 0, gSum = 0, bSum = 0, aSum = 0, count = 0;
                    for (int sy = yStart; sy < yEnd; sy++)
                    {
                        int row = sy * sourceWidth;
                        for (int sx = xStart; sx < xEnd; sx++)
                        {
                            Color32 pixel = source[row + sx];
                            rSum += pixel.r;
                            gSum += pixel.g;
                            bSum += pixel.b;
                            aSum += pixel.a;
                            count++;
                        }
                    }
                    result[y * targetWidth + x] = new Color32(
                        (byte)(rSum / count), (byte)(gSum / count), (byte)(bSum / count), (byte)(aSum / count));
                }
            }
            return result;
        }
    }
}

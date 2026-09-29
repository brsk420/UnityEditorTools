using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace Brsk420.EditorTools
{
    /// <summary>
    /// Builds a full-color TMP_FontAsset (AtlasPopulationMode.Static, Sprite shader) from a set
    /// of individually authored glyph sprites, so the result can be typed as plain text
    /// (no &lt;sprite&gt; tags). Assumes all source sprites share the same canvas height so a
    /// single shared baseline is valid across the whole set.
    /// </summary>
    public static class BitmapFontBuilder
    {
        public class GlyphSource
        {
            public string assetPath;
            public char character;
        }

        private struct Placement
        {
            public GlyphSource source;
            public Texture2D texture;
            public int x, y, width, height; // bottom-left origin, atlas space
        }

        public static void Build(List<GlyphSource> glyphs, string outputFolder, string fontAssetName, int atlasSize, int padding)
        {
            if (glyphs == null || glyphs.Count == 0)
            {
                Debug.LogError("[BitmapFontBuilder] No glyphs to build.");
                return;
            }

            EnsureFolderExists(outputFolder);

            var textures = LoadReadableTextures(glyphs);
            if (textures.Count == 0)
            {
                Debug.LogError("[BitmapFontBuilder] None of the source sprites could be loaded.");
                return;
            }

            int commonHeight = textures[0].tex.height;

            var placements = PackShelf(textures, atlasSize, padding, out int atlasW, out int atlasH);

            var atlasTexture = CompositeAtlas(placements, atlasW, atlasH);
            string atlasAssetPath = CombinePath(outputFolder, fontAssetName + "_Atlas.png");
            var savedAtlas = SaveAtlasTexture(atlasTexture, atlasAssetPath);
            Object.DestroyImmediate(atlasTexture);

            var shader = Shader.Find("TextMeshPro/Sprite");
            if (shader == null)
            {
                Debug.LogError("[BitmapFontBuilder] Shader 'TextMeshPro/Sprite' not found.");
                return;
            }

            var material = new Material(shader) { name = fontAssetName + " Material", mainTexture = savedAtlas };

            var newAsset = ScriptableObject.CreateInstance<TMP_FontAsset>();
            newAsset.name = fontAssetName;
            SetInternalField(newAsset, "m_Version", "1.1.0"); // otherwise TMP thinks this is a legacy asset and tries to upgrade it from the (unused) legacy m_fontInfo field, which is null and crashes
            newAsset.atlasTextures = new[] { savedAtlas };

            SetInternalField(newAsset, "m_AtlasWidth", atlasW);
            SetInternalField(newAsset, "m_AtlasHeight", atlasH);
            SetInternalField(newAsset, "m_AtlasPadding", padding);
            SetInternalField(newAsset, "m_AtlasRenderMode", GlyphRenderMode.COLOR);
            newAsset.atlasPopulationMode = AtlasPopulationMode.Static;
            newAsset.isMultiAtlasTexturesEnabled = false;

            var fi = newAsset.faceInfo;
            fi.familyName = fontAssetName;
            fi.styleName = "Regular";
            fi.pointSize = commonHeight;
            fi.scale = 1f;
            fi.lineHeight = commonHeight + padding;
            fi.ascentLine = commonHeight;
            fi.capLine = commonHeight;
            fi.baseline = 0;
            fi.descentLine = 0;
            fi.underlineOffset = -Mathf.Max(2f, commonHeight * 0.05f);
            fi.underlineThickness = 2f;
            fi.strikethroughOffset = commonHeight * 0.4f;
            fi.strikethroughThickness = 2f;
            fi.superscriptOffset = commonHeight * 0.5f;
            fi.superscriptSize = 0.5f;
            fi.subscriptOffset = -commonHeight * 0.2f;
            fi.subscriptSize = 0.5f;
            fi.tabWidth = commonHeight * 2f;
            newAsset.faceInfo = fi;
            TrySetFieldOnStructField(newAsset, "m_FaceInfo", "unitsPerEM", 1000);

            uint glyphIndex = 1;
            foreach (var p in placements)
            {
                var metrics = new GlyphMetrics
                {
                    width = p.width,
                    height = p.height,
                    horizontalBearingX = 0,
                    horizontalBearingY = p.height, // full source canvas => baseline sits at its bottom edge
                    horizontalAdvance = p.width + padding
                };
                var glyphRect = new GlyphRect { x = p.x, y = p.y, width = p.width, height = p.height };
                var glyph = new Glyph { index = glyphIndex, metrics = metrics, glyphRect = glyphRect, atlasIndex = 0 };

                newAsset.glyphTable.Add(glyph);
                newAsset.characterTable.Add(new TMP_Character((uint)p.source.character, glyph));
                glyphIndex++;
            }

            newAsset.creationSettings = new FontAssetCreationSettings
            {
                sourceFontFileName = fontAssetName,
                sourceFontFileGUID = "",
                pointSize = commonHeight,
                padding = padding,
                atlasWidth = atlasW,
                atlasHeight = atlasH,
                characterSetSelectionMode = 7,
                renderMode = 6
            };

            newAsset.material = material;
            newAsset.ReadFontAssetDefinition();

            string fontAssetPath = CombinePath(outputFolder, fontAssetName + ".asset");
            var finalAsset = SaveOrUpdateFontAsset(newAsset, material, fontAssetPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(fontAssetPath, ImportAssetOptions.ForceUpdate);
            EditorGUIUtility.PingObject(finalAsset);

            Debug.Log($"[BitmapFontBuilder] '{fontAssetName}' ready at {fontAssetPath} — {placements.Count} glyphs, atlas {atlasW}x{atlasH}.");
        }

        private static List<(GlyphSource src, Texture2D tex)> LoadReadableTextures(List<GlyphSource> glyphs)
        {
            var result = new List<(GlyphSource src, Texture2D tex)>();
            foreach (var g in glyphs)
            {
                var importer = AssetImporter.GetAtPath(g.assetPath) as TextureImporter;
                if (importer != null && (!importer.isReadable
                    || importer.textureCompression != TextureImporterCompression.Uncompressed
                    || importer.npotScale != TextureImporterNPOTScale.None))
                {
                    importer.isReadable = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed; // compressed source pixels bake blur straight into the atlas
                    importer.crunchedCompression = false;
                    importer.npotScale = TextureImporterNPOTScale.None; // default NPOT resampling would resample sprites at import time
                    importer.SaveAndReimport();
                }

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(g.assetPath);
                if (tex == null)
                {
                    Debug.LogError($"[BitmapFontBuilder] Could not load '{g.assetPath}', skipping.");
                    continue;
                }
                result.Add((g, tex));
            }
            return result;
        }

        private static List<Placement> PackShelf(List<(GlyphSource src, Texture2D tex)> textures, int atlasSize, int padding, out int atlasW, out int atlasH)
        {
            var raw = new List<(GlyphSource src, Texture2D tex, int topX, int topY)>();

            int cursorX = padding, cursorY = padding, rowHeight = 0, usedWidth = 0, usedHeight = 0;
            foreach (var (src, tex) in textures)
            {
                if (cursorX + tex.width + padding > atlasSize)
                {
                    cursorX = padding;
                    cursorY += rowHeight + padding;
                    rowHeight = 0;
                }

                raw.Add((src, tex, cursorX, cursorY));
                cursorX += tex.width + padding;
                rowHeight = Mathf.Max(rowHeight, tex.height);
                usedWidth = Mathf.Max(usedWidth, cursorX);
                usedHeight = Mathf.Max(usedHeight, cursorY + rowHeight + padding);
            }

            atlasW = Mathf.Min(Mathf.NextPowerOfTwo(usedWidth), 8192);
            atlasH = Mathf.Min(Mathf.NextPowerOfTwo(usedHeight), 8192);

            var placements = new List<Placement>(raw.Count);
            foreach (var (src, tex, topX, topY) in raw)
            {
                int bottomY = atlasH - topY - tex.height; // flip: our packing grows top-down, atlas/GlyphRect origin is bottom-left
                placements.Add(new Placement { source = src, texture = tex, x = topX, y = bottomY, width = tex.width, height = tex.height });
            }
            return placements;
        }

        private static Texture2D CompositeAtlas(List<Placement> placements, int atlasW, int atlasH)
        {
            var pixels = new Color32[atlasW * atlasH];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0, 0);

            foreach (var p in placements)
            {
                var src = p.texture.GetPixels32();
                for (int y = 0; y < p.height; y++)
                {
                    for (int x = 0; x < p.width; x++)
                    {
                        int dx = p.x + x;
                        int dy = p.y + y;
                        if (dx < 0 || dx >= atlasW || dy < 0 || dy >= atlasH) continue;
                        pixels[dy * atlasW + dx] = src[y * p.width + x];
                    }
                }
            }

            var tex = new Texture2D(atlasW, atlasH, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D SaveAtlasTexture(Texture2D atlasTexture, string assetPath)
        {
            string absPath = ToAbsolutePath(assetPath);
            File.WriteAllBytes(absPath, ImageConversion.EncodeToPNG(atlasTexture));
            AssetDatabase.ImportAsset(assetPath);

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true; // without mips, downscaling the rendered text (common for UI) looks muddy under plain bilinear minification
                importer.streamingMipmaps = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.crunchedCompression = false;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static TMP_FontAsset SaveOrUpdateFontAsset(TMP_FontAsset newAsset, Material material, string fontAssetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontAssetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(newAsset, fontAssetPath);
                AssetDatabase.AddObjectToAsset(material, newAsset);
                EditorUtility.SetDirty(newAsset);
                return newAsset;
            }

            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(fontAssetPath))
            {
                if (sub is Material)
                    Object.DestroyImmediate(sub, true);
            }

            EditorUtility.CopySerialized(newAsset, existing);
            Object.DestroyImmediate(newAsset);
            AssetDatabase.AddObjectToAsset(material, existing);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void EnsureFolderExists(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath)) return;
            int slash = assetFolderPath.LastIndexOf('/');
            string parent = assetFolderPath.Substring(0, slash);
            string leaf = assetFolderPath.Substring(slash + 1);
            EnsureFolderExists(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static string CombinePath(string folder, string fileName) => folder.TrimEnd('/') + "/" + fileName;

        private static string ToAbsolutePath(string assetPath)
        {
            return Application.dataPath + assetPath.Substring("Assets".Length);
        }

        /// <summary>Sets a private/internal backing field via reflection (atlas dimensions/render mode have no public setter).</summary>
        private static void SetInternalField(object obj, string fieldName, object value)
        {
            var field = obj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
                field.SetValue(obj, value);
            else
                Debug.LogWarning($"[BitmapFontBuilder] Could not set field '{fieldName}' on {obj.GetType().Name}");
        }

        /// <summary>Sets a nested field on a struct field (FaceInfo is a value type, so it must be boxed, edited, and written back).</summary>
        private static void TrySetFieldOnStructField(object obj, string outerFieldName, string innerFieldName, object value)
        {
            var outerField = obj.GetType().GetField(outerFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (outerField == null) return;

            object boxed = outerField.GetValue(obj);
            if (boxed == null) return;

            var innerField = boxed.GetType().GetField(innerFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (innerField == null) return;

            innerField.SetValue(boxed, value);
            outerField.SetValue(obj, boxed);
        }
    }
}

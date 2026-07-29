using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using Object = UnityEngine.Object;

namespace _Brsk420.EditorTools
{
    internal static class DownscaleTool
    {
        // Quality (%) presets. Label shows the reduction, value is the kept quality.
        [MenuItem("Assets/_BrskTools/Textures/Downscale/Downscale textures (-25%)", false, -1000)]
        private static void DownscaleSelected_25()
        {
            DownscaleSelection(75);
        }

        [MenuItem("Assets/_BrskTools/Textures/Downscale/Downscale textures (-50%)", false, -999)]
        private static void DownscaleSelected_50()
        {
            DownscaleSelection(50);
        }

        [MenuItem("Assets/_BrskTools/Textures/Downscale/Downscale textures (-75%)", false, -998)]
        private static void DownscaleSelected_75()
        {
            DownscaleSelection(25);
        }

        [MenuItem("Assets/_BrskTools/Textures/Downscale/Custom…", false, -997)]
        private static void OpenCustomWindow()
        {
            DownscaleWindow.Open();
        }

        /// <summary>
        /// Collects all .png asset paths from the current Project selection
        /// (individual Texture2D/Sprite assets and, recursively, any selected folders).
        /// </summary>
        internal static HashSet<string> CollectSelectedTexturePaths()
        {
            var textures = new HashSet<string>();

            foreach (var obj in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(obj);

                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (obj is Texture2D || obj is Sprite)
                {
                    if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        textures.Add(path);
                    }

                    continue;
                }

                if (AssetDatabase.IsValidFolder(path))
                {
                    textures.UnionWith(Directory.GetFiles(path, "*.png", SearchOption.AllDirectories));
                }
            }

            return textures;
        }

        /// <summary>
        /// Downscales every selected .png to <paramref name="quality"/> percent of its size.
        /// </summary>
        internal static void DownscaleSelection(int quality)
        {
            quality = Mathf.Clamp(quality, 1, 100);

            var textures = CollectSelectedTexturePaths();
            if (textures.Count == 0)
            {
                return;
            }

            if (quality >= 100)
            {
                // Nothing to do at full quality.
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing();

                int index = 1;

                foreach (var path in textures)
                {
                    EditorUtility.DisplayProgressBar($"Downscale textures ({index}/{textures.Count})", path, (float)index++ / textures.Count);
                    Downscale(path, quality);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
            }
        }

        private static void Downscale(string path, int multiplier)
        {
            var bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);

            try
            {
                texture.LoadImage(bytes);

                int targetWidth = Mathf.Max(1, texture.width * multiplier / 100);
                int targetHeight = Mathf.Max(1, texture.height * multiplier / 100);

                var rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
                var state = RenderTexture.active;

                RenderTexture.active = rt;
                Graphics.Blit(texture, rt);

                var result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false, false);
                result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);

                RenderTexture.active = state;
                RenderTexture.ReleaseTemporary(rt);

                bytes = result.EncodeToPNG();
                Object.DestroyImmediate(result);

                File.WriteAllBytes(path, bytes);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
            {
                return;
            }

            float factor = multiplier / 100f;

            if (importer.textureType == TextureImporterType.Sprite)
            {
                ScaleSpriteMetadata(importer, factor);
            }

            importer.spritePixelsPerUnit *= factor;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Sprite rects, borders, 2D Animation bones, custom mesh vertices, and outline/physics
        /// shapes are all stored in pixel space on the importer. They don't get rescaled when the
        /// texture is resized, so they'd desync from the new pixel grid (and the proportionally
        /// adjusted PPU) unless scaled here by the same factor.
        /// </summary>
        private static void ScaleSpriteMetadata(TextureImporter importer, float factor)
        {
            var factories = new SpriteDataProviderFactories();
            factories.Init();

            var dataProvider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (dataProvider == null)
            {
                return;
            }

            dataProvider.InitSpriteEditorDataProvider();

            var spriteRects = dataProvider.GetSpriteRects();

            foreach (var spriteRect in spriteRects)
            {
                var rect = spriteRect.rect;
                rect.x *= factor;
                rect.y *= factor;
                rect.width *= factor;
                rect.height *= factor;
                spriteRect.rect = rect;
                spriteRect.border *= factor;
            }

            dataProvider.SetSpriteRects(spriteRects);

            var boneProvider = dataProvider.GetDataProvider<ISpriteBoneDataProvider>();
            var meshProvider = dataProvider.GetDataProvider<ISpriteMeshDataProvider>();
            var outlineProvider = dataProvider.GetDataProvider<ISpriteOutlineDataProvider>();
            var physicsProvider = dataProvider.GetDataProvider<ISpritePhysicsOutlineDataProvider>();

            foreach (var spriteRect in spriteRects)
            {
                var guid = spriteRect.spriteID;

                if (boneProvider != null)
                {
                    var bones = boneProvider.GetBones(guid);
                    if (bones != null && bones.Count > 0)
                    {
                        for (int i = 0; i < bones.Count; i++)
                        {
                            var bone = bones[i];
                            bone.position *= factor;
                            bone.length *= factor;
                            bones[i] = bone;
                        }

                        boneProvider.SetBones(guid, bones);
                    }
                }

                if (meshProvider != null)
                {
                    var vertices = meshProvider.GetVertices(guid);
                    if (vertices != null && vertices.Length > 0)
                    {
                        for (int i = 0; i < vertices.Length; i++)
                        {
                            vertices[i].position *= factor;
                        }

                        // Indices/edges are topology, not positions, so they're unchanged — but
                        // Apply() requires all three to be set together or it null-refs internally.
                        var indices = meshProvider.GetIndices(guid);
                        var edges = meshProvider.GetEdges(guid);

                        meshProvider.SetVertices(guid, vertices);
                        meshProvider.SetIndices(guid, indices);
                        meshProvider.SetEdges(guid, edges);
                    }
                }

                if (outlineProvider != null)
                {
                    var outlines = outlineProvider.GetOutlines(guid);
                    if (outlines != null && outlines.Count > 0)
                    {
                        foreach (var outline in outlines)
                        {
                            for (int i = 0; i < outline.Length; i++)
                            {
                                outline[i] *= factor;
                            }
                        }

                        outlineProvider.SetOutlines(guid, outlines);
                    }
                }

                if (physicsProvider != null)
                {
                    var shapes = physicsProvider.GetOutlines(guid);
                    if (shapes != null && shapes.Count > 0)
                    {
                        foreach (var shape in shapes)
                        {
                            for (int i = 0; i < shape.Length; i++)
                            {
                                shape[i] *= factor;
                            }
                        }

                        physicsProvider.SetOutlines(guid, shapes);
                    }
                }
            }

            dataProvider.Apply();
        }
    }
}

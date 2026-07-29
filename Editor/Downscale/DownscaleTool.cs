using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
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

            importer.spritePixelsPerUnit *= multiplier / 100f;
            importer.SaveAndReimport();
        }
    }
}

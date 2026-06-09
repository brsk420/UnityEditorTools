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
        [MenuItem("Assets/_BrskTools/Textures/Downscale/Downscale textures (-5%)")]
        private static void DownscaleSelected_5()
        {
            Downscale(95);
        }

        [MenuItem("Assets/_BrskTools/Textures/Downscale/Downscale textures (-10%)")]
        private static void DownscaleSelected_10()
        {
            Downscale(90);
        }

        [MenuItem("Assets/_BrskTools/Textures/Downscale/Downscale textures (-25%)")]
        private static void DownscaleSelected_25()
        {
            Downscale(75);
        }

        [MenuItem("Assets/_BrskTools/Textures/Downscale/Downscale textures (-30%)")]
        private static void DownscaleSelected_30()
        {
            Downscale(70);
        }

        [MenuItem("Assets/_BrskTools/Textures/Downscale/Downscale textures (-50%)")]
        private static void DownscaleSelected_50()
        {
            Downscale(50);
        }

        [MenuItem("Assets/_BrskTools/Textures/Downscale/Downscale textures (-75%)")]
        private static void DownscaleSelected_75()
        {
            Downscale(25);
        }

        private static void Downscale(int multiplier)
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

            try
            {
                AssetDatabase.StartAssetEditing();

                int index = 1;

                foreach (var path in textures)
                {
                    EditorUtility.DisplayProgressBar($"Downscale textures ({index}/{textures.Count})", path, (float)index++ / textures.Count);
                    Downscale(path, multiplier);
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

                int targetWidth = texture.width * multiplier / 100;
                int targetHeight = texture.height * multiplier / 100;

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
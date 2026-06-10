using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace _Brsk420.EditorTools
{
    internal static class MakeTextureDividedByFour
    {
        [MenuItem("Assets/_BrskTools/Textures/MakeTextureDividedByFour")]
        private static void Run()
        {
            ProcessSelectedTextures();
        }

        private static void ProcessSelectedTextures()
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

                var index = 1;

                foreach (var path in textures)
                {
                    EditorUtility.DisplayProgressBar($"Downscale textures ({index}/{textures.Count})", path,
                                                     (float)index++ / textures.Count);
                    FixDimension(path);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
            }
        }

        private static void FixDimension(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);

            try
            {
                texture.LoadImage(bytes);
                var targetHeight = texture.height;
                var targetWidth = texture.width;

                if (texture.width % 4 != 0)
                {
                    targetWidth = texture.width + (4 - (texture.width % 4));
                }

                if (texture.height % 4 != 0)
                {
                    targetHeight = texture.height + (4 - (texture.height % 4));
                }

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

            importer.SaveAndReimport();
        }
    }
}
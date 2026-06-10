using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace _Brsk420.EditorTools
{
    internal static class SpriteVerticalCutter
    {
        private const string MenuPath = "Assets/_BrskTools/Sprites/Cut Vertically (Keep Left Half)";

        [MenuItem(MenuPath)]
        private static void CutSelected()
        {
            var texturePaths = CollectTexturePathsFromSelection();

            if (texturePaths.Count == 0)
            {
                EditorUtility.DisplayDialog("Sprite Vertical Cutter", "Выберите один или несколько спрайтов или текстур.", "OK");
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing();

                int index = 1;
                foreach (var path in texturePaths)
                {
                    EditorUtility.DisplayProgressBar(
                        $"Режем текстуры ({index}/{texturePaths.Count})",
                        path,
                        (float)index / texturePaths.Count);

                    index++;
                    CutTextureInHalf(path);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool CutSelectedValidate()
        {
            foreach (var obj in Selection.objects)
            {
                if (obj is Sprite || obj is Texture2D)
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<string> CollectTexturePathsFromSelection()
        {
            var textures = new HashSet<string>();

            foreach (var obj in Selection.objects)
            {
                if (obj == null) continue;

                if (obj is Sprite sprite)
                {
                    var path = AssetDatabase.GetAssetPath(sprite.texture);
                    if (!string.IsNullOrEmpty(path))
                    {
                        textures.Add(path);
                    }
                }
                else if (obj is Texture2D tex)
                {
                    var path = AssetDatabase.GetAssetPath(tex);
                    if (!string.IsNullOrEmpty(path))
                    {
                        textures.Add(path);
                    }
                }
            }

            return textures;
        }

        private static void CutTextureInHalf(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            byte[] bytes;

            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException e)
            {
                Debug.LogError($"SpriteVerticalCutter: Не удалось прочитать файл {path}: {e.Message}");
                return;
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);

            try
            {
                if (!source.LoadImage(bytes))
                {
                    Debug.LogError($"SpriteVerticalCutter: Не удалось загрузить текстуру из {path}.");
                    return;
                }

                if (source.width < 2)
                {
                    Debug.LogWarning($"SpriteVerticalCutter: Ширина текстуры слишком мала для разрезания: {path}");
                    return;
                }

                var halfWidth = source.width / 2;
                var height = source.height;

                var result = new Texture2D(halfWidth, height, TextureFormat.RGBA32, false, false);

                // Берём левую половину (x = 0 .. halfWidth)
                var pixels = source.GetPixels(0, 0, halfWidth, height);
                result.SetPixels(pixels);
                result.Apply();

                var outBytes = result.EncodeToPNG();
                Object.DestroyImmediate(result);

                File.WriteAllBytes(path, outBytes);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.RightCenter;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
        }
    }
}


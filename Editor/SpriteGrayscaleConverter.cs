using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace _Brsk420.EditorTools
{
    internal static class SpriteGrayscaleConverter
    {
        private const string MenuPath = "Assets/_BrskTools/Sprites/Make Grayscale";

        [MenuItem(MenuPath)]
        private static void MakeSelectedGrayscale()
        {
            var texturePaths = CollectTexturePathsFromSelection();

            if (texturePaths.Count == 0)
            {
                EditorUtility.DisplayDialog("Sprite Grayscale Converter", "Выберите один или несколько спрайтов или текстур.", "OK");
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing();

                int index = 1;
                foreach (var path in texturePaths)
                {
                    EditorUtility.DisplayProgressBar(
                        $"Конвертируем в ЧБ ({index}/{texturePaths.Count})",
                        path,
                        (float)index / texturePaths.Count);

                    index++;
                    ConvertTextureToGrayscale(path);
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
        private static bool MakeSelectedGrayscaleValidate()
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

        private static void ConvertTextureToGrayscale(string path)
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
                Debug.LogError($"SpriteGrayscaleConverter: Не удалось прочитать файл {path}: {e.Message}");
                return;
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);

            try
            {
                if (!source.LoadImage(bytes))
                {
                    Debug.LogError($"SpriteGrayscaleConverter: Не удалось загрузить текстуру из {path}.");
                    return;
                }

                var pixels = source.GetPixels();

                for (int i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i];
                    // Стандартная формула яркости
                    float gray = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                    pixels[i] = new Color(gray, gray, gray, c.a);
                }

                source.SetPixels(pixels);
                source.Apply();

                var outBytes = source.EncodeToPNG();
                File.WriteAllBytes(path, outBytes);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.SaveAndReimport();
            }
        }
    }
}


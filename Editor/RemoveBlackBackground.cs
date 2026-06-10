using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace _Brsk420.EditorTools
{
    internal static class RemoveBlackBackground
    {
        [MenuItem("Assets/_BrskTools/Textures/Remove Black Background (exact)")]
        private static void RemoveExact() => Process(0);

        [MenuItem("Assets/_BrskTools/Textures/Remove Black Background (threshold 10)")]
        private static void RemoveThreshold10() => Process(10);

        [MenuItem("Assets/_BrskTools/Textures/Remove Black Background (threshold 25)")]
        private static void RemoveThreshold25() => Process(25);

        private static void Process(int threshold)
        {
            var paths = new HashSet<string>();

            foreach (var obj in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(obj);

                if (string.IsNullOrEmpty(path))
                    continue;

                if (obj is Texture2D || obj is Sprite)
                {
                    if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        paths.Add(path);

                    continue;
                }

                if (AssetDatabase.IsValidFolder(path))
                    paths.UnionWith(Directory.GetFiles(path, "*.png", SearchOption.AllDirectories));
            }

            if (paths.Count == 0)
            {
                Debug.LogWarning("RemoveBlackBackground: no PNG textures selected.");
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing();
                int index = 1;

                foreach (var path in paths)
                {
                    EditorUtility.DisplayProgressBar(
                        $"Remove Black Background ({index}/{paths.Count})",
                        path,
                        (float)index++ / paths.Count);

                    ProcessTexture(path, threshold);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
            }
        }

        private static void ProcessTexture(string path, int threshold)
        {
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);

            try
            {
                tex.LoadImage(bytes);

                var pixels = tex.GetPixels32();
                bool changed = false;

                for (int i = 0; i < pixels.Length; i++)
                {
                    ref var p = ref pixels[i];

                    if (p.r <= threshold && p.g <= threshold && p.b <= threshold)
                    {
                        p.a = 0;
                        changed = true;
                    }
                }

                if (!changed)
                    return;

                tex.SetPixels32(pixels);
                tex.Apply();

                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
                return;

            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
    }
}

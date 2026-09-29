using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace _Brsk420.EditorTools
{
    /// <summary>
    /// Small editor window for the Downscale Tool that lets you pick an arbitrary quality
    /// (1–100%) with a slider instead of the fixed presets, then apply it to the current
    /// Project selection. The chosen quality is remembered between sessions.
    /// </summary>
    internal sealed class DownscaleWindow : EditorWindow
    {
        private const string QualityPrefKey = "Brsk420.DownscaleWindow.Quality";

        private int _quality = 50;

        public static void Open()
        {
            var window = GetWindow<DownscaleWindow>(true, "Downscale Textures", true);
            window.minSize = new Vector2(320, 170);
            window.maxSize = new Vector2(600, 220);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            _quality = Mathf.Clamp(EditorPrefs.GetInt(QualityPrefKey, 50), 1, 100);
        }

        private void OnDisable()
        {
            EditorPrefs.SetInt(QualityPrefKey, _quality);
        }

        // Repaint when the Project selection changes so the count/size info stays fresh.
        private void OnSelectionChange()
        {
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "Пережимает выбранные PNG (и PNG внутри выбранных папок) до заданного качества.\n" +
                "Применяется к текущему выделению в Project. Изменяет сами ассеты!",
                MessageType.Info);

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            _quality = EditorGUILayout.IntSlider("Quality (%)", _quality, 1, 100);
            if (EditorGUI.EndChangeCheck())
            {
                _quality = Mathf.Clamp(_quality, 1, 100);
                EditorPrefs.SetInt(QualityPrefKey, _quality);
            }

            DrawSelectionInfo(out int textureCount);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(textureCount == 0 || _quality >= 100))
            {
                if (GUILayout.Button($"Downscale {textureCount} texture(s) to {_quality}%", GUILayout.Height(28)))
                {
                    DownscaleTool.DownscaleSelection(_quality);
                    // Refresh the size preview after the assets changed.
                    Repaint();
                }
            }

            if (_quality >= 100)
            {
                EditorGUILayout.HelpBox("100% = без изменений.", MessageType.None);
            }
            else if (textureCount == 0)
            {
                EditorGUILayout.HelpBox("Нет выбранных PNG или папок с PNG.", MessageType.Warning);
            }
        }

        private void DrawSelectionInfo(out int textureCount)
        {
            HashSet<string> paths = DownscaleTool.CollectSelectedTexturePaths();
            textureCount = paths.Count;

            EditorGUILayout.LabelField("Selected PNGs", textureCount.ToString());

            // Show a size example using the first texture in the selection.
            foreach (var path in paths)
            {
                var size = ReadPngSize(path);
                if (size.x <= 0 || size.y <= 0)
                {
                    break;
                }

                int w = Mathf.Max(1, (int)size.x * _quality / 100);
                int h = Mathf.Max(1, (int)size.y * _quality / 100);

                EditorGUILayout.LabelField(
                    "Example",
                    $"{(int)size.x}x{(int)size.y}  →  {w}x{h}   ({Path.GetFileName(path)})");
                break;
            }
        }

        /// <summary>
        /// Reads a PNG's pixel dimensions straight from the file header without importing it.
        /// </summary>
        private static Vector2Int ReadPngSize(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);

                // PNG: 8-byte signature, then IHDR chunk: 4-byte length, 4-byte "IHDR",
                // then width (4 bytes, big-endian) and height (4 bytes, big-endian).
                stream.Seek(16, SeekOrigin.Begin);

                int width = ReadBigEndianInt32(reader);
                int height = ReadBigEndianInt32(reader);

                return new Vector2Int(width, height);
            }
            catch
            {
                return new Vector2Int(0, 0);
            }
        }

        private static int ReadBigEndianInt32(BinaryReader reader)
        {
            var b = reader.ReadBytes(4);
            if (b.Length < 4)
            {
                return 0;
            }

            return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Brsk420.EditorTools
{
    /// <summary>
    /// Drop a folder of individually authored glyph sprites, review/fix the auto-detected
    /// character mapping, and build a full-color TMP_FontAsset that can be typed as plain text.
    /// </summary>
    public class BitmapFontBuilderWindow : EditorWindow
    {
        private static readonly Dictionary<string, string> SpecialCharacterNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "colon", ":" }, { "comma", "," }, { "dot", "." }, { "period", "." },
                { "space", " " }, { "slash", "/" }, { "backslash", "\\" },
                { "percent", "%" }, { "plus", "+" }, { "minus", "-" }, { "dash", "-" },
                { "star", "*" }, { "asterisk", "*" }, { "hash", "#" }, { "dollar", "$" },
                { "exclaim", "!" }, { "excl", "!" }, { "quote", "'" }, { "apostrophe", "'" },
                { "amp", "&" }, { "ampersand", "&" }, { "at", "@" }, { "question", "?" },
                { "lparen", "(" }, { "rparen", ")" }, { "openparen", "(" }, { "closeparen", ")" },
            };

        private class Row
        {
            public string assetPath;
            public string fileName;
            public Texture2D preview;
            public string character;
        }

        private string spritesFolder = "";
        private string outputFolder = "";
        private string fontAssetName = "";
        private int atlasSize = 1024;
        private int padding = 4;
        private readonly List<Row> rows = new List<Row>();
        private Vector2 scroll;

        [MenuItem("_BrskTools/Bitmap Font Builder")]
        public static void Open()
        {
            var window = GetWindow<BitmapFontBuilderWindow>("Bitmap Font Builder");
            window.minSize = new Vector2(480, 420);
        }

        private void OnGUI()
        {
            DrawFolderPicker();

            if (rows.Count == 0)
                return;

            EditorGUILayout.Space();
            DrawTable();
            EditorGUILayout.Space();
            DrawSettings();
            EditorGUILayout.Space();
            DrawBuildButton();
        }

        private void DrawFolderPicker()
        {
            EditorGUILayout.LabelField("Sprites Folder", EditorStyles.boldLabel);

            Rect dropRect = GUILayoutUtility.GetRect(0, 44, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, string.IsNullOrEmpty(spritesFolder) ? "Drop a folder here, or Browse..." : spritesFolder);
            HandleDragAndDrop(dropRect);

            if (GUILayout.Button("Browse..."))
            {
                string picked = EditorUtility.OpenFolderPanel("Select sprites folder", Application.dataPath, "");
                if (!string.IsNullOrEmpty(picked))
                    SetFolder(picked);
            }
        }

        private void HandleDragAndDrop(Rect dropRect)
        {
            Event evt = Event.current;
            if (!dropRect.Contains(evt.mousePosition))
                return;

            if (evt.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.Use();
            }
            else if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var path in DragAndDrop.paths)
                {
                    string assetPath = NormalizeToAssetPath(path);
                    if (AssetDatabase.IsValidFolder(assetPath))
                    {
                        SetFolder(assetPath);
                        break;
                    }
                }
                evt.Use();
            }
        }

        private static string NormalizeToAssetPath(string path)
        {
            path = path.Replace('\\', '/');
            string dataPath = Application.dataPath.Replace('\\', '/');
            return path.StartsWith(dataPath) ? "Assets" + path.Substring(dataPath.Length) : path;
        }

        private void SetFolder(string rawPath)
        {
            spritesFolder = NormalizeToAssetPath(rawPath).TrimEnd('/');
            outputFolder = GetParentAssetFolder(spritesFolder);
            fontAssetName = Path.GetFileName(spritesFolder) + "_Font";
            ScanFolder();
        }

        private static string GetParentAssetFolder(string assetFolder)
        {
            int idx = assetFolder.LastIndexOf('/');
            return idx > 0 ? assetFolder.Substring(0, idx) : assetFolder;
        }

        private void ScanFolder()
        {
            rows.Clear();
            if (string.IsNullOrEmpty(spritesFolder) || !AssetDatabase.IsValidFolder(spritesFolder))
                return;

            string absFolder = Application.dataPath + spritesFolder.Substring("Assets".Length);
            var files = Directory.GetFiles(absFolder, "*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                string assetPath = spritesFolder + "/" + Path.GetFileName(file);

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (tex == null)
                {
                    AssetDatabase.ImportAsset(assetPath);
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                }

                rows.Add(new Row
                {
                    assetPath = assetPath,
                    fileName = fileName,
                    preview = tex,
                    character = GuessCharacter(fileName)
                });
            }
        }

        private static string GuessCharacter(string fileName)
        {
            if (fileName.Length == 1) return fileName;
            if (SpecialCharacterNames.TryGetValue(fileName, out var symbol)) return symbol;
            return fileName.Substring(0, 1);
        }

        private void DrawTable()
        {
            EditorGUILayout.LabelField($"Glyph Mapping ({rows.Count})", EditorStyles.boldLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(220));
            foreach (var row in rows)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(row.preview, GUILayout.Width(32), GUILayout.Height(32));
                EditorGUILayout.LabelField(row.fileName, GUILayout.Width(180));
                row.character = EditorGUILayout.TextField(row.character, GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            var duplicates = rows.Where(r => !string.IsNullOrEmpty(r.character))
                .GroupBy(r => r.character)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicates.Count > 0)
                EditorGUILayout.HelpBox("Duplicate characters: " + string.Join(", ", duplicates), MessageType.Error);

            var heights = rows.Where(r => r.preview != null).Select(r => r.preview.height).Distinct().ToList();
            if (heights.Count > 1)
                EditorGUILayout.HelpBox("Sprites have different heights (" + string.Join(", ", heights) + ") — baseline will misalign.", MessageType.Warning);
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("Font Asset Settings", EditorStyles.boldLabel);
            fontAssetName = EditorGUILayout.TextField("Font Asset Name", fontAssetName);
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            atlasSize = EditorGUILayout.IntPopup("Atlas Size", atlasSize,
                new[] { "512", "1024", "2048", "4096" }, new[] { 512, 1024, 2048, 4096 });
            padding = EditorGUILayout.IntSlider("Padding", padding, 0, 32);
        }

        private void DrawBuildButton()
        {
            bool hasDuplicates = rows.Where(r => !string.IsNullOrEmpty(r.character))
                .GroupBy(r => r.character).Any(g => g.Count() > 1);
            bool hasEmpty = rows.Any(r => string.IsNullOrEmpty(r.character));
            bool disabled = hasDuplicates || hasEmpty || string.IsNullOrEmpty(fontAssetName) || string.IsNullOrEmpty(outputFolder);

            using (new EditorGUI.DisabledScope(disabled))
            {
                if (GUILayout.Button("Build Font Asset", GUILayout.Height(32)))
                {
                    var glyphs = rows.Select(r => new BitmapFontBuilder.GlyphSource
                    {
                        assetPath = r.assetPath,
                        character = r.character[0]
                    }).ToList();

                    BitmapFontBuilder.Build(glyphs, outputFolder, fontAssetName, atlasSize, padding);
                }
            }
        }
    }
}

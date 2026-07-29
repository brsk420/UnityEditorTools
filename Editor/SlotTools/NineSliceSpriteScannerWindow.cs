using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class NineSliceSpriteScannerWindow : EditorWindow
{
    private enum Severity { Bad, Risky }

    private class Entry
    {
        public string AssetPath;
        public string SubSpriteName;
        public float Width;
        public float Height;
        public Vector4 Border;
        public float Ppu;
        public Severity Severity;
    }

    private List<Entry> _entries = new List<Entry>();
    private Vector2 _scroll;
    private string _scannedFolder;

    [MenuItem("_BrskTools/SlotTools/Scan Nine-Slice Sprites", false, 2000)]
    private static void ScanSelectedFolder()
    {
        string folder = GetSelectedFolder();
        if (string.IsNullOrEmpty(folder))
        {
            EditorUtility.DisplayDialog("Nine-Slice Scanner", "Выдели папку в окне Project.", "OK");
            return;
        }

        var window = GetWindow<NineSliceSpriteScannerWindow>("Nine-Slice Scan");
        window.RunScan(folder);
    }

    [MenuItem("_BrskTools/SlotTools/Scan Nine-Slice Sprites", true)]
    private static bool ValidateScanSelectedFolder()
    {
        return !string.IsNullOrEmpty(GetSelectedFolder());
    }

    private static string GetSelectedFolder()
    {
        Object obj = Selection.activeObject;
        if (obj == null) return null;
        string path = AssetDatabase.GetAssetPath(obj);
        if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)) return null;
        return path;
    }

    private void RunScan(string folder)
    {
        _scannedFolder = folder;
        _entries.Clear();

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                if (i % 200 == 0)
                    EditorUtility.DisplayProgressBar("Nine-Slice Scan", $"{i}/{guids.Length}", (float)i / guids.Length);

                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || importer.textureType != TextureImporterType.Sprite) continue;

                if (importer.spriteImportMode == SpriteImportMode.Single)
                {
                    Vector4 b = importer.spriteBorder;
                    if (b == Vector4.zero) continue;
                    importer.GetSourceTextureWidthAndHeight(out int w, out int h);
                    EvaluateAndAdd(path, null, b, w, h, importer.spritePixelsPerUnit);
                }
                else if (importer.spriteImportMode == SpriteImportMode.Multiple)
                {
                    var sheet = importer.spritesheet;
                    if (sheet == null) continue;
                    foreach (var meta in sheet)
                    {
                        Vector4 b = meta.border;
                        if (b == Vector4.zero) continue;
                        EvaluateAndAdd(path, meta.name, b, meta.rect.width, meta.rect.height, importer.spritePixelsPerUnit);
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        _entries = _entries.OrderBy(e => e.Severity).ThenBy(e => e.AssetPath).ToList();
        Repaint();
    }

    private void EvaluateAndAdd(string path, string subName, Vector4 b, float w, float h, float ppu)
    {
        bool bad = (b.x + b.z >= w) || (b.y + b.w >= h);
        bool risky = !bad && ((b.x + b.z) >= 0.9f * w || (b.y + b.w) >= 0.9f * h);
        if (!bad && !risky) return;

        _entries.Add(new Entry
        {
            AssetPath = path,
            SubSpriteName = subName,
            Width = w,
            Height = h,
            Border = b,
            Ppu = ppu,
            Severity = bad ? Severity.Bad : Severity.Risky
        });
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Папка:", string.IsNullOrEmpty(_scannedFolder) ? "(не выбрана)" : _scannedFolder);

        int badCount = _entries.Count(e => e.Severity == Severity.Bad);
        int riskyCount = _entries.Count - badCount;
        EditorGUILayout.LabelField($"Bad: {badCount}   Risky: {riskyCount}");

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Rescan") && !string.IsNullOrEmpty(_scannedFolder))
            {
                RunScan(_scannedFolder);
            }

            if (GUILayout.Button("Scan selected folder"))
            {
                string folder = GetSelectedFolder();
                if (string.IsNullOrEmpty(folder))
                    EditorUtility.DisplayDialog("Nine-Slice Scanner", "Выдели папку в окне Project.", "OK");
                else
                    RunScan(folder);
            }
        }

        EditorGUILayout.Space();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var entry in _entries)
        {
            DrawEntry(entry);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawEntry(Entry entry)
    {
        Color prevColor = GUI.color;

        string label = entry.SubSpriteName == null
            ? entry.AssetPath
            : $"{entry.AssetPath} [{entry.SubSpriteName}]";
        string detail = $"size={entry.Width}x{entry.Height}px  border(L,B,R,T)=({entry.Border.x},{entry.Border.y},{entry.Border.z},{entry.Border.w})  ppu={entry.Ppu}";

        Rect rowRect = EditorGUILayout.BeginVertical();

        GUI.color = entry.Severity == Severity.Bad ? new Color(0.85f, 0.35f, 0.35f) : new Color(0.85f, 0.72f, 0.25f);
        EditorGUILayout.LabelField($"[{entry.Severity}] {label}", EditorStyles.boldLabel);
        GUI.color = prevColor;
        EditorGUILayout.LabelField(detail, EditorStyles.miniLabel);

        EditorGUILayout.EndVertical();

        HandleRowClick(rowRect, entry);

        EditorGUILayout.Space(2);
    }

    private void HandleRowClick(Rect rowRect, Entry entry)
    {
        Event e = Event.current;
        if (e.type != EventType.MouseDown || !rowRect.Contains(e.mousePosition)) return;

        SelectEntry(entry);
        if (e.clickCount >= 2)
        {
            OpenInSpriteEditor(entry);
        }

        e.Use();
    }

    private void SelectEntry(Entry entry)
    {
        Object obj = AssetDatabase.LoadAssetAtPath<Object>(entry.AssetPath);
        if (obj == null) return;
        Selection.activeObject = obj;
        EditorGUIUtility.PingObject(obj);
    }

    private void OpenInSpriteEditor(Entry entry)
    {
        Object obj = AssetDatabase.LoadAssetAtPath<Object>(entry.AssetPath);
        if (obj == null) return;
        Selection.activeObject = obj;
        EditorApplication.ExecuteMenuItem("Window/2D/Sprite Editor");
    }
}

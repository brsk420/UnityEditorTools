using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SpriteSlicer : EditorWindow
{
    private List<Sprite> sprites = new List<Sprite>();
    private float left = 0f;
    private float right = 0f;
    private float top = 0f;
    private float bottom = 0f;
    private Vector2 scroll;

    // New: reference sprite to take border values from
    private Sprite referenceSprite = null;

    [MenuItem("_BrskTools/Sprite 9-Slice Setter")]
    public static void ShowWindow()
    {
        GetWindow<SpriteSlicer>("Sprite 9-Slice Setter");
    }

    private void OnGUI()
    {
        GUILayout.Label("9-Slice Border Setter", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Reference sprite field
        EditorGUI.BeginChangeCheck();
        referenceSprite = (Sprite)EditorGUILayout.ObjectField("Reference Sprite", referenceSprite, typeof(Sprite), false);
        if (EditorGUI.EndChangeCheck() && referenceSprite != null)
        {
            Vector4 b = GetImporterBorder(referenceSprite); // read border from importer metadata (pixels)
            left = b.x;
            bottom = b.y;
            right = b.z;
            top = b.w;
        }

        EditorGUILayout.BeginHorizontal();
        if (referenceSprite != null)
        {
            if (GUILayout.Button("Clear Reference"))
            {
                referenceSprite = null;
            }
        }
        else
        {
            if (GUILayout.Button("Use Selected (Project) as Reference"))
            {
                var sel = Selection.activeObject as Sprite;
                if (sel != null)
                {
                    referenceSprite = sel;
                    Vector4 b = GetImporterBorder(referenceSprite);
                    left = b.x;
                    bottom = b.y;
                    right = b.z;
                    top = b.w;
                }
                else
                {
                    Debug.LogWarning("Select a Sprite in Project to use as reference.");
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Show border values (read-only when referenceSprite != null, editable otherwise)
        bool readOnly = referenceSprite != null;
        EditorGUI.BeginDisabledGroup(readOnly);
        EditorGUILayout.BeginHorizontal();
        left = EditorGUILayout.FloatField("Left", left);
        right = EditorGUILayout.FloatField("Right", right);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        top = EditorGUILayout.FloatField("Top", top);
        bottom = EditorGUILayout.FloatField("Bottom", bottom);
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox(readOnly
            ? "Values are taken from reference sprite. Clear reference to edit manually."
            : "You can edit values manually, or set a reference sprite to take values automatically.", MessageType.Info);

        EditorGUILayout.Space();

        Rect dropArea = GUILayoutUtility.GetRect(0, 60, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drag & Drop Sprites here\n(or drop texture to add all sprites from it)", EditorStyles.helpBox);

        HandleDragAndDrop(dropArea);

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Selected (Project)"))
        {
            AddSelection();
        }
        if (GUILayout.Button("Clear List"))
        {
            sprites.Clear();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        GUILayout.Label($"Sprites in list: {sprites.Count}", EditorStyles.miniLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(150));
        for (int i = 0; i < sprites.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            sprites[i] = (Sprite)EditorGUILayout.ObjectField(sprites[i], typeof(Sprite), false);
            if (GUILayout.Button("X", GUILayout.Width(24)))
            {
                sprites.RemoveAt(i);
                i--;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        if (GUILayout.Button("Apply 9-Slice to Sprites"))
        {
            ApplyBorders();
        }
    }

    private void HandleDragAndDrop(Rect dropArea)
    {
        Event evt = Event.current;
        if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && dropArea.Contains(evt.mousePosition))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (Object obj in DragAndDrop.objectReferences)
                {
                    if (obj is Sprite s)
                    {
                        AddSpriteUnique(s);
                    }
                    else if (obj is Texture2D)
                    {
                        string path = AssetDatabase.GetAssetPath(obj);
                        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                        foreach (var a in assets)
                        {
                            if (a is Sprite sp)
                                AddSpriteUnique(sp);
                        }
                    }
                }
            }
            evt.Use();
        }
    }

    private void AddSelection()
    {
        Object[] sel = Selection.GetFiltered(typeof(Sprite), SelectionMode.Assets);
        foreach (Object o in sel)
        {
            if (o is Sprite s)
                AddSpriteUnique(s);
        }
    }

    private void AddSpriteUnique(Sprite s)
    {
        if (s == null) return;
        if (!sprites.Contains(s))
            sprites.Add(s);
    }

    private void ApplyBorders()
    {
        if (sprites.Count == 0)
        {
            Debug.LogWarning("No sprites in list.");
            return;
        }

        // Group sprites by asset path to avoid multiple reimports per texture
        Dictionary<string, List<Sprite>> byPath = new Dictionary<string, List<Sprite>>();
        foreach (var s in sprites)
        {
            if (s == null) continue;
            string path = AssetDatabase.GetAssetPath(s);
            if (string.IsNullOrEmpty(path)) continue;
            if (!byPath.ContainsKey(path)) byPath[path] = new List<Sprite>();
            byPath[path].Add(s);
        }

        int changedCount = 0;
        foreach (var kv in byPath)
        {
            string path = kv.Key;
            List<Sprite> list = kv.Value;
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                Debug.LogWarning($"Can't get TextureImporter for {path}");
                continue;
            }

            // Ensure texture type is sprite
            if (ti.textureType != TextureImporterType.Sprite)
            {
                ti.textureType = TextureImporterType.Sprite;
            }

            bool anyChange = false;
            Vector4 newBorder = new Vector4(left, bottom, right, top);

            if (ti.spriteImportMode == SpriteImportMode.Single)
            {
                // Single sprite - set spriteBorder
                if (ti.spriteBorder != newBorder)
                {
                    ti.spriteBorder = newBorder;
                    anyChange = true;
                }
            }
            else // Multiple
            {
                var metas = ti.spritesheet;
                bool metaChanged = false;
                for (int i = 0; i < metas.Length; i++)
                {
                    // If this meta corresponds to one of the sprites in the list
                    foreach (var sp in list)
                    {
                        if (metas[i].name == sp.name)
                        {
                            if (metas[i].border != newBorder)
                            {
                                metas[i].border = newBorder;
                                metaChanged = true;
                            }
                            break;
                        }
                    }
                }
                if (metaChanged)
                {
                    ti.spritesheet = metas;
                    anyChange = true;
                }
            }

            if (anyChange)
            {
                ti.SaveAndReimport();
                changedCount++;
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"Applied borders to {changedCount} texture(s).");
    }

    // Read sprite border in pixels from the TextureImporter metadata.
    // Falls back to Sprite.border if importer/meta not found.
    private Vector4 GetImporterBorder(Sprite sprite)
    {
        if (sprite == null) return Vector4.zero;

        string path = AssetDatabase.GetAssetPath(sprite);
        if (string.IsNullOrEmpty(path))
        {
            // fallback: sprite.border is in "units" (pixels / PPU), convert to pixels
            return sprite.border * sprite.pixelsPerUnit;
        }

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            return sprite.border * sprite.pixelsPerUnit;
        }

        // Prefer importer-stored values (they are in pixels)
        if (importer.spriteImportMode == SpriteImportMode.Single)
        {
            Vector4 b = importer.spriteBorder;
            if (b != Vector4.zero) return b;
            // fallback
            return sprite.border * sprite.pixelsPerUnit;
        }
        else
        {
            var metas = importer.spritesheet;
            for (int i = 0; i < metas.Length; i++)
            {
                if (metas[i].name == sprite.name)
                {
                    // metas[i].border is in pixels
                    Vector4 b = metas[i].border;
                    if (b != Vector4.zero) return b;

                    // if border is zero, fallback to sprite.border * PPU
                    return sprite.border * sprite.pixelsPerUnit;
                }
            }
        }

        // Last resort: convert sprite.border (units) to pixels
        return sprite.border * sprite.pixelsPerUnit;
    }
}

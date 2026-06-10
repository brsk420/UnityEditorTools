using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using TMPro;
using UnityEngine.TextCore;

/// <summary>
/// Editor tool that creates TextMeshPro Sprite Assets from all Sprite assets in a selected folder.
/// Usage:
/// - Place sprites (either as multiple sprites in a single texture or individual textures) under a folder in Assets.
/// - Window -> Tools -> Sprite Font Creator. Select the folder and press "Create Sprite Assets".
/// The tool will create one TMP_SpriteAsset per source texture and add a default material.
/// The created sprite characters will keep the sprites' names so you can use <sprite name="SpriteName"> in TMP text.
/// </summary>
public class SpriteFontCreator : EditorWindow
{
    string folderPath = "Assets";
    string filePrefix = "SpriteFont";

    [MenuItem("_BrskTools/Sprite Font Creator")]
    public static void ShowWindow()
    {
        SpriteFontCreator w = GetWindow<SpriteFontCreator>("Sprite Font Creator");
        w.minSize = new Vector2(420, 120);
    }

    void OnGUI()
    {
        GUILayout.Label("Create TMP Sprite Assets from folder of Sprites", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Folder (Assets/...)", GUILayout.Width(120));
        folderPath = EditorGUILayout.TextField(folderPath);
        if (GUILayout.Button("Select", GUILayout.Width(60)))
        {
            string abs = EditorUtility.OpenFolderPanel("Select sprites folder", Application.dataPath, "");
            if (!string.IsNullOrEmpty(abs))
            {
                if (abs.StartsWith(Application.dataPath))
                    folderPath = "Assets" + abs.Substring(Application.dataPath.Length);
                else
                    EditorUtility.DisplayDialog("Invalid Folder", "Please select a folder inside this project's Assets folder.", "OK");
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Asset Prefix", GUILayout.Width(120));
        filePrefix = EditorGUILayout.TextField(filePrefix);
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(8);

        if (GUILayout.Button("Create Sprite Assets"))
        {
            CreateSpriteAssetsForFolder(folderPath, filePrefix);
        }
    }

    void CreateSpriteAssetsForFolder(string assetsFolderPath, string prefix)
    {
        if (string.IsNullOrEmpty(assetsFolderPath) || !AssetDatabase.IsValidFolder(assetsFolderPath))
        {
            EditorUtility.DisplayDialog("Folder not found", "The provided folder path is not a valid Assets folder.", "OK");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { assetsFolderPath });

        if (guids == null || guids.Length == 0)
        {
            EditorUtility.DisplayDialog("No Sprites Found", "No Sprite assets were found in the selected folder.", "OK");
            return;
        }

        // Load sprites and group by their source texture
        var sprites = new List<Sprite>();
        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s != null)
                sprites.Add(s);
        }

        if (sprites.Count == 0)
        {
            EditorUtility.DisplayDialog("No Sprites Loaded", "Unable to load sprites from the selected folder.", "OK");
            return;
        }

        // Group by sprite.texture (textures can be the same for sliced atlases)
        var groups = sprites.GroupBy(sp => sp.texture).ToList();

        int created = 0;

        foreach (var g in groups)
        {
            Texture2D tex = g.Key as Texture2D;
            var spriteList = g.OrderByDescending(s => s.rect.y).ThenBy(s => s.rect.x).ToArray();

            // Determine output path
            string outFolder = assetsFolderPath.TrimEnd('/') + "/";
            string texName = tex != null ? tex.name : "Texture" + created;
            string assetPath = outFolder + prefix + "_" + texName + ".asset";

            // Ensure unique path
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            TMP_SpriteAsset spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            AssetDatabase.CreateAsset(spriteAsset, assetPath);

            // version is internal to TMP package; skip setting it here.
            spriteAsset.hashCode = TMP_TextUtilities.GetSimpleHashCode(spriteAsset.name);

            List<TMP_SpriteGlyph> spriteGlyphTable = new List<TMP_SpriteGlyph>();
            List<TMP_SpriteCharacter> spriteCharacterTable = new List<TMP_SpriteCharacter>();

            if (tex != null)
            {
                spriteAsset.spriteSheet = tex;

                for (int i = 0; i < spriteList.Length; i++)
                {
                    Sprite sprite = spriteList[i];

                    TMP_SpriteGlyph spriteGlyph = new TMP_SpriteGlyph();
                    spriteGlyph.index = (uint)i;
                    spriteGlyph.metrics = new GlyphMetrics(sprite.rect.width, sprite.rect.height, -sprite.pivot.x, sprite.rect.height - sprite.pivot.y, sprite.rect.width);
                    spriteGlyph.glyphRect = new GlyphRect(sprite.rect);
                    spriteGlyph.scale = 1.0f;
                    spriteGlyph.sprite = sprite;

                    spriteGlyphTable.Add(spriteGlyph);

                    TMP_SpriteCharacter spriteCharacter = new TMP_SpriteCharacter(0xFFFE, spriteGlyph);
                    spriteCharacter.name = sprite.name;
                    spriteCharacter.scale = 1.0f;

                    spriteCharacterTable.Add(spriteCharacter);
                }

                // Populate existing tables (properties expose the lists via getters).
                spriteAsset.spriteCharacterTable.Clear();
                spriteAsset.spriteCharacterTable.AddRange(spriteCharacterTable);

                spriteAsset.spriteGlyphTable.Clear();
                spriteAsset.spriteGlyphTable.AddRange(spriteGlyphTable);

                // Create and add default material
                Shader shader = Shader.Find("TextMeshPro/Sprite");
                Material material = new Material(shader);
                material.SetTexture(ShaderUtilities.ID_MainTex, spriteAsset.spriteSheet);
                material.hideFlags = HideFlags.HideInHierarchy;
                spriteAsset.material = material;
                AssetDatabase.AddObjectToAsset(material, spriteAsset);
            }
            else
            {
                // If texture is null (rare), skip
                Debug.LogWarning("Sprite texture was null for group; skipping.");
                Object.DestroyImmediate(spriteAsset);
                continue;
            }

            // Update lookup tables and save
            spriteAsset.UpdateLookupTables();
            EditorUtility.SetDirty(spriteAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(spriteAsset));

            created++;
        }

        EditorUtility.DisplayDialog("Done", $"Created {created} TMP Sprite Asset(s).", "OK");
    }
}

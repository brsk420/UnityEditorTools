using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Replaces PlayStudios.GameEngineLua.Tagging.TagComponent → GameEngine2.TagComponent
/// on every GameObject inside a prefab, preserving the Tag string value.
///
/// Usage:
///   • Open a prefab for editing, then: Tools/_Bsrk/Replace TagComponents → In Open Prefab
///   • Or select a prefab asset in the Project window, then: Tools/_Bsrk/Replace TagComponents → In Selected Prefab Asset
/// </summary>
public static class TagComponentReplacer
{
    const string OldType = "PlayStudios.GameEngineLua.Tagging.TagComponent";

    // ─── Open Prefab Stage ────────────────────────────────────────────────────

    [MenuItem("_BrskTools/Replace TagComponents/In Open Prefab", priority = 100)]
    static void ReplaceInOpenPrefab()
    {
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null)
        {
            EditorUtility.DisplayDialog("TagComponentReplacer",
                "No prefab is open for editing.\nOpen a prefab first.", "OK");
            return;
        }

        int count = ReplaceAll(stage.prefabContentsRoot);

        // Mark the prefab stage dirty so Unity knows to save it
        EditorSceneManager.MarkSceneDirty(stage.scene);
        Debug.Log($"[TagComponentReplacer] Replaced {count} component(s) in '{stage.assetPath}'. Save the prefab to persist.");
    }

    [MenuItem("_BrskTools/Replace TagComponents/In Open Prefab", true)]
    static bool ReplaceInOpenPrefabValidate() =>
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    // ─── Selected Prefab Asset ────────────────────────────────────────────────

    [MenuItem("_BrskTools/Replace TagComponents/In Selected Prefab Asset", priority = 101)]
    static void ReplaceInSelectedAsset()
    {
        var go = Selection.activeObject as GameObject;
        if (go == null || !PrefabUtility.IsPartOfPrefabAsset(go))
        {
            EditorUtility.DisplayDialog("TagComponentReplacer",
                "Select a prefab asset in the Project window.", "OK");
            return;
        }

        string path = AssetDatabase.GetAssetPath(go);
        var root = PrefabUtility.LoadPrefabContents(path);

        int count = ReplaceAll(root);

        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log($"[TagComponentReplacer] Replaced {count} component(s) in '{path}' and saved.");
    }

    [MenuItem("_BrskTools/Replace TagComponents/In Selected Prefab Asset", true)]
    static bool ReplaceInSelectedAssetValidate()
    {
        var go = Selection.activeObject as GameObject;
        return go != null && PrefabUtility.IsPartOfPrefabAsset(go);
    }

    // ─── Core logic ───────────────────────────────────────────────────────────

    static int ReplaceAll(GameObject root)
    {
        var oldComps = root.GetComponentsInChildren<PlayStudios.GameEngineLua.Tagging.TagComponent>(includeInactive: true);
        int count = 0;

        foreach (var old in oldComps)
        {
            GameObject go = old.gameObject;
            string savedTag = old.Tag;

            Object.DestroyImmediate(old, allowDestroyingAssets: true);

            var newComp = go.AddComponent<GameEngine2.TagComponent>();
            newComp.Tag = savedTag;
            EditorUtility.SetDirty(go);
            count++;
        }

        return count;
    }
}

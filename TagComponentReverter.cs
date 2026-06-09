using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Reverts GameEngine2.TagComponent → PlayStudios.GameEngineLua.Tagging.TagComponent
/// on every GameObject inside a prefab, preserving the Tag string value.
///
/// Usage:
///   • Open a prefab for editing, then: _BrskTools/Revert TagComponents → In Open Prefab
///   • Or select a prefab asset in the Project window, then: _BrskTools/Revert TagComponents → In Selected Prefab Asset
/// </summary>
public static class TagComponentReverter
{
    // ─── Open Prefab Stage ────────────────────────────────────────────────────

    [MenuItem("_BrskTools/Revert TagComponents/In Open Prefab", priority = 100)]
    static void RevertInOpenPrefab()
    {
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null)
        {
            EditorUtility.DisplayDialog("TagComponentReverter",
                "No prefab is open for editing.\nOpen a prefab first.", "OK");
            return;
        }

        int count = RevertAll(stage.prefabContentsRoot);

        EditorSceneManager.MarkSceneDirty(stage.scene);
        Debug.Log($"[TagComponentReverter] Reverted {count} component(s) in '{stage.assetPath}'. Save the prefab to persist.");
    }

    [MenuItem("_BrskTools/Revert TagComponents/In Open Prefab", true)]
    static bool RevertInOpenPrefabValidate() =>
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    // ─── Selected Prefab Asset ────────────────────────────────────────────────

    [MenuItem("_BrskTools/Revert TagComponents/In Selected Prefab Asset", priority = 101)]
    static void RevertInSelectedAsset()
    {
        var go = Selection.activeObject as GameObject;
        if (go == null || !PrefabUtility.IsPartOfPrefabAsset(go))
        {
            EditorUtility.DisplayDialog("TagComponentReverter",
                "Select a prefab asset in the Project window.", "OK");
            return;
        }

        string path = AssetDatabase.GetAssetPath(go);
        var root = PrefabUtility.LoadPrefabContents(path);

        int count = RevertAll(root);

        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log($"[TagComponentReverter] Reverted {count} component(s) in '{path}' and saved.");
    }

    [MenuItem("_BrskTools/Revert TagComponents/In Selected Prefab Asset", true)]
    static bool RevertInSelectedAssetValidate()
    {
        var go = Selection.activeObject as GameObject;
        return go != null && PrefabUtility.IsPartOfPrefabAsset(go);
    }

    // ─── Core logic ───────────────────────────────────────────────────────────

    static int RevertAll(GameObject root)
    {
        var oldComps = root.GetComponentsInChildren<GameEngine2.TagComponent>(includeInactive: true);
        int count = 0;

        foreach (var old in oldComps)
        {
            GameObject go = old.gameObject;
            string savedTag = old.Tag;

            Object.DestroyImmediate(old, allowDestroyingAssets: true);

            var newComp = go.AddComponent<PlayStudios.GameEngineLua.Tagging.TagComponent>();
            newComp.Tag = savedTag;
            EditorUtility.SetDirty(go);
            count++;
        }

        return count;
    }
}

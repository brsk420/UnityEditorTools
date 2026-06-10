using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class DeleteEvery2
{
    [MenuItem("Assets/_BrskTools/DeleteEvery2", false, 81)]
    private static void DeleteEvery2Menu()
    {
        var selection = Selection.GetFiltered(typeof(Object), SelectionMode.Assets)
                                 .Cast<Object>()
                                 .ToArray();
        if (selection == null || selection.Length < 2) return;

        // Sort selection by asset path to ensure deterministic "by order" behavior
        var sorted = selection.OrderBy(o => AssetDatabase.GetAssetPath(o)).ToArray();

        // Determine parent folder (use parent of first selected asset in sorted order).
        string firstPath = AssetDatabase.GetAssetPath(sorted[0]);
        string parent = GetParentFolder(firstPath);
        if (string.IsNullOrEmpty(parent)) parent = "Assets";

        // Create new folder
        string newFolderName = "_MovedEvery2";
        string guid = AssetDatabase.CreateFolder(parent, newFolderName);
        string folderPath = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(folderPath))
        {
            Debug.LogError("Failed to create folder.");
            return;
        }

        try
        {
            AssetDatabase.StartAssetEditing();

            // Move every second asset in the sorted list: indices 1,3,5,...
            for (int i = 1; i < sorted.Length; i += 2)
            {
                var obj = sorted[i];
                string srcPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(srcPath)) continue;

                string fileName = Path.GetFileName(srcPath);
                string destPath = folderPath + "/" + fileName;

                // If same path (e.g. selecting the folder itself), skip
                if (srcPath == destPath) continue;

                // show cancellable progress (use index relative to sorted length)
                if (EditorUtility.DisplayCancelableProgressBar("Moving every 2nd asset", $"Moving {fileName}", (float)i / sorted.Length))
                {
                    Debug.Log("Operation cancelled by user.");
                    break;
                }

                string err = AssetDatabase.MoveAsset(srcPath, destPath);
                if (!string.IsNullOrEmpty(err))
                {
                    Debug.LogWarning($"MoveAsset failed: {err} (from {srcPath} to {destPath})");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Assets/_BrskTools/DeleteEvery2", true)]
    private static bool DeleteEvery2MenuValidate()
    {
        var sel = Selection.GetFiltered(typeof(Object), SelectionMode.Assets);
        return sel != null && sel.Length > 1;
    }

    private static string GetParentFolder(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;

        if (AssetDatabase.IsValidFolder(assetPath))
        {
            // assetPath is a folder, return its parent
            string parent = Path.GetDirectoryName(assetPath);
            return string.IsNullOrEmpty(parent) ? "Assets" : parent.Replace("\\", "/");
        }

        // otherwise return directory containing the asset
        string dir = Path.GetDirectoryName(assetPath);
        return string.IsNullOrEmpty(dir) ? "Assets" : dir.Replace("\\", "/");
    }
}

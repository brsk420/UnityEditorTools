using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;


public static class NewFolderWithSelectionEditor
{
    [MenuItem("Assets/Create/New Folder With Selection", false, 80)]
    private static void CreateNewFolderWithSelectionMenu()
    {
        var selection = Selection.GetFiltered(typeof(Object), SelectionMode.Assets)
                                 .Cast<Object>()
                                 .ToArray();
        if (selection.Length == 0) return;

        // Determine parent folder (use parent of first selected asset).
        string firstPath = AssetDatabase.GetAssetPath(selection[0]);
        string parent = GetParentFolder(firstPath);
        if (string.IsNullOrEmpty(parent)) parent = "Assets";

        // Create new folder
        string newFolderName = "_NewFolder";
        string guid = AssetDatabase.CreateFolder(parent, newFolderName);
        string folderPath = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(folderPath))
        {
            Debug.LogError("Failed to create folder.");
            return;
        }

        // Batch move selected assets into new folder (faster)
        try
        {
            AssetDatabase.StartAssetEditing();

            for (int i = 0; i < selection.Length; i++)
            {
                var obj = selection[i];
                string srcPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(srcPath)) continue;

                string fileName = Path.GetFileName(srcPath);
                string destPath = folderPath + "/" + fileName;

                // If same path (e.g. selecting the folder itself), skip
                if (srcPath == destPath) continue;

                // show cancellable progress
                if (EditorUtility.DisplayCancelableProgressBar("Moving assets", $"Moving {i+1}/{selection.Length}: {fileName}", (float)i / selection.Length))
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

        // Select and ping new folder (no rename)
        Object folderObj = AssetDatabase.LoadAssetAtPath<Object>(folderPath);
        Selection.activeObject = folderObj;
        EditorGUIUtility.PingObject(folderObj);
    }

    // Validator: enable menu only when at least one asset is selected
    [MenuItem("Assets/Create/New Folder With Selection", true)]
    private static bool CreateNewFolderWithSelectionMenuValidate()
    {
        var sel = Selection.GetFiltered(typeof(Object), SelectionMode.Assets);
        return sel != null && sel.Length > 0;
    }

    private static string GetParentFolder(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;

        // If the selected path is a folder, use its parent directory
        if (AssetDatabase.IsValidFolder(assetPath))
        {
            return Path.GetDirectoryName(assetPath).Replace("\\", "/");
        }

        // Otherwise use the directory containing the asset
        string dir = Path.GetDirectoryName(assetPath);
        return string.IsNullOrEmpty(dir) ? "Assets" : dir.Replace("\\", "/");
    }
}

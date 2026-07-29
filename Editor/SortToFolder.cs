using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

namespace _Brsk420.EditorTools
{
    public static class SortToFolder
    {
        // Вызов из контекстного меню Project (ПКМ по ассетам/папке)
        [MenuItem("Assets/_BrskTools/Sort To Folders", priority = -1000)]
        private static void Sort()
        {
            var selectedObjects = Selection.GetFiltered<Object>(SelectionMode.Assets);
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                Debug.LogWarning("[SortToFolder] No assets selected.");
                return;
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var obj in selectedObjects)
                {
                    var path = AssetDatabase.GetAssetPath(obj);
                    if (string.IsNullOrEmpty(path))
                        continue;

                    if (AssetDatabase.IsValidFolder(path))
                    {
                        SortFolder(path);
                    }
                    else
                    {
                        var folderPath = Path.GetDirectoryName(path).Replace("\\", "/");
                        if (!string.IsNullOrEmpty(folderPath))
                            SortFolder(folderPath);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }

        [MenuItem("Assets/_BrskTools/Sort To Folders", validate = true)]
        private static bool Sort_Validate()
        {
            var selected = Selection.GetFiltered<Object>(SelectionMode.Assets);
            return selected != null && selected.Length > 0;
        }

        private static void SortFolder(string rootFolder)
        {
            if (string.IsNullOrEmpty(rootFolder))
                return;

            rootFolder = rootFolder.Replace("\\", "/");

            string[] guids = AssetDatabase.FindAssets("", new[] { rootFolder });
            foreach (var guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath))
                    continue;

                // skip folders
                if (AssetDatabase.IsValidFolder(assetPath))
                    continue;

                // work only with assets directly inside root folder
                var parentDir = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
                if (!string.Equals(parentDir, rootFolder, System.StringComparison.Ordinal))
                    continue;

                var fileName = Path.GetFileName(assetPath);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(assetPath);
                string group = GetGroupName(nameWithoutExt);
                if (string.IsNullOrEmpty(group))
                    continue;

                string groupFolderPath = $"{rootFolder}/{group}";
                if (!AssetDatabase.IsValidFolder(groupFolderPath))
                {
                    AssetDatabase.CreateFolder(rootFolder, group);
                }

                string newPath = $"{groupFolderPath}/{fileName}";
                if (assetPath == newPath)
                    continue;

                var error = AssetDatabase.MoveAsset(assetPath, newPath);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogWarning($"[SortToFolder] Failed to move '{assetPath}' -> '{newPath}': {error}");
                }
            }
        }

        private static string GetGroupName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var normalized = name.Replace(' ', '_');
            var parts = normalized.Split(new[] { '_' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1)
                return null;

            // group key = everything except last token (usually frame index)
            var groupParts = parts.Take(parts.Length - 1);
            return string.Join("_", groupParts);
        }
    }
}
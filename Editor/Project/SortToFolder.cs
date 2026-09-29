using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
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

            var rootFolders = new HashSet<string>();
            foreach (var obj in selectedObjects)
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path))
                    continue;

                var folderPath = AssetDatabase.IsValidFolder(path)
                    ? path
                    : Path.GetDirectoryName(path)?.Replace("\\", "/");

                if (!string.IsNullOrEmpty(folderPath))
                    rootFolders.Add(folderPath);
            }

            var moves = new List<(string from, string to)>();
            foreach (var rootFolder in rootFolders)
                CollectMoves(rootFolder, moves);

            if (moves.Count == 0)
                return;

            // Folders must exist in the AssetDatabase BEFORE StartAssetEditing: a folder created
            // inside it isn't registered yet, so IsValidFolder() stays false (CreateFolder then
            // makes "Group 1", "Group 2"...) and MoveAsset fails with
            // "Parent directory is not in asset database".
            foreach (var folder in moves.Select(m => Path.GetDirectoryName(m.to).Replace("\\", "/")).Distinct())
            {
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace("\\", "/"), Path.GetFileName(folder));
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var (from, to) in moves)
                {
                    var error = AssetDatabase.MoveAsset(from, to);
                    if (!string.IsNullOrEmpty(error))
                    {
                        Debug.LogWarning($"[SortToFolder] Failed to move '{from}' -> '{to}': {error}");
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

        private static void CollectMoves(string rootFolder, List<(string from, string to)> moves)
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

                string group = GetGroupName(Path.GetFileNameWithoutExtension(assetPath));
                if (string.IsNullOrEmpty(group))
                    continue;

                moves.Add((assetPath, $"{rootFolder}/{group}/{Path.GetFileName(assetPath)}"));
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
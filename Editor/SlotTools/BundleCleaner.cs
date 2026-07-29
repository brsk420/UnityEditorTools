using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class SlotBundleCleaner
{
    [MenuItem("Assets/_BrskTools/Clean Bundle", priority = -1980)]
    public static void CleanBundle()
    {
        var selectedFolder = GetSelectedFolder();
        if (string.IsNullOrEmpty(selectedFolder))
        {
            EditorUtility.DisplayDialog("Error", "Please select a bundle folder", "OK");
            return;
        }

        var bundleRoot = selectedFolder;
        var prefabsFolder = Path.Combine(bundleRoot, "Prefabs").Replace("\\", "/");
        var audioFolder = Path.Combine(bundleRoot, "Audio").Replace("\\", "/");
        var luaScriptsFolder = Path.Combine(bundleRoot, "LuaScripts").Replace("\\", "/");
        var configFolder = Path.Combine(bundleRoot, "Config").Replace("\\", "/");

        // Find all prefabs in Prefabs folder
        var requiredPrefabs = FindAllPrefabs(prefabsFolder);
        if (requiredPrefabs.Length == 0)
        {
            EditorUtility.DisplayDialog("Info", $"No prefabs found in {prefabsFolder}", "OK");
            return;
        }

        // Analyze
        var dependencies = new HashSet<string>(
            AssetDatabase.GetDependencies(requiredPrefabs, recursive: true)
        );

        foreach (var p in requiredPrefabs)
            dependencies.Add(p);

        var allGuids = AssetDatabase.FindAssets("", new[] { bundleRoot });
        var allPaths = allGuids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !AssetDatabase.IsValidFolder(p))
            .ToList();

        var toDelete = new List<string>();
        var toKeep = new List<string>();

        foreach (var path in allPaths)
        {
            // Keep Audio folder
            if (path.StartsWith(audioFolder))
            {
                toKeep.Add(path);
                continue;
            }

            // Keep LuaScripts folder
            if (path.StartsWith(luaScriptsFolder))
            {
                toKeep.Add(path);
                continue;
            }

            // Keep Config folder
            if (path.StartsWith(configFolder))
            {
                toKeep.Add(path);
                continue;
            }

            // Keep sprite atlases
            if (path.EndsWith(".spriteatlas"))
            {
                toKeep.Add(path);
                continue;
            }

            if (dependencies.Contains(path))
                toKeep.Add(path);
            else
                toDelete.Add(path);
        }

        toDelete.Sort();
        toKeep.Sort();

        // Show results and ask for confirmation
        string bundleName = Path.GetFileName(bundleRoot);
        var message = $"Bundle: {bundleName}\n\n" +
                      $"Keep: {toKeep.Count} assets\n" +
                      $"Delete: {toDelete.Count} assets\n\n" +
                      $"Prefabs found: {requiredPrefabs.Length}";

        if (EditorUtility.DisplayDialog("Clean Bundle", message, "Preview", "Cancel"))
        {
            ShowPreview(bundleName, bundleRoot, toDelete, toKeep);
        }
    }

    private static void ShowPreview(string bundleName, string bundleRoot, List<string> toDelete, List<string> toKeep)
    {
        var window = EditorWindow.GetWindow<BundleCleanerWindow>("Clean " + bundleName);
        window.Init(bundleName, bundleRoot, toDelete, toKeep);
        window.minSize = new Vector2(700, 500);
    }

    private static string GetSelectedFolder()
    {
        var selectedGuid = Selection.assetGUIDs.FirstOrDefault();
        if (string.IsNullOrEmpty(selectedGuid))
            return null;

        var path = AssetDatabase.GUIDToAssetPath(selectedGuid);
        return AssetDatabase.IsValidFolder(path) ? path : null;
    }

    private static string[] FindAllPrefabs(string folderPath)
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
            return System.Array.Empty<string>();

        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
        return guids.Select(AssetDatabase.GUIDToAssetPath).ToArray();
    }

    private static void DeleteAssets(List<string> toDelete, string bundleRoot)
    {
        int deleted = 0;
        int failed = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var path in toDelete)
            {
                bool ok = AssetDatabase.DeleteAsset(path);
                if (ok) deleted++;
                else
                {
                    failed++;
                    Debug.LogWarning($"[Bundle Cleaner] Failed to delete: {path}");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        RemoveEmptyFolders(bundleRoot);
        AssetDatabase.Refresh();

        Debug.Log($"[Bundle Cleaner] Deleted: {deleted}, Failed: {failed}");
        EditorUtility.DisplayDialog("Done",
            $"Deleted: {deleted} assets\nFailed: {failed}\n\nEmpty folders cleaned.", "OK");
    }

    public static void RemoveEmptyFolders(string folderPath)
    {
        if (!AssetDatabase.IsValidFolder(folderPath)) return;

        var subFolders = AssetDatabase.GetSubFolders(folderPath);
        foreach (var sub in subFolders)
            RemoveEmptyFolders(sub);

        var contents = AssetDatabase.FindAssets("", new[] { folderPath });
        if (contents.Length == 0 && folderPath != "Assets")
        {
            AssetDatabase.DeleteAsset(folderPath);
            Debug.Log($"[Bundle Cleaner] Deleted empty folder: {folderPath}");
        }
    }
}

public class BundleCleanerWindow : EditorWindow
{
    private string _bundleName;
    private string _bundleRoot;
    private List<string> _toDelete;
    private List<string> _toKeep;
    private Vector2 _scrollDelete;
    private Vector2 _scrollKeep;
    private int _tab = 0;

    public void Init(string bundleName, string bundleRoot, List<string> toDelete, List<string> toKeep)
    {
        _bundleName = bundleName;
        _bundleRoot = bundleRoot;
        _toDelete = toDelete;
        _toKeep = toKeep;
    }

    private void OnGUI()
    {
        if (_toDelete == null) return;

        GUILayout.Label($"Clean Bundle — {_bundleName}", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"Keeps: All Prefab dependencies + Audio + LuaScripts + Config folders + Sprite atlases\n" +
            $"Deletes: Everything else\n\n" +
            $"Keep: {_toKeep.Count}   Delete: {_toDelete.Count}\n\n" +
            $"Click asset path to locate in Project",
            MessageType.Info);

        EditorGUILayout.Space();

        _tab = GUILayout.Toolbar(_tab, new[] { $"Delete ({_toDelete.Count})", $"Keep ({_toKeep.Count})" });

        var list = _tab == 0 ? _toDelete : _toKeep;
        var scroll = _tab == 0 ? _scrollDelete : _scrollKeep;

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(300));

        for (int i = 0; i < list.Count; i++)
        {
            var path = list[i];
            var rect = EditorGUILayout.GetControlRect(GUILayout.Height(16));

            if (GUI.Button(rect, path, EditorStyles.miniLabel))
            {
                PingAsset(path);
            }
        }

        EditorGUILayout.EndScrollView();

        if (_tab == 0) _scrollDelete = scroll; else _scrollKeep = scroll;

        EditorGUILayout.Space();

        GUI.backgroundColor = Color.red;
        if (GUILayout.Button($"DELETE {_toDelete.Count} ASSETS", GUILayout.Height(40)))
        {
            if (EditorUtility.DisplayDialog("Confirm Deletion",
                $"Delete {_toDelete.Count} assets?\nThis cannot be undone (use Git to revert).",
                "Delete", "Cancel"))
            {
                PerformDelete();
            }
        }
        GUI.backgroundColor = Color.white;
    }

    private void PingAsset(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        if (asset != null)
        {
            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }
    }

    private void PerformDelete()
    {
        int deleted = 0;
        int failed = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var path in _toDelete)
            {
                bool ok = AssetDatabase.DeleteAsset(path);
                if (ok) deleted++;
                else
                {
                    failed++;
                    Debug.LogWarning($"[Bundle Cleaner] Failed to delete: {path}");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        if (!string.IsNullOrEmpty(_bundleRoot))
        {
            SlotBundleCleaner.RemoveEmptyFolders(_bundleRoot);
            AssetDatabase.Refresh();
        }

        Debug.Log($"[Bundle Cleaner] Deleted: {deleted}, Failed: {failed}");
        EditorUtility.DisplayDialog("Done",
            $"Deleted: {deleted} assets\nFailed: {failed}\n\nEmpty folders cleaned.", "OK");

        Close();
    }
}

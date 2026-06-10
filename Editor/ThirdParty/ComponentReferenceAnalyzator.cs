using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class ComponentReferenceAnalyzator : EditorWindow
{
    [MenuItem("Tools/FindMissingScripts")]
    public static void ShowWindow()
    {
        GetWindow(typeof(ComponentReferenceAnalyzator));
    }

    private List<GameObject> _gameObjects;

    public void OnGUI()
    {
        if (GUILayout.Button("Catch all SELECTED"))
        {
            _gameObjects = Selection.gameObjects.ToList();
            Debug.Log($"CATCH {_gameObjects.Count} PREFABS");
        }

        if (GUILayout.Button("Catch ALL PREFABS (21+ Gb RAM NEEDED)"))
        {
            _gameObjects = GetAllPrefabPaths()
                .Select(AssetDatabase.LoadMainAssetAtPath)
                .OfType<GameObject>()
                .ToList();
            Debug.Log($"CATCH {_gameObjects.Count} PREFABS");
        }

        if (_gameObjects != null)
        {
            if (GUILayout.Button("LOG MISSING AND DISCONNECTED NESTED PREFABS"))
            {
                foreach (var gameObject in _gameObjects)
                {
                    LogMissedAndDisconnectedNestedPrefabs(gameObject);
                }
            }
            if (GUILayout.Button("LOG NESTED PREFABS"))
            {
                foreach (var gameObject in _gameObjects)
                {
                    Recursive(gameObject, go =>
                    {
                        bool isPrefab = PrefabUtility.IsAnyPrefabInstanceRoot(go);
                        if (isPrefab)
                        {
                            Debug.Log($"{go} is IsAnyPrefabInstanceRoot", go);
                        }
                    });
                }
            }
            
            if (GUILayout.Button("LOG MISSED COMPONENTS FROM NESTED"))
            {
                foreach (var gameObject in _gameObjects)
                {
                    Recursive(gameObject, go =>
                    {
                        int missedCount = go.GetComponents<Component>().Count(x => x == null);
                        if (missedCount > 0)
                        {
                            var nearestPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                            var rootPath = AssetDatabase.GetAssetPath(gameObject);
                            if (nearestPath != rootPath)
                            {
                                Debug.Log($"MISSED IN NESTED! {go} contains {missedCount} missed in {nearestPath} that NESTED from {rootPath}", go);
                            }
                        }
                    });
                }

                Debug.Log("Complete LOG MISSED COMPONENTS FROM NESTED");
            }
            if (GUILayout.Button("LOG MISSED COMPONENTS"))
            {
                CheckMissedPrefabs(false);
                Debug.Log("Complete LOG MISSED COMPONENTS");
            }
            if (GUILayout.Button("LOG AND REMOVE MISSED COMPONENTS"))
            {
                CheckMissedPrefabs(false);
                Debug.Log("Complete LOG AND REMOVE MISSED COMPONENTS");
            }
        }
    }

    private void CheckMissedPrefabs(bool autoRemove)
    {
        Dictionary<string, int> corruptedAssets = new Dictionary<string, int>();
        foreach (var gameObject in _gameObjects)
        {
            var gameObjectPath = AssetDatabase.GetAssetPath(gameObject);
            Recursive(gameObject, go =>
            {
                int missedCount = go.GetComponents<Component>().Count(x => x == null);
                if (missedCount > 0)
                {
                    var nearestPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                    if (!corruptedAssets.ContainsKey(nearestPath))
                    {
                        corruptedAssets.Add(nearestPath, 0);
                    }

                    if (autoRemove)
                    {
                        if (nearestPath != gameObjectPath)
                        {
                            Debug.LogError($"DROPPED! MissedComponents in NESTED {nearestPath} from {gameObjectPath}! On {go} has {missedCount} missed components count");
                        }
                        else
                        {
                            //NOT TESTED
                            int removed = RemoveMissedWARNING(go);
                            Debug.LogWarning($"REMOVED {removed} missed components removed from {go} in {nearestPath}");
                        }
                    }

                    corruptedAssets[nearestPath] = corruptedAssets[nearestPath] + missedCount;
                    Debug.Log($"In {nearestPath} go {go} has {missedCount} missed components count");
                }
            });
        }

        foreach (var corrupted in corruptedAssets.Keys)
        {
            Debug.Log($"Corrupted {corrupted} with {corruptedAssets[corrupted]} missed components count", 
                AssetDatabase.LoadMainAssetAtPath(corrupted));
        }
    }

    private void LogMissedAndDisconnectedNestedPrefabs(GameObject gameObject)
    {
        var prefabContents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(gameObject));
        List<string> missedPrefabNames = new List<string>();
        List<string> disconnectedPrefabNames = new List<string>();
        Recursive(prefabContents, go =>
        {
            bool isPrefab = PrefabUtility.IsAnyPrefabInstanceRoot(go);
            if (isPrefab)
            {
                if (PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.MissingAsset)
                {
                    missedPrefabNames.Add(go.name);
                }
                if (PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.Disconnected)
                {
                    disconnectedPrefabNames.Add(go.name);
                }
            }
        });
        PrefabUtility.UnloadPrefabContents(prefabContents);
        if (missedPrefabNames.Count > 0)
        {
            string names = string.Join(",", missedPrefabNames);
            Debug.Log($"{gameObject} HAS MISSING NESTED PREFABS {names}", gameObject);
        }
        if (disconnectedPrefabNames.Count > 0)
        {
            string names = string.Join(",", disconnectedPrefabNames);
            Debug.Log($"{gameObject} HAS DISCONNECTED NESTED PREFABS {names}", gameObject);
        }
    }
    
    private static int RemoveMissedWARNING(GameObject g)
    {
        var components = g.GetComponents<Component>();
         
        var removedIndex = 0;
           
        for (var index = 0; index < components.Length; index++)
        {
            if (components[index] != null) continue;
               
            var serializedObject = new SerializedObject(g);
               
            var prop = serializedObject.FindProperty("m_Component");
               
            prop.DeleteArrayElementAtIndex(index-removedIndex);
            removedIndex++;
         
            serializedObject.ApplyModifiedProperties();
        }

        return removedIndex;
    }

    private void Recursive(GameObject gameObject, Action<GameObject> action)
    {
        action(gameObject);
        foreach (Transform child in gameObject.transform)
        {
            Recursive(child.gameObject, action);
        }
    }

    public static string[] GetAllPrefabPaths()
    {
        string[] temp = AssetDatabase.GetAllAssetPaths();

        List<string> result = new List<string>();
        foreach (string s in temp)
        {
            if (s.Contains(".prefab")) result.Add(s);
        }

        return result.ToArray();
    }
}
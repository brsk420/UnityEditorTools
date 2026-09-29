using UnityEngine;
using UnityEditor;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

namespace _Brsk420.EditorTools
{
    public class PrefabCloner
    {
        private class DependencyReplacementData
        {
            public string OriginalPath;
            public string OriginalGUID;

            public string NewPath;
            public string NewGUID;
        }

        /// <summary>
        /// Clones the passed in prefab in a user defined directory
        /// </summary>
        /// <param name="prefab">The prefab to clone</param>
        public void ClonePrefab(GameObject prefab)
        {
            string prefabPath = AssetDatabase.GetAssetPath(prefab.GetInstanceID());
            if(!string.IsNullOrEmpty(prefabPath))
            {
                string contentRoot = GetContentRootFolder(prefabPath);

                string directory = Path.GetDirectoryName(prefabPath);//EditorUtility.SaveFolderPanel("Save cloned prefab to", Path.GetDirectoryName(prefabPath), "");

                string pathToAssets = Application.dataPath + "/";
                pathToAssets = CorrectPath(pathToAssets);
                // Make directory path from the asset folder and an absolute path.
                directory = directory.Replace(pathToAssets, "");

                string folderPathGUID = AssetDatabase.CreateFolder(directory, "(CLONED)" + prefab.name);
                string cloneFolderPath = AssetDatabase.GUIDToAssetPath(folderPathGUID);
                string dependencyFolderPath = AssetDatabase.CreateFolder(cloneFolderPath, "Dependencies");
                dependencyFolderPath = AssetDatabase.GUIDToAssetPath(dependencyFolderPath);

                if(!string.IsNullOrEmpty(dependencyFolderPath) && Clone(prefab, cloneFolderPath, dependencyFolderPath, contentRoot))
                {
                    Debug.Log("The prefab " + prefab.name + " has been cloned to " + cloneFolderPath);
                }
                else
                {
                    // Remove the folder if we failed the cloning.
                    AssetDatabase.DeleteAsset(cloneFolderPath);
                }
            }
        }

        private string CorrectPath(string path)
        {
            return path.Replace("\\", "/");
        }

        /// <summary>
        /// Finds the content/game root folder an asset belongs to, so that dependencies living
        /// outside of it (e.g. project-wide shared folders like Assets/TextMeshPro) are not cloned.
        /// Walks the path from the "Bundles" folder down, skipping category folders (name starts
        /// with "_", e.g. _gel, _games, _cards) until it reaches the actual content folder.
        /// </summary>
        /// <param name="assetPath">Path of the asset to find the content root for</param>
        private string GetContentRootFolder(string assetPath)
        {
            string[] segments = assetPath.Split('/');

            int bundlesIndex = Array.IndexOf(segments, "Bundles");
            if(bundlesIndex < 0)
            {
                return CorrectPath(Path.GetDirectoryName(assetPath));
            }

            int rootIndex = bundlesIndex + 1;
            while(rootIndex < segments.Length - 1 && segments[rootIndex].StartsWith("_"))
            {
                rootIndex++;
            }

            if(rootIndex >= segments.Length - 1)
            {
                return CorrectPath(Path.GetDirectoryName(assetPath));
            }

            return string.Join("/", segments.Take(rootIndex + 1));
        }

        /// <summary>
        /// Clone the prefab into the cloneFolderPath and clones the dependencies of the prefab into the dependencyFolderPath
        /// </summary>
        /// <param name="prefab">Prefab to clone</param>
        /// <param name="cloneFolderPath">Folder path to put the clone into</param>
        /// <param name="dependencyFolderPath">Folder path to put the dependencies into</param>
        /// <param name="contentRoot">The content/game root folder that dependencies must live under to be cloned</param>
        private bool Clone(GameObject prefab, string cloneFolderPath, string dependencyFolderPath, string contentRoot)
        {
            try
            {
                GameObject clone = GameObject.Instantiate(prefab) as GameObject;

                if(clone != null)
                {
                    GameObject clonedPrefab = PrefabUtility.CreatePrefab(cloneFolderPath + "/" + prefab.name + ".prefab", clone);

                    CloneDependencies(clonedPrefab, dependencyFolderPath, contentRoot);

                    GameObject.DestroyImmediate(clone);
                }
                else
                {
                    return false;
                }
            }
            catch(Exception ex)
            {
                Debug.LogError("Cloning the prefab " + prefab.name + " to " + cloneFolderPath + " has failed.");
                Debug.LogError(ex.Message);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Clones the dependent files and edits all the files to point at the new dependencies
        /// </summary>
        /// <param name="prefab">The cloned prefab</param>
        /// <param name="dependencyFolderPath">The folder to place the cloned dependency assets in</param>
        /// <param name="contentRoot">The content/game root folder that dependencies must live under to be cloned</param>
        private void CloneDependencies(GameObject prefab, string dependencyFolderPath, string contentRoot)
        {
            string cloneObjPath = AssetDatabase.GetAssetPath(prefab.GetInstanceID());

            // Walk the prefab's own serialized asset references instead of EditorUtility.CollectDependencies,
            // which also pulls in a referenced Sprite's entire SpriteAtlas and everything packed into it.
            string[] dependencyPaths = AssetDatabase.GetDependencies(cloneObjPath, true);

            List<DependencyReplacementData> depedencyReplacement = new List<DependencyReplacementData>();

            for(int i = 0; i < dependencyPaths.Length; i++)
            {
                string path = dependencyPaths[i];
                if(path == cloneObjPath)
                {
                    continue;
                }

                if(!depedencyReplacement.Any(x => x.OriginalPath.Equals(path)))
                {
                    if(ValidDependency(path, contentRoot))
                    {
                        string nameWithExt = Path.GetFileName(path);
                        string newPath = dependencyFolderPath + "/" + nameWithExt;
                        if(AssetDatabase.CopyAsset(path, newPath))
                        {
                            DependencyReplacementData drd = new DependencyReplacementData()
                                {
                                    OriginalPath = path,
                                    OriginalGUID = AssetDatabase.AssetPathToGUID(path),

                                    NewPath = newPath,
                                    NewGUID = AssetDatabase.AssetPathToGUID(newPath),
                                };

                            depedencyReplacement.Add(drd);
                        }
                        else
                        {
                            Debug.LogError("Failed to copy " + path + " to " + dependencyFolderPath);
                        }
                    }
                }
            }

            RelinkDependency(cloneObjPath, depedencyReplacement);

            for(int i = 0; i < depedencyReplacement.Count; i++)
            {
                RelinkDependency(depedencyReplacement[i].NewPath, depedencyReplacement);
            }

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Makes sure the dependeny is valid to be cloned
        /// </summary>
        /// <returns><c>true</c>, if dependency was valided, <c>false</c> otherwise</returns>
        /// <param name="path">Path</param>
        /// <param name="contentRoot">The content/game root folder that the dependency must live under to be cloned</param>
        private bool ValidDependency(string path, string contentRoot)
        {
            // Dependencies without paths are not needed.
            if(string.IsNullOrEmpty(path))
            {
                return false;
            }

            // We don't need to deal with scripts.
            string extension = Path.GetExtension(path);
            if(string.IsNullOrEmpty(extension) || extension == ".cs")
            {
                return false;
            }

            // Don't clone assets that live outside the prefab's own content/game folder
            // (e.g. project-wide shared folders like Assets/TextMeshPro) - keep those
            // referencing the original asset instead of duplicating them.
            if(!string.IsNullOrEmpty(contentRoot) && !path.StartsWith(contentRoot + "/"))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Checks the file to see if it has any of the dependencies
        /// </summary>
        /// <param name="objectPath">Object path.</param>
        /// <param name="dependencyReplacement">Dependency replacement.</param>
        private void RelinkDependency(string objectPath, List<DependencyReplacementData> dependencyReplacement)
        {
            for(int i = 0; i < dependencyReplacement.Count; i++)
            {
                ReplaceDepedency(objectPath, dependencyReplacement[i]);
            }
        }

        /// <summary>
        /// Reads the asset's file text and replace the old dependencies with the new dependencies
        /// </summary>
        /// <param name="clonePath">The path of the cloned asset</param>
        /// <param name="drd">The dependency replacement information</param>
        private void ReplaceDepedency(string clonePath, DependencyReplacementData drd)
        {
            StreamReader reader = new StreamReader(clonePath);
            string fileText = reader.ReadToEnd();
            reader.Close();

            string newText = Replacement(fileText, drd);
            if(!string.IsNullOrEmpty(newText))
            {
                StreamWriter writer = new StreamWriter(clonePath, false);
                writer.Write(newText);
                writer.Close();
            }
        }

        /// <summary>
        /// Replaces the old GUIDs in the text file with the new GUIDs
        /// </summary>
        /// <param name="fileText">The text of the file to parse</param>
        /// <param name="drd">The dependency replacement information</param>
        private string Replacement(string fileText, DependencyReplacementData drd)
        {
            string[] split = fileText.Split(new string[] { drd.OriginalGUID }, StringSplitOptions.None);
            if (split.Length > 1)
            {
                fileText = "";

                for (int i = 0; i < split.Length; i++)
                {
                    fileText += split[i];
                    if (i < split.Length - 1)
                    {
                        fileText += drd.NewGUID;
                    }
                }
                return fileText;
            }
            return string.Empty;
        }
    }

    public class PrefabClonerMenuItem
    {
        [MenuItem("Assets/_BrskTools/Clone Prefab")]
        static void ClonePrefab()
        {
            PrefabCloner pc = null;
            Debug.Log("Start Cloning " + Selection.gameObjects.Length + " items");
            for(int i = 0; i < Selection.gameObjects.Length; i++)
            {
                if(Selection.objects[i] is GameObject)
                {
                    if(pc == null)
                    {
                        pc = new PrefabCloner();
                    }
                    pc.ClonePrefab(Selection.gameObjects[i]);
                }
            }
            Selection.objects = new UnityEngine.Object[0];
            Debug.Log("End Cloning");
        }

        [MenuItem("Assets/_BrskTools/Clone Prefab", true)]
        static bool CanClonePrefab()
        {
            // We are not supporting multiple prefabs at the moment.
            if(Selection.gameObjects.Length != 1)
            {
                return false;
            }

            for(int i = 0; i < Selection.gameObjects.Length; i++)
            {
                bool parentIsPrefab = PrefabUtility.GetCorrespondingObjectFromSource(Selection.gameObjects[i]) != null;
                bool objIsPrefab = PrefabUtility.GetPrefabObject(Selection.gameObjects[i]) != null;
                if(!parentIsPrefab && !objIsPrefab)
                {
                    return false;
                }
            }
            return true;
        }
    }
}

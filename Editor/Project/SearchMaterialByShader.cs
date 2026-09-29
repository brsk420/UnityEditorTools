using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace _Brsk420.EditorTools
{
    public class SearchMaterialsByShader : EditorWindow
    {
        private Shader selectedShader;
        private Vector2 scrollPosition;
        private List<Material> foundMaterials = new List<Material>();

        [MenuItem("_BrskTools/Search Materials By Shader")]
        public static void OpenWindow()
        {
            GetWindow<SearchMaterialsByShader>("Search Materials");
        }

        private void OnGUI()
        {
            GUILayout.Label("Search Materials by Shader", EditorStyles.boldLabel);

            selectedShader = (Shader)EditorGUILayout.ObjectField("Shader", selectedShader, typeof(Shader), false);

            if (GUILayout.Button("Search", GUILayout.Height(30)))
            {
                SearchForMaterials();
            }

            GUILayout.Space(10);

            if (foundMaterials.Count > 0)
            {
                if (GUILayout.Button("Select All", GUILayout.Height(30)))
                {
                    SelectAllMaterials();
                }
            }

            GUILayout.Label("Found Materials:", EditorStyles.label);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
            foreach (var material in foundMaterials)
            {
                if (GUILayout.Button(material.name))
                {
                    Selection.activeObject = material;
                }
            }
            GUILayout.EndScrollView();
        }

        private void SearchForMaterials()
        {
            foundMaterials.Clear();

            if (selectedShader == null)
            {
                Debug.LogWarning("Please select a shader.");
                return;
            }

            string[] allMaterialGUIDs = AssetDatabase.FindAssets("t:Material");
            foreach (string guid in allMaterialGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material != null && material.shader == selectedShader)
                {
                    foundMaterials.Add(material);
                }
            }

            Debug.Log($"Found {foundMaterials.Count} materials using the shader: {selectedShader.name}");
        }

        private void SelectAllMaterials()
        {
            if (foundMaterials.Count > 0)
            {
                Selection.objects = foundMaterials.ToArray();
                Debug.Log($"Selected {foundMaterials.Count} materials in the Inspector.");
            }
            else
            {
                Debug.LogWarning("No materials to select.");
            }
        }
    }
}
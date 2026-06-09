
    using UnityEditor;
    using UnityEngine;

    namespace _Brsk420.EditorTools 
    {
        public class GUIDToAssetPath : EditorWindow {
            string guid = "";
            string path = "";

            [MenuItem("_BrskTools/GUIDToAssetPath")]
            static void CreateWindow() {
                GUIDToAssetPath window =
                    (GUIDToAssetPath) EditorWindow.GetWindow(typeof(GUIDToAssetPath));
            }

            void OnGUI() {
                
                
                GUILayout.Label("Enter guid");
                guid = GUILayout.TextField(guid);
                
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Get Asset Path", GUILayout.Width(120)))
                    path = GetAssetPath(guid);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                
                
               
                GUILayout.Label("Enter Path");
                path = GUILayout.TextField(path);
                
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("GetGUID", GUILayout.Width(120)))
                    guid = GetAssetGUID(path);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                
                
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Abort", GUILayout.Width(120)))
                    Close();
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                
            }

            static string GetAssetGUID(string path) {
                string p = AssetDatabase.GUIDFromAssetPath(path).ToString();
                Debug.Log(p);
                if (p.Length == 0) p = "not found";
                return p;
            }

            static string GetAssetPath(string guid) {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                Debug.Log(p);
                if (p.Length == 0) p = "not found";
                return p;
            }
        }
    }

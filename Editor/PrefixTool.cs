using UnityEditor;
using UnityEngine;

namespace _Brsk420.EditorTools
{
    class PrefixTool : EditorWindow
    {
        private string addText;
        private string oldText;
        private string replaceText;
        private string baseName = "Object"; // Шаблонное имя
        private int startNumber = 1; // Стартовый номер

        [MenuItem("_BrskTools/Prefix Tool", false, 45)]
        static void OpenWindow()
        {
            EditorWindow.GetWindow(typeof(PrefixTool), false, "Prefix Tool", true);
        }

        void OnGUI()
        {
            GUILayout.BeginVertical();

            // Добавление префикса или суффикса
            GUILayout.Label("Add Prefix or Suffix: ");
            addText = GUILayout.TextField(addText);

            if (GUILayout.Button("Add Prefix", GUILayout.Width(200)))
            {
                ModifySelectedObjects(name => addText + name);
            }

            if (GUILayout.Button("Add Suffix", GUILayout.Width(200)))
            {
                ModifySelectedObjects(name => name + addText);
            }

            if (GUILayout.Button("Remove Keyword", GUILayout.Width(200)))
            {
                ModifySelectedObjects(name => name.Replace(addText, ""));
            }

            // Замена текста
            GUILayout.Label("Replace Keyword");
            GUILayout.BeginHorizontal();
            oldText = GUILayout.TextField(oldText);
            replaceText = GUILayout.TextField(replaceText);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Replace", GUILayout.Width(200)))
            {
                ModifySelectedObjects(name => name.Replace(oldText, replaceText));
            }

            // Новая функциональность: Генерация имен по шаблону ИмяНомер
            GUILayout.Space(10);
            GUILayout.Label("Generate Names with Number:");
            baseName = GUILayout.TextField(baseName, GUILayout.Width(200));
            startNumber = EditorGUILayout.IntField("Start Number", startNumber);

            if (GUILayout.Button("Generate Names", GUILayout.Width(200)))
            {
                GenerateNames();
            }

            GUILayout.EndVertical();
        }

        void ModifySelectedObjects(System.Func<string, string> modifyName)
        {
            if (string.IsNullOrEmpty(addText) && string.IsNullOrEmpty(oldText))
            {
                Debug.LogWarning("Text cannot be empty");
                return;
            }

            foreach (Object obj in Selection.objects)
            {
                Undo.RecordObject(obj, "Modify Object Name");
                string newName = modifyName(obj.name);

                // Rename asset if applicable
                if (Selection.assetGUIDs.Length > 0)
                {
                    string path = AssetDatabase.GetAssetPath(obj);
                    AssetDatabase.RenameAsset(path, newName);
                }
                else
                {
                    obj.name = newName;
                }
            }
        }

        void GenerateNames()
        {
            int currentNumber = startNumber;

            foreach (Object obj in Selection.objects)
            {
                Undo.RecordObject(obj, "Generate Object Name");
                string newName = $"{baseName}{currentNumber}";

                // Переименование ассета, если возможно
                if (Selection.assetGUIDs.Length > 0)
                {
                    string path = AssetDatabase.GetAssetPath(obj);
                    AssetDatabase.RenameAsset(path, newName);
                }
                else
                {
                    obj.name = newName;
                }

                currentNumber++; // Увеличиваем номер для следующего объекта
            }
        }
    }
}
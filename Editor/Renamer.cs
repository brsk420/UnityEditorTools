using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace _Brsk420.EditorTools
{
    enum RenameType
    {
        ReplaceText,
        AddText,
        Format
    }

    enum TextPlace
    {
        BeforeName,
        AfterName
    }

    public class Renamer : EditorWindow
    {

        private GameObject[] selected;
        private string newName;
        [SerializeField]  RenameType renameType;
        [SerializeField]  TextPlace textPlace;
        
        [FormerlySerializedAs("findString")] [SerializeField] string oldString;
        [FormerlySerializedAs("nawString")] [FormerlySerializedAs("replaceToString")] [SerializeField] string newString;
        [SerializeField] int index = 0;

        [MenuItem("_BrskTools/Renamer", false, 10003)]
        static void Init()
        {
            EditorWindow.GetWindow(typeof(Renamer));
        }


        private void OnGUI()
        {
            selected = Selection.gameObjects;
            GUILayout.Label("Rename You GameObjects", EditorStyles.boldLabel);
            
            renameType =  (RenameType)EditorGUILayout.EnumPopup("RenameType", renameType, EditorStyles.popup);
            switch (renameType)
            {
                case RenameType.ReplaceText:
                    oldString = EditorGUILayout.TextField("Find", oldString);
                    newString = EditorGUILayout.TextField("ReplaceTo", newString);
                    break;
                
                case RenameType.Format:
                    GUILayout.Label("Work Not Correct", EditorStyles.boldLabel);
                    newString = EditorGUILayout.TextField("Name", newString);
                    index = EditorGUILayout.IntField("StartIndex", index);
                    break;
                
                case RenameType.AddText:
                    textPlace = (TextPlace)EditorGUILayout.EnumPopup("TextPlace", textPlace, EditorStyles.popup);
                    newString = EditorGUILayout.TextField("TextToAdd", newString);
                    break;
            }
            
            if (GUILayout.Button("Rename"))
            {
                Rename(renameType);
            }
            
        }

        private void Rename(RenameType type)
        {

            for (int i = 0; i < selected.Length; i++)
            {
                var obj = selected[i];
                switch (type)
                {
                    case RenameType.ReplaceText:
                        newName = obj.name.Replace(oldString, newString);
                        break;
                    case RenameType.Format:
                        newName = newString + "_" + (i + index);
                        break;
                    case RenameType.AddText:
                        newName = RenameAdd(textPlace, obj);
                        break;
                }
                obj.name = newName;
            }
            
              
        }

        private string RenameAdd(TextPlace place, GameObject obj)
        {
            var str = newString;
            switch (place)
            {
               case TextPlace.AfterName:
                   str = obj.name.Insert(obj.name.Length, newString);
                   break;
               
               case TextPlace.BeforeName:
                   str = obj.name.Insert(0, newString);
                   break;
            }

            return str;
        }
    }
}
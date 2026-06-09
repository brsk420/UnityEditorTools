using UnityEngine;
using UnityEditor;

public class TransformCopier : EditorWindow
{
    public GameObject[] sourceObjects = new GameObject[0];
    public GameObject[] targetObjects = new GameObject[0];

    public bool copyX = true;
    public bool copyY = true;
    public bool copyZ = true;
    public bool copyRotation = true;
    public bool copyScale = true;

    private SerializedObject so;
    private SerializedProperty propSource;
    private SerializedProperty propTarget;

    [MenuItem("_BrskTools/Transform Copier")]
    public static void ShowWindow()
    {
        GetWindow<TransformCopier>("Transform Copier");
    }

    private void OnEnable()
    {
        so = new SerializedObject(this);
        propSource = so.FindProperty("sourceObjects");
        propTarget = so.FindProperty("targetObjects");
    }

    private void OnGUI()
    {
        so.Update();

        EditorGUILayout.LabelField("Настройки копирования", EditorStyles.boldLabel);
        
        // Ряд чекбоксов для осей
        EditorGUILayout.BeginHorizontal();
        copyX = EditorGUILayout.ToggleLeft("X", copyX, GUILayout.Width(40));
        copyY = EditorGUILayout.ToggleLeft("Y", copyY, GUILayout.Width(40));
        copyZ = EditorGUILayout.ToggleLeft("Z", copyZ, GUILayout.Width(40));
        EditorGUILayout.EndHorizontal();

        copyRotation = EditorGUILayout.ToggleLeft("Copy Rotation", copyRotation);
        copyScale = EditorGUILayout.ToggleLeft("Copy Scale", copyScale);

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(propSource, new GUIContent("Откуда (Source)"), true);
        EditorGUILayout.PropertyField(propTarget, new GUIContent("Куда (Target)"), true);

        EditorGUILayout.Space();

        if (GUILayout.Button("GO", GUILayout.Height(30)))
        {
            ExecuteCopy();
        }

        so.ApplyModifiedProperties();
    }

    private void ExecuteCopy()
    {
        int count = Mathf.Min(sourceObjects.Length, targetObjects.Length);

        if (count == 0) return;

        Undo.RecordObjects(targetObjects, "Copy Transforms Selective");

        for (int i = 0; i < count; i++)
        {
            if (sourceObjects[i] == null || targetObjects[i] == null) continue;

            Transform src = sourceObjects[i].transform;
            Transform dst = targetObjects[i].transform;

            // Позиция
            Vector3 newPos = dst.position;
            if (copyX) newPos.x = src.position.x;
            if (copyY) newPos.y = src.position.y;
            if (copyZ) newPos.z = src.position.z;
            dst.position = newPos;

            // Ротация
            if (copyRotation) dst.rotation = src.rotation;

            // Масштаб
            if (copyScale) dst.localScale = src.localScale;
        }

        Debug.Log($"Готово! Обработано объектов: {count}");
    }
}
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

[InitializeOnLoad]
public class HierarchyCustomizer
{
    private static readonly GUIStyle LabelStyle;

    static HierarchyCustomizer()
    {
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
        
        LabelStyle = new GUIStyle()
        {
            fontSize = 9,
            alignment = TextAnchor.MiddleRight,
            normal = new GUIStyleState { textColor = Color.gray }
        };
    }

    private static void OnHierarchyItemGUI(int instanceID, Rect selectionRect)
    {
        GameObject obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        if (obj == null) return;

        // Определяем позиции для колонок
        float rightOffset = 20f; // Отступ от правого края
        float zWidth = 45f;      // Ширина колонки Z
        float iconWidth = 60f;   // Ширина области иконок

        // 1. Рисуем значение Z
        Rect zRect = new Rect(selectionRect.xMax - rightOffset - zWidth, selectionRect.y, zWidth, selectionRect.height);
        string zValue = obj.transform.position.z.ToString("F1");
        GUI.Label(zRect, zValue, LabelStyle);

        // 2. Рисуем значки компонентов
        Rect iconRect = new Rect(selectionRect.xMax - rightOffset - zWidth - iconWidth, selectionRect.y, iconWidth, selectionRect.height);
        DrawComponentIcons(obj, iconRect);
    }

    private static void DrawComponentIcons(GameObject obj, Rect rect)
    {
        Component[] components = obj.GetComponents<Component>();
        float currentX = rect.xMax;

        // Проходимся по компонентам (кроме Transform)
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] == null || components[i] is Transform) continue;

            Texture2D icon = AssetPreview.GetMiniThumbnail(components[i]);
            if (icon != null)
            {
                currentX -= 16f;
                Rect r = new Rect(currentX, rect.y, 16f, rect.height);
                GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit);
            }
        }
    }
}
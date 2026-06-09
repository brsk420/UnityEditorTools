using UnityEngine;
using UnityEditor;

public class CreateSameNameParent
{
    // Атрибут MenuCommand позволяет добавить пункт в контекстное меню (ПКМ) в окне Иерархии.
    // 'GameObject/Create SameName Parent' определяет путь в меню.
    // 'priority = 0' задает местоположение пункта в меню.
    [MenuItem("GameObject/Create SameName Parent", priority = 0)]
    // Метод, который будет вызван при нажатии на пункт меню.
    private static void DoCreateSameNameParent()
    {
        // Получаем все выделенные объекты в Иерархии.
        // Selection.gameObjects возвращает массив GameObject.
        GameObject[] selectedObjects = Selection.gameObjects;

        // Итерируем по каждому выделенному объекту.
        foreach (GameObject childObject in selectedObjects)
        {
            // 1. Создаем новый GameObject, который будет родителем.
            GameObject newParent = new GameObject(childObject.name);

            // 2. Устанавливаем положение нового родителя.
            // Мы хотим, чтобы новый родитель появился там же, где и дочерний объект.
            Transform childTransform = childObject.transform;
            newParent.transform.position = childTransform.position;
            newParent.transform.rotation = childTransform.rotation;
            newParent.transform.localScale = childTransform.localScale;

            // 3. Устанавливаем родителя для нового родителя.
            // Если у выделенного объекта уже был родитель, 
            // новый родитель должен стать его новым дочерним объектом.
            newParent.transform.SetParent(childTransform.parent, true);

            // 4. Устанавливаем новый объект как родителя для выделенного объекта.
            // 'true' означает, что мировое положение (World Position) дочернего объекта 
            // не изменится после установки нового родителя.
            childTransform.SetParent(newParent.transform, true);

            // Опционально: Выводим сообщение в консоль.
            Debug.Log($"Создан родитель '{newParent.name}' для объекта '{childObject.name}'.");
        }

        // Опционально: Перевыбираем объекты, чтобы новый родитель был сразу виден/выделен.
        // Выделяем всех новых родителей.
        Selection.objects = System.Array.ConvertAll(selectedObjects, childObject => childObject.transform.parent.gameObject);
    }

    // Атрибут ValidateMenuItem используется для включения/выключения пункта меню.
    // Пункт будет активен только если в Иерархии выделен хотя бы один GameObject.
    [MenuItem("GameObject/Create SameName Parent", true)]
    private static bool ValidateDoCreateSameNameParent()
    {
        // Проверяем, что есть хотя бы один выделенный объект, который является GameObject.
        return Selection.activeGameObject != null;
    }
}
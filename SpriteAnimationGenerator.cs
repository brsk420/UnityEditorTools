// SpriteAnimationGenerator.cs
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace _Brsk420.EditorTools
{
    public class SpriteAnimationGenerator : EditorWindow
    {
        private List<DefaultAsset> sourceFolders = new List<DefaultAsset>();
        private DefaultAsset saveFolder;
        private int stepNumber = 2;
        private string hierarchyPath = "Sprite";
        private int FRAME_RATE = 30;

        [MenuItem("_BrskTools/Sprite Animation Generator")]
        public static void ShowWindow()
        {
            GetWindow<SpriteAnimationGenerator>("Animation Generator");
        }

        // Отрисовка интерфейса окна
        private void OnGUI()
        {
            GUILayout.Label("Animation From Sprites", EditorStyles.boldLabel);

            // --- Блок для выбора исходных папок (Drag & Drop) ---
            GUILayout.Label("Source Sprite Folders", EditorStyles.miniLabel);
            
            // Создаем область для Drag & Drop
            Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Перетащите папки сюда");

            HandleDragAndDrop(dropArea);

            // Отображение списка папок
            for (int i = 0; i < sourceFolders.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                // Поле для папки остается, чтобы можно было удалить или заменить ее
                sourceFolders[i] = (DefaultAsset)EditorGUILayout.ObjectField(
                    $"Folder {i + 1}",
                    sourceFolders[i],
                    typeof(DefaultAsset),
                    false);

                // Кнопка для удаления папки
                if (GUILayout.Button("X", GUILayout.Width(20)))
                {
                    sourceFolders.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.Space(5);

            // --- Блок для выбора папки сохранения ---
            saveFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Save Animation Folder",
                saveFolder,
                typeof(DefaultAsset),
                false);
            
            EditorGUILayout.Space(10);
            
            // Поле для ввода числа
            stepNumber = EditorGUILayout.IntField("Step", stepNumber);
            if (stepNumber < 1) stepNumber = 1;

			//Поле для ввода Оригинального FRAMERATE
			FRAME_RATE = EditorGUILayout.IntField("Original FrameRAte", FRAME_RATE);

            // Поле для ввода иерархии
            hierarchyPath = EditorGUILayout.TextField("Hierarchy Path", hierarchyPath);

            // Кнопка "Generate"
            if (GUILayout.Button("Generate Animations", GUILayout.Height(30)))
            {
                GenerateAnimations();
            }
        }

        private void HandleDragAndDrop(Rect dropArea)
        {
            // Отслеживаем событие Drag & Drop
            Event evt = Event.current;
            if (!dropArea.Contains(evt.mousePosition)) return;

            // Типы DragEventType: DragUpdated, DragPerform, DragExited
            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    // Разрешаем Drag
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    
                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        
                        // Перебираем все перетащенные объекты
                        foreach (Object draggedObject in DragAndDrop.objectReferences)
                        {
                            // Проверяем, является ли объект DefaultAsset (что часто соответствует папкам)
                            // и является ли он папкой в файловой системе.
                            if (draggedObject is DefaultAsset)
                            {
                                string path = AssetDatabase.GetAssetPath(draggedObject);
                                
                                // Проверяем, что это директория и ее еще нет в списке
                                if (Directory.Exists(path) && !sourceFolders.Contains(draggedObject as DefaultAsset))
                                {
                                    sourceFolders.Add(draggedObject as DefaultAsset);
                                }
                            }
                        }
                        // Уведомляем о необходимости перерисовки
                        GUI.changed = true;
                        // Обязательно "съедаем" событие
                        Event.current.Use();
                    }
                    break;
            }
        }

        // Метод GenerateAnimations остается без изменений
        private void GenerateAnimations()
        {
            // 1. Проверка входных данных
            if (sourceFolders == null || sourceFolders.Count == 0 || sourceFolders.All(f => f == null))
            {
                EditorUtility.DisplayDialog("Ошибка", "Пожалуйста, укажите хотя бы одну папку со спрайтами.", "OK");
                return;
            }

            if (saveFolder == null)
            {
                EditorUtility.DisplayDialog("Ошибка", "Пожалуйста, укажите папку для сохранения анимаций.", "OK");
                return;
            }

            string savePath = AssetDatabase.GetAssetPath(saveFolder);
            if (!Directory.Exists(savePath))
            {
                EditorUtility.DisplayDialog("Ошибка", "Указанный путь сохранения не является папкой.", "OK");
                return;
            }
            
            int successfulGenerations = 0;
            
            // Обрабатываем каждую выбранную папку
            foreach (DefaultAsset sourceFolder in sourceFolders.Where(f => f != null))
            {
                // Пропускаем, если папка не выбрана или не существует
                string sourcePath = AssetDatabase.GetAssetPath(sourceFolder);
                if (!Directory.Exists(sourcePath))
                {
                    Debug.LogWarning($"Путь '{sourcePath}' не найден или не является папкой. Пропуск.");
                    continue;
                }

                // 2. Загрузка всех спрайтов из текущей папки
                string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { sourcePath });
                List<Sprite> sprites = new List<Sprite>();
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    // Загружаем все спрайты (включая части атласа)
                    sprites.AddRange(AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath).OfType<Sprite>());
                }
                
                // Если LoadAllAssetRepresentationsAtPath не сработал (например, не атлас), загружаем основной
                if (sprites.Count == 0 && guids.Length > 0)
                {
                     foreach (string guid in guids)
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        Sprite mainSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                        if (mainSprite != null)
                            sprites.Add(mainSprite);
                    }
                }


                // Сортируем спрайты по имени
                sprites = sprites.OrderBy(s => s.name).ToList();

                if (sprites.Count == 0)
                {
                    Debug.LogWarning($"В папке '{sourceFolder.name}' не найдено спрайтов. Пропуск.");
                    continue;
                }

                // 3. Создание клипа анимации
                AnimationClip clip = new AnimationClip();
                clip.frameRate = FRAME_RATE / stepNumber;

                EditorCurveBinding curveBinding = new EditorCurveBinding
                {
                    type = typeof(SpriteRenderer),
                    path = hierarchyPath,
                    propertyName = "m_Sprite"
                };

                // 4. Создание ключевых кадров
                List<ObjectReferenceKeyframe> keyframes = new List<ObjectReferenceKeyframe>();

                for (int i = 0; i < sprites.Count; i += stepNumber)
                {
                    ObjectReferenceKeyframe keyframe = new ObjectReferenceKeyframe
                    {
                        time = (float)keyframes.Count / clip.frameRate,
                        value = sprites[i]
                    };
                    keyframes.Add(keyframe);
                }

                AnimationUtility.SetObjectReferenceCurve(clip, curveBinding, keyframes.ToArray());

                // 5. Сохранение анимации в указанную папку
                string clipName = $"{sourceFolder.name}.anim";
                string finalSavePath = Path.Combine(savePath, clipName);

                // Убедимся, что имя уникально
                finalSavePath = AssetDatabase.GenerateUniqueAssetPath(finalSavePath);

                AssetDatabase.CreateAsset(clip, finalSavePath);
                Debug.Log($"Анимация успешно создана: {finalSavePath}");
                successfulGenerations++;
            }
            
            // Финальные операции
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (successfulGenerations > 0)
            {
                EditorUtility.DisplayDialog("Успех", $"Создано {successfulGenerations} анимаций в папке: {saveFolder.name}", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Внимание", "Не удалось создать ни одной анимации. Проверьте папки со спрайтами.", "OK");
            }
        }
    }
}
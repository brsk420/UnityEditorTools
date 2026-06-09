using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;


// --
/// <summary>
/// Запекает анимационный клип с несколькими SpriteRenderer в последовательность
/// отдельных спрайтов + новый AnimationClip c одной дорожкой Sprite.
/// 
/// Подготовка:
/// - Создай пустую сцену.
/// - Помести туда объект с аниматором/анимацией и двумя (или больше) SpriteRenderer.
/// - Выставь объект в кадре как нужно.
///
/// Использование:
/// - Открой окно через меню: Tools/Bake/Bake Sprite Animation.
/// - Укажи источник (root-объект), анимационный клип и размер текстуры кадра.
/// - Выбери папку внутри проекта (Assets/...) для сохранения.
/// - Нажми "Bake".
///
/// Результат:
/// - PNG‑кадры в указанной папке.
/// - Импортированные как Sprite текстуры.
/// - Новый AnimationClip, анимирующий один SpriteRenderer по этим кадрам.
/// </summary>
public class BakeAnimToSprite : EditorWindow
{
    private GameObject _sourceRoot;
    private AnimationClip _clip;
    private int _textureWidth = 512;
    private int _textureHeight = 512;
    private Color _backgroundColor = new Color(0, 0, 0, 0);
    private Vector2 _cameraOffset = Vector2.zero;
    private float _cameraSize = 3f;
    private string _outputFolder = "Assets";
    private string _clipNameSuffix = "_Baked";

    [MenuItem("_BrskTools/Bake/Bake Sprite Animation")]
    private static void ShowWindow()
    {
        var window = GetWindow<BakeAnimToSprite>("Bake Anim To Sprite");
        window.minSize = new Vector2(400, 260);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Источник анимации", EditorStyles.boldLabel);
        _sourceRoot = (GameObject)EditorGUILayout.ObjectField("Root GameObject", _sourceRoot, typeof(GameObject), true);
        _clip = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", _clip, typeof(AnimationClip), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Параметры рендера", EditorStyles.boldLabel);
        _textureWidth = EditorGUILayout.IntField("Texture Width", _textureWidth);
        _textureHeight = EditorGUILayout.IntField("Texture Height", _textureHeight);
        _backgroundColor = EditorGUILayout.ColorField("Background", _backgroundColor);
        _cameraOffset = EditorGUILayout.Vector2Field("Camera Offset", _cameraOffset);
        _cameraSize = EditorGUILayout.FloatField("Camera Size", _cameraSize);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Вывод", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Output Folder", _outputFolder);
        if (GUILayout.Button("Выбрать...", GUILayout.Width(90)))
        {
            var absPath = EditorUtility.SaveFolderPanel("Выбери папку внутри проекта", Application.dataPath, "");
            if (!string.IsNullOrEmpty(absPath))
            {
                if (!absPath.StartsWith(Application.dataPath))
                {
                    EditorUtility.DisplayDialog("Ошибка", "Папка должна быть внутри Assets проекта.", "OK");
                }
                else
                {
                    _outputFolder = "Assets" + absPath.Substring(Application.dataPath.Length);
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        _clipNameSuffix = EditorGUILayout.TextField("Суффикс имени клипа", _clipNameSuffix);

        EditorGUILayout.Space();

        GUI.enabled = _sourceRoot != null && _clip != null;
        if (GUILayout.Button("Bake"))
        {
            Bake();
        }
        GUI.enabled = true;
    }

    private void Bake()
    {
        if (_sourceRoot == null || _clip == null)
        {
            Debug.LogError("Укажи Root GameObject и AnimationClip.");
            return;
        }

        if (_textureWidth <= 0 || _textureHeight <= 0)
        {
            Debug.LogError("Неверный размер текстуры.");
            return;
        }

        var renderers = _sourceRoot.GetComponentsInChildren<SpriteRenderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogError("На объекте и его детях нет SpriteRenderer.");
            return;
        }

        // Сортируем по sorting layer / order, чтобы верхние спрайты рисовались последними.
        System.Array.Sort(renderers, (a, b) =>
        {
            int layerCompare = SortingLayer.GetLayerValueFromID(a.sortingLayerID)
                .CompareTo(SortingLayer.GetLayerValueFromID(b.sortingLayerID));
            if (layerCompare != 0) return layerCompare;
            return a.sortingOrder.CompareTo(b.sortingOrder);
        });

        // Подсчёт количества кадров
        float frameRate = _clip.frameRate;
        int frameCount = Mathf.CeilToInt(_clip.length * frameRate);

        // Первый проход — собираем общие bounds по всему клипу
        Bounds clipBounds = new Bounds();
        bool hasClipBounds = false;

        AnimationMode.StartAnimationMode();
        try
        {
            for (int i = 0; i < frameCount; i++)
            {
                float time = Mathf.Min(_clip.length, i / frameRate);
                AnimationMode.SampleAnimationClip(_sourceRoot, _clip, time);

                if (TryCalculateBounds(renderers, out var frameBounds))
                {
                    if (!hasClipBounds)
                    {
                        clipBounds = frameBounds;
                        hasClipBounds = true;
                    }
                    else
                    {
                        clipBounds.Encapsulate(frameBounds);
                    }
                }
            }
        }
        finally
        {
            AnimationMode.StopAnimationMode();
        }

        if (!hasClipBounds)
        {
            Debug.LogError("Не удалось вычислить bounds клипа — возможно, в нём нет видимых спрайтов.");
            return;
        }

        // Если указан размер камеры, переопределим область захвата вручную,
        // чтобы поведение оставалось похожим на камеру.
        if (_cameraSize > 0f)
        {
            float worldHalfHeight = _cameraSize;
            float aspect = (float)_textureWidth / _textureHeight;
            float worldHalfWidth = worldHalfHeight * aspect;

            var center = _sourceRoot.transform.position +
                         new Vector3(_cameraOffset.x, _cameraOffset.y, 0f);

            clipBounds = new Bounds(center, new Vector3(worldHalfWidth * 2f, worldHalfHeight * 2f, 1f));
        }

        // Подготавливаем список спрайтов, которые будем создавать
        var spritesForClip = new List<Sprite>();
        var createdTexturePaths = new List<string>();

        // Второй проход — уже рисуем покадрово в Texture2D, без камеры.
        AnimationMode.StartAnimationMode();
        try
        {
            AssetDatabase.StartAssetEditing();

            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                float time = Mathf.Min(_clip.length, frameIndex / frameRate);
                AnimationMode.SampleAnimationClip(_sourceRoot, _clip, time);

                // Буфер итогового кадра
                var frameColors = new Color[_textureWidth * _textureHeight];
                for (int i = 0; i < frameColors.Length; i++)
                {
                    frameColors[i] = _backgroundColor;
                }

                // Рисуем каждый SpriteRenderer в буфер
                foreach (var sr in renderers)
                {
                    if (sr == null || !sr.enabled || sr.sprite == null)
                        continue;

                    var sprite = sr.sprite;
                    var srcTex = sprite.texture;
                    if (srcTex == null)
                        continue;

                    // Убеждаемся, что текстура читаемая (isReadable).
                    if (!srcTex.isReadable)
                    {
                        string texPath = AssetDatabase.GetAssetPath(srcTex);
                        if (!string.IsNullOrEmpty(texPath))
                        {
                            var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                            if (importer != null && !importer.isReadable)
                            {
                                importer.isReadable = true;
                                importer.textureCompression = TextureImporterCompression.Uncompressed;
                                importer.SaveAndReimport();

                                srcTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                                if (srcTex == null || !srcTex.isReadable)
                                {
                                    Debug.LogWarning($"Не удалось сделать текстуру читаемой: {texPath}");
                                    continue;
                                }
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"Не удалось получить путь текстуры для спрайта {sprite.name}");
                            continue;
                        }
                    }

                    Rect texRect = sprite.textureRect;
                    int srcX = Mathf.FloorToInt(texRect.x);
                    int srcY = Mathf.FloorToInt(texRect.y);
                    int srcW = Mathf.FloorToInt(texRect.width);
                    int srcH = Mathf.FloorToInt(texRect.height);

                    Color[] srcPixels;
                    try
                    {
                        srcPixels = srcTex.GetPixels(srcX, srcY, srcW, srcH);
                    }
                    catch (System.Exception e)
                    {
                        string texPath = AssetDatabase.GetAssetPath(srcTex);
                        Debug.LogWarning(
                            $"BakeAnimToSprite: не удалось прочитать пиксели из текстуры '{srcTex.name}' (путь: {texPath}). " +
                            "Проверь, что в импортёре включён Read/Write Enabled. Исключение: " + e.Message);
                        continue;
                    }

                    float ppu = sprite.pixelsPerUnit;

                    for (int y = 0; y < srcH; y++)
                    {
                        for (int x = 0; x < srcW; x++)
                        {
                            Color srcColor = srcPixels[y * srcW + x];
                            if (srcColor.a <= 0f)
                                continue;

                            // Координата в локальном пространстве спрайта
                            float localX = (x - sprite.pivot.x) / ppu;
                            float localY = (y - sprite.pivot.y) / ppu;

                            Vector3 worldPos = sr.transform.TransformPoint(new Vector3(localX, localY, 0f));

                            // Нормированные координаты в пределах clipBounds
                            float nx = (worldPos.x - clipBounds.min.x) / clipBounds.size.x;
                            float ny = (worldPos.y - clipBounds.min.y) / clipBounds.size.y;

                            if (nx < 0f || nx > 1f || ny < 0f || ny > 1f)
                                continue;

                            int dx = Mathf.RoundToInt(nx * (_textureWidth - 1));
                            int dy = Mathf.RoundToInt(ny * (_textureHeight - 1));

                            int dstIndex = dy * _textureWidth + dx;
                            Color dstColor = frameColors[dstIndex];

                            // Альфа-композитинг "src over dst"
                            float a = srcColor.a + dstColor.a * (1f - srcColor.a);
                            Color outColor;
                            if (a <= 0f)
                            {
                                outColor = Color.clear;
                            }
                            else
                            {
                                outColor = (srcColor * srcColor.a + dstColor * dstColor.a * (1f - srcColor.a)) / a;
                                outColor.a = a;
                            }

                            frameColors[dstIndex] = outColor;
                        }
                    }
                }

                var frameTex = new Texture2D(_textureWidth, _textureHeight, TextureFormat.ARGB32, false);
                frameTex.SetPixels(frameColors);
                frameTex.Apply();

                string texName = $"{_clip.name}_baked_{frameIndex:0000}.png";
                string assetPath = Path.Combine(_outputFolder, texName);

                byte[] pngData = frameTex.EncodeToPNG();
                File.WriteAllBytes(assetPath, pngData);
                createdTexturePaths.Add(assetPath);

                Object.DestroyImmediate(frameTex);
            }

            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();

            // Настраиваем импорт как Sprite и собираем спрайты
            foreach (string path in createdTexturePaths)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null)
                {
                    spritesForClip.Add(sprite);
                }
            }
        }
        finally
        {
            AnimationMode.StopAnimationMode();
        }

        if (spritesForClip.Count == 0)
        {
            Debug.LogError("Не удалось создать спрайты для клипа.");
            return;
        }

        CreateBakedClip(spritesForClip, frameRate);

        Debug.Log($"Bake завершён. Кадров: {spritesForClip.Count}, путь: {_outputFolder}");
    }

    private static bool TryCalculateBounds(SpriteRenderer[] renderers, out Bounds totalBounds)
    {
        bool hasBounds = false;
        totalBounds = new Bounds();

        foreach (var sr in renderers)
        {
            if (sr == null || !sr.enabled || sr.sprite == null)
                continue;

            if (!hasBounds)
            {
                totalBounds = sr.bounds;
                hasBounds = true;
            }
            else
            {
                totalBounds.Encapsulate(sr.bounds);
            }
        }

        return hasBounds;
    }

    private void CreateBakedClip(List<Sprite> sprites, float frameRate)
    {
        var bakedClip = new AnimationClip
        {
            frameRate = frameRate
        };

        var keyframes = new ObjectReferenceKeyframe[sprites.Count];
        for (int i = 0; i < sprites.Count; i++)
        {
            keyframes[i] = new ObjectReferenceKeyframe
            {
                time = i / frameRate,
                value = sprites[i]
            };
        }

        // Анимируем m_Sprite на SpriteRenderer на корне (путь = "")
        var binding = new EditorCurveBinding
        {
            path = string.Empty,
            type = typeof(SpriteRenderer),
            propertyName = "m_Sprite"
        };

        AnimationUtility.SetObjectReferenceCurve(bakedClip, binding, keyframes);

        string clipName = _clip.name + _clipNameSuffix;
        string clipPath = Path.Combine(_outputFolder, clipName + ".anim");

        AssetDatabase.CreateAsset(bakedClip, clipPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Создан новый AnimationClip: {clipPath}");
    }
}


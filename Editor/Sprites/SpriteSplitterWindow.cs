// P10TR / Sprite Splitter
// Режет выбранные текстуры ровно посередине (по вертикали, по горизонтали или на четвертинки)
// и обрезает прозрачные пиксели.
//
// Установка: положите файл в любую папку с именем "Editor",
// например Assets/Editor/P10TR/SpriteSplitterWindow.cs
//
// Запуск: ПКМ в окне Project -> P10TR -> Sprite Splitter
// (или верхнее меню Assets -> P10TR -> Sprite Splitter)

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace P10TR.EditorTools
{
    public class SpriteSplitterWindow : EditorWindow
    {
        // Все инструменты P10TR кладите в этот же корень меню: "Assets/P10TR/Имя инструмента"
        const string MenuRoot = "Assets/P10TR/";
        const string PrefsPrefix = "P10TR.SpriteSplitter.";

        enum Keep { First, Second, Both }              // вертикаль: Левая/Правая, горизонталь: Верхняя/Нижняя
        enum TrimMode { None, CutEdgesOnly, AllEdges }

        static readonly string[] KeepVerticalLabels = { "Левую", "Правую", "Обе" };
        static readonly string[] KeepHorizontalLabels = { "Верхнюю", "Нижнюю", "Обе" };
        static readonly string[] TrimLabels = { "Не обрезать", "Только со стороны разреза", "Со всех сторон" };

        bool cutVertical = true;
        Keep keepVertical = Keep.First;
        bool cutHorizontal;
        Keep keepHorizontal = Keep.First;
        TrimMode trimMode = TrimMode.CutEdgesOnly;
        int alphaThreshold;
        bool overwriteOriginal;

        readonly List<Texture2D> textures = new List<Texture2D>();
        Vector2 scroll;

        struct Part
        {
            public RectInt rect;
            public string suffix;
            public bool cutLeft, cutRight, cutBottom, cutTop; // с каких сторон прошёл разрез
        }

        // ------------------------------------------------------------------ Меню

        [MenuItem(MenuRoot + "Sprite Splitter", false, 1000)]
        static void Open()
        {
            var window = GetWindow<SpriteSplitterWindow>("Sprite Splitter");
            window.minSize = new Vector2(340, 380);
            window.RefreshSelection();
            window.Show();
        }

        // ------------------------------------------------------------------ Жизненный цикл

        void OnEnable()
        {
            cutVertical = EditorPrefs.GetBool(PrefsPrefix + "cutVertical", true);
            keepVertical = (Keep)EditorPrefs.GetInt(PrefsPrefix + "keepVertical", 0);
            cutHorizontal = EditorPrefs.GetBool(PrefsPrefix + "cutHorizontal", false);
            keepHorizontal = (Keep)EditorPrefs.GetInt(PrefsPrefix + "keepHorizontal", 0);
            trimMode = (TrimMode)EditorPrefs.GetInt(PrefsPrefix + "trimMode", 1);
            alphaThreshold = EditorPrefs.GetInt(PrefsPrefix + "alphaThreshold", 0);
            overwriteOriginal = EditorPrefs.GetBool(PrefsPrefix + "overwrite", false);
            RefreshSelection();
        }

        void OnDisable()
        {
            EditorPrefs.SetBool(PrefsPrefix + "cutVertical", cutVertical);
            EditorPrefs.SetInt(PrefsPrefix + "keepVertical", (int)keepVertical);
            EditorPrefs.SetBool(PrefsPrefix + "cutHorizontal", cutHorizontal);
            EditorPrefs.SetInt(PrefsPrefix + "keepHorizontal", (int)keepHorizontal);
            EditorPrefs.SetInt(PrefsPrefix + "trimMode", (int)trimMode);
            EditorPrefs.SetInt(PrefsPrefix + "alphaThreshold", alphaThreshold);
            EditorPrefs.SetBool(PrefsPrefix + "overwrite", overwriteOriginal);
        }

        void OnSelectionChange()
        {
            RefreshSelection();
            Repaint();
        }

        void RefreshSelection()
        {
            textures.Clear();
            // DeepAssets: если выделена папка — берутся все текстуры внутри неё
            foreach (var t in Selection.GetFiltered<Texture2D>(SelectionMode.DeepAssets))
                if (!textures.Contains(t)) textures.Add(t);
        }

        // ------------------------------------------------------------------ GUI

        void OnGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Выбрано текстур: {textures.Count}", EditorStyles.boldLabel);

            if (textures.Count == 0)
            {
                EditorGUILayout.HelpBox("Выделите картинки (или папку с ними) в окне Project.", MessageType.Info);
            }
            else
            {
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(140));
                using (new EditorGUI.DisabledScope(true))
                {
                    foreach (var t in textures)
                        EditorGUILayout.ObjectField($"{t.width}×{t.height}", t, typeof(Texture2D), false);
                }
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Разрез посередине", EditorStyles.boldLabel);

            cutVertical = EditorGUILayout.ToggleLeft("По вертикали (левая | правая)", cutVertical);
            if (cutVertical)
            {
                EditorGUI.indentLevel++;
                keepVertical = (Keep)EditorGUILayout.Popup("Оставить", (int)keepVertical, KeepVerticalLabels);
                EditorGUI.indentLevel--;
            }

            cutHorizontal = EditorGUILayout.ToggleLeft("По горизонтали (верх / низ)", cutHorizontal);
            if (cutHorizontal)
            {
                EditorGUI.indentLevel++;
                keepHorizontal = (Keep)EditorGUILayout.Popup("Оставить", (int)keepHorizontal, KeepHorizontalLabels);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.HelpBox(DescribeResult(), MessageType.None);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Обрезка прозрачных пикселей", EditorStyles.boldLabel);
            trimMode = (TrimMode)EditorGUILayout.Popup("Триминг", (int)trimMode, TrimLabels);
            if (trimMode != TrimMode.None)
            {
                alphaThreshold = EditorGUILayout.IntSlider(
                    new GUIContent("Порог альфы", "Пиксель считается прозрачным, если его альфа ≤ этого значения (0–254)"),
                    alphaThreshold, 0, 254);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Сохранение", EditorStyles.boldLabel);
            overwriteOriginal = EditorGUILayout.ToggleLeft("Перезаписать оригинал", overwriteOriginal);
            if (overwriteOriginal)
            {
                EditorGUILayout.HelpBox(
                    "Работает только для PNG и только когда получается одна часть. " +
                    "В остальных случаях будут созданы новые файлы рядом с оригиналом.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("Новые файлы создаются рядом с оригиналом: Имя_L.png, Имя_TR.png и т.д. " +
                                        "Настройки импорта копируются с оригинала.", MessageType.None);
            }

            GUILayout.FlexibleSpace();

            bool nothingToDo = !cutVertical && !cutHorizontal && trimMode != TrimMode.AllEdges;
            using (new EditorGUI.DisabledScope(textures.Count == 0 || nothingToDo))
            {
                if (GUILayout.Button(textures.Count > 1 ? $"Нарезать ({textures.Count})" : "Нарезать", GUILayout.Height(32)))
                    Process();
            }
            if (nothingToDo)
                EditorGUILayout.HelpBox("Выберите хотя бы одно направление разреза или триминг «Со всех сторон».", MessageType.Info);

            EditorGUILayout.Space(4);
        }

        string DescribeResult()
        {
            int parts = (cutVertical ? (keepVertical == Keep.Both ? 2 : 1) : 1)
                      * (cutHorizontal ? (keepHorizontal == Keep.Both ? 2 : 1) : 1);

            string piece;
            if (cutVertical && cutHorizontal) piece = "четвертинк" + (parts == 1 ? "а" : "и");
            else if (cutVertical || cutHorizontal) piece = "половин" + (parts == 1 ? "а" : "ы");
            else return "Без разреза: только обрезка прозрачных краёв.";

            return $"Результат: {parts} {piece} на каждую картинку.";
        }

        // ------------------------------------------------------------------ Обработка

        void Process()
        {
            var jobs = new List<(string src, string dst)>(); // новые файлы, которым нужно скопировать настройки импорта
            var written = new List<string>();
            int skipped = 0;

            try
            {
                for (int i = 0; i < textures.Count; i++)
                {
                    var tex = textures[i];
                    string path = AssetDatabase.GetAssetPath(tex);

                    if (EditorUtility.DisplayCancelableProgressBar("Sprite Splitter", path, (float)i / textures.Count))
                        break;

                    if (!TryLoadPixels(path, tex, out var pixels, out int w, out int h))
                    {
                        Debug.LogWarning($"[P10TR Sprite Splitter] Не удалось прочитать пиксели: {path}", tex);
                        skipped++;
                        continue;
                    }

                    var parts = BuildParts(w, h);
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    bool overwrite = overwriteOriginal && parts.Count == 1 && ext == ".png";
                    string dir = Path.GetDirectoryName(path);
                    string name = Path.GetFileNameWithoutExtension(path);

                    foreach (var part in parts)
                    {
                        if (!TryTrim(pixels, w, part, trimMode, alphaThreshold, out RectInt rect))
                        {
                            Debug.LogWarning($"[P10TR Sprite Splitter] Часть «{part.suffix}» полностью прозрачная, пропущена: {path}", tex);
                            skipped++;
                            continue;
                        }

                        byte[] png = EncodePng(pixels, w, rect);
                        string suffix = string.IsNullOrEmpty(part.suffix) ? "trim" : part.suffix;
                        string dst = overwrite ? path : Path.Combine(dir, $"{name}_{suffix}.png").Replace('\\', '/');

                        bool isNew = !File.Exists(dst);
                        File.WriteAllBytes(dst, png);
                        written.Add(dst);
                        if (isNew) jobs.Add((path, dst));
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();

            foreach (var job in jobs)
                CopyImportSettings(job.src, job.dst);

            // Выделяем результат
            var result = new List<Object>();
            foreach (var p in written)
            {
                var obj = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (obj != null) result.Add(obj);
            }
            if (result.Count > 0)
            {
                Selection.objects = result.ToArray();
                EditorGUIUtility.PingObject(result[0]);
            }

            Debug.Log($"[P10TR Sprite Splitter] Готово: сохранено {written.Count} файл(ов)" +
                      (skipped > 0 ? $", пропущено {skipped}" : "") + ".");
        }

        List<Part> BuildParts(int w, int h)
        {
            // Для нечётных размеров центральный столбец/строка попадает в обе половины —
            // так половинку можно без потерь отзеркалить обратно.
            var xs = new List<(int x0, int x1, string s, bool cl, bool cr)>();
            if (!cutVertical) xs.Add((0, w, "", false, false));
            else
            {
                if (keepVertical != Keep.Second) xs.Add((0, (w + 1) / 2, "L", false, true));
                if (keepVertical != Keep.First) xs.Add((w / 2, w, "R", true, false));
            }

            // Внимание: в Unity Y растёт снизу вверх
            var ys = new List<(int y0, int y1, string s, bool cb, bool ct)>();
            if (!cutHorizontal) ys.Add((0, h, "", false, false));
            else
            {
                if (keepHorizontal != Keep.Second) ys.Add((h / 2, h, "T", true, false));
                if (keepHorizontal != Keep.First) ys.Add((0, (h + 1) / 2, "B", false, true));
            }

            var parts = new List<Part>();
            foreach (var y in ys)
            foreach (var x in xs)
            {
                parts.Add(new Part
                {
                    rect = new RectInt(x.x0, y.y0, x.x1 - x.x0, y.y1 - y.y0),
                    suffix = y.s + x.s,
                    cutLeft = x.cl,
                    cutRight = x.cr,
                    cutBottom = y.cb,
                    cutTop = y.ct
                });
            }
            return parts;
        }

        static bool TryTrim(Color32[] px, int texW, Part part, TrimMode mode, int threshold, out RectInt result)
        {
            var r = part.rect;
            result = r;

            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = r.yMin; y < r.yMax; y++)
            {
                int row = y * texW;
                for (int x = r.xMin; x < r.xMax; x++)
                {
                    if (px[row + x].a <= threshold) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < 0) return mode == TrimMode.None; // часть пустая: без триминга сохраняем как есть
            if (mode == TrimMode.None) return true;

            bool all = mode == TrimMode.AllEdges;
            int x0 = all || part.cutLeft ? minX : r.xMin;
            int x1 = all || part.cutRight ? maxX + 1 : r.xMax;
            int y0 = all || part.cutBottom ? minY : r.yMin;
            int y1 = all || part.cutTop ? maxY + 1 : r.yMax;

            result = new RectInt(x0, y0, x1 - x0, y1 - y0);
            return true;
        }

        static byte[] EncodePng(Color32[] src, int texW, RectInt r)
        {
            var dst = new Color32[r.width * r.height];
            for (int y = 0; y < r.height; y++)
                System.Array.Copy(src, (r.y + y) * texW + r.x, dst, y * r.width, r.width);

            var t = new Texture2D(r.width, r.height, TextureFormat.RGBA32, false);
            t.SetPixels32(dst);
            byte[] bytes = t.EncodeToPNG();
            DestroyImmediate(t);
            return bytes;
        }

        static bool TryLoadPixels(string path, Texture2D asset, out Color32[] pixels, out int w, out int h)
        {
            pixels = null;
            w = h = 0;

            // PNG/JPG читаем прямо с диска: полное разрешение, без сжатия, Read/Write не нужен
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (t.LoadImage(File.ReadAllBytes(path)))
                {
                    w = t.width;
                    h = t.height;
                    pixels = t.GetPixels32();
                    DestroyImmediate(t);
                    return true;
                }
                DestroyImmediate(t);
            }

            // Остальные форматы (PSD, TGA, ...) — через GPU из импортированной текстуры.
            // Разрешение будет как в импорте (ограничено Max Size).
            if (asset == null) return false;
            w = asset.width;
            h = asset.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(asset, rt);
            RenderTexture.active = rt;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            pixels = read.GetPixels32();
            DestroyImmediate(read);
            return true;
        }

        static void CopyImportSettings(string srcPath, string dstPath)
        {
            var src = AssetImporter.GetAtPath(srcPath) as TextureImporter;
            var dst = AssetImporter.GetAtPath(dstPath) as TextureImporter;
            if (src == null || dst == null) return;

            var settings = new TextureImporterSettings();
            src.ReadTextureSettings(settings);
            // Нарезка Multiple у оригинала к новому файлу не подходит
            if (settings.spriteMode == (int)SpriteImportMode.Multiple)
                settings.spriteMode = (int)SpriteImportMode.Single;
            dst.SetTextureSettings(settings);

            dst.textureCompression = src.textureCompression;
            dst.maxTextureSize = src.maxTextureSize;
            dst.crunchedCompression = src.crunchedCompression;
            dst.compressionQuality = src.compressionQuality;

            foreach (var platform in new[] { "Standalone", "Android", "iPhone", "WebGL", "tvOS", "Server" })
            {
                var ps = src.GetPlatformTextureSettings(platform);
                if (ps != null && ps.overridden)
                    dst.SetPlatformTextureSettings(ps);
            }

            dst.SaveAndReimport();
        }
    }
}

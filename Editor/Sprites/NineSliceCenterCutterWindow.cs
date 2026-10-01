// P10TR / 9-Slice Center Cutter
// Removes the stretchable middle of 9-slice style sprites (buttons, panels, frames) and joins the borders,
// so the texture becomes as small as possible. Optionally writes the 9-slice borders into the sprite import settings.
//
// Install: put this file into any folder named "Editor",
// e.g. Assets/Editor/P10TR/NineSliceCenterCutterWindow.cs
//
// Open: right-click in the Project window -> P10TR -> 9-Slice Center Cutter

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace P10TR.EditorTools
{
    public class NineSliceCenterCutterWindow : EditorWindow
    {
        const string MenuRoot = "Assets/P10TR/";
        const string PrefsSettings = "P10TR.NineSliceCutter.Settings";
        const string LogPrefix = "[P10TR 9-Slice Center Cutter] ";

        public enum SingleAxis { Horizontal, Vertical }
        public enum BorderSource { Manual, SpriteBorders, AutoDetect }
        public enum OutputMode { Duplicate, Overwrite }
        public enum PreviewBackground { Checker, Dark, Light }
        public enum ViewMode { Source, Result }

        static readonly string[] SingleAxisLabels = { "Horizontally (keep left + right)", "Vertically (keep top + bottom)" };
        static readonly string[] BorderSourceLabels = { "Manual (same for all)", "Sprite borders (each sprite)", "Auto detect (each sprite)" };
        static readonly string[] OutputLabels = { "Duplicate (new file)", "Overwrite original" };
        static readonly string[] BackgroundLabels = { "Checker", "Dark", "Light" };

        [Serializable]
        public class Settings
        {
            public bool multiAxis = true;
            public SingleAxis singleAxis = SingleAxis.Horizontal;
            public bool cutHorizontal = true;   // remove middle columns (keep left + right)
            public bool cutVertical = true;     // remove middle rows (keep top + bottom)

            public BorderSource borderSource = BorderSource.Manual;
            public int left = 16, right = 16, top = 16, bottom = 16;
            public bool linkLeftRight = true, linkTopBottom = true;
            public int autoTolerance = 2;

            public int keepCenterX = 1, keepCenterY = 1;

            public OutputMode output = OutputMode.Duplicate;
            public string suffix = "_9s";
            public bool setSpriteBorders = true;

            public PreviewBackground background = PreviewBackground.Checker;
        }

        // ------------------------------------------------------------------ Bilingual texts (EN + RU)

        static string Bi(string en, string ru) => en + "\n\n" + ru;
        static GUIContent C(string label, string en, string ru) => new GUIContent(label, Bi(en, ru));

        static readonly GUIContent MultiContent = C("Multi-axis",
            "Off — choose ONE axis to cut. On — cut horizontally and vertically at the same time (only the 4 corners + edges remain).",
            "Выкл — выбирается ОДНА ось разреза. Вкл — резать по горизонтали и вертикали одновременно (остаются 4 угла + края).");
        static readonly GUIContent SingleAxisContent = C("Remove center",
            "Horizontally — middle columns are removed, left and right parts are joined.\nVertically — middle rows are removed, top and bottom parts are joined.",
            "Horizontally — удаляются средние столбцы, левая и правая части склеиваются.\nVertically — удаляются средние строки, верх и низ склеиваются.");
        static readonly GUIContent CutHContent = C("Horizontally (keep left + right)",
            "Remove the middle columns between the left and right borders.",
            "Удалить средние столбцы между левой и правой границами.");
        static readonly GUIContent CutVContent = C("Vertically (keep top + bottom)",
            "Remove the middle rows between the top and bottom borders.",
            "Удалить средние строки между верхней и нижней границами.");

        static readonly GUIContent BorderSourceContent = C("Border source",
            "Manual — the same pixel borders for all sprites (drag the green lines on the preview).\n" +
            "Sprite borders — each sprite's own 9-slice borders from its import settings.\n" +
            "Auto detect — finds the area of identical columns/rows in each sprite.",
            "Manual — одинаковые границы в пикселях для всех спрайтов (можно тянуть зелёные линии на превью).\n" +
            "Sprite borders — собственные 9-slice границы каждого спрайта из настроек импорта.\n" +
            "Auto detect — находит область одинаковых столбцов/строк в каждом спрайте.");
        static readonly GUIContent LeftContent = C("Left (px)", "Left border: this part is always kept.", "Левая граница: эта часть всегда сохраняется.");
        static readonly GUIContent RightContent = C("Right (px)", "Right border: this part is always kept.", "Правая граница: эта часть всегда сохраняется.");
        static readonly GUIContent TopContent = C("Top (px)", "Top border: this part is always kept.", "Верхняя граница: эта часть всегда сохраняется.");
        static readonly GUIContent BottomContent = C("Bottom (px)", "Bottom border: this part is always kept.", "Нижняя граница: эта часть всегда сохраняется.");
        static readonly GUIContent LinkLRContent = C("Link left / right", "Left and right borders change together.", "Левая и правая границы меняются вместе.");
        static readonly GUIContent LinkTBContent = C("Link top / bottom", "Top and bottom borders change together.", "Верхняя и нижняя границы меняются вместе.");
        static readonly GUIContent ToleranceContent = C("Detect tolerance",
            "Auto detect: how much (0–255 per channel) neighbouring columns/rows may differ and still count as identical.",
            "Автоопределение: насколько (0–255 на канал) соседние столбцы/строки могут отличаться и всё ещё считаться одинаковыми.");

        static readonly GUIContent KeepXContent = C("Keep center width (px)",
            "How many pixels of the middle are kept between left and right (taken from the center). 1–2 is enough for stretching; keep more for tiled patterns.",
            "Сколько пикселей середины остаётся между левой и правой частью (берутся из центра). Для растягивания хватает 1–2; для тайлового узора оставьте больше.");
        static readonly GUIContent KeepYContent = C("Keep center height (px)",
            "How many pixels of the middle are kept between top and bottom (taken from the center).",
            "Сколько пикселей середины остаётся между верхом и низом (берутся из центра).");

        static readonly GUIContent OutputModeContent = C("Save mode",
            "Duplicate — a new file next to the original (Name + suffix).\nOverwrite — replaces the original PNG. Cannot be undone!",
            "Duplicate — новый файл рядом с оригиналом (Имя + суффикс).\nOverwrite — заменяет исходный PNG. Отменить нельзя!");
        static readonly GUIContent SuffixContent = C("Suffix", "Added to the file name of the duplicate.", "Добавляется к имени файла дубликата.");
        static readonly GUIContent SetBordersContent = C("Set sprite borders",
            "Write the 9-slice borders into the result's import settings (and set Mesh Type = Full Rect), so it's ready for Draw Mode = Sliced / Image Type = Sliced.",
            "Записать 9-slice границы в настройки импорта результата (и выставить Mesh Type = Full Rect), чтобы он сразу работал с Draw Mode = Sliced / Image Type = Sliced.");

        static readonly GUIContent[] ViewTabs =
        {
            C("Source",
                "Original with borders. Green lines can be dragged. Red = removed area, yellow = kept piece of the middle.",
                "Оригинал с границами. Зелёные линии можно тянуть. Красное — удаляемая область, жёлтое — оставляемый кусочек середины."),
            C("Result",
                "What will be saved. Green lines = 9-slice borders written to the sprite.",
                "Что будет сохранено. Зелёные линии — 9-slice границы, которые запишутся в спрайт.")
        };
        static readonly GUIContent BackgroundContent = C("Background", "Preview background.", "Фон превью.");

        // ------------------------------------------------------------------ State

        [SerializeField] Settings s = new Settings();
        [SerializeField] float previewHeight = 320f;
        [SerializeField] bool showPixelGrid = true;
        [SerializeField] ViewMode view = ViewMode.Source;
        [SerializeField] bool foldCut = true, foldBorders = true, foldOutput = true;

        readonly List<Texture2D> textures = new List<Texture2D>();
        Vector2 mainScroll;

        Texture2D firstTex;
        Color32[] firstPixels;
        int firstW, firstH;
        int sourceVersion;

        Texture2D srcPreviewTex, resultPreviewTex;
        string lastKey;
        Borders previewBorders;
        bool previewBordersOk;
        string previewError;
        CompactResult previewResult;

        float zoom = 1f;
        Vector2 pan;
        int dragLine = -1; // 0 = left, 1 = right, 2 = top, 3 = bottom

        struct Borders { public int l, r, t, b; }

        class CompactResult
        {
            public Color32[] pixels;
            public int w, h;
            public Borders borders;     // borders in the result image
            public bool cutX, cutY;     // which axes actually changed
            public int removeX0, removeX1, removeX2, removeX3; // removed columns: [x0,x1) and [x2,x3)
            public int removeY0, removeY1, removeY2, removeY3; // removed rows (texture space, from bottom)
        }

        // ------------------------------------------------------------------ Menu

        [MenuItem(MenuRoot + "9-Slice Center Cutter", false, 1003)]
        static void Open()
        {
            var window = GetWindow<NineSliceCenterCutterWindow>("9-Slice Cutter");
            window.minSize = new Vector2(380, 560);
            window.Show();
        }

        // ------------------------------------------------------------------ Lifecycle

        void OnEnable()
        {
            wantsMouseMove = true;
            string json = EditorPrefs.GetString(PrefsSettings, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { JsonUtility.FromJsonOverwrite(json, s); } catch { /* keep defaults */ }
            }
            RefreshSelection();
        }

        void OnDisable()
        {
            EditorPrefs.SetString(PrefsSettings, JsonUtility.ToJson(s));
            DestroyTex(ref srcPreviewTex);
            DestroyTex(ref resultPreviewTex);
        }

        static void DestroyTex(ref Texture2D t)
        {
            if (t != null) DestroyImmediate(t);
            t = null;
        }

        void OnSelectionChange()
        {
            RefreshSelection();
            Repaint();
        }

        void OnProjectChange()
        {
            firstTex = null;
            RefreshSelection();
            Repaint();
        }

        void RefreshSelection()
        {
            textures.Clear();
            foreach (var t in Selection.GetFiltered<Texture2D>(SelectionMode.DeepAssets))
                if (!textures.Contains(t)) textures.Add(t);

            var first = textures.Count > 0 ? textures[0] : null;
            if (first == firstTex) return;

            firstTex = first;
            firstPixels = null;
            if (first != null && TryLoadPixels(AssetDatabase.GetAssetPath(first), first, out var px, out int w, out int h))
            {
                firstPixels = px;
                firstW = w;
                firstH = h;

                DestroyTex(ref srcPreviewTex);
                srcPreviewTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                srcPreviewTex.SetPixels32(px);
                srcPreviewTex.Apply(false);
            }
            sourceVersion++;
            zoom = 1f;
            pan = Vector2.zero;
        }

        // ------------------------------------------------------------------ Core

        bool AxisH => s.multiAxis ? s.cutHorizontal : s.singleAxis == SingleAxis.Horizontal;
        bool AxisV => s.multiAxis ? s.cutVertical : s.singleAxis == SingleAxis.Vertical;

        bool ResolveBorders(string path, Color32[] px, int w, int h, out Borders b, out string error)
        {
            error = null;
            b = default;
            switch (s.borderSource)
            {
                case BorderSource.SpriteBorders:
                {
                    var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                    Vector4 sb = imp != null ? imp.spriteBorder : Vector4.zero; // x = left, y = bottom, z = right, w = top
                    if (sb == Vector4.zero)
                    {
                        error = "No 9-slice borders in import settings";
                        return false;
                    }
                    b = new Borders { l = (int)sb.x, b = (int)sb.y, r = (int)sb.z, t = (int)sb.w };
                    break;
                }
                case BorderSource.AutoDetect:
                    if (!AutoDetect(px, w, h, s.autoTolerance, out b))
                    {
                        error = "Could not find identical columns/rows";
                        return false;
                    }
                    break;
                default:
                    b = new Borders { l = s.left, r = s.right, t = s.top, b = s.bottom };
                    break;
            }

            b.l = Mathf.Clamp(b.l, 0, w);
            b.r = Mathf.Clamp(b.r, 0, w - b.l);
            b.b = Mathf.Clamp(b.b, 0, h);
            b.t = Mathf.Clamp(b.t, 0, h - b.b);
            return true;
        }

        static CompactResult Compact(Color32[] px, int w, int h, Borders bd, bool cutH, bool cutV, int keepX, int keepY)
        {
            var res = new CompactResult();
            keepX = Mathf.Max(0, keepX);
            keepY = Mathf.Max(0, keepY);

            // Columns to keep
            var xs = new List<int>(w);
            int cw = w - bd.l - bd.r;
            res.cutX = cutH && cw > keepX;
            if (res.cutX)
            {
                int cs = bd.l + (cw - keepX) / 2;
                for (int x = 0; x < bd.l; x++) xs.Add(x);
                for (int x = cs; x < cs + keepX; x++) xs.Add(x);
                for (int x = w - bd.r; x < w; x++) xs.Add(x);
                res.removeX0 = bd.l; res.removeX1 = cs; res.removeX2 = cs + keepX; res.removeX3 = w - bd.r;
            }
            else for (int x = 0; x < w; x++) xs.Add(x);

            // Rows to keep (texture space: 0 = bottom)
            var ys = new List<int>(h);
            int chh = h - bd.t - bd.b;
            res.cutY = cutV && chh > keepY;
            if (res.cutY)
            {
                int cs = bd.b + (chh - keepY) / 2;
                for (int y = 0; y < bd.b; y++) ys.Add(y);
                for (int y = cs; y < cs + keepY; y++) ys.Add(y);
                for (int y = h - bd.t; y < h; y++) ys.Add(y);
                res.removeY0 = bd.b; res.removeY1 = cs; res.removeY2 = cs + keepY; res.removeY3 = h - bd.t;
            }
            else for (int y = 0; y < h; y++) ys.Add(y);

            res.w = Mathf.Max(1, xs.Count);
            res.h = Mathf.Max(1, ys.Count);
            res.pixels = new Color32[res.w * res.h];
            for (int j = 0; j < ys.Count; j++)
            {
                int srcRow = ys[j] * w, dstRow = j * res.w;
                for (int i = 0; i < xs.Count; i++)
                    res.pixels[dstRow + i] = px[srcRow + xs[i]];
            }
            res.borders = bd;
            return res;
        }

        // Finds the longest run of identical neighbouring columns and rows
        static bool AutoDetect(Color32[] px, int w, int h, int tol, out Borders b)
        {
            b = new Borders();
            bool okX = LongestRun(w, x => ColumnsEqual(px, w, h, x, x + 1, tol), out int ax, out int bx);
            bool okY = LongestRun(h, y => RowsEqual(px, w, y, y + 1, tol), out int ay, out int by);
            if (!okX && !okY) return false;

            // identical columns: ax .. bx+1
            if (okX) { b.l = ax; b.r = w - (bx + 2); }
            else { b.l = w / 2; b.r = w - w / 2; }
            // identical rows (from bottom): ay .. by+1
            if (okY) { b.b = ay; b.t = h - (by + 2); }
            else { b.b = h / 2; b.t = h - h / 2; }
            return true;
        }

        static bool LongestRun(int size, Func<int, bool> equalToNext, out int bestA, out int bestB)
        {
            bestA = bestB = -1;
            int runStart = -1, bestLen = 0;
            for (int i = 0; i < size - 1; i++)
            {
                if (equalToNext(i))
                {
                    if (runStart < 0) runStart = i;
                    int len = i - runStart + 1;
                    if (len > bestLen) { bestLen = len; bestA = runStart; bestB = i; }
                }
                else runStart = -1;
            }
            return bestLen > 0;
        }

        static bool Same(Color32 a, Color32 b, int tol)
        {
            return Mathf.Abs(a.r - b.r) <= tol && Mathf.Abs(a.g - b.g) <= tol &&
                   Mathf.Abs(a.b - b.b) <= tol && Mathf.Abs(a.a - b.a) <= tol;
        }

        static bool ColumnsEqual(Color32[] px, int w, int h, int x0, int x1, int tol)
        {
            for (int y = 0; y < h; y++)
                if (!Same(px[y * w + x0], px[y * w + x1], tol)) return false;
            return true;
        }

        static bool RowsEqual(Color32[] px, int w, int y0, int y1, int tol)
        {
            int r0 = y0 * w, r1 = y1 * w;
            for (int x = 0; x < w; x++)
                if (!Same(px[r0 + x], px[r1 + x], tol)) return false;
            return true;
        }

        void UpdatePreview()
        {
            if (firstPixels == null) { previewResult = null; return; }

            string key = JsonUtility.ToJson(s) + "|" + sourceVersion;
            if (key == lastKey && previewResult != null) return;
            lastKey = key;

            previewBordersOk = ResolveBorders(AssetDatabase.GetAssetPath(firstTex), firstPixels, firstW, firstH,
                                              out previewBorders, out previewError);
            if (!previewBordersOk) { previewResult = null; return; }

            previewResult = Compact(firstPixels, firstW, firstH, previewBorders, AxisH, AxisV, s.keepCenterX, s.keepCenterY);

            if (resultPreviewTex == null || resultPreviewTex.width != previewResult.w || resultPreviewTex.height != previewResult.h)
            {
                DestroyTex(ref resultPreviewTex);
                resultPreviewTex = new Texture2D(previewResult.w, previewResult.h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            }
            resultPreviewTex.SetPixels32(previewResult.pixels);
            resultPreviewTex.Apply(false);
        }

        // ------------------------------------------------------------------ GUI

        void OnGUI()
        {
            UpdatePreview();
            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);

            DrawSelection();
            DrawPreviewSection();
            DrawCutSection();
            DrawBordersSection();
            DrawOutputSection();
            DrawProcessButton();

            EditorGUILayout.EndScrollView();
        }

        void DrawSelection()
        {
            EditorGUILayout.Space(4);
            string label = textures.Count == 0 ? "Selected textures: 0"
                : textures.Count == 1 ? $"Selected: {textures[0].name}"
                : $"Selected textures: {textures.Count}  (preview: {textures[0].name})";
            EditorGUILayout.LabelField(C(label,
                "Textures selected in the Project window (folders include everything inside). The first one is shown in the preview.",
                "Текстуры, выделенные в окне Project (папки — со всем содержимым). Первая показывается в превью."),
                EditorStyles.boldLabel);
            if (textures.Count == 0)
                EditorGUILayout.HelpBox(Bi("Select sprites (or a folder with them) in the Project window.",
                                           "Выделите спрайты (или папку с ними) в окне Project."), MessageType.Info);
        }

        static bool Section(ref bool fold, GUIContent title)
        {
            EditorGUILayout.Space(6);
            fold = EditorGUILayout.Foldout(fold, title, true, EditorStyles.foldoutHeader);
            return fold;
        }

        void DrawCutSection()
        {
            if (!Section(ref foldCut, C("Cut", "Which middle part to remove.", "Какую середину удалять."))) return;
            EditorGUI.indentLevel++;
            s.multiAxis = EditorGUILayout.Toggle(MultiContent, s.multiAxis);
            if (s.multiAxis)
            {
                s.cutHorizontal = EditorGUILayout.ToggleLeft(CutHContent, s.cutHorizontal);
                s.cutVertical = EditorGUILayout.ToggleLeft(CutVContent, s.cutVertical);
            }
            else
            {
                s.singleAxis = (SingleAxis)EditorGUILayout.Popup(SingleAxisContent, (int)s.singleAxis, SingleAxisLabels);
            }

            using (new EditorGUI.DisabledScope(!AxisH))
                s.keepCenterX = EditorGUILayout.IntSlider(KeepXContent, s.keepCenterX, 0, 256);
            using (new EditorGUI.DisabledScope(!AxisV))
                s.keepCenterY = EditorGUILayout.IntSlider(KeepYContent, s.keepCenterY, 0, 256);
            EditorGUI.indentLevel--;
        }

        void DrawBordersSection()
        {
            if (!Section(ref foldBorders, C("Borders", "The parts that are always kept (corners and edges).", "Части, которые всегда сохраняются (углы и края)."))) return;
            EditorGUI.indentLevel++;
            s.borderSource = (BorderSource)EditorGUILayout.Popup(BorderSourceContent, (int)s.borderSource, BorderSourceLabels);

            int maxW = firstPixels != null ? firstW : 1024;
            int maxH = firstPixels != null ? firstH : 1024;

            if (s.borderSource == BorderSource.Manual)
            {
                using (new EditorGUI.DisabledScope(!AxisH))
                {
                    int l = EditorGUILayout.IntSlider(LeftContent, s.left, 0, maxW);
                    int r = EditorGUILayout.IntSlider(RightContent, s.right, 0, maxW);
                    if (s.linkLeftRight) { if (l != s.left) r = l; else if (r != s.right) l = r; }
                    s.left = l; s.right = r;
                    s.linkLeftRight = EditorGUILayout.Toggle(LinkLRContent, s.linkLeftRight);
                }
                using (new EditorGUI.DisabledScope(!AxisV))
                {
                    int t = EditorGUILayout.IntSlider(TopContent, s.top, 0, maxH);
                    int b = EditorGUILayout.IntSlider(BottomContent, s.bottom, 0, maxH);
                    if (s.linkTopBottom) { if (t != s.top) b = t; else if (b != s.bottom) t = b; }
                    s.top = t; s.bottom = b;
                    s.linkTopBottom = EditorGUILayout.Toggle(LinkTBContent, s.linkTopBottom);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15f);
                    using (new EditorGUI.DisabledScope(firstPixels == null))
                    {
                        if (GUILayout.Button(C("From sprite borders",
                                "Copy the 9-slice borders from the first sprite's import settings.",
                                "Взять 9-slice границы из настроек импорта первого спрайта."), EditorStyles.miniButtonLeft))
                            CopyBordersFromFirst();
                        if (GUILayout.Button(C("Auto detect",
                                "Find borders on the first sprite by looking for identical columns/rows.",
                                "Найти границы на первом спрайте по одинаковым столбцам/строкам."), EditorStyles.miniButtonRight))
                            AutoDetectFirst();
                    }
                }
            }
            else if (s.borderSource == BorderSource.SpriteBorders)
            {
                EditorGUILayout.HelpBox(Bi(
                    "Each sprite uses its own borders from the Sprite Editor. Sprites without borders are skipped.",
                    "Каждый спрайт использует свои границы из Sprite Editor. Спрайты без границ пропускаются."), MessageType.None);
            }

            if (s.borderSource == BorderSource.AutoDetect || s.borderSource == BorderSource.Manual)
                s.autoTolerance = EditorGUILayout.IntSlider(ToleranceContent, s.autoTolerance, 0, 64);

            if (firstPixels != null && !previewBordersOk && previewError != null)
                EditorGUILayout.HelpBox($"{firstTex.name}: {previewError}", MessageType.Warning);

            EditorGUI.indentLevel--;
        }

        void CopyBordersFromFirst()
        {
            var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(firstTex)) as TextureImporter;
            Vector4 sb = imp != null ? imp.spriteBorder : Vector4.zero;
            if (sb == Vector4.zero)
            {
                EditorUtility.DisplayDialog("9-Slice Center Cutter",
                    "The first sprite has no 9-slice borders in its import settings.\n\nУ первого спрайта нет 9-slice границ в настройках импорта.", "OK");
                return;
            }
            s.left = (int)sb.x; s.bottom = (int)sb.y; s.right = (int)sb.z; s.top = (int)sb.w;
            s.linkLeftRight = s.left == s.right;
            s.linkTopBottom = s.top == s.bottom;
            GUI.FocusControl(null);
        }

        void AutoDetectFirst()
        {
            if (!AutoDetect(firstPixels, firstW, firstH, s.autoTolerance, out var b))
            {
                EditorUtility.DisplayDialog("9-Slice Center Cutter",
                    "No identical columns/rows found. Try a higher tolerance or set borders manually.\n\n" +
                    "Одинаковые столбцы/строки не найдены. Увеличьте допуск или задайте границы вручную.", "OK");
                return;
            }
            s.left = b.l; s.right = b.r; s.top = b.t; s.bottom = b.b;
            s.linkLeftRight = s.left == s.right;
            s.linkTopBottom = s.top == s.bottom;
            GUI.FocusControl(null);
        }

        void DrawOutputSection()
        {
            if (!Section(ref foldOutput, C("Output", "How and where the result is saved.", "Как и куда сохраняется результат."))) return;
            EditorGUI.indentLevel++;
            s.output = (OutputMode)EditorGUILayout.Popup(OutputModeContent, (int)s.output, OutputLabels);
            if (s.output == OutputMode.Duplicate)
                s.suffix = EditorGUILayout.TextField(SuffixContent, s.suffix);
            s.setSpriteBorders = EditorGUILayout.Toggle(SetBordersContent, s.setSpriteBorders);

            if (s.output == OutputMode.Overwrite)
                EditorGUILayout.HelpBox(Bi(
                    "Overwrites original PNG files (non-PNG files are saved as duplicates). Cannot be undone.",
                    "Перезаписывает исходные PNG (не-PNG сохраняются дубликатами). Отменить нельзя."), MessageType.Warning);
            EditorGUI.indentLevel--;
        }

        void DrawProcessButton()
        {
            EditorGUILayout.Space(10);
            bool noAxis = !AxisH && !AxisV;
            using (new EditorGUI.DisabledScope(textures.Count == 0 || noAxis))
            {
                var label = new GUIContent(textures.Count > 1 ? $"Cut ({textures.Count})" : "Cut",
                    Bi("Remove the middle from all selected sprites.", "Удалить середину у всех выбранных спрайтов."));
                if (GUILayout.Button(label, GUILayout.Height(32)))
                    Process();
            }
            if (noAxis)
                EditorGUILayout.HelpBox(Bi("Enable at least one axis.", "Включите хотя бы одну ось."), MessageType.Info);
            EditorGUILayout.Space(4);
        }

        // ------------------------------------------------------------------ Preview

        void DrawPreviewSection()
        {
            if (firstPixels == null || srcPreviewTex == null) return;

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(C("Preview", "Preview of the first selected sprite.", "Превью первого выбранного спрайта."),
                    EditorStyles.boldLabel, GUILayout.Width(70));
                var newView = (ViewMode)GUILayout.Toolbar((int)view, ViewTabs, EditorStyles.miniButton, GUILayout.Width(130));
                if (newView != view) { view = newView; zoom = 1f; pan = Vector2.zero; }
                GUILayout.FlexibleSpace();
                s.background = (PreviewBackground)EditorGUILayout.Popup((int)s.background, BackgroundLabels, GUILayout.Width(80));
            }

            bool showResult = view == ViewMode.Result && previewResult != null;
            Texture2D tex = showResult ? resultPreviewTex : srcPreviewTex;
            int tw = showResult ? previewResult.w : firstW;
            int th = showResult ? previewResult.h : firstH;

            float viewW = Mathf.Max(60f, EditorGUIUtility.currentViewWidth - 30f);
            Rect area = GUILayoutUtility.GetRect(viewW, previewHeight, GUILayout.ExpandWidth(true));
            var vr = new Rect(0f, 0f, area.width, area.height);

            float fit = Mathf.Min(vr.width / tw, vr.height / th);
            float scale = fit * zoom;
            var size = new Vector2(tw * scale, th * scale);
            ClampPan(size, vr);
            var r = new Rect(vr.center - size * 0.5f + pan, size);

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(area, new Color(0.16f, 0.16f, 0.16f));

            GUI.BeginClip(area);
            if (Event.current.type == EventType.Repaint)
            {
                tex.filterMode = scale >= 1f ? FilterMode.Point : FilterMode.Bilinear;
                switch (s.background)
                {
                    case PreviewBackground.Dark: EditorGUI.DrawRect(r, new Color(0.1f, 0.1f, 0.1f)); GUI.DrawTexture(r, tex); break;
                    case PreviewBackground.Light: EditorGUI.DrawRect(r, new Color(0.92f, 0.92f, 0.92f)); GUI.DrawTexture(r, tex); break;
                    default: EditorGUI.DrawTextureTransparent(r, tex, ScaleMode.StretchToFill); break;
                }

                if (showPixelGrid && scale >= 8f) DrawPixelGrid(r, vr, scale, tw, th);

                if (!showResult && previewBordersOk) DrawSourceOverlay(r, scale);
                if (showResult) DrawResultOverlay(r, scale);
            }

            if (!showResult && previewBordersOk) HandleLineDrag(r, vr, scale);
            HandleZoomPan(vr, fit);
            GUI.EndClip();

            // Zoom toolbar
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent($"Zoom {Mathf.RoundToInt(scale * 100f)}%", Bi(
                    "Mouse wheel — zoom to cursor. Drag empty space, middle mouse or Alt+drag — pan.",
                    "Колёсико — зум к курсору. Перетаскивание по пустому месту, средняя кнопка или Alt+перетаскивание — двигать.")),
                    EditorStyles.miniLabel, GUILayout.Width(80));
                if (GUILayout.Button(C("−", "Zoom out.", "Отдалить."), EditorStyles.miniButtonLeft, GUILayout.Width(24)))
                    SetZoom(zoom / 1.5f, vr.center, vr, fit);
                if (GUILayout.Button(C("+", "Zoom in.", "Приблизить."), EditorStyles.miniButtonMid, GUILayout.Width(24)))
                    SetZoom(zoom * 1.5f, vr.center, vr, fit);
                if (GUILayout.Button(C("Fit", "Fit the whole image into the view.", "Вписать всю картинку в окно."), EditorStyles.miniButtonMid, GUILayout.Width(36)))
                { zoom = 1f; pan = Vector2.zero; }
                if (GUILayout.Button(C("1:1", "One image pixel = one screen pixel.", "Один пиксель картинки = один пиксель экрана."), EditorStyles.miniButtonRight, GUILayout.Width(36)))
                { zoom = Mathf.Clamp(1f / fit, MinZoom, MaxZoom(fit)); pan = Vector2.zero; }
                GUILayout.Space(8);
                showPixelGrid = GUILayout.Toggle(showPixelGrid, C("Grid",
                    "Show the pixel grid when zoomed in (800% and more).",
                    "Показывать пиксельную сетку при сильном приближении (от 800%)."), EditorStyles.miniButton, GUILayout.Width(40));
                GUILayout.FlexibleSpace();
                GUILayout.Label(C("Height", "Height of the preview area.", "Высота области превью."), EditorStyles.miniLabel, GUILayout.Width(40));
                previewHeight = GUILayout.HorizontalSlider(previewHeight, 150f, 900f, GUILayout.Width(70));
            }

            string sizeInfo = previewResult != null ? $"{firstW}×{firstH} → {previewResult.w}×{previewResult.h}" : $"{firstW}×{firstH}";
            string hint = view == ViewMode.Source && s.borderSource == BorderSource.Manual ? "   |   Drag green lines — borders" : "";
            EditorGUILayout.LabelField(sizeInfo + hint, EditorStyles.centeredGreyMiniLabel);
        }

        static readonly Color RemoveColor = new Color(1f, 0.1f, 0.1f, 0.4f);
        static readonly Color KeepCenterColor = new Color(1f, 0.85f, 0.1f, 0.35f);
        static readonly Color LineOn = new Color(0.2f, 1f, 0.35f, 1f);
        static readonly Color LineOff = new Color(1f, 1f, 1f, 0.3f);

        void DrawSourceOverlay(Rect r, float scale)
        {
            var b = previewBorders;
            var res = previewResult;

            // Removed areas (red) and kept center (yellow)
            if (res != null && res.cutX)
            {
                EditorGUI.DrawRect(ColRect(r, scale, res.removeX0, res.removeX1), RemoveColor);
                EditorGUI.DrawRect(ColRect(r, scale, res.removeX2, res.removeX3), RemoveColor);
                if (res.removeX2 > res.removeX1) EditorGUI.DrawRect(ColRect(r, scale, res.removeX1, res.removeX2), KeepCenterColor);
            }
            if (res != null && res.cutY)
            {
                EditorGUI.DrawRect(RowRect(r, scale, res.removeY0, res.removeY1), RemoveColor);
                EditorGUI.DrawRect(RowRect(r, scale, res.removeY2, res.removeY3), RemoveColor);
                if (res.removeY2 > res.removeY1) EditorGUI.DrawRect(RowRect(r, scale, res.removeY1, res.removeY2), KeepCenterColor);
            }

            // Border lines
            Color ch = AxisH ? LineOn : LineOff, cv = AxisV ? LineOn : LineOff;
            EditorGUI.DrawRect(new Rect(r.x + b.l * scale - 1f, r.y, 2f, r.height), ch);
            EditorGUI.DrawRect(new Rect(r.x + (firstW - b.r) * scale - 1f, r.y, 2f, r.height), ch);
            EditorGUI.DrawRect(new Rect(r.x, r.y + b.t * scale - 1f, r.width, 2f), cv);
            EditorGUI.DrawRect(new Rect(r.x, r.y + (firstH - b.b) * scale - 1f, r.width, 2f), cv);
        }

        void DrawResultOverlay(Rect r, float scale)
        {
            if (!s.setSpriteBorders || previewResult == null) return;
            var b = previewResult.borders;
            int w = previewResult.w, h = previewResult.h;
            var c = new Color(0.2f, 1f, 0.35f, 0.7f);
            EditorGUI.DrawRect(new Rect(r.x + b.l * scale, r.y, 1f, r.height), c);
            EditorGUI.DrawRect(new Rect(r.x + (w - b.r) * scale - 1f, r.y, 1f, r.height), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y + b.t * scale, r.width, 1f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y + (h - b.b) * scale - 1f, r.width, 1f), c);
        }

        // Columns [x0, x1) -> GUI rect
        static Rect ColRect(Rect r, float scale, int x0, int x1) =>
            new Rect(r.x + x0 * scale, r.y, Mathf.Max(0, x1 - x0) * scale, r.height);

        // Texture rows [y0, y1) (from bottom) -> GUI rect
        Rect RowRect(Rect r, float scale, int y0, int y1) =>
            new Rect(r.x, r.y + (firstH - y1) * scale, r.width, Mathf.Max(0, y1 - y0) * scale);

        void HandleLineDrag(Rect r, Rect vr, float scale)
        {
            var e = Event.current;
            var b = previewBorders;
            float xl = r.x + b.l * scale, xr = r.x + (firstW - b.r) * scale;
            float yt = r.y + b.t * scale, yb = r.y + (firstH - b.b) * scale;
            const float grab = 6f;

            // Cursors
            if (AxisH)
            {
                EditorGUIUtility.AddCursorRect(new Rect(xl - grab, r.y, grab * 2f, r.height), MouseCursor.ResizeHorizontal);
                EditorGUIUtility.AddCursorRect(new Rect(xr - grab, r.y, grab * 2f, r.height), MouseCursor.ResizeHorizontal);
            }
            if (AxisV)
            {
                EditorGUIUtility.AddCursorRect(new Rect(r.x, yt - grab, r.width, grab * 2f), MouseCursor.ResizeVertical);
                EditorGUIUtility.AddCursorRect(new Rect(r.x, yb - grab, r.width, grab * 2f), MouseCursor.ResizeVertical);
            }

            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                {
                    if (e.button != 0 || e.alt || !vr.Contains(e.mousePosition)) break;
                    var m = e.mousePosition;
                    int line = -1;
                    float best = grab;
                    bool insideY = m.y >= r.y && m.y <= r.yMax, insideX = m.x >= r.x && m.x <= r.xMax;
                    if (AxisH && insideY)
                    {
                        if (Mathf.Abs(m.x - xl) <= best) { best = Mathf.Abs(m.x - xl); line = 0; }
                        if (Mathf.Abs(m.x - xr) <= best) { best = Mathf.Abs(m.x - xr); line = 1; }
                    }
                    if (AxisV && insideX)
                    {
                        if (Mathf.Abs(m.y - yt) <= best) { best = Mathf.Abs(m.y - yt); line = 2; }
                        if (Mathf.Abs(m.y - yb) <= best) { line = 3; }
                    }
                    if (line < 0) break;

                    // Dragging a line switches to manual borders, starting from the current ones
                    if (s.borderSource != BorderSource.Manual)
                    {
                        s.borderSource = BorderSource.Manual;
                        s.left = b.l; s.right = b.r; s.top = b.t; s.bottom = b.b;
                    }
                    dragLine = line;
                    GUIUtility.hotControl = id;
                    GUI.FocusControl(null);
                    e.Use();
                    break;
                }
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id || dragLine < 0) break;
                    {
                        int px = Mathf.RoundToInt((e.mousePosition.x - r.x) / scale);
                        int py = Mathf.RoundToInt((e.mousePosition.y - r.y) / scale);
                        switch (dragLine)
                        {
                            case 0: s.left = Mathf.Clamp(px, 0, firstW); if (s.linkLeftRight) s.right = s.left; break;
                            case 1: s.right = Mathf.Clamp(firstW - px, 0, firstW); if (s.linkLeftRight) s.left = s.right; break;
                            case 2: s.top = Mathf.Clamp(py, 0, firstH); if (s.linkTopBottom) s.bottom = s.top; break;
                            case 3: s.bottom = Mathf.Clamp(firstH - py, 0, firstH); if (s.linkTopBottom) s.top = s.bottom; break;
                        }
                        e.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        dragLine = -1;
                        e.Use();
                    }
                    break;
            }
        }

        const float MinZoom = 0.25f;
        static float MaxZoom(float fit) => Mathf.Max(1f, 64f / Mathf.Max(0.0001f, fit));

        void ClampPan(Vector2 size, Rect vr)
        {
            float mx = Mathf.Max(0f, size.x * 0.5f + vr.width * 0.5f - 24f);
            float my = Mathf.Max(0f, size.y * 0.5f + vr.height * 0.5f - 24f);
            pan = new Vector2(Mathf.Clamp(pan.x, -mx, mx), Mathf.Clamp(pan.y, -my, my));
        }

        void SetZoom(float newZoom, Vector2 pivot, Rect vr, float fit)
        {
            newZoom = Mathf.Clamp(newZoom, MinZoom, MaxZoom(fit));
            float ratio = newZoom / zoom;
            Vector2 d = pivot - (vr.center + pan);
            pan += d - d * ratio;
            zoom = newZoom;
            Repaint();
        }

        void HandleZoomPan(Rect vr, float fit)
        {
            var e = Event.current;
            if (e.type == EventType.ScrollWheel && vr.Contains(e.mousePosition))
            {
                SetZoom(zoom * (e.delta.y > 0f ? 1f / 1.2f : 1.2f), e.mousePosition, vr, fit);
                e.Use();
                return;
            }

            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (vr.Contains(e.mousePosition) && (e.button == 0 || e.button == 2))
                    {
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        pan += e.delta;
                        e.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        static void DrawPixelGrid(Rect r, Rect vr, float scale, int w, int h)
        {
            var c = new Color(0f, 0f, 0f, 0.25f);
            int x0 = Mathf.Max(0, Mathf.FloorToInt((vr.xMin - r.x) / scale));
            int x1 = Mathf.Min(w, Mathf.CeilToInt((vr.xMax - r.x) / scale));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((vr.yMin - r.y) / scale));
            int y1 = Mathf.Min(h, Mathf.CeilToInt((vr.yMax - r.y) / scale));
            float top = Mathf.Max(r.y, vr.yMin), bottom = Mathf.Min(r.yMax, vr.yMax);
            float left = Mathf.Max(r.x, vr.xMin), right = Mathf.Min(r.xMax, vr.xMax);
            for (int x = x0; x <= x1; x++) EditorGUI.DrawRect(new Rect(r.x + x * scale, top, 1f, bottom - top), c);
            for (int y = y0; y <= y1; y++) EditorGUI.DrawRect(new Rect(left, r.y + y * scale, right - left, 1f), c);
        }

        // ------------------------------------------------------------------ Processing

        struct Job
        {
            public string src, dst;
            public bool isNew;
            public CompactResult res;
        }

        void Process()
        {
            var jobs = new List<Job>();
            int skipped = 0, unchanged = 0;
            string suffix = string.IsNullOrWhiteSpace(s.suffix) ? "_9s" : s.suffix.Trim();

            try
            {
                for (int i = 0; i < textures.Count; i++)
                {
                    var tex = textures[i];
                    string path = AssetDatabase.GetAssetPath(tex);
                    if (EditorUtility.DisplayCancelableProgressBar("9-Slice Center Cutter", path, (float)i / textures.Count))
                        break;

                    if (!TryLoadPixels(path, tex, out var px, out int w, out int h))
                    {
                        Debug.LogWarning(LogPrefix + $"Could not read pixels: {path}", tex);
                        skipped++;
                        continue;
                    }
                    if (!ResolveBorders(path, px, w, h, out var b, out string error))
                    {
                        Debug.LogWarning(LogPrefix + $"{error}, skipped: {path}", tex);
                        skipped++;
                        continue;
                    }

                    var res = Compact(px, w, h, b, AxisH, AxisV, s.keepCenterX, s.keepCenterY);
                    if (!res.cutX && !res.cutY)
                    {
                        Debug.Log(LogPrefix + $"Nothing to remove (the middle is already small), skipped: {path}", tex);
                        unchanged++;
                        continue;
                    }

                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    bool overwrite = s.output == OutputMode.Overwrite && ext == ".png";
                    if (s.output == OutputMode.Overwrite && !overwrite)
                        Debug.LogWarning(LogPrefix + $"Only PNG can be overwritten, saved as a duplicate instead: {path}", tex);

                    string dst = overwrite
                        ? path
                        : Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + suffix + ".png").Replace('\\', '/');

                    bool isNew = !File.Exists(dst);
                    File.WriteAllBytes(dst, EncodePng(res));
                    jobs.Add(new Job { src = path, dst = dst, isNew = isNew, res = res });
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();

            foreach (var job in jobs)
            {
                if (job.dst != job.src && job.isNew)
                    CopyImportSettings(job.src, job.dst);
                if (s.setSpriteBorders)
                    ApplySpriteBorders(job.dst, job.res.borders);
            }

            var selection = new List<Object>();
            foreach (var job in jobs)
            {
                var obj = AssetDatabase.LoadAssetAtPath<Texture2D>(job.dst);
                if (obj != null && !selection.Contains(obj)) selection.Add(obj);
            }
            if (selection.Count > 0 && s.output == OutputMode.Duplicate)
            {
                Selection.objects = selection.ToArray();
                EditorGUIUtility.PingObject(selection[0]);
            }

            Debug.Log(LogPrefix + $"Done: saved {jobs.Count} file(s)" +
                      (unchanged > 0 ? $", unchanged {unchanged}" : "") +
                      (skipped > 0 ? $", skipped {skipped}" : "") + ".");
        }

        static byte[] EncodePng(CompactResult r)
        {
            var t = new Texture2D(r.w, r.h, TextureFormat.RGBA32, false);
            t.SetPixels32(r.pixels);
            byte[] bytes = t.EncodeToPNG();
            DestroyImmediate(t);
            return bytes;
        }

        static void ApplySpriteBorders(string path, Borders b)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            if (imp.textureType != TextureImporterType.Sprite)
            {
                Debug.LogWarning(LogPrefix + $"Not a Sprite, borders not set: {path}");
                return;
            }
            if (imp.spriteImportMode == SpriteImportMode.Multiple)
            {
                Debug.LogWarning(LogPrefix + $"Sprite Mode is Multiple, borders not set: {path}");
                return;
            }

            var st = new TextureImporterSettings();
            imp.ReadTextureSettings(st);
            st.spriteBorder = new Vector4(b.l, b.b, b.r, b.t); // x = left, y = bottom, z = right, w = top
            st.spriteMeshType = SpriteMeshType.FullRect;       // Sliced/Tiled draw modes need Full Rect
            imp.SetTextureSettings(st);
            imp.SaveAndReimport();
        }

        static void CopyImportSettings(string srcPath, string dstPath)
        {
            var src = AssetImporter.GetAtPath(srcPath) as TextureImporter;
            var dst = AssetImporter.GetAtPath(dstPath) as TextureImporter;
            if (src == null || dst == null) return;

            var settings = new TextureImporterSettings();
            src.ReadTextureSettings(settings);
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

        static bool TryLoadPixels(string path, Texture2D asset, out Color32[] pixels, out int w, out int h)
        {
            pixels = null;
            w = h = 0;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg") && File.Exists(path))
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
    }
}

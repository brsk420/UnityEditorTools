// P10TR / Sprite Outline & Shadow Baker
// Bakes an outline and/or a drop shadow into sprite PNGs, with masks that block the effects
// in chosen areas (brush, shape, sides, edge direction, color, external texture).
// Live preview on the first selected sprite. Batch processing: masks are defined on the first frame.
//
// Install: put this file into any folder named "Editor",
// e.g. Assets/Editor/P10TR/SpriteOutlineShadowBakerWindow.cs
//
// Open: right-click in the Project window -> P10TR -> Sprite Outline & Shadow Baker

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace P10TR.EditorTools
{
    public class SpriteOutlineShadowBakerWindow : EditorWindow
    {
        const string MenuRoot = "Assets/P10TR/";
        const string PrefsSettings = "P10TR.OutlineShadowBaker.Settings";
        const string PrefsMaskTex = "P10TR.OutlineShadowBaker.MaskTexture";
        const string LogPrefix = "[P10TR Outline & Shadow Baker] ";
        const float BrushMargin = 0.25f; // brush mask also covers 25% around the image (for outline/shadow areas)

        // ------------------------------------------------------------------ Enums

        public enum OutlinePosition { Outside, Inside, Center }
        public enum OutlineShape { Round, Square, Diamond }
        public enum ColorMode { Solid, GradientAcross, GradientVertical, GradientHorizontal, GradientAngle }
        public enum MaskType { None, Brush, Shape, Sides, EdgeDirection, ColorPick, TextureFile }
        public enum MaskSpace { SourceEdges, Result }
        public enum ShapeKind { Rectangle, Ellipse }
        public enum TexChannel { Luminance, Red, Green, Blue, Alpha }
        public enum OutputMode { Duplicate, Overwrite }
        public enum PreviewBackground { Checker, Dark, Light, Custom }

        static readonly string[] PositionLabels = { "Outside", "Inside", "Center" };
        static readonly string[] ShapeLabels = { "Round", "Square", "Diamond" };
        static readonly string[] ColorModeLabels = { "Solid color", "Gradient: across width", "Gradient: vertical", "Gradient: horizontal", "Gradient: angle" };
        static readonly string[] MaskTypeLabels = { "None", "Brush", "Shape", "Sides", "Edge direction", "Color", "Texture" };
        static readonly string[] MaskSpaceLabels = { "Source edges (soft)", "Result (hard cut)" };
        static readonly string[] ShapeKindLabels = { "Rectangle", "Ellipse" };
        static readonly string[] ChannelLabels = { "Luminance", "Red", "Green", "Blue", "Alpha" };
        static readonly string[] OutputLabels = { "Duplicate (new file)", "Overwrite original" };
        static readonly string[] BackgroundLabels = { "Checker", "Dark", "Light", "Custom" };

        // ------------------------------------------------------------------ Settings

        [Serializable]
        public class Settings
        {
            // Outline
            public bool outlineEnabled = true;
            public OutlinePosition outlinePosition = OutlinePosition.Outside;
            public float outlineThickness = 2f;
            public float outlineSoftness = 0f;
            public OutlineShape outlineShape = OutlineShape.Round;
            public bool antiAliasing = true;
            public ColorMode colorMode = ColorMode.Solid;
            public Color outlineColor = Color.black;
            public Gradient outlineGradient = CreateDefaultGradient();
            public float gradientAngle = 90f;
            public float outlineOpacity = 1f;

            // Shadow
            public bool shadowEnabled;
            public Vector2Int shadowOffset = new Vector2Int(3, 3); // +X right, +Y down
            public float shadowSpread;
            public float shadowBlur = 3f;
            public Color shadowColor = new Color(0f, 0f, 0f, 0.5f);
            public bool shadowIncludesOutline = true;

            // Source
            public int alphaThreshold;

            // Mask (common)
            public MaskType maskType = MaskType.None;
            public MaskSpace maskSpace = MaskSpace.SourceEdges;
            public bool maskAffectsOutline = true;
            public bool maskAffectsShadow = true;
            public bool maskInvert;

            // Mask: brush
            public float brushSize = 16f;
            public float brushHardness = 0.5f;
            public float brushStrength = 1f;

            // Mask: shape (normalized, Y from top)
            public ShapeKind shapeKind = ShapeKind.Rectangle;
            public Vector2 shapeCenter = new Vector2(0.5f, 0.9f);
            public Vector2 shapeSize = new Vector2(0.6f, 0.2f);
            public float shapeRotation;
            public float shapeFeather = 4f;

            // Mask: sides (normalized)
            public float sideTop, sideBottom = 0.15f, sideLeft, sideRight;
            public float sideFeather = 0.05f;

            // Mask: edge direction (degrees; 0 = right, 90 = up, 180 = left, 270 = down)
            public float dirAngle = 270f;
            public float dirSpread = 45f;
            public float dirFeather = 20f;

            // Mask: color
            public Color maskColor = Color.white;
            public float colorTolerance = 0.1f;
            public float colorFeather = 0.05f;

            // Mask: texture
            public TexChannel maskChannel = TexChannel.Luminance;

            // Output
            public OutputMode output = OutputMode.Duplicate;
            public string suffix = "_fx";
            public bool expandCanvas = true;
            public bool keepPivot = true;

            // Preview
            public PreviewBackground background = PreviewBackground.Checker;
            public Color customBackground = new Color(0.3f, 0.5f, 0.3f, 1f);
            public bool showMaskOverlay = true;
        }

        static Gradient CreateDefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.85f, 0.2f), 0f), new GradientColorKey(new Color(0.9f, 0.1f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        // ------------------------------------------------------------------ Bilingual texts (EN + RU)

        static string Bi(string en, string ru) => en + "\n\n" + ru;
        static GUIContent C(string label, string en, string ru) => new GUIContent(label, Bi(en, ru));

        static readonly GUIContent OutlineHeader = C("Outline",
            "Stroke around the opaque part of the sprite.",
            "Обводка вокруг непрозрачной части спрайта.");
        static readonly GUIContent ShadowHeader = C("Shadow",
            "Drop shadow under the sprite.",
            "Падающая тень под спрайтом.");
        static readonly GUIContent MaskHeader = C("Mask",
            "Areas where outline and/or shadow are NOT created. Only one mask type is active at a time.",
            "Области, где аутлайн и/или тень НЕ создаются. Активен только один тип маски.");
        static readonly GUIContent OutputHeader = C("Output",
            "How and where the result is saved.",
            "Как и куда сохраняется результат.");
        static readonly GUIContent PreviewHeader = C("Preview",
            "Result for the first selected sprite. Magenta = mask. Faint frame = original image bounds.",
            "Результат для первого выбранного спрайта. Пурпурным — маска. Бледная рамка — границы исходной картинки.");

        static readonly GUIContent EnabledContent = C("Enabled", "Turn this effect on or off.", "Включить или выключить эффект.");
        static readonly GUIContent PositionContent = C("Position",
            "Outside — around the sprite. Inside — along the inner edge, over the sprite. Center — half inside, half outside.",
            "Outside — снаружи спрайта. Inside — по внутреннему краю, поверх спрайта. Center — наполовину внутри, наполовину снаружи.");
        static readonly GUIContent ThicknessContent = C("Thickness (px)",
            "Outline width in pixels.",
            "Толщина обводки в пикселях.");
        static readonly GUIContent SoftnessContent = C("Softness / blur (px)",
            "Soft fade after the solid part of the outline. 0 = hard edge. Use thickness 0 + softness for a glow.",
            "Мягкое затухание после сплошной части обводки. 0 — жёсткий край. Толщина 0 + мягкость = свечение.");
        static readonly GUIContent ShapeContent = C("Corner shape",
            "Round — smooth corners. Square — sharp corners. Diamond — beveled corners (good for pixel art).",
            "Round — скруглённые углы. Square — острые углы. Diamond — скошенные углы (хорошо для пиксель-арта).");
        static readonly GUIContent AAContent = C("Anti-aliasing",
            "Smooth outline edges. Turn OFF for pixel art to get crisp pixels.",
            "Сглаживание краёв обводки. ВЫКЛЮЧИТЕ для пиксель-арта, чтобы пиксели были чёткими.");
        static readonly GUIContent ColorModeContent = C("Color mode",
            "Solid color, or a gradient:\n• across width — from inner edge (left of gradient) to outer edge (right)\n• vertical — bottom (left) to top (right)\n• horizontal — left to right\n• angle — along a custom direction",
            "Один цвет или градиент:\n• across width — от внутреннего края (левый край градиента) к внешнему (правый)\n• vertical — снизу (слева) вверх (справа)\n• horizontal — слева направо\n• angle — вдоль заданного направления");
        static readonly GUIContent ColorContent = C("Color", "Outline color (alpha is used too).", "Цвет обводки (альфа тоже учитывается).");
        static readonly GUIContent GradientContent = C("Gradient", "Outline gradient. Click to edit.", "Градиент обводки. Нажмите, чтобы редактировать.");
        static readonly GUIContent AngleContent = C("Gradient angle",
            "Direction of the gradient in degrees: 0 = to the right, 90 = up.",
            "Направление градиента в градусах: 0 — вправо, 90 — вверх.");
        static readonly GUIContent OpacityContent = C("Opacity", "Overall outline opacity.", "Общая непрозрачность обводки.");

        static readonly GUIContent OffsetXContent = C("Offset X (px)", "Shadow shift to the right (negative = left).", "Сдвиг тени вправо (отрицательный — влево).");
        static readonly GUIContent OffsetYContent = C("Offset Y (px)", "Shadow shift down (negative = up).", "Сдвиг тени вниз (отрицательный — вверх).");
        static readonly GUIContent SpreadContent = C("Spread (px)", "Grows the shadow shape before blurring.", "Расширяет форму тени перед размытием.");
        static readonly GUIContent BlurContent = C("Blur (px)", "Shadow softness.", "Мягкость (размытие) тени.");
        static readonly GUIContent ShadowColorContent = C("Color", "Shadow color. Alpha = shadow opacity.", "Цвет тени. Альфа = непрозрачность тени.");
        static readonly GUIContent ShadowOutlineContent = C("Include outline",
            "The shadow is cast by the sprite together with its outer outline.",
            "Тень отбрасывает спрайт вместе с внешней обводкой.");

        static readonly GUIContent AlphaThresholdContent = C("Alpha threshold",
            "A pixel counts as part of the sprite if its alpha is > this value (0–254). Raise it to ignore faint pixels.",
            "Пиксель считается частью спрайта, если его альфа > этого значения (0–254). Увеличьте, чтобы игнорировать почти прозрачные пиксели.");

        static readonly GUIContent MaskTypeContent = C("Mask type",
            "• None — no mask\n• Brush — paint the mask on the preview\n• Shape — rectangle or ellipse\n• Sides — block effects near chosen image sides\n• Edge direction — block edges facing a direction (e.g. bottom edges)\n• Color — block edges of a picked color\n• Texture — use your own black/white mask image",
            "• None — без маски\n• Brush — рисовать маску кистью на превью\n• Shape — прямоугольник или эллипс\n• Sides — блокировать эффекты у выбранных сторон картинки\n• Edge direction — блокировать края, смотрящие в заданную сторону (например, нижние)\n• Color — блокировать края выбранного цвета\n• Texture — своя чёрно-белая картинка-маска");
        static readonly GUIContent MaskSpaceContent = C("Mask works on",
            "Source edges — the masked sprite edges simply don't emit effects, so outline/shadow fade out naturally.\nResult — effects are cut off exactly where the mask is (hard edge).",
            "Source edges — замаскированные края спрайта просто не порождают эффекты, обводка/тень естественно затухают.\nResult — эффекты срезаются ровно по маске (жёсткий край).");
        static readonly GUIContent AffectsOutlineContent = C("Affects outline", "The mask blocks the outline.", "Маска блокирует обводку.");
        static readonly GUIContent AffectsShadowContent = C("Affects shadow", "The mask blocks the shadow.", "Маска блокирует тень.");
        static readonly GUIContent InvertContent = C("Invert", "Effects appear ONLY inside the mask.", "Эффекты появляются ТОЛЬКО внутри маски.");
        static readonly GUIContent OverlayContent = C("Show mask", "Show the mask on the preview in magenta.", "Показывать маску на превью пурпурным цветом.");

        static readonly GUIContent BrushSizeContent = C("Brush size (px)", "Brush diameter in pixels of the first sprite.", "Диаметр кисти в пикселях первого спрайта.");
        static readonly GUIContent BrushHardnessContent = C("Hardness", "0 = very soft edge, 1 = hard edge.", "0 — очень мягкий край, 1 — жёсткий.");
        static readonly GUIContent BrushStrengthContent = C("Strength", "How strongly one stroke paints or erases.", "Насколько сильно рисует или стирает один мазок.");

        static readonly GUIContent ShapeKindContent = C("Shape", "Mask shape.", "Форма маски.");
        static readonly GUIContent ShapeCXContent = C("Center X (%)", "From the left edge. You can also drag on the preview.", "От левого края. Можно двигать мышкой на превью.");
        static readonly GUIContent ShapeCYContent = C("Center Y (%)", "From the top edge. You can also drag on the preview.", "От верхнего края. Можно двигать мышкой на превью.");
        static readonly GUIContent ShapeWContent = C("Width (%)", "Shape width relative to the image.", "Ширина фигуры относительно картинки.");
        static readonly GUIContent ShapeHContent = C("Height (%)", "Shape height relative to the image.", "Высота фигуры относительно картинки.");
        static readonly GUIContent ShapeRotContent = C("Rotation (°)", "Shape rotation.", "Поворот фигуры.");
        static readonly GUIContent ShapeFeatherContent = C("Feather (px)", "Soft edge of the shape.", "Мягкий край фигуры.");

        static readonly GUIContent SideTopContent = C("Top (%)", "Block effects in this much of the image from the top. 0 = off.", "Блокировать эффекты на такой доле картинки сверху. 0 — выкл.");
        static readonly GUIContent SideBottomContent = C("Bottom (%)", "Block effects in this much of the image from the bottom. 0 = off.", "Блокировать эффекты на такой доле картинки снизу. 0 — выкл.");
        static readonly GUIContent SideLeftContent = C("Left (%)", "Block effects in this much of the image from the left. 0 = off.", "Блокировать эффекты на такой доле картинки слева. 0 — выкл.");
        static readonly GUIContent SideRightContent = C("Right (%)", "Block effects in this much of the image from the right. 0 = off.", "Блокировать эффекты на такой доле картинки справа. 0 — выкл.");
        static readonly GUIContent SideFeatherContent = C("Feather (%)", "Soft transition at the border of the side areas.", "Плавный переход на границе областей.");

        static readonly GUIContent DirAngleContent = C("Direction (°)",
            "Edges facing this direction are blocked: 0 = right, 90 = up, 180 = left, 270 = down.",
            "Блокируются края, смотрящие в эту сторону: 0 — вправо, 90 — вверх, 180 — влево, 270 — вниз.");
        static readonly GUIContent DirSpreadContent = C("Spread (°)", "How far from the direction an edge still counts as facing it.", "Насколько край может отклоняться от направления и всё ещё блокироваться.");
        static readonly GUIContent DirFeatherContent = C("Feather (°)", "Soft transition beyond the spread.", "Плавный переход за пределами разброса.");

        static readonly GUIContent MaskColorContent = C("Color", "Sprite pixels of this color don't emit effects. Click on the preview to pick.", "Пиксели спрайта этого цвета не порождают эффекты. Кликните по превью, чтобы взять цвет.");
        static readonly GUIContent ToleranceContent = C("Tolerance", "How different a color may be and still match.", "Насколько цвет может отличаться и всё ещё совпадать.");
        static readonly GUIContent ColorFeatherContent = C("Feather", "Soft transition beyond the tolerance.", "Плавный переход за пределами допуска.");

        static readonly GUIContent MaskTextureContent = C("Mask texture",
            "Black/white image stretched over the sprite. White = blocked. Size doesn't have to match.",
            "Чёрно-белая картинка, растянутая на спрайт. Белое — заблокировано. Размер может не совпадать.");
        static readonly GUIContent ChannelContent = C("Channel", "Which channel of the mask texture to use.", "Какой канал текстуры-маски использовать.");

        static readonly GUIContent OutputModeContent = C("Save mode",
            "Duplicate — a new file next to the original (Name + suffix).\nOverwrite — replaces the original PNG. Cannot be undone; baking again stacks effects!",
            "Duplicate — новый файл рядом с оригиналом (Имя + суффикс).\nOverwrite — заменяет исходный PNG. Отменить нельзя; повторное запекание наложит эффекты ещё раз!");
        static readonly GUIContent SuffixContent = C("Suffix", "Added to the file name of the duplicate.", "Добавляется к имени файла дубликата.");
        static readonly GUIContent ExpandContent = C("Expand canvas",
            "Grow the image so the outline and shadow fit. If off, effects are clipped at the original size.",
            "Увеличить картинку, чтобы обводка и тень поместились. Если выключено — эффекты обрезаются по исходному размеру.");
        static readonly GUIContent KeepPivotContent = C("Keep pivot position",
            "When the canvas grows, adjust the sprite pivot so the art stays in the same place in the scene.",
            "Когда холст увеличивается, пивот спрайта подстраивается, чтобы рисунок остался на том же месте в сцене.");

        static readonly GUIContent BackgroundContent = C("Background", "Preview background, to judge outlines and shadows.", "Фон превью, чтобы оценить обводку и тень.");

        // ------------------------------------------------------------------ State

        [SerializeField] Settings s = new Settings();
        [SerializeField] Texture2D maskTexture;
        [SerializeField] byte[] brushMask;
        [SerializeField] int brushW, brushH;
        [SerializeField] bool foldOutline = true, foldShadow = true, foldMask = true, foldOutput = true;

        readonly List<Texture2D> textures = new List<Texture2D>();
        Vector2 mainScroll;

        // First sprite (preview + batch mask reference)
        Texture2D firstTex;
        Color32[] firstPixels;
        int firstW, firstH;

        // Mask texture cache
        Texture2D loadedMaskTex;
        Color32[] maskTexPixels;
        int maskTexW, maskTexH;

        // Preview
        BakeResult previewRes;
        Texture2D previewTex, overlayTex, brushOverlayTex;
        int brushVersion, brushOverlayVersion = -1, sourceVersion;
        string lastKey;
        bool pending, painting;
        double dirtyTime;
        Vector2 lastPaintUV;
        Rect previewRect;
        Rect previewView;
        [SerializeField] float previewHeight = 320f;
        [SerializeField] bool showPixelGrid = true;
        float zoom = 1f;      // 1 = fit
        Vector2 pan;

        class BakeResult
        {
            public Color32[] pixels;
            public int w, h, padL, padR, padB, padT;
            public float[] mask;
        }

        class MaskInput
        {
            public byte[] brush;
            public int brushW, brushH;
            public Color32[] tex;
            public int texW, texH;
        }

        // ------------------------------------------------------------------ Menu

        [MenuItem(MenuRoot + "Sprite Outline & Shadow Baker", false, 1002)]
        static void Open()
        {
            var window = GetWindow<SpriteOutlineShadowBakerWindow>("Outline & Shadow Baker");
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
            if (s.outlineGradient == null) s.outlineGradient = CreateDefaultGradient();

            string guid = EditorPrefs.GetString(PrefsMaskTex, "");
            if (maskTexture == null && !string.IsNullOrEmpty(guid))
                maskTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));

            EditorApplication.update += Tick;
            RefreshSelection();
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            EditorPrefs.SetString(PrefsSettings, JsonUtility.ToJson(s));
            EditorPrefs.SetString(PrefsMaskTex, maskTexture != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(maskTexture)) : "");
            DestroyTex(ref previewTex);
            DestroyTex(ref overlayTex);
            DestroyTex(ref brushOverlayTex);
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
            loadedMaskTex = null;
            sourceVersion++;
            RefreshSelection();
            Repaint();
        }

        void RefreshSelection()
        {
            textures.Clear();
            foreach (var t in Selection.GetFiltered<Texture2D>(SelectionMode.DeepAssets))
                if (!textures.Contains(t)) textures.Add(t);
            EnsureFirstLoaded();
        }

        void EnsureFirstLoaded()
        {
            var first = textures.Count > 0 ? textures[0] : null;
            if (first == firstTex) return; // also avoids re-reading an unreadable texture every frame

            firstTex = first;
            firstPixels = null;
            if (first != null && TryLoadPixels(AssetDatabase.GetAssetPath(first), first, out var px, out int w, out int h))
            {
                firstPixels = px;
                firstW = w;
                firstH = h;
                InitBrush(false);
            }
            sourceVersion++;
        }

        void EnsureMaskTextureLoaded()
        {
            if (maskTexture == loadedMaskTex) return;
            loadedMaskTex = maskTexture;
            maskTexPixels = null;
            if (maskTexture != null &&
                TryLoadPixels(AssetDatabase.GetAssetPath(maskTexture), maskTexture, out var px, out int w, out int h))
            {
                maskTexPixels = px;
                maskTexW = w;
                maskTexH = h;
            }
        }

        MaskInput GetMaskInput()
        {
            EnsureMaskTextureLoaded();
            return new MaskInput
            {
                brush = brushMask, brushW = brushW, brushH = brushH,
                tex = maskTexPixels, texW = maskTexW, texH = maskTexH
            };
        }

        // Delayed preview rebuild, so sliders stay responsive
        void Tick()
        {
            if (!pending || painting) return;
            if (EditorApplication.timeSinceStartup - dirtyTime < 0.06) return;
            pending = false;
            RebuildPreview();
            Repaint();
        }

        string BuildKey()
        {
            // Gradient is hashed by hand, in case JSON doesn't capture its keys
            var sb = new System.Text.StringBuilder();
            if (s.outlineGradient != null)
            {
                foreach (var k in s.outlineGradient.colorKeys) sb.Append(k.color).Append(k.time);
                foreach (var k in s.outlineGradient.alphaKeys) sb.Append(k.alpha).Append(k.time);
                sb.Append(s.outlineGradient.mode);
            }
            return JsonUtility.ToJson(s) + "|" + sb + "|" + brushVersion + "|" + sourceVersion + "|" +
                   (firstTex != null ? firstTex.GetInstanceID() : 0) + "|" +
                   (maskTexture != null ? maskTexture.GetInstanceID() : 0);
        }

        void RebuildPreview()
        {
            if (firstPixels == null)
            {
                previewRes = null;
                return;
            }

            previewRes = Bake(firstPixels, firstW, firstH, s, GetMaskInput());

            if (previewTex == null || previewTex.width != previewRes.w || previewTex.height != previewRes.h)
            {
                DestroyTex(ref previewTex);
                previewTex = new Texture2D(previewRes.w, previewRes.h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            }
            previewTex.SetPixels32(previewRes.pixels);
            previewTex.Apply(false);

            if (previewRes.mask != null)
            {
                if (overlayTex == null || overlayTex.width != previewRes.w || overlayTex.height != previewRes.h)
                {
                    DestroyTex(ref overlayTex);
                    overlayTex = new Texture2D(previewRes.w, previewRes.h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                }
                var ov = new Color32[previewRes.mask.Length];
                for (int i = 0; i < ov.Length; i++)
                    ov[i] = new Color32(255, 0, 255, (byte)(Mathf.Clamp01(previewRes.mask[i]) * 140f));
                overlayTex.SetPixels32(ov);
                overlayTex.Apply(false);
            }
        }

        void UpdateBrushOverlay()
        {
            if (brushMask == null || brushOverlayVersion == brushVersion) return;
            brushOverlayVersion = brushVersion;

            if (brushOverlayTex == null || brushOverlayTex.width != brushW || brushOverlayTex.height != brushH)
            {
                DestroyTex(ref brushOverlayTex);
                brushOverlayTex = new Texture2D(brushW, brushH, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            }
            var px = new Color32[brushW * brushH];
            for (int y = 0; y < brushH; y++)
            {
                int srcRow = (brushH - 1 - y) * brushW; // brush rows are stored top-first, textures are bottom-first
                int dstRow = y * brushW;
                for (int x = 0; x < brushW; x++)
                {
                    byte v = brushMask[srcRow + x];
                    if (s.maskInvert) v = (byte)(255 - v);
                    px[dstRow + x] = new Color32(255, 0, 255, (byte)(v * 140 / 255));
                }
            }
            brushOverlayTex.SetPixels32(px);
            brushOverlayTex.Apply(false);
        }

        // ------------------------------------------------------------------ GUI

        void OnGUI()
        {
            EnsureFirstLoaded();

            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);

            DrawSelection();
            DrawPreviewSection();
            DrawOutlineSection();
            DrawShadowSection();
            DrawMaskSection();
            DrawOutputSection();
            DrawProcessButton();

            EditorGUILayout.EndScrollView();

            string key = BuildKey();
            if (key != lastKey)
            {
                lastKey = key;
                pending = true;
                dirtyTime = EditorApplication.timeSinceStartup;
                brushOverlayVersion = -1; // e.g. Invert changes how the brush overlay looks
            }
        }

        void DrawSelection()
        {
            EditorGUILayout.Space(4);
            string label = textures.Count == 0 ? "Selected textures: 0"
                : textures.Count == 1 ? $"Selected: {textures[0].name}"
                : $"Selected textures: {textures.Count}  (preview & masks: {textures[0].name})";
            EditorGUILayout.LabelField(C(label,
                "Textures selected in the Project window (folders include everything inside). The first one is used for the preview and as the mask reference for the whole batch.",
                "Текстуры, выделенные в окне Project (папки — со всем содержимым). Первая используется для превью и как образец маски для всей пачки."),
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

        void DrawPreviewSection()
        {
            if (firstPixels == null) return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(PreviewHeader, EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                s.background = (PreviewBackground)EditorGUILayout.Popup(BackgroundContent, (int)s.background, BackgroundLabels);
                if (s.background == PreviewBackground.Custom)
                    s.customBackground = EditorGUILayout.ColorField(GUIContent.none, s.customBackground, false, false, false, GUILayout.Width(50));
            }
            if (s.maskType != MaskType.None)
                s.showMaskOverlay = EditorGUILayout.ToggleLeft(OverlayContent, s.showMaskOverlay);

            if (previewRes == null || previewTex == null)
            {
                RebuildPreview();
                if (previewRes == null || previewTex == null) return;
            }

            // --- Zoomable / pannable viewport
            float viewW = Mathf.Max(60f, EditorGUIUtility.currentViewWidth - 30f);
            Rect area = GUILayoutUtility.GetRect(viewW, previewHeight, GUILayout.ExpandWidth(true));
            var view = new Rect(0f, 0f, area.width, area.height);
            previewView = view;

            float fit = Mathf.Min(view.width / previewRes.w, view.height / previewRes.h);
            float scale = fit * zoom;
            var size = new Vector2(previewRes.w * scale, previewRes.h * scale);
            ClampPan(size, view);
            var r = new Rect(view.center - size * 0.5f + pan, size);
            previewRect = r;

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(area, new Color(0.16f, 0.16f, 0.16f));

            GUI.BeginClip(area);
            if (Event.current.type == EventType.Repaint)
            {
                previewTex.filterMode = scale >= 1f ? FilterMode.Point : FilterMode.Bilinear;
                switch (s.background)
                {
                    case PreviewBackground.Checker: EditorGUI.DrawTextureTransparent(r, previewTex, ScaleMode.StretchToFill); break;
                    case PreviewBackground.Dark: EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.12f)); GUI.DrawTexture(r, previewTex); break;
                    case PreviewBackground.Light: EditorGUI.DrawRect(r, new Color(0.92f, 0.92f, 0.92f)); GUI.DrawTexture(r, previewTex); break;
                    default: EditorGUI.DrawRect(r, s.customBackground); GUI.DrawTexture(r, previewTex); break;
                }

                if (s.showMaskOverlay && s.maskType != MaskType.None)
                {
                    if (s.maskType == MaskType.Brush)
                    {
                        UpdateBrushOverlay();
                        if (brushOverlayTex != null)
                        {
                            // The brush buffer covers u,v in [-margin, 1 + margin] of the source image
                            brushOverlayTex.filterMode = previewTex.filterMode;
                            var br = new Rect(
                                (previewRes.padL - BrushMargin * firstW) * scale,
                                (previewRes.padT - BrushMargin * firstH) * scale,
                                (1f + 2f * BrushMargin) * firstW * scale,
                                (1f + 2f * BrushMargin) * firstH * scale);
                            GUI.BeginClip(r);
                            GUI.DrawTexture(br, brushOverlayTex);
                            GUI.EndClip();
                        }
                    }
                    else if (previewRes.mask != null && overlayTex != null)
                    {
                        overlayTex.filterMode = previewTex.filterMode;
                        GUI.DrawTexture(r, overlayTex);
                    }
                }

                if (showPixelGrid && scale >= 8f)
                    DrawPixelGrid(r, view, scale);

                // Original image bounds
                DrawFrame(new Rect(r.x + previewRes.padL * scale, r.y + previewRes.padT * scale, firstW * scale, firstH * scale),
                          new Color(1f, 1f, 1f, 0.35f));
            }

            HandleZoomPan(view, fit);
            HandlePreviewMouse(r);
            GUI.EndClip();

            // Brush cursor (window coordinates)
            if (Event.current.type == EventType.Repaint && s.maskType == MaskType.Brush && area.Contains(Event.current.mousePosition))
            {
                float radius = s.brushSize * 0.5f * scale;
                Handles.color = Color.white;
                Handles.DrawWireDisc(Event.current.mousePosition, Vector3.forward, radius);
                Handles.color = Color.black;
                Handles.DrawWireDisc(Event.current.mousePosition, Vector3.forward, radius + 1f);
            }

            // --- Zoom toolbar
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent($"Zoom {Mathf.RoundToInt(scale * 100f)}%", Bi(
                    "Mouse wheel — zoom to cursor. Middle mouse or Alt+drag — pan (plain drag also pans when the mask has no mouse tool).",
                    "Колёсико — зум к курсору. Средняя кнопка или Alt+перетаскивание — двигать (просто перетаскивание тоже двигает, если у маски нет инструмента мышью).")),
                    EditorStyles.miniLabel, GUILayout.Width(80));

                if (GUILayout.Button(C("−", "Zoom out.", "Отдалить."), EditorStyles.miniButtonLeft, GUILayout.Width(24)))
                    SetZoom(zoom / 1.5f, view.center, view, fit);
                if (GUILayout.Button(C("+", "Zoom in.", "Приблизить."), EditorStyles.miniButtonMid, GUILayout.Width(24)))
                    SetZoom(zoom * 1.5f, view.center, view, fit);
                if (GUILayout.Button(C("Fit", "Fit the whole image into the view.", "Вписать всю картинку в окно."), EditorStyles.miniButtonMid, GUILayout.Width(36)))
                {
                    zoom = 1f;
                    pan = Vector2.zero;
                }
                if (GUILayout.Button(C("1:1", "One image pixel = one screen pixel.", "Один пиксель картинки = один пиксель экрана."), EditorStyles.miniButtonRight, GUILayout.Width(36)))
                {
                    zoom = Mathf.Clamp(1f / fit, MinZoom, MaxZoom(fit));
                    pan = Vector2.zero;
                }

                GUILayout.Space(8);
                showPixelGrid = GUILayout.Toggle(showPixelGrid, C("Grid",
                    "Show the pixel grid when zoomed in (800% and more).",
                    "Показывать пиксельную сетку при сильном приближении (от 800%)."), EditorStyles.miniButton, GUILayout.Width(40));

                GUILayout.FlexibleSpace();
                GUILayout.Label(C("Height", "Height of the preview area.", "Высота области превью."), EditorStyles.miniLabel, GUILayout.Width(40));
                previewHeight = GUILayout.HorizontalSlider(previewHeight, 150f, 900f, GUILayout.Width(70));
            }

            string hint = s.maskType == MaskType.Brush ? "LMB — paint, RMB or Shift+LMB — erase"
                        : s.maskType == MaskType.Shape ? "Click / drag — move the shape"
                        : s.maskType == MaskType.ColorPick ? "Click on the sprite — pick color"
                        : "Drag — pan";
            EditorGUILayout.LabelField($"{firstW}×{firstH} → {previewRes.w}×{previewRes.h}   |   Wheel — zoom   |   " + hint,
                EditorStyles.centeredGreyMiniLabel);
        }

        const float MinZoom = 0.25f;
        static float MaxZoom(float fit) => Mathf.Max(1f, 64f / Mathf.Max(0.0001f, fit)); // up to 64 screen px per image px

        void ClampPan(Vector2 size, Rect view)
        {
            float mx = size.x * 0.5f + view.width * 0.5f - 24f;
            float my = size.y * 0.5f + view.height * 0.5f - 24f;
            pan = new Vector2(Mathf.Clamp(pan.x, -Mathf.Max(0f, mx), Mathf.Max(0f, mx)),
                              Mathf.Clamp(pan.y, -Mathf.Max(0f, my), Mathf.Max(0f, my)));
        }

        // Zoom keeping the point under 'pivot' in place
        void SetZoom(float newZoom, Vector2 pivot, Rect view, float fit)
        {
            newZoom = Mathf.Clamp(newZoom, MinZoom, MaxZoom(fit));
            float ratio = newZoom / zoom;
            Vector2 d = pivot - (view.center + pan);
            pan += d - d * ratio;
            zoom = newZoom;
            Repaint();
        }

        void HandleZoomPan(Rect view, float fit)
        {
            var e = Event.current;
            if (e.type == EventType.ScrollWheel && view.Contains(e.mousePosition))
            {
                SetZoom(zoom * (e.delta.y > 0f ? 1f / 1.2f : 1.2f), e.mousePosition, view, fit);
                e.Use();
                return;
            }

            bool lmbPans = s.maskType != MaskType.Brush && s.maskType != MaskType.Shape && s.maskType != MaskType.ColorPick;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (view.Contains(e.mousePosition) && (e.button == 2 || (e.button == 0 && (e.alt || lmbPans))))
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

        static void DrawPixelGrid(Rect r, Rect view, float scale)
        {
            var c = new Color(0f, 0f, 0f, 0.25f);
            int w = Mathf.RoundToInt(r.width / scale), h = Mathf.RoundToInt(r.height / scale);
            int x0 = Mathf.Max(0, Mathf.FloorToInt((view.xMin - r.x) / scale));
            int x1 = Mathf.Min(w, Mathf.CeilToInt((view.xMax - r.x) / scale));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((view.yMin - r.y) / scale));
            int y1 = Mathf.Min(h, Mathf.CeilToInt((view.yMax - r.y) / scale));
            float top = Mathf.Max(r.y, view.yMin), bottom = Mathf.Min(r.yMax, view.yMax);
            float left = Mathf.Max(r.x, view.xMin), right = Mathf.Min(r.xMax, view.xMax);

            for (int x = x0; x <= x1; x++)
                EditorGUI.DrawRect(new Rect(r.x + x * scale, top, 1f, bottom - top), c);
            for (int y = y0; y <= y1; y++)
                EditorGUI.DrawRect(new Rect(left, r.y + y * scale, right - left, 1f), c);
        }

        static void DrawFrame(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
        }

        void DrawOutlineSection()
        {
            if (!Section(ref foldOutline, OutlineHeader)) return;
            EditorGUI.indentLevel++;
            s.outlineEnabled = EditorGUILayout.Toggle(EnabledContent, s.outlineEnabled);
            using (new EditorGUI.DisabledScope(!s.outlineEnabled))
            {
                s.outlinePosition = (OutlinePosition)EditorGUILayout.Popup(PositionContent, (int)s.outlinePosition, PositionLabels);
                s.outlineThickness = EditorGUILayout.Slider(ThicknessContent, s.outlineThickness, 0f, 64f);
                s.outlineSoftness = EditorGUILayout.Slider(SoftnessContent, s.outlineSoftness, 0f, 64f);
                s.outlineShape = (OutlineShape)EditorGUILayout.Popup(ShapeContent, (int)s.outlineShape, ShapeLabels);
                s.antiAliasing = EditorGUILayout.Toggle(AAContent, s.antiAliasing);
                s.colorMode = (ColorMode)EditorGUILayout.Popup(ColorModeContent, (int)s.colorMode, ColorModeLabels);
                if (s.colorMode == ColorMode.Solid)
                    s.outlineColor = EditorGUILayout.ColorField(ColorContent, s.outlineColor);
                else
                {
                    s.outlineGradient = EditorGUILayout.GradientField(GradientContent, s.outlineGradient);
                    if (s.colorMode == ColorMode.GradientAngle)
                        s.gradientAngle = EditorGUILayout.Slider(AngleContent, s.gradientAngle, 0f, 360f);
                }
                s.outlineOpacity = EditorGUILayout.Slider(OpacityContent, s.outlineOpacity, 0f, 1f);
            }
            s.alphaThreshold = EditorGUILayout.IntSlider(AlphaThresholdContent, s.alphaThreshold, 0, 254);
            EditorGUI.indentLevel--;
        }

        void DrawShadowSection()
        {
            if (!Section(ref foldShadow, ShadowHeader)) return;
            EditorGUI.indentLevel++;
            s.shadowEnabled = EditorGUILayout.Toggle(EnabledContent, s.shadowEnabled);
            using (new EditorGUI.DisabledScope(!s.shadowEnabled))
            {
                int ox = EditorGUILayout.IntSlider(OffsetXContent, s.shadowOffset.x, -64, 64);
                int oy = EditorGUILayout.IntSlider(OffsetYContent, s.shadowOffset.y, -64, 64);
                s.shadowOffset = new Vector2Int(ox, oy);
                s.shadowSpread = EditorGUILayout.Slider(SpreadContent, s.shadowSpread, 0f, 32f);
                s.shadowBlur = EditorGUILayout.Slider(BlurContent, s.shadowBlur, 0f, 64f);
                s.shadowColor = EditorGUILayout.ColorField(ShadowColorContent, s.shadowColor);
                using (new EditorGUI.DisabledScope(!s.outlineEnabled || s.outlinePosition == OutlinePosition.Inside))
                    s.shadowIncludesOutline = EditorGUILayout.Toggle(ShadowOutlineContent, s.shadowIncludesOutline);
            }
            EditorGUI.indentLevel--;
        }

        void DrawMaskSection()
        {
            if (!Section(ref foldMask, MaskHeader)) return;
            EditorGUI.indentLevel++;

            s.maskType = (MaskType)EditorGUILayout.Popup(MaskTypeContent, (int)s.maskType, MaskTypeLabels);
            if (s.maskType == MaskType.None)
            {
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUILayout.Space(2);
            switch (s.maskType)
            {
                case MaskType.Brush:
                    s.brushSize = EditorGUILayout.Slider(BrushSizeContent, s.brushSize, 1f, 256f);
                    s.brushHardness = EditorGUILayout.Slider(BrushHardnessContent, s.brushHardness, 0f, 1f);
                    s.brushStrength = EditorGUILayout.Slider(BrushStrengthContent, s.brushStrength, 0.05f, 1f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(EditorGUI.indentLevel * 15f);
                        if (GUILayout.Button(C("Clear", "Erase the whole mask.", "Стереть всю маску."), EditorStyles.miniButtonLeft))
                            FillBrush(0);
                        if (GUILayout.Button(C("Fill", "Fill the whole mask.", "Залить всю маску."), EditorStyles.miniButtonMid))
                            FillBrush(255);
                        if (GUILayout.Button(C("Save as PNG…",
                                "Save the painted mask as a PNG, e.g. to reuse it later with the Texture mask type.",
                                "Сохранить нарисованную маску в PNG, например, чтобы потом использовать её в режиме Texture."),
                                EditorStyles.miniButtonRight))
                            SaveBrushAsPng();
                    }
                    break;

                case MaskType.Shape:
                    s.shapeKind = (ShapeKind)EditorGUILayout.Popup(ShapeKindContent, (int)s.shapeKind, ShapeKindLabels);
                    float cx = EditorGUILayout.Slider(ShapeCXContent, s.shapeCenter.x * 100f, 0f, 100f) / 100f;
                    float cy = EditorGUILayout.Slider(ShapeCYContent, s.shapeCenter.y * 100f, 0f, 100f) / 100f;
                    s.shapeCenter = new Vector2(cx, cy);
                    float sw = EditorGUILayout.Slider(ShapeWContent, s.shapeSize.x * 100f, 1f, 200f) / 100f;
                    float sh = EditorGUILayout.Slider(ShapeHContent, s.shapeSize.y * 100f, 1f, 200f) / 100f;
                    s.shapeSize = new Vector2(sw, sh);
                    s.shapeRotation = EditorGUILayout.Slider(ShapeRotContent, s.shapeRotation, -180f, 180f);
                    s.shapeFeather = EditorGUILayout.Slider(ShapeFeatherContent, s.shapeFeather, 0f, 64f);
                    break;

                case MaskType.Sides:
                    s.sideTop = EditorGUILayout.Slider(SideTopContent, s.sideTop * 100f, 0f, 100f) / 100f;
                    s.sideBottom = EditorGUILayout.Slider(SideBottomContent, s.sideBottom * 100f, 0f, 100f) / 100f;
                    s.sideLeft = EditorGUILayout.Slider(SideLeftContent, s.sideLeft * 100f, 0f, 100f) / 100f;
                    s.sideRight = EditorGUILayout.Slider(SideRightContent, s.sideRight * 100f, 0f, 100f) / 100f;
                    s.sideFeather = EditorGUILayout.Slider(SideFeatherContent, s.sideFeather * 100f, 0f, 50f) / 100f;
                    break;

                case MaskType.EdgeDirection:
                    s.dirAngle = EditorGUILayout.Slider(DirAngleContent, s.dirAngle, 0f, 360f);
                    s.dirSpread = EditorGUILayout.Slider(DirSpreadContent, s.dirSpread, 0f, 180f);
                    s.dirFeather = EditorGUILayout.Slider(DirFeatherContent, s.dirFeather, 0f, 90f);
                    EditorGUILayout.HelpBox(Bi(
                        "Calculated from each sprite's own edges, so it works for every frame of the batch.",
                        "Считается по краям каждого спрайта, поэтому работает на всех кадрах пачки."), MessageType.None);
                    break;

                case MaskType.ColorPick:
                    s.maskColor = EditorGUILayout.ColorField(MaskColorContent, s.maskColor, true, false, false);
                    s.colorTolerance = EditorGUILayout.Slider(ToleranceContent, s.colorTolerance, 0f, 1f);
                    s.colorFeather = EditorGUILayout.Slider(ColorFeatherContent, s.colorFeather, 0f, 1f);
                    EditorGUILayout.HelpBox(Bi(
                        "Calculated from each sprite's own colors, so it works for every frame of the batch.",
                        "Считается по цветам каждого спрайта, поэтому работает на всех кадрах пачки."), MessageType.None);
                    break;

                case MaskType.TextureFile:
                    maskTexture = (Texture2D)EditorGUILayout.ObjectField(MaskTextureContent, maskTexture, typeof(Texture2D), false);
                    s.maskChannel = (TexChannel)EditorGUILayout.Popup(ChannelContent, (int)s.maskChannel, ChannelLabels);
                    if (maskTexture == null)
                        EditorGUILayout.HelpBox(Bi("Assign a mask texture.", "Назначьте текстуру-маску."), MessageType.Info);
                    break;
            }

            EditorGUILayout.Space(2);
            bool spaceSelectable = s.maskType != MaskType.EdgeDirection && s.maskType != MaskType.ColorPick;
            if (spaceSelectable)
                s.maskSpace = (MaskSpace)EditorGUILayout.Popup(MaskSpaceContent, (int)s.maskSpace, MaskSpaceLabels);
            s.maskAffectsOutline = EditorGUILayout.Toggle(AffectsOutlineContent, s.maskAffectsOutline);
            s.maskAffectsShadow = EditorGUILayout.Toggle(AffectsShadowContent, s.maskAffectsShadow);
            s.maskInvert = EditorGUILayout.Toggle(InvertContent, s.maskInvert);

            EditorGUI.indentLevel--;
        }

        void DrawOutputSection()
        {
            if (!Section(ref foldOutput, OutputHeader)) return;
            EditorGUI.indentLevel++;
            s.output = (OutputMode)EditorGUILayout.Popup(OutputModeContent, (int)s.output, OutputLabels);
            if (s.output == OutputMode.Duplicate)
                s.suffix = EditorGUILayout.TextField(SuffixContent, s.suffix);
            s.expandCanvas = EditorGUILayout.Toggle(ExpandContent, s.expandCanvas);
            using (new EditorGUI.DisabledScope(!s.expandCanvas))
                s.keepPivot = EditorGUILayout.Toggle(KeepPivotContent, s.keepPivot);

            if (s.output == OutputMode.Overwrite)
            {
                EditorGUILayout.HelpBox(Bi(
                    "Overwrites original PNG files (non-PNG files are saved as duplicates). Cannot be undone. " +
                    "Baking the same file again adds the effects on top of the already baked ones.",
                    "Перезаписывает исходные PNG (не-PNG сохраняются дубликатами). Отменить нельзя. " +
                    "Повторное запекание того же файла наложит эффекты поверх уже запечённых."), MessageType.Warning);
            }
            else
            {
                string sfx = string.IsNullOrWhiteSpace(s.suffix) ? "_fx" : s.suffix;
                EditorGUILayout.HelpBox(Bi(
                    $"Saved as Name{sfx}.png next to the original; import settings are copied. Re-baking updates the same duplicate.",
                    $"Сохраняется как Name{sfx}.png рядом с оригиналом, настройки импорта копируются. Повторное запекание обновляет тот же дубликат."),
                    MessageType.None);
            }
            EditorGUI.indentLevel--;
        }

        void DrawProcessButton()
        {
            EditorGUILayout.Space(10);
            bool nothing = !s.outlineEnabled && !s.shadowEnabled;
            using (new EditorGUI.DisabledScope(textures.Count == 0 || nothing))
            {
                var label = new GUIContent(textures.Count > 1 ? $"Bake ({textures.Count})" : "Bake",
                    Bi("Bake the effects into all selected sprites.", "Запечь эффекты во все выбранные спрайты."));
                if (GUILayout.Button(label, GUILayout.Height(32)))
                    Process();
            }
            if (nothing)
                EditorGUILayout.HelpBox(Bi("Enable the outline and/or the shadow.", "Включите обводку и/или тень."), MessageType.Info);
            EditorGUILayout.Space(4);
        }

        // ------------------------------------------------------------------ Preview mouse

        void HandlePreviewMouse(Rect r)
        {
            if (previewRes == null) return;
            var e = Event.current;

            if (s.maskType == MaskType.Brush && e.type == EventType.MouseMove && previewView.Contains(e.mousePosition))
                Repaint();

            if (s.maskType != MaskType.Brush && s.maskType != MaskType.Shape && s.maskType != MaskType.ColorPick)
                return;

            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!e.alt && (e.button == 0 || (e.button == 1 && s.maskType == MaskType.Brush)) &&
                        previewView.Contains(e.mousePosition) &&
                        (s.maskType == MaskType.Brush || r.Contains(e.mousePosition)))
                    {
                        GUIUtility.hotControl = id;
                        GUI.FocusControl(null);
                        MouseToUV(e.mousePosition, r, out float u, out float v);
                        if (s.maskType == MaskType.Brush)
                        {
                            painting = true;
                            lastPaintUV = new Vector2(u, v);
                            PaintStroke(lastPaintUV, lastPaintUV, e.button == 1 || e.shift);
                        }
                        else ApplyPointer(u, v);
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        MouseToUV(e.mousePosition, r, out float u, out float v);
                        if (s.maskType == MaskType.Brush)
                        {
                            var cur = new Vector2(u, v);
                            PaintStroke(lastPaintUV, cur, e.button == 1 || e.shift);
                            lastPaintUV = cur;
                        }
                        else ApplyPointer(u, v);
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        painting = false;
                        pending = true;
                        dirtyTime = 0;
                        e.Use();
                    }
                    break;

                case EventType.ContextClick:
                    if (s.maskType == MaskType.Brush && r.Contains(e.mousePosition)) e.Use();
                    break;
            }
        }

        // Preview mouse position -> normalized source coords (u from left, v from top)
        void MouseToUV(Vector2 mp, Rect r, out float u, out float vTop)
        {
            float cx = (mp.x - r.x) / r.width * previewRes.w;
            float cyTop = (mp.y - r.y) / r.height * previewRes.h;
            u = (cx - previewRes.padL) / firstW;
            vTop = (cyTop - previewRes.padT) / firstH;
        }

        void ApplyPointer(float u, float vTop)
        {
            if (s.maskType == MaskType.Shape)
            {
                s.shapeCenter = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(vTop));
            }
            else if (s.maskType == MaskType.ColorPick)
            {
                int px = Mathf.FloorToInt(u * firstW);
                int py = Mathf.FloorToInt((1f - vTop) * firstH);
                if (px >= 0 && px < firstW && py >= 0 && py < firstH)
                {
                    var c = firstPixels[py * firstW + px];
                    if (c.a > 0) s.maskColor = new Color32(c.r, c.g, c.b, 255);
                }
            }
        }

        // ------------------------------------------------------------------ Brush

        void InitBrush(bool force)
        {
            if (firstPixels == null) return;
            if (!force && brushMask != null && brushW > 0 && brushH > 0 && brushMask.Length == brushW * brushH) return;
            brushW = Mathf.Max(1, Mathf.RoundToInt(firstW * (1f + 2f * BrushMargin)));
            brushH = Mathf.Max(1, Mathf.RoundToInt(firstH * (1f + 2f * BrushMargin)));
            brushMask = new byte[brushW * brushH];
            brushVersion++;
        }

        void FillBrush(byte value)
        {
            InitBrush(false);
            if (brushMask == null) return;
            for (int i = 0; i < brushMask.Length; i++) brushMask[i] = value;
            brushVersion++;
        }

        void PaintStroke(Vector2 from, Vector2 to, bool erase)
        {
            InitBrush(false);
            if (brushMask == null) return;

            float span = 1f + 2f * BrushMargin;
            Vector2 a = new Vector2((from.x + BrushMargin) / span * brushW, (from.y + BrushMargin) / span * brushH);
            Vector2 b = new Vector2((to.x + BrushMargin) / span * brushW, (to.y + BrushMargin) / span * brushH);
            float scale = brushW / (span * firstW);
            float radius = Mathf.Max(0.5f, s.brushSize * 0.5f * scale);

            float dist = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist / Mathf.Max(0.5f, radius * 0.3f)));
            for (int i = 0; i <= steps; i++)
                PaintDab(Vector2.Lerp(a, b, (float)i / steps), radius, erase);

            brushVersion++;
        }

        void PaintDab(Vector2 c, float radius, bool erase)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(c.x - radius));
            int x1 = Mathf.Min(brushW - 1, Mathf.CeilToInt(c.x + radius));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(c.y - radius));
            int y1 = Mathf.Min(brushH - 1, Mathf.CeilToInt(c.y + radius));
            float hard = Mathf.Clamp01(s.brushHardness);

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / radius;
                if (d > 1f) continue;
                float f = (hard >= 0.999f || d <= hard) ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (d - hard) / (1f - hard));
                f *= s.brushStrength;
                int i = y * brushW + x;
                if (erase) brushMask[i] = (byte)Mathf.RoundToInt(brushMask[i] * (1f - f));
                else brushMask[i] = (byte)Mathf.Max(brushMask[i], Mathf.RoundToInt(f * 255f));
            }
        }

        void SaveBrushAsPng()
        {
            if (brushMask == null || firstPixels == null) return;
            string defaultName = (firstTex != null ? firstTex.name : "sprite") + "_mask";
            string path = EditorUtility.SaveFilePanelInProject("Save mask", defaultName, "png",
                "Save the painted mask (image area only). / Сохранить нарисованную маску (только область картинки).");
            if (string.IsNullOrEmpty(path)) return;

            float span = 1f + 2f * BrushMargin;
            var px = new Color32[firstW * firstH];
            for (int y = 0; y < firstH; y++)          // y from bottom (texture space)
            for (int x = 0; x < firstW; x++)
            {
                float u = (x + 0.5f) / firstW;
                float vTop = 1f - (y + 0.5f) / firstH;
                int bx = Mathf.Clamp(Mathf.FloorToInt((u + BrushMargin) / span * brushW), 0, brushW - 1);
                int by = Mathf.Clamp(Mathf.FloorToInt((vTop + BrushMargin) / span * brushH), 0, brushH - 1);
                byte v = brushMask[by * brushW + bx];
                px[y * firstW + x] = new Color32(v, v, v, 255);
            }
            var t = new Texture2D(firstW, firstH, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            File.WriteAllBytes(path, t.EncodeToPNG());
            DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        }

        // ------------------------------------------------------------------ Baking core

        static BakeResult Bake(Color32[] src, int W, int H, Settings s, MaskInput mi)
        {
            // --- Outline extents
            float t = s.outlineEnabled ? Mathf.Max(0f, s.outlineThickness) : 0f;
            float soft = s.outlineEnabled ? Mathf.Max(0f, s.outlineSoftness) : 0f;
            float tOut = s.outlinePosition == OutlinePosition.Outside ? t : s.outlinePosition == OutlinePosition.Center ? t * 0.5f : 0f;
            float tIn = s.outlinePosition == OutlinePosition.Inside ? t : s.outlinePosition == OutlinePosition.Center ? t * 0.5f : 0f;
            bool hasOut = s.outlineEnabled && s.outlinePosition != OutlinePosition.Inside && (tOut > 0f || soft > 0f);
            bool hasIn = s.outlineEnabled && s.outlinePosition != OutlinePosition.Outside && (tIn > 0f || soft > 0f);
            int outExt = hasOut ? Mathf.CeilToInt(tOut + soft + 1f) : 0;
            float total = Mathf.Max(0.0001f, tIn + tOut + soft);

            // --- Shadow extents (UI: +Y = down; texture: +Y = up)
            int offX = s.shadowOffset.x, offY = -s.shadowOffset.y;
            bool shadowUsesOutline = s.shadowIncludesOutline && hasOut;
            int shExt = s.shadowEnabled
                ? Mathf.CeilToInt(s.shadowSpread + s.shadowBlur + 1f) + (shadowUsesOutline ? outExt : 0)
                : 0;

            int padL = 0, padR = 0, padB = 0, padT = 0;
            if (s.expandCanvas)
            {
                padL = Mathf.Max(outExt, s.shadowEnabled ? shExt - offX : 0, 0);
                padR = Mathf.Max(outExt, s.shadowEnabled ? shExt + offX : 0, 0);
                padB = Mathf.Max(outExt, s.shadowEnabled ? shExt - offY : 0, 0);
                padT = Mathf.Max(outExt, s.shadowEnabled ? shExt + offY : 0, 0);
            }

            int CW = W + padL + padR, CH = H + padB + padT, n = CW * CH;

            // --- Canvas
            var col = new Color[n];
            var a = new float[n];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int ci = (y + padB) * CW + x + padL;
                Color c = src[y * W + x];
                col[ci] = c;
                a[ci] = c.a;
            }
            float thr = s.alphaThreshold / 255f;
            var solid = new bool[n];
            for (int i = 0; i < n; i++) solid[i] = a[i] > thr;

            // --- Mask
            float[] M = BuildMask(s, mi, col, a, CW, CH, W, H, padL, padB);
            bool srcSpace = s.maskType == MaskType.EdgeDirection || s.maskType == MaskType.ColorPick || s.maskSpace == MaskSpace.SourceEdges;

            float Keep(bool affects, int seed, int own, bool inside)
            {
                if (M == null || !affects) return 1f;
                float m;
                if (!srcSpace || seed < 0) m = M[own];
                else m = inside ? Mathf.Max(M[seed], M[own]) : M[seed];
                return 1f - Mathf.Clamp01(m);
            }

            // --- Outer outline
            var outA = new float[n];
            var outT = new float[n];
            if (hasOut)
            {
                var near = JumpFlood(solid, CW, CH, outExt + 1, s.outlineShape);
                for (int y = 0; y < CH; y++)
                for (int x = 0; x < CW; x++)
                {
                    int i = y * CW + x;
                    if (solid[i])
                    {
                        // Under semi-transparent edge pixels, so the outline shows through their soft edges
                        if (IsEdge(solid, x, y, CW, CH))
                        {
                            outA[i] = Keep(s.maskAffectsOutline, i, i, false);
                            outT[i] = tIn / total;
                        }
                        continue;
                    }
                    int nb = near[i];
                    if (nb < 0) continue;
                    float d = Dist(x, y, nb % CW, nb / CW, s.outlineShape);
                    float o = Coverage(d, tOut, soft, s.antiAliasing);
                    if (o <= 0f) continue;
                    outA[i] = o * Keep(s.maskAffectsOutline, nb, i, false);
                    outT[i] = (tIn + d - 0.5f) / total;
                }
            }

            // --- Inner outline
            var inA = new float[n];
            var inT = new float[n];
            if (hasIn)
            {
                var empty = new bool[n];
                for (int i = 0; i < n; i++) empty[i] = !solid[i];
                var near = JumpFlood(empty, CW, CH, Mathf.CeilToInt(tIn + soft + 2f), s.outlineShape);
                for (int y = 0; y < CH; y++)
                for (int x = 0; x < CW; x++)
                {
                    int i = y * CW + x;
                    if (!solid[i]) continue;
                    int nb = near[i];
                    float d = nb >= 0 ? Dist(x, y, nb % CW, nb / CW, s.outlineShape) : float.MaxValue;
                    // The area beyond the canvas border counts as transparent
                    float db = Mathf.Min(Mathf.Min(x + 1, CW - x), Mathf.Min(y + 1, CH - y));
                    if (db < d) { d = db; nb = -1; }
                    float o = Coverage(d, tIn, soft, s.antiAliasing);
                    if (o <= 0f) continue;
                    inA[i] = o * Keep(s.maskAffectsOutline, nb, i, true);
                    inT[i] = (tIn - d + 0.5f) / total;
                }
            }

            // --- Shadow
            var shA = new float[n];
            if (s.shadowEnabled && s.shadowColor.a > 0f)
            {
                var baseA = new float[n];
                for (int i = 0; i < n; i++)
                {
                    baseA[i] = a[i];
                    if (shadowUsesOutline) baseA[i] = Mathf.Max(baseA[i], outA[i] * s.outlineOpacity);
                }
                var seeds = new bool[n];
                for (int i = 0; i < n; i++) seeds[i] = baseA[i] >= 0.5f;

                float spread = Mathf.Max(0f, s.shadowSpread), blur = Mathf.Max(0f, s.shadowBlur);
                var near = JumpFlood(seeds, CW, CH, Mathf.CeilToInt(spread + blur + 2f), OutlineShape.Round);
                var val = new float[n];
                for (int y = 0; y < CH; y++)
                for (int x = 0; x < CW; x++)
                {
                    int i = y * CW + x;
                    int nb = near[i];
                    float v;
                    if (seeds[i]) { v = 1f; nb = i; }
                    else if (nb < 0) v = baseA[i];
                    else
                    {
                        float d = Dist(x, y, nb % CW, nb / CW, OutlineShape.Round);
                        float edge = spread + 0.5f;
                        if (blur > 0f) v = 1f - Mathf.SmoothStep(0f, 1f, (d - edge) / blur);
                        else if (s.antiAliasing) v = Mathf.Clamp01(edge + 0.5f - d);
                        else v = d < edge ? 1f : 0f;
                        v = Mathf.Max(v, baseA[i]);
                    }
                    if (srcSpace && M != null && s.maskAffectsShadow)
                        v *= 1f - Mathf.Clamp01(M[nb >= 0 ? nb : i]);
                    val[i] = v;
                }

                for (int y = 0; y < CH; y++)
                for (int x = 0; x < CW; x++)
                {
                    int sx = x - offX, sy = y - offY;
                    if (sx < 0 || sy < 0 || sx >= CW || sy >= CH) continue;
                    int i = y * CW + x;
                    float v = val[sy * CW + sx];
                    if (!srcSpace && M != null && s.maskAffectsShadow) v *= 1f - Mathf.Clamp01(M[i]);
                    shA[i] = v * s.shadowColor.a;
                }
            }

            // --- Composite: shadow -> outer outline -> sprite -> inner outline
            var result = new Color32[n];
            var shColor = s.shadowColor;
            for (int y = 0; y < CH; y++)
            for (int x = 0; x < CW; x++)
            {
                int i = y * CW + x;
                var dst = new Color(0f, 0f, 0f, 0f);

                if (shA[i] > 0f)
                    dst = Over(new Color(shColor.r, shColor.g, shColor.b, shA[i]), dst);

                if (outA[i] > 0f)
                {
                    var oc = OutlineColor(s, outT[i], x, y, CW, CH);
                    oc.a *= outA[i] * s.outlineOpacity;
                    dst = Over(oc, dst);
                }

                if (a[i] > 0f)
                    dst = Over(col[i], dst);

                if (inA[i] > 0f)
                {
                    // "Source-atop": painted over the sprite, never outside it
                    var ic = OutlineColor(s, inT[i], x, y, CW, CH);
                    float k = Mathf.Clamp01(ic.a * inA[i] * s.outlineOpacity);
                    dst.r = Mathf.Lerp(dst.r, ic.r, k);
                    dst.g = Mathf.Lerp(dst.g, ic.g, k);
                    dst.b = Mathf.Lerp(dst.b, ic.b, k);
                }

                result[i] = dst;
            }

            return new BakeResult
            {
                pixels = result, w = CW, h = CH,
                padL = padL, padR = padR, padB = padB, padT = padT,
                mask = M
            };
        }

        static float Coverage(float d, float t, float soft, bool aa)
        {
            float edge = t + 0.5f;
            if (soft > 0f) return 1f - Mathf.SmoothStep(0f, 1f, (d - edge) / soft);
            if (aa) return Mathf.Clamp01(edge + 0.5f - d);
            return d < edge ? 1f : 0f;
        }

        static Color OutlineColor(Settings s, float across, int x, int y, int cw, int ch)
        {
            switch (s.colorMode)
            {
                case ColorMode.GradientAcross:
                    return s.outlineGradient.Evaluate(Mathf.Clamp01(across));
                case ColorMode.GradientVertical:
                    return s.outlineGradient.Evaluate((y + 0.5f) / ch);
                case ColorMode.GradientHorizontal:
                    return s.outlineGradient.Evaluate((x + 0.5f) / cw);
                case ColorMode.GradientAngle:
                {
                    float rad = s.gradientAngle * Mathf.Deg2Rad;
                    float dx = Mathf.Cos(rad), dy = Mathf.Sin(rad);
                    float u = (x + 0.5f) / cw - 0.5f, v = (y + 0.5f) / ch - 0.5f;
                    float range = 0.5f * (Mathf.Abs(dx) + Mathf.Abs(dy));
                    return s.outlineGradient.Evaluate(Mathf.Clamp01((u * dx + v * dy) / (2f * range) + 0.5f));
                }
                default:
                    return s.outlineColor;
            }
        }

        static Color Over(Color src, Color dst)
        {
            float oa = src.a + dst.a * (1f - src.a);
            if (oa <= 1e-6f) return new Color(0f, 0f, 0f, 0f);
            float k = dst.a * (1f - src.a);
            return new Color(
                (src.r * src.a + dst.r * k) / oa,
                (src.g * src.a + dst.g * k) / oa,
                (src.b * src.a + dst.b * k) / oa,
                oa);
        }

        static bool IsEdge(bool[] solid, int x, int y, int w, int h)
        {
            if (x == 0 || y == 0 || x == w - 1 || y == h - 1) return true;
            int i = y * w + x;
            return !solid[i - 1] || !solid[i + 1] || !solid[i - w] || !solid[i + w];
        }

        static float Dist(int x0, int y0, int x1, int y1, OutlineShape m)
        {
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            switch (m)
            {
                case OutlineShape.Square: return Mathf.Max(dx, dy);
                case OutlineShape.Diamond: return dx + dy;
                default: return Mathf.Sqrt(dx * dx + dy * dy);
            }
        }

        // Jump Flooding: for every pixel finds the nearest seed pixel (index), limited to ~range
        static int[] JumpFlood(bool[] seed, int w, int h, int range, OutlineShape metric)
        {
            int n = w * h;
            var cur = new int[n];
            var nxt = new int[n];
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                cur[i] = seed[i] ? i : -1;
                any |= seed[i];
            }
            if (!any) return cur;

            int step = Mathf.NextPowerOfTwo(Mathf.Clamp(range, 1, Mathf.Max(w, h)));
            while (true)
            {
                JumpPass(cur, nxt, w, h, step, metric);
                var tmp = cur; cur = nxt; nxt = tmp;
                if (step == 1) break;
                step >>= 1;
            }
            JumpPass(cur, nxt, w, h, 1, metric); // extra pass improves accuracy
            return nxt;
        }

        static void JumpPass(int[] src, int[] dst, int w, int h, int step, OutlineShape metric)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                int best = src[i];
                float bd = best >= 0 ? Dist(x, y, best % w, best / w, metric) : float.MaxValue;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = y + dy * step;
                    if (ny < 0 || ny >= h) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx * step;
                        if (nx < 0 || nx >= w) continue;
                        int c = src[ny * w + nx];
                        if (c < 0 || c == best) continue;
                        float d = Dist(x, y, c % w, c / w, metric);
                        if (d < bd) { bd = d; best = c; }
                    }
                }
                dst[i] = best;
            }
        }

        // ------------------------------------------------------------------ Masks

        // Returns a mask over the whole (padded) canvas: 1 = effects blocked, 0 = allowed. Null = no mask.
        static float[] BuildMask(Settings s, MaskInput mi, Color[] col, float[] a, int cw, int ch, int W, int H, int padL, int padB)
        {
            if (s.maskType == MaskType.None) return null;
            if (s.maskType == MaskType.Brush && mi.brush == null) return null;
            if (s.maskType == MaskType.TextureFile && mi.tex == null) return null;

            int n = cw * ch;
            var m = new float[n];
            float span = 1f + 2f * BrushMargin;

            for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int i = y * cw + x;
                float u = (x - padL + 0.5f) / W;            // 0..1 over the source image, from the left
                float vTop = 1f - (y - padB + 0.5f) / H;    // 0..1 over the source image, from the top
                float v = 0f;

                switch (s.maskType)
                {
                    case MaskType.Brush:
                    {
                        int bx = Mathf.FloorToInt((u + BrushMargin) / span * mi.brushW);
                        int by = Mathf.FloorToInt((vTop + BrushMargin) / span * mi.brushH);
                        bx = Mathf.Clamp(bx, 0, mi.brushW - 1);
                        by = Mathf.Clamp(by, 0, mi.brushH - 1);
                        v = mi.brush[by * mi.brushW + bx] / 255f;
                        break;
                    }

                    case MaskType.Shape:
                    {
                        float px = (u - s.shapeCenter.x) * W;
                        float py = (vTop - s.shapeCenter.y) * H;
                        float rad = -s.shapeRotation * Mathf.Deg2Rad;
                        float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
                        float rx = px * cs - py * sn, ry = px * sn + py * cs;
                        float hx = Mathf.Max(0.5f, s.shapeSize.x * W * 0.5f);
                        float hy = Mathf.Max(0.5f, s.shapeSize.y * H * 0.5f);
                        float sd = s.shapeKind == ShapeKind.Rectangle
                            ? Mathf.Max(Mathf.Abs(rx) - hx, Mathf.Abs(ry) - hy)
                            : (Mathf.Sqrt((rx / hx) * (rx / hx) + (ry / hy) * (ry / hy)) - 1f) * Mathf.Min(hx, hy);
                        v = s.shapeFeather > 0f
                            ? 1f - Mathf.SmoothStep(0f, 1f, sd / s.shapeFeather + 0.5f)
                            : (sd <= 0f ? 1f : 0f);
                        break;
                    }

                    case MaskType.Sides:
                    {
                        float f = Mathf.Max(0.0001f, s.sideFeather);
                        if (s.sideTop > 0f) v = Mathf.Max(v, SideMask(s.sideTop - vTop, f));
                        if (s.sideBottom > 0f) v = Mathf.Max(v, SideMask(vTop - (1f - s.sideBottom), f));
                        if (s.sideLeft > 0f) v = Mathf.Max(v, SideMask(s.sideLeft - u, f));
                        if (s.sideRight > 0f) v = Mathf.Max(v, SideMask(u - (1f - s.sideRight), f));
                        break;
                    }

                    case MaskType.EdgeDirection:
                    {
                        // Sobel on alpha; the outward normal points where alpha decreases
                        float gx = A(a, x + 1, y - 1, cw, ch) + 2f * A(a, x + 1, y, cw, ch) + A(a, x + 1, y + 1, cw, ch)
                                 - A(a, x - 1, y - 1, cw, ch) - 2f * A(a, x - 1, y, cw, ch) - A(a, x - 1, y + 1, cw, ch);
                        float gy = A(a, x - 1, y + 1, cw, ch) + 2f * A(a, x, y + 1, cw, ch) + A(a, x + 1, y + 1, cw, ch)
                                 - A(a, x - 1, y - 1, cw, ch) - 2f * A(a, x, y - 1, cw, ch) - A(a, x + 1, y - 1, cw, ch);
                        float mag = Mathf.Sqrt(gx * gx + gy * gy);
                        if (mag < 0.01f) break;
                        float ang = Mathf.Atan2(-gy, -gx) * Mathf.Rad2Deg;
                        float diff = Mathf.Abs(Mathf.DeltaAngle(ang, s.dirAngle));
                        v = 1f - Mathf.SmoothStep(0f, 1f, (diff - s.dirSpread) / Mathf.Max(1f, s.dirFeather));
                        break;
                    }

                    case MaskType.ColorPick:
                    {
                        if (a[i] <= 0f) break;
                        var c = col[i];
                        float dr = c.r - s.maskColor.r, dg = c.g - s.maskColor.g, db = c.b - s.maskColor.b;
                        float dist = Mathf.Sqrt((dr * dr + dg * dg + db * db) / 3f);
                        v = 1f - Mathf.SmoothStep(0f, 1f, (dist - s.colorTolerance) / Mathf.Max(0.0001f, s.colorFeather));
                        break;
                    }

                    case MaskType.TextureFile:
                    {
                        int tx = Mathf.Clamp(Mathf.FloorToInt(u * mi.texW), 0, mi.texW - 1);
                        int ty = Mathf.Clamp(Mathf.FloorToInt((1f - vTop) * mi.texH), 0, mi.texH - 1);
                        v = Channel(mi.tex[ty * mi.texW + tx], s.maskChannel);
                        break;
                    }
                }

                m[i] = s.maskInvert ? 1f - v : v;
            }
            return m;
        }

        static float SideMask(float inside, float feather) => Mathf.SmoothStep(0f, 1f, inside / feather + 0.5f);

        static float A(float[] a, int x, int y, int w, int h)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return 0f;
            return a[y * w + x];
        }

        static float Channel(Color32 c, TexChannel ch)
        {
            switch (ch)
            {
                case TexChannel.Red: return c.r / 255f;
                case TexChannel.Green: return c.g / 255f;
                case TexChannel.Blue: return c.b / 255f;
                case TexChannel.Alpha: return c.a / 255f;
                default: return (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
            }
        }

        // ------------------------------------------------------------------ Processing

        struct Job
        {
            public string src, dst;
            public bool isNew;
            public int w, h;
            public BakeResult res;
        }

        void Process()
        {
            var mi = GetMaskInput();
            var jobs = new List<Job>();
            int skipped = 0;
            string suffix = string.IsNullOrWhiteSpace(s.suffix) ? "_fx" : s.suffix.Trim();

            try
            {
                for (int i = 0; i < textures.Count; i++)
                {
                    var tex = textures[i];
                    string path = AssetDatabase.GetAssetPath(tex);
                    if (EditorUtility.DisplayCancelableProgressBar("Outline & Shadow Baker", path, (float)i / textures.Count))
                        break;

                    if (!TryLoadPixels(path, tex, out var px, out int w, out int h))
                    {
                        Debug.LogWarning(LogPrefix + $"Could not read pixels: {path}", tex);
                        skipped++;
                        continue;
                    }

                    var res = Bake(px, w, h, s, mi);

                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    bool overwrite = s.output == OutputMode.Overwrite && ext == ".png";
                    if (s.output == OutputMode.Overwrite && !overwrite)
                        Debug.LogWarning(LogPrefix + $"Only PNG can be overwritten, saved as a duplicate instead: {path}", tex);

                    string dst = overwrite
                        ? path
                        : Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + suffix + ".png").Replace('\\', '/');

                    bool isNew = !File.Exists(dst);
                    File.WriteAllBytes(dst, EncodePng(res));
                    jobs.Add(new Job { src = path, dst = dst, isNew = isNew, w = w, h = h, res = res });
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
                ApplySpriteLayout(job.dst == job.src ? job.dst : job.src, job.dst, job.w, job.h, job.res, s.keepPivot);
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

            Debug.Log(LogPrefix + $"Done: baked {jobs.Count} file(s)" + (skipped > 0 ? $", skipped {skipped}" : "") + ".");
        }

        static byte[] EncodePng(BakeResult r)
        {
            var t = new Texture2D(r.w, r.h, TextureFormat.RGBA32, false);
            t.SetPixels32(r.pixels);
            byte[] bytes = t.EncodeToPNG();
            DestroyImmediate(t);
            return bytes;
        }

        // Keeps sprite pivot (single) or sprite rects (multiple) aligned with the art after the canvas grew
        static void ApplySpriteLayout(string fromPath, string dstPath, int W, int H, BakeResult r, bool keepPivot)
        {
            var from = AssetImporter.GetAtPath(fromPath) as TextureImporter;
            var dst = AssetImporter.GetAtPath(dstPath) as TextureImporter;
            if (from == null || dst == null || dst.textureType != TextureImporterType.Sprite) return;

            bool sameFile = fromPath == dstPath;
            bool shifted = r.padL != 0 || r.padB != 0 || r.w != W || r.h != H;
            if (sameFile && !shifted) return;

            var fs = new TextureImporterSettings();
            from.ReadTextureSettings(fs);
            bool changed = false;

            if (fs.spriteMode == (int)SpriteImportMode.Multiple)
            {
#pragma warning disable 618
                var sheet = from.spritesheet;
                for (int i = 0; i < sheet.Length; i++)
                {
                    var md = sheet[i];
                    md.rect = new Rect(md.rect.x + r.padL, md.rect.y + r.padB, md.rect.width, md.rect.height);
                    sheet[i] = md;
                }
                dst.spritesheet = sheet;
#pragma warning restore 618
                changed = true;
            }
            else if (keepPivot && shifted)
            {
                Vector2 p = fs.spriteAlignment == (int)SpriteAlignment.Custom
                    ? fs.spritePivot
                    : AlignmentToPivot((SpriteAlignment)fs.spriteAlignment);

                var ds = new TextureImporterSettings();
                dst.ReadTextureSettings(ds);
                ds.spriteAlignment = (int)SpriteAlignment.Custom;
                ds.spritePivot = new Vector2((p.x * W + r.padL) / r.w, (p.y * H + r.padB) / r.h);
                dst.SetTextureSettings(ds);
                changed = true;
            }

            if (changed) dst.SaveAndReimport();
        }

        static Vector2 AlignmentToPivot(SpriteAlignment a)
        {
            switch (a)
            {
                case SpriteAlignment.TopLeft: return new Vector2(0f, 1f);
                case SpriteAlignment.TopCenter: return new Vector2(0.5f, 1f);
                case SpriteAlignment.TopRight: return new Vector2(1f, 1f);
                case SpriteAlignment.LeftCenter: return new Vector2(0f, 0.5f);
                case SpriteAlignment.RightCenter: return new Vector2(1f, 0.5f);
                case SpriteAlignment.BottomLeft: return new Vector2(0f, 0f);
                case SpriteAlignment.BottomCenter: return new Vector2(0.5f, 0f);
                case SpriteAlignment.BottomRight: return new Vector2(1f, 0f);
                default: return new Vector2(0.5f, 0.5f);
            }
        }

        static void CopyImportSettings(string srcPath, string dstPath)
        {
            var src = AssetImporter.GetAtPath(srcPath) as TextureImporter;
            var dst = AssetImporter.GetAtPath(dstPath) as TextureImporter;
            if (src == null || dst == null) return;

            var settings = new TextureImporterSettings();
            src.ReadTextureSettings(settings);
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

            // PNG/JPG are read straight from disk: full resolution, no compression, Read/Write not required
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

            // Other formats (PSD, TGA, ...) go through the GPU from the imported texture (limited by Max Size)
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

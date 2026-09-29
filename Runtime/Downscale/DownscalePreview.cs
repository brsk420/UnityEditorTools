using System.Collections.Generic;
using UnityEngine;

namespace _Brsk420.Runtime
{
    /// <summary>
    /// Editor-only preview of how a sprite will look AFTER being processed by the
    /// Downscale Tool (Assets/_BrskTools/Textures/Downscale). It does NOT modify the
    /// source texture/asset — it only "шакалит" the visual on the SpriteRenderer so you
    /// can eyeball the quality loss before actually downscaling the PNG.
    ///
    /// IMPORTANT: it never touches SpriteRenderer.sprite. Instead it overrides the
    /// material's _MainTex via a MaterialPropertyBlock with a downscaled-then-upscaled
    /// copy of the current sprite's texture. That way:
    ///   - the sprite field in the inspector still points at the real asset (clicking it
    ///     pings the asset in the Project window),
    ///   - sprite-sequence animation keeps working (we re-apply per frame),
    ///   - nothing in the renderer ever ends up as a destroyed/"missing" reference.
    ///
    /// Drop it on a GameObject that has a SpriteRenderer, then drag the Quality slider.
    /// 100% = original. Lower values simulate the downscale (same bilinear Blit path the
    /// real tool uses), then upscale back to the original size to keep on-screen size
    /// constant. For Sprite Atlas entries the whole atlas page is downscaled, which only
    /// approximates the per-PNG tool.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    [DisallowMultipleComponent]
    public class DownscalePreview : MonoBehaviour
    {
        [Tooltip("Quality after downscale. 100% = original, 50% = like 'Downscale textures (-50%)'.")]
        [Range(1, 100)]
        [SerializeField] private int _quality = 100;

#if UNITY_EDITOR
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private SpriteRenderer _renderer;
        private MaterialPropertyBlock _block;

        // Cache: source texture -> downscaled-then-upscaled copy for the current quality.
        private readonly Dictionary<Texture2D, Texture2D> _cache = new Dictionary<Texture2D, Texture2D>();

        private int _cachedQuality = -1;

        // True while we have a _MainTex override pushed onto the renderer.
        private bool _overrideActive;

        public int Quality
        {
            get => _quality;
            set => _quality = Mathf.Clamp(value, 1, 100);
        }

        private void OnEnable()
        {
            _renderer = GetComponent<SpriteRenderer>();
            UnityEditor.EditorApplication.update -= Tick;
            UnityEditor.EditorApplication.update += Tick;
            Tick();
        }

        private void OnDisable()
        {
            UnityEditor.EditorApplication.update -= Tick;
            ClearOverride();
            ClearCache();
        }

        private void OnDestroy()
        {
            UnityEditor.EditorApplication.update -= Tick;
            ClearOverride();
            ClearCache();
        }

        private void LateUpdate()
        {
            // In play mode the Animator writes the sprite during the frame update;
            // LateUpdate runs after that, so we re-evaluate here too (Tick is cheap and
            // cached). EditorApplication.update covers edit-mode scrubbing/preview.
            Tick();
        }

        private void OnValidate()
        {
            // Tick (EditorApplication.update) notices the quality change on its own; here we
            // only make sure the edit-mode loop runs so it happens right away. Rebuilding from
            // a delayCall used to destroy the texture that was still bound to the renderer.
            if (_cachedQuality != _quality)
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            }
        }

        /// <summary>
        /// Runs every editor frame. Keeps the renderer's _MainTex override in sync with the
        /// downscaled version of whatever sprite is currently shown (static or animated).
        /// </summary>
        private void Tick()
        {
            if (this == null)
            {
                return;
            }

            if (_renderer == null)
            {
                _renderer = GetComponent<SpriteRenderer>();
                if (_renderer == null)
                {
                    return;
                }
            }

            // Quality changed -> cached frames are stale. They're destroyed only AFTER the
            // renderer has been switched to the new texture: destroying the one that's still
            // bound leaves the renderer sampling a dead texture until the prefab is reopened.
            List<Texture2D> stale = null;

            if (_cachedQuality != _quality)
            {
                stale = new List<Texture2D>(_cache.Values);
                _cache.Clear();
                _cachedQuality = _quality;
            }

            try
            {
                // At full quality (or when there's no sprite/texture), make sure no override
                // is left on the renderer and bail.
                var sprite = _renderer.sprite;
                var sourceTexture = sprite != null ? GetRenderedTexture(sprite) : null;

                if (_quality >= 100 || sourceTexture == null)
                {
                    ClearOverride();
                    return;
                }

                var downscaled = GetOrBuildPreviewTexture(sourceTexture, _quality);
                if (downscaled == null)
                {
                    ClearOverride();
                    return;
                }

                ApplyOverride(downscaled);
            }
            finally
            {
                if (stale != null)
                {
                    foreach (var texture in stale)
                    {
                        if (texture != null)
                        {
                            DestroyImmediate(texture);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The texture the renderer actually samples. With Sprite Atlas enabled in the editor
        /// sprite.texture is still the source PNG while the mesh UVs point into the atlas, so
        /// overriding _MainTex with a source-sized copy would scramble the sprite.
        /// </summary>
        private static Texture2D GetRenderedTexture(Sprite sprite)
        {
            // getAtlasData: true throws for sprites that aren't in an atlas.
            return sprite.packed
                ? UnityEditor.Sprites.SpriteUtility.GetSpriteTexture(sprite, true)
                : sprite.texture;
        }

        private void ApplyOverride(Texture2D texture)
        {
            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);

            bool changed = !_overrideActive || _block.GetTexture(MainTexId) != texture;

            if (changed)
            {
                // Round-tripping the block (Get -> Set) keeps a reference to the previous preview
                // texture somewhere inside SpriteRenderer (Unity 6): update/Layout see the new
                // _MainTex, but the Repaint pass samples the old, already destroyed one -> grey
                // sprite. Dropping the block first and starting from a clean one avoids that.
                _renderer.SetPropertyBlock(null);
                _renderer.GetPropertyBlock(_block);
            }

            _block.SetTexture(MainTexId, texture);
            _renderer.SetPropertyBlock(_block);
            _overrideActive = true;

            if (changed)
            {
                // A property block change doesn't repaint the Scene view by itself.
                UnityEditor.SceneView.RepaintAll();
            }
        }

        private void ClearOverride()
        {
            if (!_overrideActive || _renderer == null)
            {
                _overrideActive = false;
                return;
            }

            // MaterialPropertyBlock can't drop a single property, so this clears the whole block.
            _renderer.SetPropertyBlock(null);
            _overrideActive = false;
            UnityEditor.SceneView.RepaintAll();
        }

        private Texture2D GetOrBuildPreviewTexture(Texture2D source, int quality)
        {
            if (_cache.TryGetValue(source, out var cached) && cached != null)
            {
                return cached;
            }

            var generated = BuildTexture(source, quality);
            if (generated != null)
            {
                _cache[source] = generated;
            }

            return generated;
        }

        private Texture2D BuildTexture(Texture2D source, int quality)
        {
            return BuildTextureFromFile(source, quality) ?? BuildTextureOnGpu(source, quality);
        }

        /// <summary>
        /// Reads the source PNG from disk (exactly what DownscaleTool reads) and resamples it
        /// on the CPU. Blit+ReadPixels from EditorApplication.update came back grey, so the GPU
        /// path is only a fallback for textures without a PNG behind them (atlas pages).
        /// </summary>
        private static Texture2D BuildTextureFromFile(Texture2D source, int quality)
        {
            var path = UnityEditor.AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            Color32[] srcPixels;
            int srcWidth;
            int srcHeight;

            try
            {
                if (!PngLoader.TryLoad(decoded, System.IO.File.ReadAllBytes(path), path))
                {
                    return null;
                }

                srcPixels = decoded.GetPixels32();
                srcWidth = decoded.width;
                srcHeight = decoded.height;
            }
            finally
            {
                DestroyImmediate(decoded);
            }

            // Same math as DownscaleTool.Downscale(path, multiplier).
            int downWidth = Mathf.Max(1, srcWidth * quality / 100);
            int downHeight = Mathf.Max(1, srcHeight * quality / 100);

            var downPixels = ResampleBilinear(srcPixels, srcWidth, srcHeight, downWidth, downHeight);
            var upPixels = ResampleBilinear(downPixels, downWidth, downHeight, srcWidth, srcHeight);

            // Sprite UVs are normalized, so a PNG-sized texture fits even if the importer
            // shrank the texture (Max Size).
            var previewTexture = new Texture2D(srcWidth, srcHeight, TextureFormat.RGBA32, false, false)
            {
                name = $"{source.name}_DownscalePreview_{quality}",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = source.filterMode,
                wrapMode = source.wrapMode
            };
            previewTexture.SetPixels32(upPixels);
            previewTexture.Apply();

            return previewTexture;
        }

        /// <summary>
        /// One bilinear tap per destination pixel centre, clamped at the edges — the same
        /// sampling a single Graphics.Blit into a smaller/larger target does.
        /// </summary>
        private static Color32[] ResampleBilinear(Color32[] src, int srcWidth, int srcHeight, int dstWidth, int dstHeight)
        {
            var dst = new Color32[dstWidth * dstHeight];

            for (int y = 0; y < dstHeight; y++)
            {
                float sy = Mathf.Clamp((y + 0.5f) * srcHeight / dstHeight - 0.5f, 0f, srcHeight - 1);
                int y0 = (int)sy;
                int y1 = Mathf.Min(y0 + 1, srcHeight - 1);
                float ty = sy - y0;

                for (int x = 0; x < dstWidth; x++)
                {
                    float sx = Mathf.Clamp((x + 0.5f) * srcWidth / dstWidth - 0.5f, 0f, srcWidth - 1);
                    int x0 = (int)sx;
                    int x1 = Mathf.Min(x0 + 1, srcWidth - 1);
                    float tx = sx - x0;

                    var top = Color32.Lerp(src[y0 * srcWidth + x0], src[y0 * srcWidth + x1], tx);
                    var bottom = Color32.Lerp(src[y1 * srcWidth + x0], src[y1 * srcWidth + x1], tx);
                    dst[y * dstWidth + x] = Color32.Lerp(top, bottom, ty);
                }
            }

            return dst;
        }

        private static Texture2D BuildTextureOnGpu(Texture2D source, int quality)
        {
            int srcWidth = source.width;
            int srcHeight = source.height;

            // Same math as DownscaleTool.Downscale(path, multiplier).
            int downWidth = Mathf.Max(1, srcWidth * quality / 100);
            int downHeight = Mathf.Max(1, srcHeight * quality / 100);

            var previousActive = RenderTexture.active;

            // Step 1: downscale (loses detail, exactly like the real tool's Blit).
            // Temporary RTs come from a pool with old contents, so clear them first: if the
            // Blit gets clipped by leftover editor GL state, garbage must not leak through.
            var downRt = RenderTexture.GetTemporary(downWidth, downHeight, 0, RenderTextureFormat.ARGB32);
            downRt.filterMode = FilterMode.Bilinear;
            ClearTarget(downRt);
            Graphics.Blit(source, downRt);

            // Step 2: upscale back to original size so on-screen size / UVs stay the same,
            // while the lost detail (the "шакал" effect) remains visible.
            var upRt = RenderTexture.GetTemporary(srcWidth, srcHeight, 0, RenderTextureFormat.ARGB32);
            upRt.filterMode = FilterMode.Bilinear;
            ClearTarget(upRt);
            Graphics.Blit(downRt, upRt);

            RenderTexture.active = upRt;

            var previewTexture = new Texture2D(srcWidth, srcHeight, TextureFormat.RGBA32, false, false)
            {
                name = $"{source.name}_DownscalePreview_{quality}",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = source.filterMode,
                wrapMode = source.wrapMode
            };
            previewTexture.ReadPixels(new Rect(0, 0, srcWidth, srcHeight), 0, 0);
            previewTexture.Apply();

            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(downRt);
            RenderTexture.ReleaseTemporary(upRt);

            return previewTexture;
        }

        private static void ClearTarget(RenderTexture target)
        {
            RenderTexture.active = target;
            GL.Viewport(new Rect(0, 0, target.width, target.height));
            GL.Clear(true, true, Color.clear);
        }

        private void ClearCache()
        {
            foreach (var texture in _cache.Values)
            {
                if (texture != null)
                {
                    DestroyImmediate(texture);
                }
            }

            _cache.Clear();
        }
#endif
    }
}

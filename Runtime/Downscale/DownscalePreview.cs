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
    /// constant. Intended for single-texture sprites (not Sprite Atlas entries).
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
            // Quality changed -> previously cached frames are stale.
            if (_cachedQuality != _quality)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this == null)
                    {
                        return;
                    }

                    ClearCache();
                    Tick();
                };
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

            if (_cachedQuality != _quality)
            {
                ClearCache();
                _cachedQuality = _quality;
            }

            // At full quality (or when there's no sprite/texture), make sure no override
            // is left on the renderer and bail.
            var sprite = _renderer.sprite;
            var sourceTexture = sprite != null ? sprite.texture : null;

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

        private void ApplyOverride(Texture2D texture)
        {
            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetTexture(MainTexId, texture);
            _renderer.SetPropertyBlock(_block);
            _overrideActive = true;
        }

        private void ClearOverride()
        {
            if (!_overrideActive || _renderer == null)
            {
                _overrideActive = false;
                return;
            }

            // Remove just our _MainTex override; keep any other property-block data intact.
            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.Clear();
            _renderer.SetPropertyBlock(_block);
            _overrideActive = false;
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
            int srcWidth = source.width;
            int srcHeight = source.height;

            // Same math as DownscaleTool.Downscale(path, multiplier).
            int downWidth = Mathf.Max(1, srcWidth * quality / 100);
            int downHeight = Mathf.Max(1, srcHeight * quality / 100);

            var previousActive = RenderTexture.active;

            // Step 1: downscale (loses detail, exactly like the real tool's Blit).
            var downRt = RenderTexture.GetTemporary(downWidth, downHeight, 0, RenderTextureFormat.ARGB32);
            downRt.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, downRt);

            // Step 2: upscale back to original size so on-screen size / UVs stay the same,
            // while the lost detail (the "шакал" effect) remains visible.
            var upRt = RenderTexture.GetTemporary(srcWidth, srcHeight, 0, RenderTextureFormat.ARGB32);
            upRt.filterMode = FilterMode.Bilinear;
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

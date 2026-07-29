using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Sprites;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

public class SpriteAtlasViewer : EditorWindow
{
    // ---- data -------------------------------------------------------
    private SpriteAtlas _atlas;
    private Texture2D[] _pages;
    private PageData[]  _pageData;
    private int         _currentPage;
    private int         _selectedSprite = -1;

    // ---- view -------------------------------------------------------
    private Vector2 _panOffset;   // manual pan (middle-mouse drag)
    private Vector2 _tabsScroll;
    private bool    _needsPack;
    private bool    _isPanning;
    private float   _zoom    = 1f;
    private const float ZoomStep = 0.15f;
    private const float ZoomMin  = 0.05f;
    private const float ZoomMax  = 8f;
    private const float ToolbarH = 18f;
    private const float TabsW    = 96f;   // right sidebar width
    private const float TabBtnH  = 18f;  // height of each tab button
    private const float FooterH  = 22f;
    private const float PagePad  = 8f;

    private struct SpriteEntry
    {
        public string Name;
        public Rect   TexRect;   // pixels, bottom-left origin
        public Sprite Original;
    }

    private class PageData
    {
        public List<SpriteEntry> Sprites = new List<SpriteEntry>();
    }

    // Session cache: atlas GUID → page data (avoids re-packing every open)
    private static readonly Dictionary<string, PageData[]> s_PageDataCache =
        new Dictionary<string, PageData[]>();

    // Atlases where the repack fallback (Approach C) already failed this session —
    // skip retrying it automatically (it's slow and some Unity versions show a
    // blocking native "Error!" dialog for oversized 9-slice meshes on every repack).
    private static readonly HashSet<string> s_RepackFailed = new HashSet<string>();

    // ================================================================= lifecycle

    [OnOpenAsset]
    public static bool OnOpenAsset(int instanceId, int line)
    {
        var obj = EditorUtility.InstanceIDToObject(instanceId);
        if (obj is SpriteAtlas atlas) { Open(atlas); return true; }
        return false;
    }

    public static void Open(SpriteAtlas atlas)
    {
        var w = GetWindow<SpriteAtlasViewer>("Atlas Viewer");
        w.minSize = new Vector2(400, 300);
        w.LoadAtlas(atlas);
        w.Show();
    }

    // ================================================================= loading

    private void LoadAtlas(SpriteAtlas atlas, bool forceRepack = false)
    {
        _atlas          = atlas;
        _pages          = null;
        _pageData       = null;
        _currentPage    = 0;
        _selectedSprite = -1;
        _panOffset      = Vector2.zero;
        _needsPack      = false;

        if (_atlas == null) return;

        try
        {
            LoadAtlasInternal(_atlas, forceRepack);
        }
        catch (System.Exception ex)
        {
            // Never let an exception escape LoadAtlas — it can be called mid-OnGUI
            // (toolbar buttons), and an uncaught throw there corrupts IMGUI's
            // GUILayout Begin/End stack for the rest of the window's life.
            Debug.LogWarning($"[Atlas Viewer] Failed to load '{_atlas?.name}': {ex.Message}");
            _needsPack = true;
            titleContent = new GUIContent($"Atlas: {_atlas?.name}  (load failed)");
        }
    }

    private void LoadAtlasInternal(SpriteAtlas atlas, bool forceRepack = false)
    {
        string atlasPath = AssetDatabase.GetAssetPath(atlas);
        string atlasGuid = AssetDatabase.AssetPathToGUID(atlasPath);
        var originals    = BuildOriginalMap();

        if (forceRepack) s_RepackFailed.Remove(atlasGuid);

        // --- step 1: load display pages (always fresh) ------------------
        Texture2D[] pages = LoadAtlasPages(atlas, atlasPath);

        if (pages == null || pages.Length == 0)
        {
            _needsPack = true;
            titleContent = new GUIContent($"Atlas: {atlas.name}");
            return;
        }

        _pages = pages;

        // --- step 2: use cached page data if available ------------------
        if (s_PageDataCache.TryGetValue(atlasGuid, out PageData[] cached)
            && cached.Length == pages.Length)
        {
            _pageData = cached;
            titleContent = new GUIContent(
                $"Atlas: {atlas.name}  ({atlas.spriteCount} sprites / {_pages.Length} page{(_pages.Length > 1 ? "s" : "")})");
            return;
        }

        // --- step 3: build sprite→page mapping --------------------------
        _pageData = new PageData[pages.Length];
        for (int i = 0; i < pages.Length; i++) _pageData[i] = new PageData();

        Dictionary<int, int>    instToPage = BuildInstToPage(pages);
        Dictionary<string, int> nameToPage = BuildNameToPage(pages);
        Sprite[] packed = GetPackedSpritesReflect(atlas);

        int expectedSprites = packed?.Length ?? 0;
        Debug.Log($"[AV] pages={pages.Length}  page[0].name={pages[0]?.name}  packed={expectedSprites}");

        // Approach A: sprite.texture directly matches a loaded page (Mac/DXT5 projects)
        if (packed != null)
            PopulateFromPacked(packed, instToPage, nameToPage, originals, useSpriteTexRect: true);

        // Approach B: GetSpriteTexture(true) + GetSpriteUVs(true) — no repacking needed.
        // Works only for sprites currently loaded in memory (unreliable for iOS/ASTC).
        if (packed != null)
            TryPopulateViaGetSpriteTexture(packed, instToPage, nameToPage, originals);

        int foundAfterAB = _pageData.Sum(pd => pd.Sprites.Count);
        // Need at least 90% of expected sprites — if not, must repack
        bool anyPopulated = expectedSprites > 0 && foundAfterAB >= expectedSprites * 0.9f;
        Debug.Log($"[AV] after A+B: found={foundAfterAB}/{expectedSprites}  sufficient={anyPopulated}");

        // Approach C: iOS/ASTC format — temporarily pack as StandaloneOSX, extract data, restore iOS.
        // Result is cached so this only runs once per session per atlas. Skip it entirely if it
        // already failed for this atlas this session — it's slow and some Unity versions pop a
        // blocking native "Error!" dialog for oversized 9-slice meshes on every repack attempt.
        if (!anyPopulated && s_RepackFailed.Contains(atlasGuid))
        {
            Debug.Log($"[AV] skipping repack — already known to fail for this atlas this session");
        }
        else if (!anyPopulated)
        {
            BuildTarget origTarget = GuessPackTarget(pages);
            Debug.Log($"[AV] C: origTarget={origTarget}  repacking as StandaloneOSX…");

            EditorUtility.DisplayProgressBar("Atlas Viewer", "Packing preview (mac)…", 0.3f);
            try   { SpriteAtlasUtility.PackAtlases(new[] { atlas }, BuildTarget.StandaloneOSX); AssetDatabase.Refresh(); }
            finally { EditorUtility.ClearProgressBar(); }

            var macPages   = LoadAtlasPages(atlas, atlasPath);
            var macInst    = BuildInstToPage(macPages);
            var macName    = BuildNameToPage(macPages);
            packed         = GetPackedSpritesReflect(atlas);
            Debug.Log($"[AV] C after repack: macPages={macPages?.Length}  macPage[0]={macPages?[0]?.name}  packed={packed?.Length}");

            anyPopulated   = PopulateFromPacked(packed, macInst, macName, originals, useSpriteTexRect: true);
            Debug.Log($"[AV] C after PopulateFromPacked: anyPopulated={anyPopulated}");
            if (!anyPopulated && packed != null)
                anyPopulated = TryPopulateViaGetSpriteTexture(packed, macInst, macName, originals);
            Debug.Log($"[AV] C after GetSpriteTexture: anyPopulated={anyPopulated}");

            // Restore original format (iOS/ASTC) immediately
            if (origTarget != BuildTarget.StandaloneOSX)
            {
                EditorUtility.DisplayProgressBar("Atlas Viewer", "Restoring atlas format…", 0.7f);
                try   { SpriteAtlasUtility.PackAtlases(new[] { atlas }, origTarget); AssetDatabase.Refresh(); }
                finally { EditorUtility.ClearProgressBar(); }
                _pages = LoadAtlasPages(atlas, atlasPath) ?? _pages;
                var restoredInst = BuildInstToPage(_pages);
                var restoredName = BuildNameToPage(_pages);
                RemapPageData(_pageData, macPages, restoredInst, restoredName);
            }
        }

        if (!anyPopulated)
        {
            s_RepackFailed.Add(atlasGuid);
            _needsPack = true;
            titleContent = new GUIContent($"Atlas: {atlas.name}  (pack failed)");
            return;
        }

        // Cache so subsequent opens don't re-pack
        s_PageDataCache[atlasGuid] = _pageData;

        titleContent = new GUIContent(
            $"Atlas: {atlas.name}  ({atlas.spriteCount} sprites / {_pages.Length} page{(_pages.Length > 1 ? "s" : "")})");
    }

    // Clear cache for a specific atlas (e.g. after manual Refresh)
    private void InvalidateCache()
    {
        if (_atlas == null) return;
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_atlas));
        s_PageDataCache.Remove(guid);
    }

    // ================================================================= static helpers

    private static Texture2D[] LoadAtlasPages(SpriteAtlas atlas, string atlasPath)
    {
        var list = new List<Texture2D>();

        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(atlasPath))
            if (a is Texture2D t) list.Add(t);

        var refl = ReflectTextures(atlas, "GetPreviewTextures");
        if (refl != null)
            foreach (var t in refl)
                if (t != null && !list.Contains(t)) list.Add(t);

        return list.Count > 0 ? list.ToArray() : null;
    }

    private static Dictionary<int, int> BuildInstToPage(Texture2D[] pages)
    {
        var d = new Dictionary<int, int>();
        if (pages == null) return d;
        for (int i = 0; i < pages.Length; i++)
            if (pages[i] != null) d[pages[i].GetInstanceID()] = i;
        return d;
    }

    // Name-based fallback: GetSpriteTexture(true) may return a different instance
    // than what LoadAllAssetsAtPath gave us, but the name is the same.
    private static Dictionary<string, int> BuildNameToPage(Texture2D[] pages)
    {
        var d = new Dictionary<string, int>();
        if (pages == null) return d;
        for (int i = 0; i < pages.Length; i++)
            if (pages[i] != null && !d.ContainsKey(pages[i].name)) d[pages[i].name] = i;
        return d;
    }

    private static Sprite[] GetPackedSpritesReflect(SpriteAtlas atlas)
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        var m     = typeof(SpriteAtlasExtensions).GetMethod("GetPackedSprites", flags);
        try { return m?.Invoke(null, new object[] { atlas }) as Sprite[]; }
        catch (TargetInvocationException) { return null; } // atlas has no packed data yet
        catch (System.ArgumentException) { return null; }
    }

    // Approach C: GetSpriteTexture(sprite,true) returns the real atlas page.
    // GetSpriteUVs(sprite,true) returns UVs in atlas page space → convert to pixel rect.
    private bool TryPopulateViaGetSpriteTexture(
        Sprite[] packed, Dictionary<int, int> instToPage, Dictionary<string, int> nameToPage,
        Dictionary<string, Sprite> originals)
    {
        var flags  = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var getUVs = typeof(SpriteUtility).GetMethod("GetSpriteUVs", flags, null,
                         new[] { typeof(Sprite), typeof(bool) }, null);

        bool any = false;
        foreach (var ps in packed)
        {
            if (ps == null) continue;

            Texture2D atlasTex;
            try { atlasTex = SpriteUtility.GetSpriteTexture(ps, true); }
            catch { continue; }
            if (atlasTex == null || atlasTex == ps.texture) continue;

            // Try instance ID first, fall back to texture name
            if (!instToPage.TryGetValue(atlasTex.GetInstanceID(), out int pi))
                if (!nameToPage.TryGetValue(atlasTex.name, out pi)) continue;

            Rect texRect;
            if (getUVs != null)
            {
                // UVs in atlas page space (bottom-left origin, 0-1)
                Vector2[] uvs;
                try { uvs = getUVs.Invoke(null, new object[] { ps, true }) as Vector2[]; }
                catch { continue; }
                if (uvs != null && uvs.Length > 0)
                {
                    float uMin = float.MaxValue, uMax = float.MinValue;
                    float vMin = float.MaxValue, vMax = float.MinValue;
                    foreach (var uv in uvs)
                    {
                        if (uv.x < uMin) uMin = uv.x; if (uv.x > uMax) uMax = uv.x;
                        if (uv.y < vMin) vMin = uv.y; if (uv.y > vMax) vMax = uv.y;
                    }
                    texRect = new Rect(
                        Mathf.Round(uMin * atlasTex.width),
                        Mathf.Round(vMin * atlasTex.height),
                        Mathf.Round((uMax - uMin) * atlasTex.width),
                        Mathf.Round((vMax - vMin) * atlasTex.height));
                }
                else texRect = ps.textureRect;
            }
            else texRect = ps.textureRect;

            _pageData[pi].Sprites.Add(MakeEntry(ps, texRect, originals));
            any = true;
        }
        return any;
    }

    private SpriteEntry MakeEntry(Sprite ps, Rect texRect, Dictionary<string, Sprite> originals)
    {
        originals.TryGetValue(ps.name, out Sprite orig);
        return new SpriteEntry { Name = ps.name, TexRect = texRect, Original = orig ?? ps };
    }

    private static bool IsAtlasBound(Sprite[] packed, Dictionary<int, int> instToPage)
    {
        if (packed == null || packed.Length == 0) return false;
        foreach (var s in packed)
            if (s?.texture != null && instToPage.ContainsKey(s.texture.GetInstanceID())) return true;
        return false;
    }

    private static Texture2D[] ReflectTextures(SpriteAtlas atlas, string methodName)
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        var m     = typeof(SpriteAtlasExtensions).GetMethod(methodName, flags);
        return m?.Invoke(null, new object[] { atlas }) as Texture2D[];
    }

    private void PackAndReload()
    {
        if (_atlas == null) return;
        EditorUtility.DisplayProgressBar("Atlas Viewer", "Repacking atlas…", 0.4f);
        try
        {
            SpriteAtlasUtility.CleanupAtlasPacking();
            SpriteAtlasUtility.PackAtlases(new[] { _atlas }, EditorUserBuildSettings.activeBuildTarget);
        }
        finally { EditorUtility.ClearProgressBar(); }
        InvalidateCache();
        LoadAtlas(_atlas, forceRepack: true);
    }

    // Guess original pack target from page texture format name
    private static BuildTarget GuessPackTarget(Texture2D[] pages)
    {
        string n = pages?.Length > 0 ? (pages[0]?.name ?? "") : "";
        if (n.Contains("ASTC"))                          return BuildTarget.iOS;
        if (n.Contains("ETC2") || n.Contains("ETC"))    return BuildTarget.Android;
        return BuildTarget.StandaloneOSX;
    }

    private bool PopulateFromPacked(Sprite[] packed, Dictionary<int,int> instToPage,
        Dictionary<string,int> nameToPage, Dictionary<string,Sprite> originals, bool useSpriteTexRect)
    {
        bool any = false;
        if (packed == null) return false;
        foreach (var ps in packed)
        {
            if (ps == null || ps.texture == null) continue;
            int pi;
            if (!instToPage.TryGetValue(ps.texture.GetInstanceID(), out pi))
                if (!nameToPage.TryGetValue(ps.texture.name, out pi)) continue;
            _pageData[pi].Sprites.Add(MakeEntry(ps, ps.textureRect, originals));
            any = true;
        }
        return any;
    }

    // After restoring ASTC pages, remap pageData indices from mac pages → restored pages by name
    private void RemapPageData(PageData[] data, Texture2D[] macPages,
        Dictionary<int,int> restoredInst, Dictionary<string,int> restoredName)
    {
        // Build macIndex → restoredIndex map by texture base name (strip format string)
        var remap = new Dictionary<int, int>();
        for (int i = 0; i < macPages.Length; i++)
        {
            if (macPages[i] == null) continue;
            // mac name: "sactx-0-2048x2048-DXT5|BC3 UNorm-AtlasName"
            // restored: "sactx-0-2048x2048-ASTC 6x6-AtlasName"
            // Common key: page index embedded as first number after "sactx-"
            int pageIdx = ParseSactxIndex(macPages[i].name);
            if (pageIdx < 0) { remap[i] = i; continue; }
            // Find restored page with same index
            for (int j = 0; j < _pages.Length; j++)
                if (_pages[j] != null && ParseSactxIndex(_pages[j].name) == pageIdx)
                    { remap[i] = j; break; }
        }

        // Re-sort sprites into new PageData array
        var newData = new PageData[_pages.Length];
        for (int i = 0; i < _pages.Length; i++) newData[i] = new PageData();
        for (int i = 0; i < data.Length; i++)
        {
            if (!remap.TryGetValue(i, out int j)) j = i;
            if (j < newData.Length) newData[j].Sprites.AddRange(data[i].Sprites);
        }
        for (int i = 0; i < _pages.Length; i++) _pageData[i] = newData[i];
    }

    private static int ParseSactxIndex(string name)
    {
        // "sactx-N-…" → N
        if (name == null || !name.StartsWith("sactx-")) return -1;
        int dash = name.IndexOf('-', 6);
        if (dash < 0) return -1;
        return int.TryParse(name.Substring(6, dash - 6), out int n) ? n : -1;
    }

    // ================================================================= original-sprite map

    private Dictionary<string, Sprite> BuildOriginalMap()
    {
        var map = new Dictionary<string, Sprite>();
        foreach (var p in SpriteAtlasExtensions.GetPackables(_atlas))
        {
            if (p == null) continue;
            string path = AssetDatabase.GetAssetPath(p);
            if      (p is Sprite s)     AddSprite(map, s);
            else if (p is Texture2D)    CollectFrom(path, map);
            else if (p is DefaultAsset) // folder
                foreach (string g in AssetDatabase.FindAssets("t:Sprite", new[] { path }))
                    CollectFrom(AssetDatabase.GUIDToAssetPath(g), map);
        }
        return map;
    }

    private static void CollectFrom(string path, Dictionary<string, Sprite> map)
    {
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
            if (a is Sprite s) AddSprite(map, s);
    }

    private static void AddSprite(Dictionary<string, Sprite> map, Sprite s)
    {
        if (s != null && !map.ContainsKey(s.name)) map[s.name] = s;
    }

    // ================================================================= GUI

    private void OnGUI()
    {
        float winW = position.width;
        float winH = position.height;

        DrawToolbar(new Rect(0, 0, winW, ToolbarH));

        if (_atlas == null || _pages == null || _pages.Length == 0)
        {
            string msg = _atlas == null
                ? "No SpriteAtlas loaded. Double-click a SpriteAtlas asset."
                : _needsPack
                    ? "Could not resolve sprite layout. Click 'Pack Preview' to pack for this editor platform."
                    : "Atlas has no packed textures. Pack it via the SpriteAtlas inspector.";
            EditorGUI.HelpBox(new Rect(4, ToolbarH + 4, winW - 8, 40), msg, MessageType.Info);
            return;
        }

        bool hasTabs  = _pages.Length > 1;
        float sideW   = hasTabs ? TabsW : 0f;
        float contentY = ToolbarH;
        float contentH = winH - ToolbarH - FooterH;

        DrawPage(new Rect(0, contentY, winW - sideW, contentH));

        if (hasTabs)
            DrawPageTabs(new Rect(winW - sideW, contentY, sideW, contentH));

        DrawFooter(new Rect(0, winH - FooterH, winW, FooterH));
    }

    // ---- toolbar ----------------------------------------------------

    private void DrawToolbar(Rect r)
    {
        GUI.BeginGroup(r);
        GUILayout.BeginArea(new Rect(0, 0, r.width, r.height), EditorStyles.toolbar);
        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginChangeCheck();
        var na = (SpriteAtlas)EditorGUILayout.ObjectField(_atlas, typeof(SpriteAtlas), false, GUILayout.Width(220));
        if (EditorGUI.EndChangeCheck()) { GUILayout.EndArea(); GUI.EndGroup(); LoadAtlas(na); return; }

        GUILayout.FlexibleSpace();
        GUILayout.Label("Zoom:", EditorStyles.miniLabel);
        float nz = GUILayout.HorizontalSlider(_zoom, ZoomMin, ZoomMax, GUILayout.Width(80));
        if (!Mathf.Approximately(nz, _zoom)) { _zoom = nz; Repaint(); }
        GUILayout.Label($"{_zoom:0.0}x", EditorStyles.miniLabel, GUILayout.Width(32));
        if (GUILayout.Button("1:1", EditorStyles.toolbarButton, GUILayout.Width(28))) { _zoom = 1f; Repaint(); }
        if (GUILayout.Button("Fit", EditorStyles.toolbarButton, GUILayout.Width(28))) { FitZoom(); Repaint(); }
        GUILayout.Space(4);
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(55))) { InvalidateCache(); LoadAtlas(_atlas); }

        if (_needsPack)
        {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.6f, 0.2f);
            if (GUILayout.Button("Pack Preview", EditorStyles.toolbarButton, GUILayout.Width(85))) PackAndReload();
            GUI.backgroundColor = prev;
        }

        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();
        GUI.EndGroup();
    }

    private void FitZoom()
    {
        if (_pages == null || _currentPage >= _pages.Length) return;
        var t     = _pages[_currentPage];
        float avW = position.width  - (_pages.Length > 1 ? TabsW : 0);
        float avH = position.height - ToolbarH - FooterH;
        _zoom = Mathf.Clamp(Mathf.Min(avW / t.width, avH / t.height), ZoomMin, ZoomMax);
        _panOffset = Vector2.zero;
    }

    // ---- page tabs (vertical sidebar) ----------------------------------

    private void DrawPageTabs(Rect r)
    {
        // Background + separator line
        EditorGUI.DrawRect(r, new Color(0.2f, 0.2f, 0.2f));
        EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), new Color(0.1f, 0.1f, 0.1f));

        float totalH      = _pages.Length * TabBtnH;
        bool  needsScroll = totalH > r.height;
        float btnW        = needsScroll ? r.width - 14f : r.width;
        Rect  viewRect    = new Rect(r.x, r.y, r.width, r.height);
        Rect  contentRect = new Rect(0,   0,   btnW,    totalH);

        _tabsScroll = GUI.BeginScrollView(viewRect, _tabsScroll, contentRect, false, needsScroll);

        for (int i = 0; i < _pages.Length; i++)
        {
            int    cnt = _pageData?[i].Sprites.Count ?? 0;
            string lbl = $"p.{i + 1} ({cnt})";
            Rect   btn = new Rect(0, i * TabBtnH, btnW, TabBtnH);

            Color prev = GUI.backgroundColor;
            if (i == _currentPage) GUI.backgroundColor = new Color(0.35f, 0.6f, 1f);

            if (GUI.Toggle(btn, i == _currentPage, lbl, EditorStyles.toolbarButton)
                && i != _currentPage)
            {
                _currentPage    = i;
                _selectedSprite = -1;
                _panOffset      = Vector2.zero;
                // Scroll to keep selected tab visible
                float btnTop = i * TabBtnH;
                if (btnTop < _tabsScroll.y)                      _tabsScroll.y = btnTop;
                if (btnTop + TabBtnH > _tabsScroll.y + r.height) _tabsScroll.y = btnTop + TabBtnH - r.height;
            }

            GUI.backgroundColor = prev;
        }

        GUI.EndScrollView();
    }

    // ---- page view --------------------------------------------------

    private void DrawPage(Rect area)
    {
        if (_pages == null || _currentPage >= _pages.Length) return;
        Texture2D page = _pages[_currentPage];
        if (page == null) return;

        var sprites = _pageData?[_currentPage].Sprites ?? new List<SpriteEntry>();
        Event e = Event.current;

        // --- input: middle-mouse pan ---
        if (e.type == EventType.MouseDown && e.button == 2 && area.Contains(e.mousePosition))
        {
            _isPanning = true;
            e.Use();
        }
        if (_isPanning && e.type == EventType.MouseDrag && e.button == 2)
        {
            _panOffset += e.delta;
            e.Use();
            Repaint();
        }
        if (e.type == EventType.MouseUp && e.button == 2)
        {
            _isPanning = false;
            e.Use();
        }

        // --- input: F key = Fit ---
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F && area.Contains(e.mousePosition))
        {
            FitZoom();
            _panOffset = Vector2.zero;
            e.Use();
            Repaint();
        }

        // --- input: scroll wheel zoom (around mouse) ---
        if (e.type == EventType.ScrollWheel && area.Contains(e.mousePosition))
        {
            float oldZoom = _zoom;
            _zoom = Mathf.Clamp(_zoom * (e.delta.y > 0 ? 1f - ZoomStep : 1f + ZoomStep), ZoomMin, ZoomMax);
            // Zoom toward mouse position
            Vector2 mouseLocal = e.mousePosition - area.position - area.size * 0.5f - _panOffset;
            _panOffset += mouseLocal * (1f - _zoom / oldZoom);
            e.Use();
            Repaint();
        }

        // --- draw clipped to area ---
        GUI.BeginClip(area);

        float scaledW = page.width  * _zoom;
        float scaledH = page.height * _zoom;
        float ox = (area.width  - scaledW) * 0.5f + _panOffset.x;
        float oy = (area.height - scaledH) * 0.5f + _panOffset.y;

        Rect texRect = new Rect(ox, oy, scaledW, scaledH);
        DrawCheckerboard(texRect);
        GUI.DrawTexture(texRect, page, ScaleMode.StretchToFill);

        for (int i = 0; i < sprites.Count; i++)
        {
            Rect gui      = TexToGui(sprites[i].TexRect, texRect, page.width, page.height);
            bool selected = i == _selectedSprite;
            bool hovered  = gui.Contains(e.mousePosition);

            Color outline = selected ? new Color(1f, 0.55f, 0f, 1f)
                          : hovered  ? new Color(1f, 1f,    0.2f, 1f)
                                     : new Color(0.3f, 0.8f, 1f, 0.55f);
            DrawOutline(gui, outline, selected || hovered ? 2f : 1f);

            if (selected || hovered) DrawLabel(gui, sprites[i].Name);

            if (hovered && e.type == EventType.MouseDown && e.button == 0)
            {
                _selectedSprite = i;
                if (sprites[i].Original != null)
                    EditorGUIUtility.PingObject(sprites[i].Original);
                e.Use();
                Repaint();
            }
        }

        GUI.EndClip();
    }

    // ---- footer -----------------------------------------------------

    private void DrawFooter(Rect r)
    {
        GUI.BeginGroup(r);
        GUILayout.BeginArea(new Rect(0, 0, r.width, r.height), EditorStyles.toolbar);
        EditorGUILayout.BeginHorizontal();

        bool hasSel = _pageData != null && _currentPage < _pageData.Length
                   && _selectedSprite >= 0 && _selectedSprite < _pageData[_currentPage].Sprites.Count;
        if (hasSel)
        {
            SpriteEntry info = _pageData[_currentPage].Sprites[_selectedSprite];
            string path = info.Original != null ? AssetDatabase.GetAssetPath(info.Original) : "—";
            GUILayout.Label($"{info.Name}   {(int)info.TexRect.width}×{(int)info.TexRect.height} px   {path}",
                EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Select", EditorStyles.toolbarButton, GUILayout.Width(50)) && info.Original != null)
            {
                Selection.activeObject = info.Original;
                EditorGUIUtility.PingObject(info.Original);
            }
        }
        else
        {
            GUILayout.Label("Click a sprite rect to highlight it in the Project window.", EditorStyles.miniLabel);
        }

        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();
        GUI.EndGroup();
    }

    // ================================================================= draw helpers

    // texRect: pixels bottom-left origin → GUI rect top-left origin, scaled to display
    private static Rect TexToGui(Rect tr, Rect display, int texW, int texH)
    {
        float sx = display.width  / texW;
        float sy = display.height / texH;
        return new Rect(
            display.x + tr.x * sx,
            display.y + (texH - tr.y - tr.height) * sy,
            tr.width  * sx,
            tr.height * sy);
    }

    private static void DrawCheckerboard(Rect r)
    {
        const float cell = 8f;
        Color dark = new Color(0.22f, 0.22f, 0.22f);
        Color lite = new Color(0.28f, 0.28f, 0.28f);
        EditorGUI.DrawRect(r, lite);
        int cols = Mathf.CeilToInt(r.width  / cell);
        int rows = Mathf.CeilToInt(r.height / cell);
        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            if ((row + col) % 2 == 0) continue;
            EditorGUI.DrawRect(new Rect(
                r.x + col * cell, r.y + row * cell,
                Mathf.Min(cell, r.xMax - r.x - col * cell),
                Mathf.Min(cell, r.yMax - r.y - row * cell)), dark);
        }
    }

    private static void DrawOutline(Rect r, Color c, float t)
    {
        EditorGUI.DrawRect(new Rect(r.x,        r.y,        r.width, t),  c);
        EditorGUI.DrawRect(new Rect(r.x,        r.yMax - t, r.width, t),  c);
        EditorGUI.DrawRect(new Rect(r.x,        r.y,        t, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - t, r.y,        t, r.height), c);
    }

    private static void DrawLabel(Rect sprite, string name)
    {
        Vector2 size = EditorStyles.miniLabel.CalcSize(new GUIContent(name));
        float lx = sprite.x;
        float ly = sprite.y - size.y - 2;
        if (ly < 0) ly = sprite.y + 2;
        Rect bg = new Rect(lx, ly, size.x + 4, size.y + 2);
        EditorGUI.DrawRect(bg, new Color(0f, 0f, 0f, 0.75f));
        GUI.Label(new Rect(bg.x + 2, bg.y + 1, bg.width, bg.height), name, EditorStyles.miniLabel);
    }
}

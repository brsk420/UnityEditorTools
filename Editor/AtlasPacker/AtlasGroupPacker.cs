using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace _Brsk420.EditorTools.AtlasPacker
{
    /// <summary>
    /// Right-click a folder -> scans all sprites in it (recursively), measures each
    /// sprite's alpha-trimmed bounding box, then bins sprites into "Group_NN" sets
    /// using the real SpriteAtlas packer as an oracle so every group ends up on
    /// exactly one atlas page. Sprites are physically moved into "Group_NN" folders
    /// (preserving GUIDs via AssetDatabase.MoveAsset, so scene references stay intact)
    /// and one SpriteAtlas is created per group.
    /// </summary>
    internal static class AtlasGroupPacker
    {
        private const int PageSize = 2048;
        private const int Padding = 4;
        private const bool AllowRotation = true;
        private const int MaxTextureSize = 2048;
        private const int CompressionQuality = 50; // Normal

        private const string TempAtlasPath = "Assets/_AtlasGroupPacker_PackTest.spriteatlas";

        private struct SpriteItem
        {
            public string AssetPath;
            public string RelativePath;
            public int W;
            public int H;
            public long BBoxArea;
        }

        [MenuItem("Assets/_BrskTools/Sprites/Repack Folder Into Atlas Groups", priority = -1949)]
        private static void Run()
        {
            var folderPath = GetSelectedFolderPath();
            if (folderPath == null)
            {
                Debug.LogWarning("[AtlasGroupPacker] Select a single folder in the Project window.");
                return;
            }

            var items = CollectSpriteItems(folderPath);
            if (items.Count == 0)
            {
                Debug.LogWarning($"[AtlasGroupPacker] No PNG sprites found under '{folderPath}'.");
                return;
            }

            var groups = PackIntoGroups(items);

            Debug.Log(BuildSummary(folderPath, groups));

            GroupPreviewWindow.Show(folderPath, groups);
        }

        [MenuItem("Assets/_BrskTools/Sprites/Repack Folder Into Atlas Groups", true)]
        private static bool Validate()
        {
            return GetSelectedFolderPath() != null;
        }

        private static string GetSelectedFolderPath()
        {
            var objects = Selection.GetFiltered<Object>(SelectionMode.Assets);
            if (objects.Length != 1)
            {
                return null;
            }

            var path = AssetDatabase.GetAssetPath(objects[0]);
            return AssetDatabase.IsValidFolder(path) ? path : null;
        }

        // ------------------------------------------------------------------
        // Collect + measure (alpha-trimmed bounding box)
        // ------------------------------------------------------------------

        private static List<SpriteItem> CollectSpriteItems(string root)
        {
            var files = Directory.GetFiles(root, "*.png", SearchOption.AllDirectories)
                                  .Select(p => p.Replace('\\', '/'))
                                  .Where(p => !p.Contains("_AtlasGroups/"))
                                  .OrderBy(p => p)
                                  .ToList();

            var result = new List<SpriteItem>(files.Count);

            try
            {
                for (var i = 0; i < files.Count; i++)
                {
                    var assetPath = files[i];
                    EditorUtility.DisplayProgressBar(
                        $"Analyzing sprites ({i + 1}/{files.Count})", assetPath, (float)(i + 1) / files.Count);

                    if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer
                        && importer.spriteImportMode == SpriteImportMode.None)
                    {
                        Debug.LogWarning($"[AtlasGroupPacker] '{assetPath}' is not imported as a Sprite "
                                          + "(textureType != Sprite) - SpriteAtlas won't pack it, skipping.");
                        continue;
                    }

                    var (w, h) = MeasureSprite(assetPath);
                    if (w <= 0 || h <= 0)
                    {
                        continue;
                    }

                    result.Add(new SpriteItem
                    {
                        AssetPath = assetPath,
                        RelativePath = assetPath.Substring(root.Length).TrimStart('/'),
                        W = w,
                        H = h,
                        BBoxArea = (long)(w + Padding) * (h + Padding),
                    });
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return result;
        }

        /// <summary>
        /// Returns (trimmedWidth, trimmedHeight) - the alpha-trimmed bounding box
        /// SpriteAtlas tight-packing places on the page (plus padding).
        /// </summary>
        private static (int, int) MeasureSprite(string assetPath)
        {
            var bytes = File.ReadAllBytes(assetPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            try
            {
                if (!tex.LoadImage(bytes))
                {
                    return (0, 0);
                }

                var pixels = tex.GetPixels32();
                int w = tex.width, h = tex.height;
                int minX = w, minY = h, maxX = -1, maxY = -1;

                for (var y = 0; y < h; y++)
                {
                    var row = y * w;
                    for (var x = 0; x < w; x++)
                    {
                        if (pixels[row + x].a == 0)
                        {
                            continue;
                        }

                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }

                if (maxX < 0)
                {
                    // Fully transparent - keep original size.
                    return (w, h);
                }

                return (maxX - minX + 1, maxY - minY + 1);
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        // ------------------------------------------------------------------
        // Real-packer-oracle grouping: binary-search the largest prefix of
        // the (bbox-area sorted) pool that fits on a single atlas page.
        // ------------------------------------------------------------------

        private static List<List<SpriteItem>> PackIntoGroups(List<SpriteItem> items)
        {
            foreach (var item in items)
            {
                if (Mathf.Max(item.W, item.H) > PageSize)
                {
                    Debug.LogWarning($"[AtlasGroupPacker] '{item.AssetPath}' ({item.W}x{item.H}) "
                                      + $"is larger than a {PageSize}x{PageSize} page on its own.");
                }
            }

            const long PageArea = (long)PageSize * PageSize;

            var sorted = items.OrderByDescending(it => it.BBoxArea).ToList();
            var totalBbox = sorted.Sum(it => it.BBoxArea);

            // Target group count slightly above the raw bbox-area estimate - this
            // gives every group a little headroom so the rebalancing pass below has
            // somewhere to shed sprites from groups that real-pack to >1 page.
            var groupCount = Mathf.Max(1, Mathf.CeilToInt((float)(totalBbox / (double)PageArea * 1.05)));

            // Longest-Processing-Time-first load balancing: each sprite goes to the
            // currently lightest group, so every group ends up close to one page's
            // worth of bbox area.
            var groups = new List<List<SpriteItem>>(groupCount);
            var loads = new long[groupCount];
            for (var i = 0; i < groupCount; i++)
            {
                groups.Add(new List<SpriteItem>());
            }

            foreach (var item in sorted)
            {
                var minIndex = 0;
                for (var i = 1; i < groupCount; i++)
                {
                    if (loads[i] < loads[minIndex])
                    {
                        minIndex = i;
                    }
                }

                groups[minIndex].Add(item);
                loads[minIndex] += item.BBoxArea;
            }

            try
            {
                RebalanceOverfullGroups(groups);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                DisposeOracleAtlas();
            }

            return groups;
        }

        /// <summary>
        /// After LPT load-balancing, some groups may still real-pack to more than one
        /// page. Repeatedly move the smallest sprite out of an overfull group into
        /// whichever other group has the most spare headroom (verified via the
        /// real-pack oracle), until every group fits on one page or no further
        /// beneficial move exists.
        /// </summary>
        private static void RebalanceOverfullGroups(List<List<SpriteItem>> groups)
        {
            var pages = new int[groups.Count];
            for (var i = 0; i < groups.Count; i++)
            {
                pages[i] = CountPackedPages(groups[i]);
            }

            const int MaxMoves = 200;
            for (var move = 0; move < MaxMoves; move++)
            {
                EditorUtility.DisplayProgressBar(
                    "Balancing atlas groups (real-pack oracle)", $"pass {move + 1}",
                    (float)(move + 1) / MaxMoves);

                var srcIndex = -1;
                for (var i = 0; i < groups.Count; i++)
                {
                    if (pages[i] > 1 && groups[i].Count > 1)
                    {
                        srcIndex = i;
                        break;
                    }
                }

                if (srcIndex < 0)
                {
                    break; // every group fits on one page (or can't be improved further)
                }

                var src = groups[srcIndex];
                var moveIndex = 0;
                for (var j = 1; j < src.Count; j++)
                {
                    if (src[j].BBoxArea < src[moveIndex].BBoxArea)
                    {
                        moveIndex = j;
                    }
                }

                var item = src[moveIndex];

                var destOrder = Enumerable.Range(0, groups.Count)
                                           .Where(g => g != srcIndex)
                                           .OrderBy(g => groups[g].Sum(it => it.BBoxArea))
                                           .ToList();

                var moved = false;
                foreach (var d in destOrder)
                {
                    var trialDest = new List<SpriteItem>(groups[d]) { item };
                    var destPages = CountPackedPages(trialDest);
                    if (destPages > 1)
                    {
                        continue;
                    }

                    var trialSrc = new List<SpriteItem>(src);
                    trialSrc.RemoveAt(moveIndex);
                    var srcPages = CountPackedPages(trialSrc);

                    groups[d].Add(item);
                    src.RemoveAt(moveIndex);
                    pages[d] = destPages;
                    pages[srcIndex] = srcPages;
                    moved = true;
                    break;
                }

                if (!moved)
                {
                    // Shedding the smallest sprite doesn't help anywhere - mark this
                    // group as "tried" (negative) so we don't loop on it forever.
                    pages[srcIndex] = -pages[srcIndex];
                }
            }
        }

        private static UnityEngine.U2D.SpriteAtlas _oracleAtlas;

        /// <summary>
        /// Packs <paramref name="items"/> into a reusable temporary persisted SpriteAtlas
        /// using the real Unity packer and returns how many pages (distinct textures)
        /// it produced. PackAtlases only populates results for atlases that are saved
        /// assets, so a single temp asset is created once and its packable set is
        /// swapped between calls (call <see cref="DisposeOracleAtlas"/> when done).
        /// </summary>
        private static int CountPackedPages(List<SpriteItem> items)
        {
            if (_oracleAtlas == null)
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(TempAtlasPath) != null)
                {
                    AssetDatabase.DeleteAsset(TempAtlasPath);
                }

                _oracleAtlas = BuildConfiguredAtlas();
                AssetDatabase.CreateAsset(_oracleAtlas, TempAtlasPath);
            }

            var existing = _oracleAtlas.GetPackables();
            if (existing.Length > 0)
            {
                _oracleAtlas.Remove(existing);
            }

            var sprites = items
                .Select(it => AssetDatabase.LoadAssetAtPath<Sprite>(it.AssetPath))
                .Where(s => s != null)
                .Cast<Object>()
                .ToArray();

            _oracleAtlas.Add(sprites);
            EditorUtility.SetDirty(_oracleAtlas);
            AssetDatabase.SaveAssets();

            SpriteAtlasUtility.PackAtlases(new[] { _oracleAtlas }, EditorUserBuildSettings.activeBuildTarget, false);

            var so = new SerializedObject(_oracleAtlas);
            var packedSprites = so.FindProperty("m_PackedSprites");

            var textureIds = new HashSet<int>();
            for (var i = 0; i < packedSprites.arraySize; i++)
            {
                var sprite = packedSprites.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                if (sprite != null && sprite.texture != null)
                {
                    textureIds.Add(sprite.texture.GetInstanceID());
                }
            }

            return Mathf.Max(textureIds.Count, 1);
        }

        private static void DisposeOracleAtlas()
        {
            if (_oracleAtlas == null)
            {
                return;
            }

            _oracleAtlas = null;
            if (AssetDatabase.LoadAssetAtPath<Object>(TempAtlasPath) != null)
            {
                AssetDatabase.DeleteAsset(TempAtlasPath);
            }
        }

        private static string BuildSummary(string folderPath, List<List<SpriteItem>> groups)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[AtlasGroupPacker] '{folderPath}': {groups.Sum(g => g.Count)} sprites -> {groups.Count} group(s)");

            for (var i = 0; i < groups.Count; i++)
            {
                var fill = GroupFillPercent(groups[i]);
                sb.AppendLine($"  Group_{i:D2}: {groups[i].Count} sprites, ~{fill:F1}% bbox fill");
            }

            return sb.ToString();
        }

        private static double GroupFillPercent(List<SpriteItem> group)
        {
            var area = group.Sum(it => it.BBoxArea);
            return 100.0 * area / ((long)PageSize * PageSize);
        }

        // ------------------------------------------------------------------
        // Preview window: shows the full per-group breakdown with Sort/Cancel
        // ------------------------------------------------------------------

        private sealed class GroupPreviewWindow : EditorWindow
        {
            private string _folderPath;
            private List<List<SpriteItem>> _groups;
            private Vector2 _scroll;

            public static void Show(string folderPath, List<List<SpriteItem>> groups)
            {
                var window = CreateInstance<GroupPreviewWindow>();
                window._folderPath = folderPath;
                window._groups = groups;
                window.titleContent = new GUIContent("Repack Into Atlas Groups");
                window.minSize = new Vector2(360, 400);
                window.maxSize = new Vector2(360, 800);
                window.ShowUtility();
            }

            private void OnGUI()
            {
                var totalSprites = _groups.Sum(g => g.Count);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(_folderPath, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField($"{totalSprites} sprites -> {_groups.Count} group(s)", EditorStyles.boldLabel);
                EditorGUILayout.Space(4);

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                for (var i = 0; i < _groups.Count; i++)
                {
                    var fill = GroupFillPercent(_groups[i]);
                    EditorGUILayout.LabelField($"Group_{i:D2}: {_groups[i].Count} sprites, ~{fill:F1}% bbox fill");
                }
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space(4);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Cancel"))
                {
                    Close();
                }

                if (GUILayout.Button("Sort"))
                {
                    ApplyGroups(_folderPath, _groups);
                    Close();
                }
                GUILayout.EndHorizontal();
            }
        }

        // ------------------------------------------------------------------
        // Apply: move assets into Group_NN folders + create SpriteAtlas per group
        // ------------------------------------------------------------------

        private static void ApplyGroups(string folderPath, List<List<SpriteItem>> groups)
        {
            var rootName = Path.GetFileName(folderPath.TrimEnd('/'));
            var parentFolder = Path.GetDirectoryName(folderPath)?.Replace('\\', '/') ?? "Assets";

            // AssetDatabase.IsValidFolder() can return stale results for folders
            // created earlier in the same call, which causes CreateFolder to
            // "succeed" again with a disambiguated " 1"/" 2" name. Track folders
            // we've created/seen ourselves to avoid that.
            var knownFolders = new HashSet<string>();

            var groupsRoot = EnsureFolderRecursive($"{parentFolder}/{rootName}_AtlasGroups", knownFolders);

            var groupFolders = new List<string>();
            var total = groups.Sum(g => g.Count);
            var done = 0;

            try
            {
                for (var i = 0; i < groups.Count; i++)
                {
                    var groupFolder = EnsureFolderRecursive($"{groupsRoot}/Group_{i:D2}", knownFolders);
                    groupFolders.Add(groupFolder);

                    foreach (var item in groups[i])
                    {
                        EditorUtility.DisplayProgressBar(
                            $"Moving sprites ({done + 1}/{total})", item.RelativePath, (float)(done + 1) / total);
                        done++;

                        var dstPath = $"{groupFolder}/{item.RelativePath}";
                        var dstDir = Path.GetDirectoryName(dstPath)?.Replace('\\', '/');
                        if (!string.IsNullOrEmpty(dstDir))
                        {
                            EnsureFolderRecursive(dstDir, knownFolders);
                        }

                        var error = AssetDatabase.MoveAsset(item.AssetPath, dstPath);
                        if (!string.IsNullOrEmpty(error))
                        {
                            Debug.LogError($"[AtlasGroupPacker] Failed to move '{item.AssetPath}' -> '{dstPath}': {error}");
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            foreach (var groupFolder in groupFolders)
            {
                CreateAtlas(groupFolder);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[AtlasGroupPacker] Done. Created {groupFolders.Count} group(s) under '{groupsRoot}'.");
        }

        private static UnityEngine.U2D.SpriteAtlas BuildConfiguredAtlas()
        {
            var atlas = new UnityEngine.U2D.SpriteAtlas();

            atlas.SetPackingSettings(new SpriteAtlasPackingSettings
            {
                padding = Padding,
                blockOffset = 1,
                enableRotation = AllowRotation,
                enableTightPacking = true,
                enableAlphaDilation = false,
            });

            atlas.SetTextureSettings(new SpriteAtlasTextureSettings
            {
                readable = false,
                generateMipMaps = false,
                sRGB = true,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 1,
            });

            var defaultSettings = atlas.GetPlatformSettings("DefaultTexturePlatform");
            defaultSettings.maxTextureSize = MaxTextureSize;
            defaultSettings.textureCompression = TextureImporterCompression.Compressed;
            defaultSettings.compressionQuality = CompressionQuality;
            defaultSettings.crunchedCompression = false;
            atlas.SetPlatformSettings(defaultSettings);

            foreach (var platform in new[] { "Android", "iPhone" })
            {
                var settings = atlas.GetPlatformSettings(platform);
                settings.overridden = true;
                settings.maxTextureSize = MaxTextureSize;
                settings.format = TextureImporterFormat.ASTC_6x6;
                settings.compressionQuality = CompressionQuality;
                settings.crunchedCompression = false;
                atlas.SetPlatformSettings(settings);
            }

            return atlas;
        }

        private static void CreateAtlas(string folderPath)
        {
            var folderName = Path.GetFileName(folderPath.TrimEnd('/'));
            var parentFolder = Path.GetDirectoryName(folderPath)?.Replace('\\', '/') ?? "Assets";
            var atlasPath = $"{parentFolder}/{folderName}_Atlas.spriteatlas";

            if (AssetDatabase.LoadAssetAtPath<Object>(atlasPath) != null)
            {
                Debug.LogError($"[AtlasGroupPacker] '{atlasPath}' already exists.");
                return;
            }

            var atlas = BuildConfiguredAtlas();

            var folderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
            atlas.Add(new Object[] { folderAsset });

            AssetDatabase.CreateAsset(atlas, atlasPath);

            Debug.Log($"[AtlasGroupPacker] Created '{atlasPath}' packing '{folderPath}'.");
        }

        private static string EnsureFolderRecursive(string fullPath, HashSet<string> knownFolders)
        {
            var parts = fullPath.Split('/');
            var current = parts[0];
            knownFolders.Add(current);

            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!knownFolders.Contains(next) && !AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                knownFolders.Add(next);
                current = next;
            }

            return current;
        }
    }
}

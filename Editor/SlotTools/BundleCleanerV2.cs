using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// Finds assets inside a bundle folder that nothing can reach.
///
/// Folder == bundle in this project, and AssetBundleExporter tags the FOLDER, so every file
/// under it inherits the tag and ships inside the .unity3d whether or not anything references
/// it. Unreferenced files are therefore paid for by the player, not just by the repository.
///
/// Three verdicts:
///   Keep    - reachable from a root asset, or its name appears in config/code
///   Review  - unreachable, but sits inside a folder a SpriteAtlas packs wholesale.
///             It bloats the atlas, but removing it changes the atlas: a human decides.
///   Delete  - unreachable by every check we have
///
/// Known blind spots, listed so nobody mistakes this for proof:
///   - paths assembled at runtime from fragments ("Symbols/" + name + "_big")
///   - references from compiled assemblies under Assets/Assemblies
///   - Spine/Lua addressing files by relative path
/// Always re-run the bundle build after deleting and compare bundle sizes.
/// </summary>
public static class SlotBundleCleanerV2
{
    const string MENU = "Assets/_BrskTools/Bundle Cleaner V2/";
    const string BUNDLES_ROOT = "Assets/Bundles";

    // Anything the server may plausibly request by name is a root, so its dependencies survive.
    static readonly HashSet<string> RootExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".prefab", ".unity", ".asset", ".spriteatlas", ".controller", ".overridecontroller",
      ".json", ".txt", ".bytes", ".lua", ".playable", ".mixer" };

    // Files scanned to build the "referenced by name" index.
    static readonly HashSet<string> ConfigExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".json", ".txt", ".bytes", ".lua", ".cs", ".xml", ".csv", ".yaml", ".yml" };

    public enum Verdict { Keep, Review, Delete }

    public class Entry
    {
        public string Bundle;
        public string Path;
        public long Size;
        public Verdict Verdict;
        public string Reason;
    }

    // ---------------------------------------------------------------- menu

    [MenuItem(MENU + "Analyze selected folder", priority = -1979)]
    static void AnalyzeSelected()
    {
        var folder = GetSelectedFolder();
        if (string.IsNullOrEmpty(folder))
        {
            EditorUtility.DisplayDialog("Bundle Cleaner V2", "Select a bundle folder first.", "OK");
            return;
        }
        var entries = Analyze(new[] { folder });
        ShowWindow("Bundle Cleaner V2 — " + Path.GetFileName(folder), entries);
    }

    [MenuItem(MENU + "Analyze ALL bundles (report only)", priority = -1978)]
    static void AnalyzeAll()
    {
        if (!EditorUtility.DisplayDialog("Bundle Cleaner V2",
                "Analyze every bundle under " + BUNDLES_ROOT + "?\n\n" +
                "This only writes a report. Nothing is deleted.", "Analyze", "Cancel"))
            return;

        var bundles = EnumerateBundles().ToArray();
        var entries = Analyze(bundles);
        var path = WriteReport(entries);
        EditorUtility.DisplayDialog("Bundle Cleaner V2",
            Summary(entries) + "\n\nReport written to:\n" + path, "OK");
        ShowWindow("Bundle Cleaner V2 — all bundles", entries);
    }

    [MenuItem(MENU + "Rebuild name index", priority = -1977)]
    static void RebuildIndex()
    {
        _nameIndex = null;
        var n = NameIndex.Count;
        EditorUtility.DisplayDialog("Bundle Cleaner V2",
            "Name index rebuilt: " + n + " tokens.\n\nScanned:\n" + LastIndexBreakdown
            + "\nNot covered: server-side AVA configs, which never enter this repository.", "OK");
    }

    // ---------------------------------------------------------------- analysis

    public static List<Entry> Analyze(IReadOnlyList<string> bundleRoots)
    {
        var result = new List<Entry>();
        var atlasFolders = AtlasPackedFolders();
        try
        {
            for (int i = 0; i < bundleRoots.Count; i++)
            {
                var b = bundleRoots[i];
                if (EditorUtility.DisplayCancelableProgressBar("Bundle Cleaner V2",
                        b, (float)i / Mathf.Max(1, bundleRoots.Count)))
                    break;
                AnalyzeBundle(b, atlasFolders, result);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        return result;
    }

    static void AnalyzeBundle(string bundleRoot, HashSet<string> atlasFolders, List<Entry> into)
    {
        if (!AssetDatabase.IsValidFolder(bundleRoot)) return;

        var all = AssetDatabase.FindAssets("", new[] { bundleRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .Where(p => !string.IsNullOrEmpty(p) && !AssetDatabase.IsValidFolder(p))   // never a folder
            .Where(p => p.StartsWith(bundleRoot + "/", StringComparison.Ordinal))
            .ToList();
        if (all.Count == 0) return;

        // Roots: every loadable-by-name asset ANYWHERE in the bundle, not just <bundle>/Prefabs.
        var roots = all.Where(p => RootExtensions.Contains(Path.GetExtension(p))).ToArray();

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in roots) reachable.Add(r);
        if (roots.Length > 0)
            foreach (var d in AssetDatabase.GetDependencies(roots, true)) reachable.Add(d);

        foreach (var p in all)
        {
            var e = new Entry { Bundle = bundleRoot, Path = p, Size = FileSize(p) };

            if (reachable.Contains(p))
            {
                e.Verdict = Verdict.Keep;
                e.Reason = roots.Contains(p) ? "root asset" : "reachable from a root";
            }
            else if (IsNamedInConfig(p))
            {
                e.Verdict = Verdict.Keep;
                e.Reason = "name appears in config/code";
            }
            else if (InAtlasPackedFolder(p, atlasFolders))
            {
                e.Verdict = Verdict.Review;
                e.Reason = "packed into a SpriteAtlas via its folder, but nothing references it";
            }
            else
            {
                e.Verdict = Verdict.Delete;
                e.Reason = "unreachable";
            }
            into.Add(e);
        }
    }

    // ---------------------------------------------------------------- name index

    static HashSet<string> _nameIndex;
    static HashSet<string> NameIndex
    {
        get
        {
            if (_nameIndex != null) return _nameIndex;
            _nameIndex = BuildNameIndex();
            return _nameIndex;
        }
    }

    /// Roots scanned for the name index. Assets/ alone is not enough: package code - the GEL
    /// engine above all - can load bundle assets by name, and those packages live outside it.
    /// Library/PackageCache holds the resolved scoped-registry packages, so it is scanned too,
    /// which does make the index depend on what this machine has resolved.
    public static string[] NameIndexRoots()
    {
        var project = Directory.GetParent(Application.dataPath).FullName;          // .../Konami-Slots
        var repoClient = Directory.GetParent(project).FullName;                    // .../Client
        return new[]
        {
            Application.dataPath,                                                  // Assets
            Path.Combine(project, "Packages"),
            Path.Combine(project, "Library", "PackageCache"),
            Path.Combine(repoClient, "UnityPackages"),
        }.Where(Directory.Exists).ToArray();
    }

    public static string LastIndexBreakdown { get; private set; } = "(not built yet)";

    /// Every identifier-like token found in config and code. An asset whose file name shows up
    /// here may be loaded by name rather than by GUID - audio driven by JSON is the common case.
    static HashSet<string> BuildNameIndex()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = NameIndexRoots();
        var breakdown = new StringBuilder();
        var collected = new List<string>();
        foreach (var root in roots)
        {
            string[] here;
            try
            {
                here = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(f => ConfigExtensions.Contains(Path.GetExtension(f)))
                    .ToArray();
            }
            catch (Exception ex) { breakdown.AppendLine(root + ": unreadable (" + ex.GetType().Name + ")"); continue; }
            collected.AddRange(here);
            breakdown.AppendLine(here.Length + " files\t" + root);
        }
        LastIndexBreakdown = breakdown.ToString();
        var files = collected.ToArray();
        var sep = new[] { ' ', '\t', '\r', '\n', '"', '\'', '(', ')', '[', ']', '{', '}', ',', ';',
                          ':', '/', '\\', '=', '+', '*', '<', '>', '|', '&', '?', '!', '#', '@', '%', '$', '^', '~', '`' };
        try
        {
            for (int i = 0; i < files.Length; i++)
            {
                if (i % 500 == 0 && EditorUtility.DisplayCancelableProgressBar(
                        "Bundle Cleaner V2", "Building name index " + i + "/" + files.Length,
                        (float)i / files.Length))
                    break;
                string text;
                try { text = File.ReadAllText(files[i]); } catch { continue; }
                foreach (var tok in text.Split(sep, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (tok.Length < 3 || tok.Length > 128) continue;
                    set.Add(tok);
                    var dot = tok.LastIndexOf('.');
                    if (dot > 0) set.Add(tok.Substring(0, dot));   // "clip.aif" also indexes "clip"
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        return set;
    }

    static bool IsNamedInConfig(string assetPath)
        => NameIndex.Contains(Path.GetFileNameWithoutExtension(assetPath));

    // ---------------------------------------------------------------- sprite atlases

    /// Folders listed for packing in any SpriteAtlas. Every image inside such a folder ends up in
    /// the atlas even though its own GUID appears nowhere.
    static HashSet<string> AtlasPackedFolders()
    {
        var folders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var guid in AssetDatabase.FindAssets("t:SpriteAtlas"))
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AssetDatabase.GUIDToAssetPath(guid));
            if (atlas == null) continue;
            foreach (var o in atlas.GetPackables())
            {
                if (o == null) continue;
                var p = AssetDatabase.GetAssetPath(o);
                if (!string.IsNullOrEmpty(p) && AssetDatabase.IsValidFolder(p)) folders.Add(p);
            }
        }
        return folders;
    }

    static bool InAtlasPackedFolder(string assetPath, HashSet<string> atlasFolders)
    {
        foreach (var f in atlasFolders)
            if (assetPath.StartsWith(f + "/", StringComparison.Ordinal)) return true;
        return false;
    }

    // ---------------------------------------------------------------- helpers

    public static IEnumerable<string> EnumerateBundles()
    {
        foreach (var grp in AssetDatabase.GetSubFolders(BUNDLES_ROOT))
        {
            if (Path.GetFileName(grp).StartsWith("_", StringComparison.Ordinal))
            {
                foreach (var sub in AssetDatabase.GetSubFolders(grp))
                {
                    if (Path.GetFileName(sub).StartsWith("_", StringComparison.Ordinal))
                        foreach (var s2 in AssetDatabase.GetSubFolders(sub)) yield return s2;
                    else yield return sub;
                }
            }
            else yield return grp;
        }
    }

    static long FileSize(string assetPath)
    {
        try { var fi = new FileInfo(assetPath); return fi.Exists ? fi.Length : 0; }
        catch { return 0; }
    }

    static string GetSelectedFolder()
    {
        var guid = Selection.assetGUIDs.FirstOrDefault();
        if (string.IsNullOrEmpty(guid)) return null;
        var p = AssetDatabase.GUIDToAssetPath(guid);
        return AssetDatabase.IsValidFolder(p) ? p : null;
    }

    public static string Summary(List<Entry> e)
    {
        Func<Verdict, string> line = v =>
        {
            var s = e.Where(x => x.Verdict == v).ToList();
            return v + ": " + s.Count + " files, " + (s.Sum(x => x.Size) / 1048576.0).ToString("F1") + " MB";
        };
        return "Bundles: " + e.Select(x => x.Bundle).Distinct().Count() + "\n"
             + line(Verdict.Keep) + "\n" + line(Verdict.Review) + "\n" + line(Verdict.Delete);
    }

    public static string WriteReport(List<Entry> entries)
    {
        var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "BundleCleanerReports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "bundle-cleaner-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".tsv");
        var sb = new StringBuilder();
        sb.AppendLine("verdict\tsize_bytes\tbundle\tpath\treason");
        foreach (var e in entries.OrderBy(x => x.Verdict).ThenByDescending(x => x.Size))
            sb.AppendLine(e.Verdict + "\t" + e.Size + "\t" + e.Bundle + "\t" + e.Path + "\t" + e.Reason);
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    public static void Delete(List<Entry> entries)
    {
        var targets = entries.Where(e => e.Verdict == Verdict.Delete).Select(e => e.Path).ToList();
        int ok = 0, fail = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var p in targets)
            {
                if (AssetDatabase.IsValidFolder(p)) { continue; }   // belt and braces: never a folder
                if (AssetDatabase.DeleteAsset(p)) ok++;
                else { fail++; Debug.LogWarning("[BundleCleanerV2] could not delete " + p); }
            }
        }
        finally { AssetDatabase.StopAssetEditing(); AssetDatabase.Refresh(); }
        Debug.Log("[BundleCleanerV2] deleted " + ok + ", failed " + fail);
        EditorUtility.DisplayDialog("Bundle Cleaner V2",
            "Deleted " + ok + "\nFailed " + fail + "\n\nRebuild the affected bundles and compare sizes.", "OK");
    }

    static void ShowWindow(string title, List<Entry> entries)
    {
        var w = EditorWindow.GetWindow<BundleCleanerV2Window>(title);
        w.Init(entries);
        w.minSize = new Vector2(900, 560);
    }
}

public class BundleCleanerV2Window : EditorWindow
{
    List<SlotBundleCleanerV2.Entry> _all;
    SlotBundleCleanerV2.Verdict _tab = SlotBundleCleanerV2.Verdict.Delete;
    Vector2 _scroll;

    public void Init(List<SlotBundleCleanerV2.Entry> entries) { _all = entries; }

    void OnGUI()
    {
        if (_all == null) return;

        EditorGUILayout.HelpBox(SlotBundleCleanerV2.Summary(_all)
            + "\n\nKeep   - reachable from a root asset, or named in config/code"
            + "\nReview - unreachable, but a SpriteAtlas packs its folder wholesale"
            + "\nDelete - unreachable by every check"
            + "\n\nBlind spots: runtime-composed paths, compiled assemblies, Spine/Lua relative paths."
            + " Rebuild the bundles afterwards and compare sizes.", MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            foreach (SlotBundleCleanerV2.Verdict v in Enum.GetValues(typeof(SlotBundleCleanerV2.Verdict)))
            {
                var n = _all.Count(x => x.Verdict == v);
                if (GUILayout.Toggle(_tab == v, v + " (" + n + ")", EditorStyles.toolbarButton)) _tab = v;
            }
            if (GUILayout.Button("Write TSV report", EditorStyles.toolbarButton, GUILayout.Width(130)))
            {
                var p = SlotBundleCleanerV2.WriteReport(_all);
                Debug.Log("[BundleCleanerV2] report: " + p);
                EditorUtility.RevealInFinder(p);
            }
        }

        var list = _all.Where(x => x.Verdict == _tab).OrderByDescending(x => x.Size).ToList();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var e in list.Take(3000))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label((e.Size / 1024.0).ToString("F0") + " KB", GUILayout.Width(80));
                if (GUILayout.Button(e.Path, EditorStyles.miniLabel))
                {
                    var a = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(e.Path);
                    if (a != null) { EditorGUIUtility.PingObject(a); Selection.activeObject = a; }
                }
                GUILayout.Label(e.Reason, EditorStyles.miniLabel, GUILayout.Width(300));
            }
        }
        if (list.Count > 3000) GUILayout.Label("... " + (list.Count - 3000) + " more, see the TSV report");
        EditorGUILayout.EndScrollView();

        var del = _all.Count(x => x.Verdict == SlotBundleCleanerV2.Verdict.Delete);
        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("DELETE " + del + " assets marked Delete", GUILayout.Height(36)))
        {
            if (EditorUtility.DisplayDialog("Bundle Cleaner V2",
                    "Delete " + del + " assets?\n\nOnly the Delete tab is touched. Review and Keep stay.\n"
                    + "Commit or stash first - this is not undoable from the editor.", "Delete", "Cancel"))
                SlotBundleCleanerV2.Delete(_all);
        }
        GUI.backgroundColor = Color.white;
    }
}

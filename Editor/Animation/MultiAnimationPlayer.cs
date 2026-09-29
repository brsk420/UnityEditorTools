// Put this file into any "Editor" folder, e.g. Assets/Editor/MultiAnimationPlayer.cs
// Open: Tools > Multi Animation Player
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public class MultiAnimationPlayer : EditorWindow
{
    [System.Serializable]
    class Entry
    {
        public Animator animator;
        public int clipIndex;
        public bool enabled = true;
        [System.NonSerialized] public PlayableGraph graph;
        [System.NonSerialized] public AnimationClipPlayable playable;

        public AnimationClip[] Clips =>
            animator != null && animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.animationClips.Distinct().ToArray()
                : new AnimationClip[0];

        public AnimationClip Clip
        {
            get
            {
                var c = Clips;
                return c.Length == 0 ? null : c[Mathf.Clamp(clipIndex, 0, c.Length - 1)];
            }
        }
    }

    [SerializeField] List<Entry> entries = new List<Entry> { new Entry(), new Entry() };
    [SerializeField] bool loop = true;
    [SerializeField] float speed = 1f;

    bool playing;
    float time;
    double lastTime;
    Vector2 scroll;

    [MenuItem("Tools/Multi Animation Player")]
    static void Open() => GetWindow<MultiAnimationPlayer>("Multi Anim Player");

    void OnEnable() => EditorApplication.update += Tick;

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        StopAll();
    }

    float MaxLength()
    {
        float max = 0f;
        foreach (var e in entries)
            if (e.enabled && e.Clip != null) max = Mathf.Max(max, e.Clip.length);
        return max;
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            EditorGUILayout.BeginHorizontal("box");

            e.enabled = EditorGUILayout.Toggle(e.enabled, GUILayout.Width(16));

            EditorGUI.BeginChangeCheck();
            var go = (GameObject)EditorGUILayout.ObjectField(
                e.animator ? e.animator.gameObject : null, typeof(GameObject), true, GUILayout.MinWidth(120));
            if (EditorGUI.EndChangeCheck())
            {
                if (playing) StopAll();
                e.animator = go ? go.GetComponentInChildren<Animator>() : null;
                e.clipIndex = 0;
                if (go && !e.animator) Debug.LogWarning($"[MultiAnimPlayer] No Animator on '{go.name}'");
            }

            var clips = e.Clips;
            if (clips.Length > 0)
            {
                EditorGUI.BeginChangeCheck();
                e.clipIndex = EditorGUILayout.Popup(e.clipIndex, clips.Select(c => c.name).ToArray(), GUILayout.MinWidth(120));
                if (EditorGUI.EndChangeCheck() && playing) RebuildGraph(e);
            }
            else
            {
                EditorGUILayout.LabelField(e.animator ? "no clips" : "—", GUILayout.MinWidth(120));
            }

            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                DestroyGraph(e);
                entries.RemoveAt(i);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button("+ Add object")) entries.Add(new Entry());

        EditorGUILayout.EndScrollView();
        EditorGUILayout.Space();

        loop = EditorGUILayout.Toggle("Loop", loop);
        speed = EditorGUILayout.Slider("Speed", speed, 0f, 3f);

        float max = MaxLength();
        EditorGUI.BeginChangeCheck();
        float t = EditorGUILayout.Slider("Time", time, 0f, Mathf.Max(max, 0.01f));
        if (EditorGUI.EndChangeCheck())
        {
            if (!AnimationMode.InAnimationMode() && !Application.isPlaying) StartAll();
            time = t;
            SampleAll();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(playing ? "❚❚ Pause" : "▶ Play", GUILayout.Height(30)))
        {
            if (playing) playing = false;
            else
            {
                StartAll();
                if (time >= max) time = 0f;
                playing = true;
                lastTime = EditorApplication.timeSinceStartup;
            }
        }
        if (GUILayout.Button("■ Stop", GUILayout.Height(30))) StopAll();
        EditorGUILayout.EndHorizontal();
    }

    void StartAll()
    {
        if (Application.isPlaying)
        {
            foreach (var e in entries) RebuildGraph(e);
        }
        else if (!AnimationMode.InAnimationMode())
        {
            AnimationMode.StartAnimationMode();
        }
    }

    void StopAll()
    {
        playing = false;
        time = 0f;
        foreach (var e in entries) DestroyGraph(e);
        if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode(); // restores edit-mode poses
        Repaint();
    }

    void RebuildGraph(Entry e)
    {
        DestroyGraph(e);
        if (!Application.isPlaying || !e.enabled || e.animator == null || e.Clip == null) return;
        e.playable = AnimationPlayableUtilities.PlayClip(e.animator, e.Clip, out e.graph);
        e.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
    }

    static void DestroyGraph(Entry e)
    {
        if (e.graph.IsValid()) e.graph.Destroy();
    }

    void Tick()
    {
        if (!playing) return;

        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastTime) * speed;
        lastTime = now;

        float max = MaxLength();
        time += dt;
        if (time > max)
        {
            if (loop && max > 0f) time %= max;
            else { time = max; playing = false; }
        }

        SampleAll();
        Repaint();
    }

    void SampleAll()
    {
        if (Application.isPlaying)
        {
            foreach (var e in entries)
            {
                if (!e.graph.IsValid()) continue;
                e.playable.SetTime(ClipTime(e.Clip));
                e.graph.Evaluate();
            }
        }
        else
        {
            if (!AnimationMode.InAnimationMode()) return;
            AnimationMode.BeginSampling();
            foreach (var e in entries)
            {
                if (!e.enabled || e.animator == null || e.Clip == null) continue;
                AnimationMode.SampleAnimationClip(e.animator.gameObject, e.Clip, ClipTime(e.Clip));
            }
            AnimationMode.EndSampling();
            SceneView.RepaintAll();
        }
    }

    // Shorter clips loop inside the longest one when Loop is on, otherwise hold the last frame
    float ClipTime(AnimationClip clip)
    {
        if (clip.length <= 0f) return 0f;
        return loop ? time % clip.length : Mathf.Min(time, clip.length);
    }
}

using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class MultiAnimatorPreviewWindow : EditorWindow
{
    private class AnimatorEntry
    {
        public Animator animator;
        public int clipIndex = 0;
        public bool loop = true; // галочка лупа
    }

    private List<AnimatorEntry> entries = new List<AnimatorEntry>();
    private bool isPlaying = false;
    private float time = 0f;
    private float speed = 1f;
    private double lastEditorTime = 0.0;

    [MenuItem("Tools/Multi Animator Preview")] 
    public static void ShowWindow()
    {
        GetWindow<MultiAnimatorPreviewWindow>("Multi Animator Preview");
    }

    private void OnGUI()
    {
        GUILayout.Label("Drag the Animator objects", EditorStyles.boldLabel);

        int removeIndex = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            EditorGUILayout.BeginVertical("box");

            entry.animator = (Animator)EditorGUILayout.ObjectField("Animator", entry.animator, typeof(Animator), true);

            if (entry.animator != null && entry.animator.runtimeAnimatorController != null)
            {
                var clips = entry.animator.runtimeAnimatorController.animationClips;
                if (clips != null && clips.Length > 0)
                {
                    string[] names = new string[clips.Length];
                    for (int c = 0; c < clips.Length; c++) names[c] = clips[c].name;

                    entry.clipIndex = EditorGUILayout.Popup("Clip", entry.clipIndex, names);
                    entry.clipIndex = Mathf.Clamp(entry.clipIndex, 0, clips.Length - 1);

                    entry.loop = EditorGUILayout.Toggle("Loop", entry.loop);
                }
                else
                {
                    EditorGUILayout.LabelField("No clips found in controller");
                    entry.clipIndex = 0;
                }
            }

            if (GUILayout.Button("Delete")) removeIndex = i;

            EditorGUILayout.EndVertical();
        }

        if (removeIndex >= 0) entries.RemoveAt(removeIndex);

        if (GUILayout.Button("Add Animator")) entries.Add(new AnimatorEntry());

        GUILayout.Space(10);
        speed = EditorGUILayout.Slider("Speed", speed, 0.01f, 5f);

        GUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        if (!isPlaying)
        {
            if (GUILayout.Button("▶ Play All", GUILayout.Height(30))) StartPlaying();
        }
        else
        {
            if (GUILayout.Button("■ Stop", GUILayout.Height(30))) StopPlaying();
        }

        if (GUILayout.Button("Add All Animator in scene", GUILayout.Height(30))) AddAllAnimatorsInScene();
        EditorGUILayout.EndHorizontal();
    }

    private void StartPlaying()
    {
        if (isPlaying) return;
        isPlaying = true;
        time = 0f;
        lastEditorTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += UpdatePreview;
    }

    private void StopPlaying()
    {
        if (!isPlaying) return;
        isPlaying = false;
        EditorApplication.update -= UpdatePreview;
        SceneView.RepaintAll();
    }

    private void UpdatePreview()
    {
        if (!isPlaying) return;

        double now = EditorApplication.timeSinceStartup;
        float delta = (float)(now - lastEditorTime);
        lastEditorTime = now;
        time += delta * speed;

        foreach (var entry in entries)
        {
            if (entry.animator == null) continue;

            var controller = entry.animator.runtimeAnimatorController;
            if (controller == null) continue;

            var clips = controller.animationClips;
            if (clips == null || clips.Length == 0) continue;

            int index = Mathf.Clamp(entry.clipIndex, 0, clips.Length - 1);
            AnimationClip clip = clips[index];
            if (clip == null) continue;

            float localTime;
            if (clip.length > 0f)
                localTime = entry.loop ? (time % clip.length) : Mathf.Min(time, clip.length);
            else
                localTime = 0f;

            clip.SampleAnimation(entry.animator.gameObject, localTime);
        }

        SceneView.RepaintAll();
    }

    private void AddAllAnimatorsInScene()
    {
        entries.Clear();
        var all = FindObjectsOfType<Animator>();
        foreach (var a in all) entries.Add(new AnimatorEntry { animator = a, clipIndex = 0 });
    }

    private void OnDisable()
    {
        if (isPlaying)
        {
            EditorApplication.update -= UpdatePreview;
            isPlaying = false;
        }
    }
}
/*using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class MultiAnimatorPreviewWindow : EditorWindow
{
    private class AnimatorEntry
    {
        public Animator animator;
        public int clipIndex = 0;
    }

    private List<AnimatorEntry> entries = new List<AnimatorEntry>();
    private bool isPlaying = false;
    private float time = 0f;
    private float speed = 1f;
    private double lastEditorTime = 0.0;

    [MenuItem("Tools/Multi Animator Preview")]
    public static void ShowWindow()
    {
        GetWindow<MultiAnimatorPreviewWindow>("Multi Animator Preview");
    }

    private void OnGUI()
    {
        GUILayout.Label("Перетащи сюда объекты с Animator", EditorStyles.boldLabel);

        int removeIndex = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            EditorGUILayout.BeginVertical("box");

            entry.animator = (Animator)EditorGUILayout.ObjectField("Animator", entry.animator, typeof(Animator), true);

            if (entry.animator != null && entry.animator.runtimeAnimatorController != null)
            {
                var clips = entry.animator.runtimeAnimatorController.animationClips;
                if (clips != null && clips.Length > 0)
                {
                    string[] names = new string[clips.Length];
                    for (int c = 0; c < clips.Length; c++) names[c] = clips[c].name;

                    entry.clipIndex = EditorGUILayout.Popup("Clip", entry.clipIndex, names);
                    entry.clipIndex = Mathf.Clamp(entry.clipIndex, 0, clips.Length - 1);
                }
                else
                {
                    EditorGUILayout.LabelField("No clips found in controller");
                    entry.clipIndex = 0;
                }
            }

            if (GUILayout.Button("Удалить")) removeIndex = i;

            EditorGUILayout.EndVertical();
        }

        if (removeIndex >= 0) entries.RemoveAt(removeIndex);

        if (GUILayout.Button("Добавить Animator")) entries.Add(new AnimatorEntry());

        GUILayout.Space(10);
        speed = EditorGUILayout.Slider("Speed", speed, 0.01f, 5f);

        GUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        if (!isPlaying)
        {
            if (GUILayout.Button("▶ Play All", GUILayout.Height(30)))
            {
                StartPlaying();
            }
        }
        else
        {
            if (GUILayout.Button("■ Stop", GUILayout.Height(30)))
            {
                StopPlaying();
            }
        }

        if (GUILayout.Button("Добавить все Animator в сцене", GUILayout.Height(30)))
        {
            AddAllAnimatorsInScene();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void StartPlaying()
    {
        if (isPlaying) return;
        isPlaying = true;
        time = 0f;
        lastEditorTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += UpdatePreview;
    }

    private void StopPlaying()
    {
        if (!isPlaying) return;
        isPlaying = false;
        EditorApplication.update -= UpdatePreview;
        SceneView.RepaintAll();
    }

    private void UpdatePreview()
    {
        if (!isPlaying) return;

        double now = EditorApplication.timeSinceStartup;
        float delta = (float)(now - lastEditorTime);
        lastEditorTime = now;

        time += delta * speed;

        foreach (var entry in entries)
        {
            if (entry.animator == null) continue;

            var controller = entry.animator.runtimeAnimatorController;
            if (controller == null) continue;

            var clips = controller.animationClips;
            if (clips == null || clips.Length == 0) continue;

            int index = Mathf.Clamp(entry.clipIndex, 0, clips.Length - 1);
            AnimationClip clip = clips[index];
            if (clip == null) continue;

            float localTime = clip.length > 0f ? time % clip.length : 0f;
            clip.SampleAnimation(entry.animator.gameObject, localTime);
        }

        SceneView.RepaintAll();
    }

    private void AddAllAnimatorsInScene()
    {
        entries.Clear();
        var all = FindObjectsOfType<Animator>();
        foreach (var a in all)
        {
            entries.Add(new AnimatorEntry { animator = a, clipIndex = 0 });
        }
    }

    private void OnDisable()
    {
        // Убедимся, что делегат отписан при закрытии окна или перекомпиляции
        if (isPlaying)
        {
            EditorApplication.update -= UpdatePreview;
            isPlaying = false;
        }
    }
}*/
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace _Brsk420.EditorTools
{
    public class AnimationOptimizer : EditorWindow
    {
        private int step = 2; // N: Delete every 'step' frame (e.g., 2 = every second)

        [MenuItem("_BrskTools/Animation/Animation Optimizer (Delete Nth frame)")]
        public static void ShowWindow()
        {
            GetWindow<AnimationOptimizer>("Animation Optimizer");
        }

        private void OnGUI()
        {
            GUILayout.Label("Batch Animation Optimization", EditorStyles.boldLabel);

            step = EditorGUILayout.IntField("Deletion Step (N)", step);
            step = Mathf.Max(2, step); // Minimum step is 2

            if (GUILayout.Button("Process Selected Clips"))
            {
                ProcessSelectedAnimationClips(step);
            }

            EditorGUILayout.HelpBox(
                $"Deletes every {step}th keyframe from ALL curves (including sprites) in the selected .anim files." +
                "\nThe last keyframe is always preserved." +
                "\nIt is recommended to back up your files first.", MessageType.Warning);
        }

        private static void ProcessSelectedAnimationClips(int step)
        {
            Object[] selectedObjects = Selection.GetFiltered(typeof(AnimationClip), SelectionMode.Assets);
            int processedCount = 0;

            if (selectedObjects.Length == 0)
            {
                Debug.LogWarning("Please select one or more 'AnimationClip' (.anim) files in the Project window.");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("Optimizing Animations", "Processing clips...", 0.0f);

                for (int i = 0; i < selectedObjects.Length; i++)
                {
                    AnimationClip clip = selectedObjects[i] as AnimationClip;
                    if (clip == null) continue;

                    float progress = (float)i / selectedObjects.Length;
                    EditorUtility.DisplayProgressBar("Optimizing Animations", $"Processing: {clip.name}", progress);

                    ProcessFloatCurves(clip, step);
                    ProcessObjectReferenceCurves(clip, step);

                    EditorUtility.SetDirty(clip);
                    processedCount++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            
            AssetDatabase.SaveAssets();
            Debug.Log($"Done. Optimized {processedCount} clips. Removed every {step}th frame.");
        }

        private static List<T> OptimizeKeys<T>(T[] keys, int step)
        {
            if (keys == null || keys.Length < 3)
            {
                return keys?.ToList() ?? new List<T>();
            }

            var newKeys = new List<T>();
            newKeys.Add(keys[0]); // Always keep the first frame

            T lastKey = keys[keys.Length - 1];

            // Iterate through frames, skipping every Nth, but don't touch the last one
            for (int i = 1; i < keys.Length - 1; i++)
            {
                // The frame's position is (i + 1).
                // If the position is not a multiple of 'step', we keep the key.
                if ((i + 1) % step != 0)
                {
                    newKeys.Add(keys[i]);
                }
            }

            newKeys.Add(lastKey); // Always keep the last frame
            return newKeys;
        }
        
        private static void ProcessFloatCurves(AnimationClip clip, int step)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            
            foreach (EditorCurveBinding binding in bindings)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.keys.Length < 3) continue;

                var newKeys = OptimizeKeys(curve.keys, step);
                AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(newKeys.ToArray()));
            }
        }

        private static void ProcessObjectReferenceCurves(AnimationClip clip, int step)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);

            foreach (EditorCurveBinding binding in bindings)
            {
                ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (keys == null || keys.Length < 3) continue;

                var newKeys = OptimizeKeys(keys, step);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, newKeys.ToArray());
               
            }
        }
    }
}
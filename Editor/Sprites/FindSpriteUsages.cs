using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace _Brsk420.EditorTools
{
    public class FindSpriteUsagesWindow : EditorWindow
    {
        private Sprite _target;
        private List<Object> _prefabResults = new List<Object>();
        private List<Object> _animationResults = new List<Object>();
        private Vector2 _scrollPosition;

        [MenuItem("Assets/Find Sprite Usages (Prefabs & Anims)", false, 20)]
        private static void FindUsages()
        {
            var target = ResolveSprite(Selection.activeObject);
            if (target == null)
            {
                Debug.LogWarning("Selected asset is not a sprite.");
                return;
            }

            var window = GetWindow<FindSpriteUsagesWindow>("Sprite Usages");
            window.Search(target);
        }

        [MenuItem("Assets/Find Sprite Usages (Prefabs & Anims)", true)]
        private static bool ValidateFindUsages()
        {
            return ResolveSprite(Selection.activeObject) != null;
        }

        private static Sprite ResolveSprite(Object selected)
        {
            if (selected is Sprite sprite)
            {
                return sprite;
            }

            if (selected is Texture2D texture)
            {
                var path = AssetDatabase.GetAssetPath(texture);
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }

            return null;
        }

        private void Search(Sprite target)
        {
            _target = target;
            _prefabResults.Clear();
            _animationResults.Clear();

            var texturePath = AssetDatabase.GetAssetPath(target);

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            for (var i = 0; i < prefabGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                EditorUtility.DisplayProgressBar("Find Sprite Usages", $"Scanning prefabs ({i + 1}/{prefabGuids.Length})",
                    (float)i / prefabGuids.Length);

                if (!AssetDatabase.GetDependencies(path, true).Contains(texturePath))
                {
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && PrefabUsesSprite(prefab, target))
                {
                    _prefabResults.Add(prefab);
                }
            }

            var clipGuids = AssetDatabase.FindAssets("t:AnimationClip");
            for (var i = 0; i < clipGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(clipGuids[i]);
                EditorUtility.DisplayProgressBar("Find Sprite Usages", $"Scanning animations ({i + 1}/{clipGuids.Length})",
                    (float)i / clipGuids.Length);

                if (!AssetDatabase.GetDependencies(path, true).Contains(texturePath))
                {
                    continue;
                }

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null && ClipUsesSprite(clip, target))
                {
                    _animationResults.Add(clip);
                }
            }

            EditorUtility.ClearProgressBar();

            Debug.Log($"Found sprite '{target.name}' used in {_prefabResults.Count} prefab(s) and {_animationResults.Count} animation(s).");
        }

        private static bool PrefabUsesSprite(GameObject prefab, Sprite target)
        {
            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }

                var serializedObject = new SerializedObject(component);
                var property = serializedObject.GetIterator();

                while (property.NextVisible(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference &&
                        property.objectReferenceValue == target)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ClipUsesSprite(AnimationClip clip, Sprite target)
        {
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (keyframes.Any(keyframe => keyframe.value == target))
                {
                    return true;
                }
            }

            return false;
        }

        private void OnGUI()
        {
            if (_target == null)
            {
                EditorGUILayout.LabelField("No sprite selected.");
                return;
            }

            EditorGUILayout.ObjectField("Sprite", _target, typeof(Sprite), false);

            GUILayout.Space(10);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.LabelField($"Prefabs ({_prefabResults.Count})", EditorStyles.boldLabel);
            foreach (var prefab in _prefabResults)
            {
                if (GUILayout.Button(prefab.name))
                {
                    EditorGUIUtility.PingObject(prefab);
                    Selection.activeObject = prefab;
                }
            }

            GUILayout.Space(10);

            EditorGUILayout.LabelField($"Animations ({_animationResults.Count})", EditorStyles.boldLabel);
            foreach (var clip in _animationResults)
            {
                if (GUILayout.Button(clip.name))
                {
                    EditorGUIUtility.PingObject(clip);
                    Selection.activeObject = clip;
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }
}

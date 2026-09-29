using _Brsk420.Runtime;
using UnityEditor;
using UnityEngine;

namespace _Brsk420.EditorTools
{
    [CustomEditor(typeof(DownscalePreview))]
    [CanEditMultipleObjects]
    internal sealed class DownscalePreviewEditor : Editor
    {
        private SerializedProperty _quality;

        private void OnEnable()
        {
            _quality = serializedObject.FindProperty("_quality");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Превью качества спрайта ПОСЛЕ Downscale Tool.\n" +
                "Шакалит только визуал в SpriteRenderer — сам ассет не трогается.\n" +
                "100% = оригинал. Меньше = как будто пережали Downscale-тулой.\n" +
                "Работает и для анимации (sprite sequence): каждый кадр шакалится на лету.",
                MessageType.Info);


            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_quality, new GUIContent("Quality (%)"));
            bool changed = EditorGUI.EndChangeCheck();

            DrawSizeInfo();

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset (100%)"))
                {
                    _quality.intValue = 100;
                    changed = true;
                }

                if (GUILayout.Button("-50%"))
                {
                    _quality.intValue = 50;
                    changed = true;
                }

                if (GUILayout.Button("-75%"))
                {
                    _quality.intValue = 25;
                    changed = true;
                }
            }

            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
                // OnValidate on the target rebuilds the preview automatically.
            }
            else
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void DrawSizeInfo()
        {
            if (targets.Length != 1)
            {
                return;
            }

            var preview = target as DownscalePreview;
            if (preview == null)
            {
                return;
            }

            var renderer = preview.GetComponent<SpriteRenderer>();
            var sprite = renderer != null ? renderer.sprite : null;
            var texture = sprite != null ? sprite.texture : null;

            if (texture == null)
            {
                return;
            }

            int q = Mathf.Clamp(_quality.intValue, 1, 100);
            int w = Mathf.Max(1, texture.width * q / 100);
            int h = Mathf.Max(1, texture.height * q / 100);

            EditorGUILayout.LabelField(
                "Simulated size",
                $"{texture.width}x{texture.height}  →  {w}x{h}");
        }
    }
}

using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace _Brsk420.EditorTools
{
    public class AnimationPropertyRenamer : EditorWindow
    {
        // ── rename fields ─────────────────────────────────────────────────────
        private string _oldString = "";
        private string _newString = "";

        private enum RenameTarget { Path, PropertyName, Both }
        private RenameTarget _target = RenameTarget.PropertyName;

        // ── binding list ──────────────────────────────────────────────────────
        private class BindingEntry
        {
            public AnimationClip      Clip;
            public EditorCurveBinding Binding;
            public bool               IsObjRef;
            public bool               Selected;
        }

        private List<BindingEntry> _entries   = new List<BindingEntry>();
        private Vector2            _scroll;
        private string             _statusMsg = "";

        // ── reflection cache ──────────────────────────────────────────────────
        private static System.Type  _animWinType;
        private static FieldInfo    _animEditorField;
        private static FieldInfo    _stateField;
        private static PropertyInfo _activeCurvesProp;
        private static PropertyInfo _bindingProp;
        private static PropertyInfo _clipProp;
        private static bool         _reflectionReady;

        // ─────────────────────────────────────────────────────────────────────
        [MenuItem("_BrskTools/Animation/Animation Property Renamer", false, 10010)]
        static void Init() => GetWindow<AnimationPropertyRenamer>("Anim Prop Renamer");

        // ─────────────────────────────────────────────────────────────────────
        void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Source: selected in Animation window");
            if (GUILayout.Button("↺ Sync", GUILayout.Width(60)))
                RefreshFromAnimationWindow();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_statusMsg))
                EditorGUILayout.HelpBox(_statusMsg, MessageType.Warning);

            if (_entries.Count == 0)
            {
                EditorGUILayout.HelpBox("Select properties in the Animation window, then press ↺ Sync.", MessageType.Info);
                return;
            }

            // ── rename controls ───────────────────────────────────────────────
            _target    = (RenameTarget)EditorGUILayout.EnumPopup("Rename",     _target);
            _oldString = EditorGUILayout.TextField("Find",       _oldString);
            _newString = EditorGUILayout.TextField("Replace To", _newString);

            // ── select-all / none ─────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All",  GUILayout.Width(50))) foreach (var e in _entries) e.Selected = true;
            if (GUILayout.Button("None", GUILayout.Width(50))) foreach (var e in _entries) e.Selected = false;
            GUILayout.FlexibleSpace();
            int sel = 0; foreach (var e in _entries) if (e.Selected) sel++;
            GUILayout.Label($"{sel} / {_entries.Count} selected");
            EditorGUILayout.EndHorizontal();

            // ── binding list ──────────────────────────────────────────────────
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            AnimationClip lastClip = null;
            foreach (var e in _entries)
            {
                if (e.Clip != lastClip)
                {
                    EditorGUILayout.LabelField(e.Clip.name, EditorStyles.boldLabel);
                    lastClip = e.Clip;
                }
                EditorGUILayout.BeginHorizontal();
                e.Selected = EditorGUILayout.Toggle(e.Selected, GUILayout.Width(18));
                EditorGUILayout.LabelField(e.Binding.path,         GUILayout.MinWidth(100), GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField(e.Binding.propertyName, GUILayout.Width(200));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            // ── rename button ─────────────────────────────────────────────────
            GUI.enabled = sel > 0;
            if (GUILayout.Button("Rename", GUILayout.Height(30)))
                ApplyRename();
            GUI.enabled = true;
        }

        // ── read Animation window selection ───────────────────────────────────
        void RefreshFromAnimationWindow()
        {
            _entries.Clear();
            _statusMsg = "";

            if (!EnsureReflection()) { _statusMsg = "Could not reflect AnimationWindow internals."; return; }

            var wins = Resources.FindObjectsOfTypeAll(_animWinType);
            if (wins == null || wins.Length == 0) { _statusMsg = "Animation window is not open."; return; }

            var animEditor = _animEditorField.GetValue(wins[0]);
            if (animEditor == null) { _statusMsg = "AnimEditor is null."; return; }

            var state = _stateField.GetValue(animEditor);
            if (state == null) { _statusMsg = "AnimationWindowState is null."; return; }

            var curves = _activeCurvesProp.GetValue(state) as System.Collections.IList;
            if (curves == null || curves.Count == 0) { _statusMsg = "No properties selected in the Animation window."; return; }

            foreach (var curve in curves)
            {
                var binding = (EditorCurveBinding)_bindingProp.GetValue(curve);
                var clip    = _clipProp.GetValue(curve) as AnimationClip;
                if (clip == null) continue;

                bool isObjRef = AnimationUtility.GetObjectReferenceCurve(clip, binding) != null;
                _entries.Add(new BindingEntry { Clip = clip, Binding = binding, IsObjRef = isObjRef, Selected = true });
            }

            if (_entries.Count == 0) _statusMsg = "No valid bindings found in selection.";
            Repaint();
        }

        // ── apply rename ──────────────────────────────────────────────────────
        void ApplyRename()
        {
            int count = 0;
            foreach (var e in _entries)
            {
                if (!e.Selected) continue;

                string newPath = e.Binding.path;
                string newProp = e.Binding.propertyName;

                if (_target == RenameTarget.Path || _target == RenameTarget.Both)
                    newPath = newPath.Replace(_oldString, _newString);
                if (_target == RenameTarget.PropertyName || _target == RenameTarget.Both)
                    newProp = newProp.Replace(_oldString, _newString);

                if (newPath == e.Binding.path && newProp == e.Binding.propertyName) continue;

                var nb = new EditorCurveBinding { path = newPath, propertyName = newProp, type = e.Binding.type };

                Undo.RecordObject(e.Clip, "Rename Animation Property");
                if (e.IsObjRef)
                {
                    var c = AnimationUtility.GetObjectReferenceCurve(e.Clip, e.Binding);
                    AnimationUtility.SetObjectReferenceCurve(e.Clip, e.Binding, null);
                    AnimationUtility.SetObjectReferenceCurve(e.Clip, nb, c);
                }
                else
                {
                    var c = AnimationUtility.GetEditorCurve(e.Clip, e.Binding);
                    AnimationUtility.SetEditorCurve(e.Clip, e.Binding, null);
                    AnimationUtility.SetEditorCurve(e.Clip, nb, c);
                }
                EditorUtility.SetDirty(e.Clip);
                count++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[AnimPropRenamer] Renamed {count} binding(s).");
            _entries.Clear();
            _statusMsg = $"Done — renamed {count} binding(s).";
        }

        // ── reflection setup ──────────────────────────────────────────────────
        static bool EnsureReflection()
        {
            if (_reflectionReady) return true;

            var editorAsm = typeof(EditorWindow).Assembly;
            var internalAsm = typeof(UnityEditorInternal.InternalEditorUtility).Assembly;

            _animWinType = editorAsm.GetType("UnityEditor.AnimationWindow");
            if (_animWinType == null) return false;

            _animEditorField = _animWinType.GetField("m_AnimEditor",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (_animEditorField == null) return false;

            var animEditorType = _animEditorField.FieldType;

            _stateField = animEditorType.GetField("m_State",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (_stateField == null) return false;

            var stateType = _stateField.FieldType;

            _activeCurvesProp = stateType.GetProperty("activeCurves",
                BindingFlags.Public | BindingFlags.Instance);
            if (_activeCurvesProp == null) return false;

            // AnimationWindowCurve type from the list generic arg
            var listType  = _activeCurvesProp.PropertyType;
            var curveType = listType.IsGenericType ? listType.GetGenericArguments()[0] : null;
            if (curveType == null) curveType = internalAsm.GetType("UnityEditorInternal.AnimationWindowCurve");
            if (curveType == null) return false;

            _bindingProp = curveType.GetProperty("binding", BindingFlags.Public | BindingFlags.Instance);
            _clipProp    = curveType.GetProperty("clip",    BindingFlags.Public | BindingFlags.Instance);

            if (_bindingProp == null || _clipProp == null) return false;

            _reflectionReady = true;
            return true;
        }
    }
}

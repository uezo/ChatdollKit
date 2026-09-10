using System;
using System.Collections.Generic;
using ChatdollKit.Orchestration;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Window = ChatdollKit.UI.MessageWindow.MessageWindow;

namespace ChatdollKit.UI.MessageWindow.Editor
{
    /// <summary>Resolves a dropped GameObject to its conversation publisher instead of an arbitrary component.</summary>
    [CustomEditor(typeof(Window)), CanEditMultipleObjects]
    public sealed class MessageWindowEditor : UnityEditor.Editor
    {
        private string selectionError;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

            var source = serializedObject.FindProperty("Source");
            var sourceRect = EditorGUILayout.GetControlRect();
            var sourceLabel = new GUIContent("Source",
                "A conversation publisher implementing IConversationEventSource. Drop its GameObject or component.");
            using (new EditorGUI.PropertyScope(sourceRect, sourceLabel, source))
            {
                EditorGUI.showMixedValue = source.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                var selected = EditorGUI.ObjectField(sourceRect, sourceLabel, source.objectReferenceValue,
                    typeof(Object), !EditorUtility.IsPersistent(target));
                var changed = EditorGUI.EndChangeCheck();
                EditorGUI.showMixedValue = false;
                if (changed) SelectSource(selected);
            }

            if (!string.IsNullOrEmpty(selectionError)) EditorGUILayout.HelpBox(selectionError, MessageType.Warning);
            if (!source.hasMultipleDifferentValues && source.objectReferenceValue != null &&
                !(source.objectReferenceValue is IConversationEventSource))
            {
                EditorGUILayout.HelpBox("Source must implement IConversationEventSource. The current component cannot publish conversation events.",
                    MessageType.Warning);
                var candidates = FindSources(source.objectReferenceValue);
                if (candidates.Length > 0 && GUILayout.Button("Select conversation source on this object"))
                    ChooseSource(candidates);
            }

            DrawPropertiesExcluding(serializedObject, "m_Script", "Source");
            serializedObject.ApplyModifiedProperties();
        }

        private void SelectSource(Object selected)
        {
            selectionError = null;
            if (selected == null)
            {
                SetSource(null);
                return;
            }
            var candidates = FindSources(selected);
            if (candidates.Length == 0)
            {
                selectionError = "The selected object has no component implementing IConversationEventSource.";
                return;
            }
            ChooseSource(candidates);
        }

        private void ChooseSource(MonoBehaviour[] candidates)
        {
            if (candidates.Length == 1) { SetSource(candidates[0]); return; }
            var menu = new GenericMenu();
            var current = serializedObject.FindProperty("Source").objectReferenceValue;
            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                menu.AddItem(new GUIContent($"{i + 1}: {candidate.name} ({candidate.GetType().Name})"),
                    candidate == current, () => { if (this != null && candidate != null) SetSource(candidate); });
            }
            menu.ShowAsContext();
        }

        private void SetSource(MonoBehaviour source)
        {
            if (!AssignSource(targets, source))
                selectionError = "A prefab asset cannot reference a source in the scene.";
            else selectionError = null;
            serializedObject.Update();
            Repaint();
        }

        /// <summary>Preserves an explicitly selected publisher. A GameObject or another component yields
        /// all compatible publishers on that same object, so the Inspector can ask when there are several.</summary>
        public static MonoBehaviour[] FindSources(Object selected)
        {
            if (selected == null) return Array.Empty<MonoBehaviour>();
            if (selected is MonoBehaviour source && source is IConversationEventSource)
                return new[] { source };
            var owner = selected as GameObject;
            if (owner == null && selected is Component component) owner = component.gameObject;
            if (owner == null) return Array.Empty<MonoBehaviour>();
            var sources = new List<MonoBehaviour>();
            foreach (var candidate in owner.GetComponents<MonoBehaviour>())
                if (candidate != null && candidate is IConversationEventSource) sources.Add(candidate);
            return sources.ToArray();
        }

        /// <summary>Applies an explicit selection with Unity serialization, Undo and prefab override support.
        /// Invalid selections never modify the existing field.</summary>
        public static bool AssignSource(Object[] windows, MonoBehaviour source)
        {
            if (source != null && !(source is IConversationEventSource)) return false;
            if (windows == null || windows.Length == 0) return false;
            foreach (var window in windows)
            {
                if (!(window is Window)) return false;
                if (source != null && EditorUtility.IsPersistent(window) && !EditorUtility.IsPersistent(source)) return false;
            }
            var serialized = new SerializedObject(windows);
            serialized.Update();
            serialized.FindProperty("Source").objectReferenceValue = source;
            serialized.ApplyModifiedProperties();
            return true;
        }
    }
}

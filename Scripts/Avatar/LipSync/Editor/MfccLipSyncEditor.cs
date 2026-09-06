using ChatdollKit.Avatar.LipSync;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ChatdollKit.Avatar.LipSync.Editor
{
    [CustomEditor(typeof(MfccLipSync))]
    [CanEditMultipleObjects]
    public sealed class MfccLipSyncEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var profile = serializedObject.FindProperty("Profile");
            if (!profile.hasMultipleDifferentValues && profile.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Leave Profile empty to use the bundled default-female profile.", MessageType.Info);
            }

            var rendererProperty = serializedObject.FindProperty("TargetRenderer");
            if (rendererProperty.hasMultipleDifferentValues) return;
            var usesDefaultRenderer = false;
            var mappings = serializedObject.FindProperty("Mappings");
            for (var i = 0; i < mappings.arraySize; i++)
            {
                var mappingRenderer = mappings.GetArrayElementAtIndex(i).FindPropertyRelative("Renderer");
                if (!mappingRenderer.hasMultipleDifferentValues && mappingRenderer.objectReferenceValue == null)
                    usesDefaultRenderer = true;
            }
            if (!usesDefaultRenderer) return;
            var renderer = rendererProperty.objectReferenceValue as SkinnedMeshRenderer;
            if (renderer == null || renderer.sharedMesh == null)
            {
                EditorGUILayout.HelpBox("Assign a mouth mesh to Target Renderer or a mapping's Renderer to choose blend shapes.", MessageType.Info);
            }
            else if (renderer.sharedMesh.blendShapeCount == 0)
            {
                EditorGUILayout.HelpBox("Target Renderer has no blend shapes. Select a mesh with mouth blend shapes.", MessageType.Warning);
            }
        }
    }

    [CustomPropertyDrawer(typeof(VisemeMapping))]
    public sealed class VisemeMappingDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUIUtility.singleLineHeight * 4f + EditorGUIUtility.standardVerticalSpacing * 3f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(line, property.FindPropertyRelative("Viseme"), new GUIContent("Viseme"));
            line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.PropertyField(line, property.FindPropertyRelative("Renderer"),
                new GUIContent("Renderer", "Optional mesh for this mapping. Leave empty to use Target Renderer."));
            line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            DrawBlendShape(line, property);
            line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.PropertyField(line, property.FindPropertyRelative("MaxWeight"),
                new GUIContent("Max Weight", "Blend shape weight when this viseme is fully open. Usually 100."));
            EditorGUI.EndProperty();
        }

        private static void DrawBlendShape(Rect position, SerializedProperty mapping)
        {
            var property = mapping.FindPropertyRelative("BlendShape");
            var label = new GUIContent("Blend Shape", "Mouth blend shape for this viseme. None leaves it unassigned.");
            var rendererProperty = mapping.FindPropertyRelative("Renderer");
            if (!rendererProperty.hasMultipleDifferentValues && rendererProperty.objectReferenceValue == null)
                rendererProperty = property.serializedObject.FindProperty("TargetRenderer");
            var renderer = rendererProperty == null ? null : rendererProperty.objectReferenceValue as SkinnedMeshRenderer;
            var mesh = renderer == null ? null : renderer.sharedMesh;
            if (mesh == null || rendererProperty.hasMultipleDifferentValues)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var names = new List<string> { string.Empty };
            var choices = new List<GUIContent> { new GUIContent("None (Unassigned)") };
            for (var index = 0; index < mesh.blendShapeCount; index++)
            {
                var name = mesh.GetBlendShapeName(index);
                names.Add(name);
                choices.Add(new GUIContent(name));
            }

            var selected = names.FindIndex(name => string.Equals(name, property.stringValue, StringComparison.Ordinal));
            if (selected < 0)
            {
                // Keep a stale reference visible until the user explicitly replaces it.
                selected = names.Count;
                names.Add(property.stringValue);
                choices.Add(new GUIContent("Missing: " + property.stringValue));
            }

            EditorGUI.BeginProperty(position, label, property);
            var previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var next = EditorGUI.Popup(position, label, selected, choices.ToArray());
            if (EditorGUI.EndChangeCheck()) property.stringValue = names[next];
            EditorGUI.showMixedValue = previousMixedValue;
            EditorGUI.EndProperty();
        }
    }
}

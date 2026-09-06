using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.LLM;
using ChatdollKit.SpeechPipeline.TTS;
using ChatdollKit.SpeechPipeline.VAD;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

[assembly: InternalsVisibleTo("ChatdollKit.SpeechPipeline.Unity.Editor.Tests")]

namespace ChatdollKit.SpeechPipeline.Editor
{
    [CustomEditor(typeof(LocalSpeechPipeline))]
    [CanEditMultipleObjects]
    public sealed class LocalSpeechPipelineEditor : LiveSpeechComponentEditor
    {
        internal sealed class ComponentChoice
        {
            public readonly MonoBehaviour Component;
            public readonly GUIContent Label;

            public ComponentChoice(MonoBehaviour component, string label)
            {
                Component = component;
                Label = new GUIContent(label);
            }
        }

        protected override void DrawComponentInspector()
        {
            // Object fields still support assigning references to several selected pipelines.
            if (targets.Length != 1)
            {
                base.DrawComponentInspector();
                return;
            }

            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            DrawSelection("Vad", "VAD", typeof(SpeechDetectorComponent), optional: true);
            DrawSelection("Stt", "STT", typeof(SpeechRecognizerComponent));
            DrawSelection("Llm", "LLM", typeof(LlmServiceComponent));
            DrawSelection("Tts", "TTS", typeof(SpeechSynthesizerComponent));
            DrawPropertiesExcluding(serializedObject, "m_Script", "Vad", "Stt", "Llm", "Tts");
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSelection(string propertyName, string label, Type componentType, bool optional = false)
        {
            var property = serializedObject.FindProperty(propertyName);
            var assigned = property.objectReferenceValue as MonoBehaviour;
            var choices = BuildChoices((LocalSpeechPipeline)target, componentType, assigned, optional);
            var selected = Array.FindIndex(choices, choice => choice.Component == assigned);
            var rect = EditorGUILayout.GetControlRect();
            var content = new GUIContent(label,
                "Auto uses the single enabled component on this GameObject. Select a component explicitly when several are attached.");
            EditorGUI.BeginProperty(rect, content, property);
            EditorGUI.BeginChangeCheck();
            var next = EditorGUI.Popup(rect, content, Math.Max(0, selected), choices.Select(choice => choice.Label).ToArray());
            if (EditorGUI.EndChangeCheck()) SelectChoice(property, choices[next]);
            EditorGUI.EndProperty();
            using (new EditorGUI.IndentLevelScope())
                EditorGUILayout.PropertyField(property, new GUIContent("Reference", "Drag a component here to use one on another GameObject."));
        }

        internal static ComponentChoice[] BuildChoices(LocalSpeechPipeline pipeline,
            Type componentType, MonoBehaviour assigned, bool optional = false)
        {
            var components = pipeline.GetComponents(componentType).Cast<MonoBehaviour>().ToArray();
            var enabled = components.Where(component => component.isActiveAndEnabled).ToArray();
            var automatic = enabled.Length == 1 ? DescribeComponent(enabled[0]) :
                enabled.Length > 1 ? "select one: " + enabled.Length + " enabled" :
                optional ? "none; optional" : "none found";
            var choices = new List<ComponentChoice> { new ComponentChoice(null, "Auto (" + automatic + ")") };
            foreach (var component in components)
                choices.Add(new ComponentChoice(component, DescribeComponent(component)));
            if (assigned != null && !components.Contains(assigned))
                choices.Add(new ComponentChoice(assigned,
                    DescribeComponent(assigned) + " @ " + GetObjectPath(assigned.transform) + " (external)"));
            return choices.ToArray();
        }

        internal static void SelectChoice(SerializedProperty property, ComponentChoice choice)
            => property.objectReferenceValue = choice.Component;

        private static string DescribeComponent(MonoBehaviour component)
        {
            var type = component.GetType();
            var sameType = component.GetComponents(type).Where(item => item.GetType() == type).ToArray();
            var label = type.Name;
            if (sameType.Length > 1) label += " #" + (Array.IndexOf(sameType, component) + 1);
            if (!component.enabled) label += " (disabled)";
            else if (!component.gameObject.activeInHierarchy) label += " (inactive)";
            return label;
        }

        private static string GetObjectPath(Transform transform)
            => transform.parent == null ? transform.name : GetObjectPath(transform.parent) + "/" + transform.name;
    }
}

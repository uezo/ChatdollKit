using ChatdollKit.SpeechPipeline;
using UnityEditor;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.Editor
{
    [CustomEditor(typeof(LiveSpeechComponent), true)]
    [CanEditMultipleObjects]
    public class LiveSpeechComponentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawComponentInspector();
            if (targets.Length != 1) return;
            var component = (LiveSpeechComponent)target;
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Settings are applied when the pipeline starts. Editable service settings also apply during Play Mode.", MessageType.Info);
                return;
            }
            if (!component.IsBound) EditorGUILayout.HelpBox("This component is not used by a running pipeline.", MessageType.Info);
            else if (component.NeedsRestart) EditorGUILayout.HelpBox(component.SettingsError, MessageType.Warning);
            else if (!string.IsNullOrEmpty(component.SettingsError)) EditorGUILayout.HelpBox(component.SettingsError, MessageType.Error);
            else EditorGUILayout.HelpBox(component.IsApplying ? "Waiting to apply settings…" : "Settings applied.", MessageType.Info);
            if (component.IsBound && GUILayout.Button("Apply Settings")) _ = component.ApplySettingsAsync();
        }

        protected virtual void DrawComponentInspector() => DrawDefaultInspector();

        public override bool RequiresConstantRepaint() => Application.isPlaying;
    }
}

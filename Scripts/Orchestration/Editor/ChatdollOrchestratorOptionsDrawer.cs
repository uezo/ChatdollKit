using UnityEditor;
using UnityEngine;

namespace ChatdollKit.Orchestration.Editor
{
    [CustomPropertyDrawer(typeof(ChatdollOrchestratorOptions))]
    public sealed class ChatdollOrchestratorOptionsDrawer : PropertyDrawer
    {
        private static readonly GUIContent AllowBargeInLabel = new GUIContent("Allow Barge In");

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2
                + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(nameof(ChatdollOrchestratorOptions.MaxPendingAudioFrames)), true)
                + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(nameof(ChatdollOrchestratorOptions.MaxPendingPresentations)), true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var mute = property.FindPropertyRelative(nameof(ChatdollOrchestratorOptions.MuteMicrophoneDuringResponse));
            var audio = property.FindPropertyRelative(nameof(ChatdollOrchestratorOptions.MaxPendingAudioFrames));
            var presentations = property.FindPropertyRelative(nameof(ChatdollOrchestratorOptions.MaxPendingPresentations));
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            // Keep the existing serialized value so saved scenes retain their interruption policy.
            EditorGUI.BeginProperty(row, AllowBargeInLabel, mute);
            var previousMixedValue = EditorGUI.showMixedValue;
            try
            {
                EditorGUI.showMixedValue = mute.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                var allowBargeIn = EditorGUI.Toggle(row, AllowBargeInLabel, !mute.boolValue);
                if (EditorGUI.EndChangeCheck()) mute.boolValue = !allowBargeIn;
            }
            finally
            {
                EditorGUI.showMixedValue = previousMixedValue;
                EditorGUI.EndProperty();
            }

            row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
            row.height = EditorGUI.GetPropertyHeight(audio, true);
            EditorGUI.PropertyField(row, audio, true);

            row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
            row.height = EditorGUI.GetPropertyHeight(presentations, true);
            EditorGUI.PropertyField(row, presentations, true);
        }
    }
}

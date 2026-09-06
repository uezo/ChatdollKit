using ChatdollKit.Avatar;
using ChatdollKit.Avatar.LipSync;
using UnityEditor;
using UnityEngine;
using ILipSyncHelper = ChatdollKit.Model.ILipSyncHelper;

namespace ChatdollKit.Avatar.Editor
{
    [CustomEditor(typeof(SpeechController))]
    [CanEditMultipleObjects]
    public sealed class SpeechControllerEditor : UnityEditor.Editor
    {
        private bool rejectedEngineAssignment;
        private bool rejectedHelperAssignment;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AudioSource"));
            DrawLipSyncComponent<ILipSync>("lipSyncEngineComponent", "Lip Sync Engine",
                "Assign the lip sync engine that receives speech playback. Leave empty when using uLipSync.",
                ref rejectedEngineAssignment,
                "Assign a lip sync engine such as MfccLipSync. Setup helpers cannot be used here.",
                "Assign MfccLipSync to use MFCC lip sync. Leave empty when using uLipSync.");
            DrawLipSyncComponent<ILipSyncHelper>("LipSyncHelper", "Lip Sync Helper",
                "Helper used for ConfigureViseme during avatar setup and ResetViseme when playback ends.",
                ref rejectedHelperAssignment,
                "Assign a helper implementing ILipSyncHelper, such as uLipSyncHelper or MfccLipSyncHelper.",
                "Assign a helper to reset the mouth when playback ends. Avatar setup configures its viseme mappings.");
            DrawPropertiesExcluding(serializedObject, "m_Script", "AudioSource", "lipSyncEngineComponent", "LipSyncHelper");
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawLipSyncComponent<T>(string propertyName, string labelText, string tooltip,
            ref bool rejectedAssignment, string invalidMessage, string hint) where T : class
        {
            var property = serializedObject.FindProperty(propertyName);
            var label = new GUIContent(labelText, tooltip);
            var position = EditorGUILayout.GetControlRect();
            EditorGUI.BeginProperty(position, label, property);
            var previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            var allowSceneObjects = true;
            foreach (var editedObject in serializedObject.targetObjects)
            {
                if (!EditorUtility.IsPersistent(editedObject)) continue;
                allowSceneObjects = false;
                break;
            }
            EditorGUI.BeginChangeCheck();
            var selected = EditorGUI.ObjectField(position, label, property.objectReferenceValue, typeof(MonoBehaviour), allowSceneObjects);
            if (EditorGUI.EndChangeCheck())
            {
                rejectedAssignment = selected != null && !(selected is T);
                if (!rejectedAssignment) property.objectReferenceValue = selected;
            }
            EditorGUI.showMixedValue = previousMixedValue;
            EditorGUI.EndProperty();

            var assigned = property.objectReferenceValue;
            if (rejectedAssignment || (!property.hasMultipleDifferentValues && assigned != null && !(assigned is T)))
            {
                EditorGUILayout.HelpBox(invalidMessage, MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(hint, MessageType.Info);
            }
        }
    }
}

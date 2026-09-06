using ChatdollKit.Avatar.LipSync;
using UnityEditor;
using UnityEngine;

namespace ChatdollKit.Extension.VRM.Editor
{
    [CustomEditor(typeof(VRMMfccLipSyncHelper)), CanEditMultipleObjects]
    public sealed class VRMMfccLipSyncHelperEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

            EditorGUILayout.HelpBox(
                "Uses MfccLipSync on this GameObject, which is added automatically. " +
                "Run Setup ModelController to create A/I/U/E/O mappings from the avatar's VRM BlendShape clips, " +
                "including each renderer and weight.", MessageType.Info);
            // VRM clips define the mouth shapes; inherited VRC name settings do not apply here.
            serializedObject.ApplyModifiedProperties();
        }
    }
}

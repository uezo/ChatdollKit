using UnityEngine;
using UnityEditor;


namespace ChatdollKit.Examples.ModelControl
{
    [CustomEditor(typeof(ModelControllerExample))]
    public class ModelControllerExampleEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var app = target as ModelControllerExample;

            if (GUILayout.Button("Animate"))
            {
                _ = app.Animate();
            }
            else if (GUILayout.Button("Say"))
            {
                _ = app.Say();
            }
            else if (GUILayout.Button("Face"))
            {
                _ = app.Face();
            }
            else if (GUILayout.Button("AnimatedSay"))
            {
                _ = app.AnimatedSay();
            }
            else if (GUILayout.Button("Stop"))
            {
                app.CancelRequest();
            }
        }
    }
}

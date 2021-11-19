using UnityEngine;
using UnityEditor;

namespace ChatdollKit.Extension.Gatebox
{
    [CustomEditor(typeof(GateboxApplication), true)]
    public class GateboxApplicationEditor : ChatdollEditor
    {
        private GUIStyle labelStyle;
        private bool isHumanSensorLeft = false;
        private bool isHumanSensorRight = false;

        private void Awake()
        {
            labelStyle = new GUIStyle();
            labelStyle.fontStyle = FontStyle.Bold;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (!EditorApplication.isPlaying) return;

            var app = target as GateboxApplication;

            GUILayout.Space(10f);
            GUILayout.Label("Gatebox Button", labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Single"))
            {
                app.OnGateboxButtonSingleTap?.Invoke();
            }
            if (GUILayout.Button("Double"))
            {
                app.OnGateboxButtonDoubleTap?.Invoke();
            }
            if (GUILayout.Button("Long"))
            {
                app.OnGateboxButtonLong?.Invoke();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);
            GUILayout.Label("Human Sensor", labelStyle);
            GUILayout.BeginHorizontal();
            isHumanSensorLeft = EditorGUILayout.ToggleLeft("Left", isHumanSensorLeft);
            if (isHumanSensorLeft)
            {
                app.OnHumanSensorLeftOn?.Invoke();
            }
            else
            {
                app.OnHumanSensorLeftOff?.Invoke();
            }
            isHumanSensorRight = EditorGUILayout.ToggleLeft("Right", isHumanSensorRight);
            if (isHumanSensorRight)
            {
                app.OnHumanSensorRightOn?.Invoke();
            }
            else
            {
                app.OnHumanSensorRightOff?.Invoke();
            }
            GUILayout.EndHorizontal();
        }
    }
}

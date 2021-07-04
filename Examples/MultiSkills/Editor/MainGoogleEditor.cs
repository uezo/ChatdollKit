using UnityEngine;
using UnityEditor;

namespace ChatdollKit.Examples.MultiSkills
{
    [CustomEditor(typeof(MainGoogle))]
    public class MainGoogleEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var app = target as MainGoogle;

            if (GUILayout.Button("Start Chat"))
            {
#pragma warning disable CS4014
                app.StartChatAsync();
#pragma warning restore CS4014
            }

            if (GUILayout.Button("Stop Chat"))
            {
                app.StopChat();
            }
        }
    }
}

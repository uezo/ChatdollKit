using UnityEngine;
using UnityEditor;
using ChatdollKit.Dialog;
using ChatdollKit.IO;
using ChatdollKit.Model;

namespace ChatdollKit
{
    [CustomEditor(typeof(ChatdollApplication), true)]
    public class ChatdollEditor : Editor
    {
        private GUIStyle headerStyle = new GUIStyle();
        private string dummyText = string.Empty;

        public override void OnInspectorGUI()
        {
            var app = target as ChatdollApplication;

            // Playmode only
            if (EditorApplication.isPlaying)
            {
                // Start and Stop button
                GUILayout.BeginHorizontal();
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
                GUILayout.EndHorizontal();

                // Send request button
                app.voiceRequestProvider.UseDummy = EditorGUILayout.ToggleLeft("Send text request instead of voice", app.voiceRequestProvider.UseDummy);
                GUILayout.BeginHorizontal();
                dummyText = EditorGUILayout.TextField(dummyText);
                if (GUILayout.Button("Send"))
                {
                    app.voiceRequestProvider.DummyText = dummyText;
                    dummyText = string.Empty;
                    GUI.FocusControl(string.Empty); // Remove focus to clear input field
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(20.0f);
            }

            base.OnInspectorGUI();

            GUILayout.Space(20.0f);

            // Always available
            if (GUILayout.Button("Save Config"))
            {
                var config = app.CreateConfig();
                if (config == null)
                {
                    Debug.LogWarning("Implement `CreateConfig()` to save configuration");
                }
                else
                {
                    var configName = string.IsNullOrEmpty(app.ApplicationName) ? app.name : app.ApplicationName;
                    AssetDatabase.CreateAsset(config, $"Assets/Resources/{configName}.asset");
                }
            }
        }

        // Remove ChatdollKit components and objects
        [MenuItem("CONTEXT/ChatdollApplication/Remove ChatdollKit components")]
        private static void RemoveComponents(MenuCommand menuCommand)
        {
            if (!EditorUtility.DisplayDialog("Confirmation", "Are you sure to remove all ChatdollKit components?", "OK", "Cancel"))
            {
                return;
            }

            var chatdoll = menuCommand.context as ChatdollApplication;
            var gameObject = chatdoll.gameObject;

            // Main Application
            DestroyComponents(gameObject.GetComponents<ChatdollApplication>());

            // RequestProviders and WakeWordListener
            DestroyComponents(gameObject.GetComponents<IRequestProvider>());
            DestroyComponents(gameObject.GetComponents<WakeWordListenerBase>());

            // Microphone (Voice recorder depends on this)
            DestroyComponents(gameObject.GetComponents<ChatdollMicrophone>());

            // Voice loaders
            DestroyComponents(gameObject.GetComponents<IVoiceLoader>());

            // Prompter
            DestroyComponents(gameObject.GetComponents<HttpPrompter>());

            // Router
            DestroyComponents(gameObject.GetComponents<ISkillRouter>());

            // LipSyncHelper
            DestroyComponents(gameObject.GetComponents<ILipSyncHelper>());

            // Skills
            DestroyComponents(gameObject.GetComponents<ISkill>());

            // ModelController
            foreach (var c in gameObject.GetComponents<ModelController>())
            {
                // VoiceAudio Control Object
                DestroyImmediate(c.AudioSource.gameObject);
                // ModelController itself
                DestroyImmediate(c);
            }
        }

        public static void DestroyComponents(object[] components)
        {
            foreach (var c in components)
            {
                DestroyImmediate(c as UnityEngine.Object);
            }
        }
    }
}

using UnityEngine;
using UnityEditor;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.IO;
using ChatdollKit.Model;

namespace ChatdollKit
{
    [CustomEditor(typeof(ChatdollApplication), true)]
    public class ChatdollEditor : Editor
    {
        private string requestText = string.Empty;
        private string wakewordText = string.Empty;

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

                GUILayout.Space(5.0f);

                if (GUILayout.Button("Stop Chat"))
                {
                    app.StopChat();
                }
                GUILayout.EndHorizontal();

                // Send wakeword button
                GUILayout.BeginHorizontal();
                wakewordText = EditorGUILayout.TextField(wakewordText);
                if (GUILayout.Button("Send WakeWord"))
                {
                    app.SendWakeWord(wakewordText);
                    wakewordText = string.Empty;
                    GUI.FocusControl(string.Empty); // Remove focus to clear input field
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(5.0f);

                // Send request button
                GUILayout.BeginHorizontal();
                requestText = EditorGUILayout.TextField(requestText);

                GUILayout.Space(5.0f);

                if (GUILayout.Button("Send Request"))
                {
                    app.SendTextRequest(requestText);
                    requestText = string.Empty;
                    GUI.FocusControl(string.Empty); // Remove focus to clear input field
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(20.0f);
            }

            base.OnInspectorGUI();

            GUILayout.Space(20.0f);

            // Always available
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Load Config"))
            {
                var config = app.LoadConfig();
                if (config == null)
                {
                    Debug.LogWarning("Implement `CreateConfig()` to save configuration");
                }
            }

            GUILayout.Space(5.0f);

            if (GUILayout.Button("Save Config"))
            {
                var config = app.CreateConfig();
                if (config == null)
                {
                    Debug.LogWarning("Implement `CreateConfig()` to save configuration");
                }
                else
                {
                    AssetDatabase.CreateAsset(config, $"Assets/Resources/{app.ApplicationName}.asset");
                }
            }

            GUILayout.EndHorizontal();
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

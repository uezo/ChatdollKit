using UnityEngine;
using UnityEditor;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.IO;
using ChatdollKit.Model;
using ChatdollKit.Extension.Azure;
using ChatdollKit.Extension.Google;
using ChatdollKit.Extension.OpenAI;
using ChatdollKit.Extension.Watson;

namespace ChatdollKit
{
    [CustomEditor(typeof(ChatdollKit), true)]
    public class ChatdollEditor : Editor
    {
        private GUIStyle headerStyle;

        private string requestText = string.Empty;
        private string wakewordText = string.Empty;

        public override void OnInspectorGUI()
        {
            if (headerStyle == null)
            {
                headerStyle = GUI.skin.label;
                headerStyle.fontStyle = FontStyle.Bold;
            }

            var app = target as ChatdollKit;

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

            // Show public fields
            base.OnInspectorGUI();

            // Select Speech Service
            GUILayout.Space(12.0f);
            EditorGUILayout.LabelField("Speech Service", headerStyle);
            EditorGUI.BeginChangeCheck();
            app.SpeechService = (CloudService)EditorGUILayout.EnumPopup("Speech Service", app.SpeechService);
            if (EditorGUI.EndChangeCheck())
            {
                WebVoiceLoaderBase ttsLoader = null;
                IVoiceRequestProvider voiceRequestProvider = null;
                IWakeWordListener wakeWordListener = null;

                if (app.SpeechService == CloudService.Azure)
                {
                    ttsLoader = app.gameObject.GetComponent<AzureTTSLoader>() ?? app.gameObject.AddComponent<AzureTTSLoader>();
                    voiceRequestProvider = app.gameObject.GetComponent<AzureVoiceRequestProvider>() ?? app.gameObject.AddComponent<AzureVoiceRequestProvider>();
                    wakeWordListener = app.gameObject.GetComponent<AzureWakeWordListener>() ?? app.gameObject.AddComponent<AzureWakeWordListener>();
                }
                else if (app.SpeechService == CloudService.Google)
                {
                    ttsLoader = app.gameObject.GetComponent<GoogleTTSLoader>() ?? app.gameObject.AddComponent<GoogleTTSLoader>();
                    voiceRequestProvider = app.gameObject.GetComponent<GoogleVoiceRequestProvider>() ?? app.gameObject.AddComponent<GoogleVoiceRequestProvider>();
                    wakeWordListener = app.gameObject.GetComponent<GoogleWakeWordListener>() ?? app.gameObject.AddComponent<GoogleWakeWordListener>();
                }
                else if (app.SpeechService == CloudService.OpenAI)
                {
                    ttsLoader = app.gameObject.GetComponent<OpenAITTSLoader>() ?? app.gameObject.AddComponent<OpenAITTSLoader>();
                    voiceRequestProvider = app.gameObject.GetComponent<OpenAIVoiceRequestProvider>() ?? app.gameObject.AddComponent<OpenAIVoiceRequestProvider>();
                    wakeWordListener = app.gameObject.GetComponent<OpenAIWakeWordListener>() ?? app.gameObject.AddComponent<OpenAIWakeWordListener>();
                }
                else if (app.SpeechService == CloudService.Watson)
                {
                    ttsLoader = app.gameObject.GetComponent<WatsonTTSLoader>() ?? app.gameObject.AddComponent<WatsonTTSLoader>();
                    voiceRequestProvider = app.gameObject.GetComponent<WatsonVoiceRequestProvider>() ?? app.gameObject.AddComponent<WatsonVoiceRequestProvider>();
                    wakeWordListener = app.gameObject.GetComponent<WatsonWakeWordListener>() ?? app.gameObject.AddComponent<WatsonWakeWordListener>();
                }
                EnableComponents(app.gameObject, ttsLoader, voiceRequestProvider, wakeWordListener);
                EditorUtility.SetDirty(app);
            }

            // Configure Speech Service
            if (app.SpeechService == CloudService.Azure)
            {
                EditorGUI.BeginChangeCheck();
                app.AzureApiKey = EditorGUILayout.TextField("API Key", app.AzureApiKey);
                app.AzureRegion = EditorGUILayout.TextField("Region", app.AzureRegion);
                app.AzureLanguage = EditorGUILayout.TextField("Language", app.AzureLanguage);
                app.AzureGender = EditorGUILayout.TextField("Gender", app.AzureGender);
                app.AzureSpeakerName = EditorGUILayout.TextField("Speaker name", app.AzureSpeakerName);
                if (EditorGUI.EndChangeCheck())
                {
                    var ttsLoader = app.gameObject.GetComponent<AzureTTSLoader>() ?? app.gameObject.AddComponent<AzureTTSLoader>();
                    var voiceRequestProvider = app.gameObject.GetComponent<AzureVoiceRequestProvider>() ?? app.gameObject.AddComponent<AzureVoiceRequestProvider>();
                    var wakeWordListener = app.gameObject.GetComponent<AzureWakeWordListener>() ?? app.gameObject.AddComponent<AzureWakeWordListener>();
                    ttsLoader.Configure(app.AzureApiKey, app.AzureLanguage, app.AzureGender, app.AzureSpeakerName, app.AzureRegion, true);
                    voiceRequestProvider.Configure(app.AzureApiKey, app.AzureLanguage, app.AzureRegion, true);
                    wakeWordListener.Configure(app.AzureApiKey, app.AzureLanguage, app.AzureRegion, true);

                    EditorUtility.SetDirty(app);
                }
            }
            else if (app.SpeechService == CloudService.Google)
            {
                EditorGUI.BeginChangeCheck();
                app.GoogleApiKey = EditorGUILayout.TextField("API Key", app.GoogleApiKey);
                app.GoogleLanguage = EditorGUILayout.TextField("Language", app.GoogleLanguage);
                app.GoogleGender = EditorGUILayout.TextField("Gender", app.GoogleGender);
                app.GoogleSpeakerName = EditorGUILayout.TextField("Speaker name", app.GoogleSpeakerName);
                if (EditorGUI.EndChangeCheck())
                {
                    var ttsLoader = app.gameObject.GetComponent<GoogleTTSLoader>() ?? app.gameObject.AddComponent<GoogleTTSLoader>();
                    var voiceRequestProvider = app.gameObject.GetComponent<GoogleVoiceRequestProvider>() ?? app.gameObject.AddComponent<GoogleVoiceRequestProvider>();
                    var wakeWordListener = app.gameObject.GetComponent<GoogleWakeWordListener>() ?? app.gameObject.AddComponent<GoogleWakeWordListener>();
                    ttsLoader.Configure(app.GoogleApiKey, app.GoogleLanguage, app.GoogleGender, app.GoogleSpeakerName, true);
                    voiceRequestProvider.Configure(app.GoogleApiKey, app.GoogleLanguage, true);
                    wakeWordListener.Configure(app.GoogleApiKey, app.GoogleLanguage, true);

                    EditorUtility.SetDirty(app);
                }
            }
            else if (app.SpeechService == CloudService.OpenAI)
            {
                EditorGUI.BeginChangeCheck();
                app.OpenAIApiKey = EditorGUILayout.TextField("API Key", app.OpenAIApiKey);
                app.OpenAILanguage = EditorGUILayout.TextField("Language", app.OpenAILanguage);
                app.OpenAIVoice = EditorGUILayout.TextField("Voice", app.OpenAIVoice);
                if (EditorGUI.EndChangeCheck())
                {
                    var ttsLoader = app.gameObject.GetComponent<OpenAITTSLoader>() ?? app.gameObject.AddComponent<OpenAITTSLoader>();
                    var voiceRequestProvider = app.gameObject.GetComponent<OpenAIVoiceRequestProvider>() ?? app.gameObject.AddComponent<OpenAIVoiceRequestProvider>();
                    var wakeWordListener = app.gameObject.GetComponent<OpenAIWakeWordListener>() ?? app.gameObject.AddComponent<OpenAIWakeWordListener>();
                    ttsLoader.Configure(app.OpenAIApiKey, app.OpenAIVoice, true);
                    voiceRequestProvider.Configure(app.OpenAIApiKey, app.OpenAILanguage, true);
                    wakeWordListener.Configure(app.OpenAIApiKey, app.OpenAILanguage, true);

                    EditorUtility.SetDirty(app);
                }
            }
            else if (app.SpeechService == CloudService.Watson)
            {
                EditorGUI.BeginChangeCheck();
                app.WatsonTTSApiKey = EditorGUILayout.TextField("API Key (TTS)", app.WatsonTTSApiKey);
                app.WatsonTTSBaseUrl = EditorGUILayout.TextField("Base Url (TTS)", app.WatsonTTSBaseUrl);
                app.WatsonTTSSpeakerName = EditorGUILayout.TextField("Speaker name", app.WatsonTTSSpeakerName);
                app.WatsonSTTApiKey = EditorGUILayout.TextField("API Key (STT)", app.WatsonSTTApiKey);
                app.WatsonSTTBaseUrl = EditorGUILayout.TextField("Base Url (STT)", app.WatsonSTTBaseUrl);
                app.WatsonSTTModel = EditorGUILayout.TextField("Model", app.WatsonSTTModel);
                app.WatsonSTTRemoveWordSeparation = EditorGUILayout.Toggle("Remove Word Separation", app.WatsonSTTRemoveWordSeparation);
                if (EditorGUI.EndChangeCheck())
                {
                    var ttsLoader = app.gameObject.GetComponent<WatsonTTSLoader>() ?? app.gameObject.AddComponent<WatsonTTSLoader>();
                    var voiceRequestProvider = app.gameObject.GetComponent<WatsonVoiceRequestProvider>() ?? app.gameObject.AddComponent<WatsonVoiceRequestProvider>();
                    var wakeWordListener = app.gameObject.GetComponent<WatsonWakeWordListener>() ?? app.gameObject.AddComponent<WatsonWakeWordListener>();
                    ttsLoader.Configure(app.WatsonTTSApiKey, app.WatsonTTSBaseUrl, app.WatsonTTSSpeakerName, true);
                    voiceRequestProvider.Configure(app.WatsonSTTApiKey, app.WatsonSTTModel, app.WatsonSTTBaseUrl, app.WatsonSTTRemoveWordSeparation, true);
                    wakeWordListener.Configure(app.WatsonSTTApiKey, app.WatsonSTTModel, app.WatsonSTTBaseUrl, app.WatsonSTTRemoveWordSeparation, true);

                    EditorUtility.SetDirty(app);
                }
            }

            GUILayout.Space(20.0f);

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Load Config"))
            {
                var config = app.LoadConfig();
                GUI.FocusControl(string.Empty); // Remove focus to load values to input fields
                if (config == null)
                {
                    Debug.LogWarning("Configuration is not loaded");
                }
            }

            GUILayout.Space(5.0f);

            if (GUILayout.Button("Save Config"))
            {
                var config = app.CreateConfig();
                GUI.FocusControl(string.Empty);
                if (config == null)
                {
                    Debug.LogWarning("Configuration is not saved");
                }
                else
                {
                    AssetDatabase.CreateAsset(config, $"Assets/Resources/{app.ApplicationName}.asset");
                }
            }

            GUILayout.EndHorizontal();
        }

        private void EnableComponents(GameObject gameObject, WebVoiceLoaderBase ttsLoader = null, IVoiceRequestProvider voiceRequestProvider = null, IWakeWordListener wakeWordListener = null)
        {
            // TTSLoaders
            foreach (var vl in gameObject.GetComponents<WebVoiceLoaderBase>())
            {
                if (vl.Type == VoiceLoaderType.TTS)
                {
                    vl.IsDefault = vl == ttsLoader;
                }
            }
            if (ttsLoader != null)
            {
                ttsLoader.IsDefault = true;
            }

            foreach (var rp in gameObject.GetComponents<IVoiceRequestProvider>())
            {
                ((MonoBehaviour)rp).enabled = rp == voiceRequestProvider;
            }

            foreach (var wwl in gameObject.GetComponents<IWakeWordListener>())
            {
                ((MonoBehaviour)wwl).enabled = wwl == wakeWordListener;
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

            var chatdoll = menuCommand.context as ChatdollKit;
            var gameObject = chatdoll.gameObject;

            // Main Application
            DestroyComponents(gameObject.GetComponents<ChatdollKit>());

            // RequestProviders and WakeWordListener
            DestroyComponents(gameObject.GetComponents<IRequestProvider>());
            DestroyComponents(gameObject.GetComponents<IWakeWordListener>());

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

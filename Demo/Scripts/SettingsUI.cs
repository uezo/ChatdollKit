using UnityEngine;
using UnityEngine.UI;
using ChatdollKit.Dialog;
using ChatdollKit.Extension.OpenAI;
using ChatdollKit.Model;

#if UNITY_WEBGL && !UNITY_EDITOR
using ChatGPTService = ChatdollKit.LLM.ChatGPT.ChatGPTServiceWebGL;
using ClaudeService = ChatdollKit.LLM.Claude.ClaudeServiceWebGL;
using GeminiService = ChatdollKit.LLM.Gemini.GeminiServiceWebGL;
#else
using ChatGPTService = ChatdollKit.LLM.ChatGPT.ChatGPTService;
using ClaudeService = ChatdollKit.LLM.Claude.ClaudeService;
using GeminiService = ChatdollKit.LLM.Gemini.GeminiService;
#endif

using VoicevoxTTS = ChatdollKit.Extension.Voicevox.VoicevoxTTSLoader;


namespace ChatdollKit.Demo
{
    public class SettingsUI : MonoBehaviour
    {
        [SerializeField]
        private GameObject chatdollKitObject;

        // Setting UI
        [SerializeField]
        private GameObject SettingPanel;
        [SerializeField]
        private InputField OpenAIApiKeyInput;
        [SerializeField]
        private Dropdown LLMDropdown;
        [SerializeField]
        private InputField LLMModelInput;
        [SerializeField]
        private InputField LLMApiKeyInput;
        [SerializeField]
        private InputField TTSSpeakerInput;
        [SerializeField]
        private InputField TTSUrlInput;

        private DialogController dialogController;
        private ModelController modelController;
        private OpenAIWakeWordListener wakeWordListener;
        private OpenAIVoiceRequestProvider voiceRequestProvider;
        private OpenAITTSLoader openAITTSLoader;
        private VoicevoxTTS voicevoxTTSLoader;

        private ChatGPTService chatGPTService;
        private ClaudeService claudeService;
        private GeminiService geminiService;

        private void Start()
        {
            if (chatdollKitObject == null)
            {
                chatdollKitObject = FindObjectOfType<ChatdollKit>()?.gameObject;
                if (chatdollKitObject == null)
                {
                    Debug.LogError("ChatdollKit is not found in this scene.");
                }
            }

            dialogController = chatdollKitObject.GetComponent<DialogController>();
            modelController = chatdollKitObject.GetComponent<ModelController>();
            wakeWordListener = chatdollKitObject.GetComponent<OpenAIWakeWordListener>();
            voiceRequestProvider = chatdollKitObject.GetComponent<OpenAIVoiceRequestProvider>();
            openAITTSLoader = chatdollKitObject.GetComponent<OpenAITTSLoader>();
            voicevoxTTSLoader = chatdollKitObject.GetComponent<VoicevoxTTS>();
        }

        public void Show()
        {
            if (chatdollKitObject == null)
            {
                chatdollKitObject = FindObjectOfType<ChatdollKit>()?.gameObject;
                if (chatdollKitObject == null)
                {
                    Debug.LogError("ChatdollKit is not found in this scene.");
                }
            }

            if (dialogController == null)
            {
                dialogController = chatdollKitObject.GetComponent<DialogController>();
            }

            // Get LLMService components
            chatGPTService = dialogController.GetComponent<ChatGPTService>();
            claudeService = dialogController.GetComponent<ClaudeService>();
            geminiService = dialogController.GetComponent<GeminiService>();

            // Get API key
            OpenAIApiKeyInput.text = voiceRequestProvider.ApiKey;

            // Set current LLM
            LLMDropdown.options.Clear();
            if (chatGPTService != null)
            {
                LLMDropdown.options.Add(new Dropdown.OptionData("ChatGPT"));
                if (chatGPTService.IsEnabled)
                {
                    LLMDropdown.value = LLMDropdown.options.Count - 1;
                }
            }
            if (claudeService != null)
            {
                LLMDropdown.options.Add(new Dropdown.OptionData("Claude"));
                if (claudeService.IsEnabled)
                {
                    LLMDropdown.value = LLMDropdown.options.Count - 1;
                }
            }
            if (geminiService != null)
            {
                LLMDropdown.options.Add(new Dropdown.OptionData("Gemini"));
                if (geminiService.IsEnabled)
                {
                    LLMDropdown.value = LLMDropdown.options.Count - 1;
                }
            }

            OnChangeLLMDropdown();

            // TTS
            if (!string.IsNullOrEmpty(voicevoxTTSLoader.EndpointUrl))
            {
                TTSUrlInput.text = voicevoxTTSLoader.EndpointUrl;
                TTSSpeakerInput.text = voicevoxTTSLoader.Speaker.ToString();
            }

            SettingPanel.SetActive(true);
        }

        public void ApplySettings()
        {
            // Set API keys
            wakeWordListener.ApiKey = OpenAIApiKeyInput.text;
            voiceRequestProvider.ApiKey = OpenAIApiKeyInput.text;
            openAITTSLoader.ApiKey = OpenAIApiKeyInput.text;

            // Switch LLM
            var selectedItem = LLMDropdown.options[LLMDropdown.value];

            if (selectedItem.text == "ChatGPT")
            {
                chatGPTService.IsEnabled = true;
                chatGPTService.ApiKey = OpenAIApiKeyInput.text;
                chatGPTService.Model = LLMModelInput.text;
            }
            else if (selectedItem.text == "Claude")
            {
                claudeService.IsEnabled = true;
                claudeService.ApiKey = LLMApiKeyInput.text;
                claudeService.Model = LLMModelInput.text;
            }
            else if (selectedItem.text == "Gemini")
            {
                geminiService.IsEnabled = true;
                geminiService.ApiKey = LLMApiKeyInput.text;
                geminiService.Model = LLMModelInput.text;
            }

            // Switch TTS
            if (string.IsNullOrEmpty(TTSUrlInput.text))
            {
                modelController.RegisterTTSFunction(openAITTSLoader.Name, openAITTSLoader.GetAudioClipAsync, true);
            }
            else
            {
                voicevoxTTSLoader.EndpointUrl = TTSUrlInput.text;
                try
                {
                    voicevoxTTSLoader.Speaker = int.Parse(TTSSpeakerInput.text);
                }
                catch
                {
                    voicevoxTTSLoader.Speaker = 2;
                }
                voicevoxTTSLoader.ClearCache();
                modelController.RegisterTTSFunction(voicevoxTTSLoader.Name, voicevoxTTSLoader.GetAudioClipAsync, true);
            }

            SettingPanel.SetActive(false);
        }

        public void OnChangeLLMDropdown()
        {
            var selectedItem = LLMDropdown.options[LLMDropdown.value];

            if (selectedItem.text == "ChatGPT")
            {
                LLMApiKeyInput.text = string.Empty;
                LLMModelInput.text = chatGPTService.Model;
            }
            else if (selectedItem.text == "Claude")
            {
                LLMApiKeyInput.text = claudeService.ApiKey;
                LLMModelInput.text = claudeService.Model;
            }
            else if (selectedItem.text == "Gemini")
            {
                LLMApiKeyInput.text = geminiService.ApiKey;
                LLMModelInput.text = geminiService.Model;
            }
        }
    }
}

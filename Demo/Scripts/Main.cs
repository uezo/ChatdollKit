using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.IO;
using ChatdollKit.Model;
using ChatdollKit.LLM;

using ChatdollKit.Extension.OpenAI;
using VoicevoxTTS = ChatdollKit.Extension.Voicevox.VoicevoxTTSLoader;

#if UNITY_WEBGL && !UNITY_EDITOR
using ChatGPTService = ChatdollKit.LLM.ChatGPT.ChatGPTServiceWebGL;
using ClaudeService = ChatdollKit.LLM.Claude.ClaudeServiceWebGL;
using GeminiService = ChatdollKit.LLM.Gemini.GeminiServiceWebGL;
#else
using ChatGPTService = ChatdollKit.LLM.ChatGPT.ChatGPTService;
using ClaudeService = ChatdollKit.LLM.Claude.ClaudeService;
using GeminiService = ChatdollKit.LLM.Gemini.GeminiService;
#endif

namespace ChatdollKit.Demo
{
    public class Main : MonoBehaviour
    {
        // ChatdollKit components
        private ModelController modelController;
        private DialogController dialogController;

        private OpenAIWakeWordListener wakeWordListener;
        private OpenAIVoiceRequestProvider voiceRequestProvider;
        private OpenAITTSLoader openAITTSLoader;
        private VoicevoxTTS voicevoxTTSLoader;

        private ChatGPTService chatGPTService;
        private ClaudeService claudeService;
        private GeminiService geminiService;

        //private DateTime sleepStartAt;

        // Input UI
        [SerializeField]
        private InputField requestInput;
        [SerializeField]
        private Image imagePreview;
        [SerializeField]
        private Image imageIcon;
        [SerializeField]
        private GameObject imagePathPanel;
        [SerializeField]
        private InputField imagePathInput;
        [SerializeField]
        private SimpleCamera simpleCamera;

        // Setting UI
        public GameObject SettingPanel;
        public InputField OpenAIApiKeyInput;
        public Dropdown LLMDropdown;
        public InputField LLMModelInput;
        public InputField LLMApiKeyInput;
        public InputField TTSSpeakerInput;
        public InputField TTSUrlInput;

        public GameObject PromptPanel;
        public Dropdown PromptDropdown;
        public InputField PromptInput;

        void Start()
        {
            // Get ChatdollKit components
            modelController = gameObject.GetComponent<ModelController>();
            dialogController = gameObject.GetComponent<DialogController>();

            wakeWordListener = gameObject.GetComponent<OpenAIWakeWordListener>();
            voiceRequestProvider = gameObject.GetComponent<OpenAIVoiceRequestProvider>();
            openAITTSLoader = gameObject.GetComponent<OpenAITTSLoader>();
            voicevoxTTSLoader = gameObject.GetComponent<VoicevoxTTS>();

            chatGPTService = gameObject.GetComponent<ChatGPTService>();
            claudeService = gameObject.GetComponent<ClaudeService>();
            geminiService = gameObject.GetComponent<GeminiService>();

            chatGPTService.CaptureImage = CaptureImageAsync;

            // Animation and face expression for idling
            modelController.AddIdleAnimation(new Model.Animation("BaseParam", 6, 5f));

            //// Add idle animations with `mode` if you want to have extra idling modes
            //modelController.AddIdleAnimation(new Model.Animation("BaseParam", 101, 5f), mode: "sleep");
            //modelController.AddIdleFace("sleep", "Blink");
            //sleepStartAt = DateTime.UtcNow.AddSeconds(secondsToSleep);

            // Animation and face expression for processing
            var processingAnimation = new List<Model.Animation>();
            processingAnimation.Add(new Model.Animation("BaseParam", 3, 0.3f));
            processingAnimation.Add(new Model.Animation("BaseParam", 3, 20.0f, "AGIA_Layer_nodding_once_01", "Additive Layer"));
            var processingFace = new List<FaceExpression>();
            processingFace.Add(new FaceExpression("Blink", 3.0f));

            var neutralFaceRequest = new List<FaceExpression>();
            neutralFaceRequest.Add(new FaceExpression("Neutral"));

#pragma warning disable CS1998
            dialogController.OnRequestAsync = async (request, token) =>
            {
                if (request.Type == RequestType.Voice)
                {
                    if (imagePreview.sprite != null)
                    {
                        // Set image to request
                        var imageBytes = imagePreview.sprite.texture.EncodeToJPG();
                        imagePreview.sprite = null;
                        imagePreview.gameObject.SetActive(false);
                        imageIcon.gameObject.SetActive(true);
                        request.Payloads["imageBytes"] = imageBytes;
                    }
                }

                modelController.StopIdling();
                modelController.Animate(processingAnimation);
                modelController.SetFace(processingFace);
            };
            dialogController.OnStartShowingResponseAsync = async (response, token) =>
            {
                modelController.SetFace(neutralFaceRequest);
            };
#pragma warning restore CS1998

            // Animations used in conversation
            foreach (var llmContentSkill in gameObject.GetComponents<LLMContentSkill>())
            {
                if (llmContentSkill.GetType() == typeof(LLMContentSkill))
                {
                    llmContentSkill.RegisterAnimation("angry_hands_on_waist", new Model.Animation("BaseParam", 0, 3.0f));
                    llmContentSkill.RegisterAnimation("brave_hand_on_chest", new Model.Animation("BaseParam", 1, 3.0f));
                    llmContentSkill.RegisterAnimation("calm_hands_on_back", new Model.Animation("BaseParam", 2, 3.0f));
                    llmContentSkill.RegisterAnimation("concern_right_hand_front", new Model.Animation("BaseParam", 3, 3.0f));
                    llmContentSkill.RegisterAnimation("energetic_right_fist_up", new Model.Animation("BaseParam", 4, 3.0f));
                    llmContentSkill.RegisterAnimation("energetic_right_hand_piece", new Model.Animation("BaseParam", 5, 3.0f));
                    llmContentSkill.RegisterAnimation("pitiable_right_hand_on_back_head", new Model.Animation("BaseParam", 7, 3.0f));
                    llmContentSkill.RegisterAnimation("surprise_hands_open_front", new Model.Animation("BaseParam", 8, 3.0f));
                    llmContentSkill.RegisterAnimation("walking", new Model.Animation("BaseParam", 9, 3.0f));
                    llmContentSkill.RegisterAnimation("waving_arm", new Model.Animation("BaseParam", 10, 3.0f));
                    llmContentSkill.RegisterAnimation("look_away", new Model.Animation("BaseParam", 6, 3.0f, "AGIA_Layer_look_away_01", "Additive Layer"));
                    llmContentSkill.RegisterAnimation("nodding_once", new Model.Animation("BaseParam", 6, 3.0f, "AGIA_Layer_nodding_once_01", "Additive Layer"));
                    llmContentSkill.RegisterAnimation("swinging_body", new Model.Animation("BaseParam", 6, 3.0f, "AGIA_Layer_swinging_body_01", "Additive Layer"));
                    break;
                }
            }

            // Setting on start
            if (string.IsNullOrEmpty(voiceRequestProvider.ApiKey) && SettingPanel != null)
            {
                ShowSettingPanel();
            }
            else
            {
                chatGPTService.ApiKey = voiceRequestProvider.ApiKey;
            }

            // Animation and face expression for start up
            var animationOnStart = new List<Model.Animation>();
            animationOnStart.Add(new Model.Animation("BaseParam", 6, 0.5f));
            animationOnStart.Add(new Model.Animation("BaseParam", 10, 3.0f));
            //animationOnStart.Add(new Model.Animation("BaseParam", 101, 20.0f));
            modelController.Animate(animationOnStart);

            var faceOnStart = new List<FaceExpression>();
            faceOnStart.Add(new FaceExpression("Joy", 3.0f));
            modelController.SetFace(faceOnStart);
        }

        //private void Update()
        //{
        //    // Example for switching idling mode

        //    if (dialogController.IsChatting)
        //    {
        //        // Update the time to sleep when chatting
        //        sleepStartAt = DateTime.UtcNow.AddSeconds(secondsToSleep);
        //    }

        //    if (DateTime.UtcNow > sleepStartAt && modelController.IdlingMode != "sleep")
        //    {
        //        // Change mode to sleep after time to sleep
        //        _ = modelController.ChangeIdlingModeAsync("sleep");
        //    }
        //}

        // Autonomous vision control
        private async UniTask<byte[]> CaptureImageAsync(string source)
        {
            if (simpleCamera != null)
            {
                try
                {
                    return await simpleCamera.CaptureImageAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error at CaptureImageAsync: {ex.Message}\n{ex.StackTrace}");
                }
            }

            return null;
        }

        // Conversation UI
        public void OnWakeButton()
        {
            _ = dialogController.StartDialogAsync();
        }

        public void OnSubmitRequestInput()
        {
            var inputText = requestInput.text.Trim();
            requestInput.text = string.Empty;
            if (string.IsNullOrEmpty(inputText)) return;

            if (dialogController.Status == DialogController.DialogStatus.Idling)
            {
                var dialogRequest = new DialogRequest("_", new WakeWord() { Text = inputText, SkipPrompt = true }.CloneWithRecognizedText(inputText), true);
                _ = dialogController.StartDialogAsync(dialogRequest);
            }
            else
            {
                ((IVoiceRequestProvider)dialogController.RequestProviders[RequestType.Voice]).TextInput = inputText;
            }
        }

        // Image UI
        public void OnImageButton()
        {
            ActivateImagePathPanel(!imagePathPanel.activeSelf);
        }

        public void OnSubmitImagePath()
        {
            var path = imagePathInput.text;
            ActivateImagePathPanel(false);

            if (string.IsNullOrEmpty(path))
            {
                // Clear image when path is empty
                imagePreview.sprite = null;
                imagePreview.gameObject.SetActive(false);
                imageIcon.gameObject.SetActive(true);
                return;
            }

            // Load image from file
            var imageBytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2);
            texture.LoadImage(imageBytes);

            // Resize image
            var resizedTexture = ResizeTexture(texture, 640);

            // Set image to preview
            var sprite = Sprite.Create(resizedTexture, new Rect(0.0f, 0.0f, resizedTexture.width, resizedTexture.height), new Vector2(0.5f, 0.5f));
            imagePreview.preserveAspect = true;
            imagePreview.sprite = sprite;
            imageIcon.gameObject.SetActive(false);
            imagePreview.gameObject.SetActive(true);
        }

        private void ActivateImagePathPanel(bool activate)
        {
            imagePathInput.text = string.Empty;
            imagePathPanel.SetActive(activate);
            requestInput.enabled = !activate;

            if (activate)
            {
                imagePathInput.Select();
            }
            else
            {
                requestInput.Select();
            }
        }

        private static Texture2D ResizeTexture(Texture2D originalTexture, int maxLength)
        {
            var width = originalTexture.width;
            var height = originalTexture.height;

            if (Mathf.Max(width, height) < maxLength)
            {
                // Use original texture if smaller than the max
                return originalTexture;
            }

            // Calculate the resized size keeping aspect ratio
            var aspect = (float)width / height;
            if (width > height)
            {
                width = maxLength;
                height = Mathf.RoundToInt(maxLength / aspect);
            }
            else
            {
                height = maxLength;
                width = Mathf.RoundToInt(maxLength * aspect);
            }

            // Make resized texture
            var resizedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u = (float)x / (width - 1);
                    float v = (float)y / (height - 1);
                    resizedTexture.SetPixel(x, y, originalTexture.GetPixelBilinear(u, v));
                }
            }
            resizedTexture.Apply();

            return resizedTexture;
        }

        // Settings
        public void ShowSettingPanel()
        {
            // Get API key
            OpenAIApiKeyInput.text = voiceRequestProvider.ApiKey;

            // Set current LLM
            if (chatGPTService.IsEnabled)
            {
                LLMDropdown.value = 0;
            }
            else if (claudeService.IsEnabled)
            {
                LLMDropdown.value = 1;
            }
            else if (geminiService.IsEnabled)
            {
                LLMDropdown.value = 2;
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
            if (LLMDropdown.value == 0)
            {
                chatGPTService.IsEnabled = true;
                chatGPTService.ApiKey = OpenAIApiKeyInput.text;
                chatGPTService.Model = LLMModelInput.text;
            }
            else if (LLMDropdown.value == 1)
            {
                claudeService.IsEnabled = true;
                claudeService.ApiKey = LLMApiKeyInput.text;
                claudeService.Model = LLMModelInput.text;
            }
            else if (LLMDropdown.value == 2)
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
            if (LLMDropdown.value == 0)
            {
                LLMModelInput.text = chatGPTService.Model;
            }
            else if (LLMDropdown.value == 1)
            {
                LLMApiKeyInput.text = claudeService.ApiKey;
                LLMModelInput.text = claudeService.Model;
            }
            else if (LLMDropdown.value == 2)
            {
                LLMApiKeyInput.text = geminiService.ApiKey;
                LLMModelInput.text = geminiService.Model;
            }
        }

        // Prompt
        public void ShowPromptPanel()
        {
            // Set current LLM
            if (chatGPTService.IsEnabled)
            {
                PromptDropdown.value = 0;
            }
            else if (claudeService.IsEnabled)
            {
                PromptDropdown.value = 1;
            }
            else if (geminiService.IsEnabled)
            {
                PromptDropdown.value = 2;
            }

            OnChangePromptDropdown();

            PromptPanel.SetActive(true);
        }

        public void ApplyPrompt()
        {
            if (PromptDropdown.value == 0)
            {
                chatGPTService.SystemMessageContent = PromptInput.text;
            }
            else if (PromptDropdown.value == 1)
            {
                claudeService.SystemMessageContent = PromptInput.text;
            }
            else if (PromptDropdown.value == 2)
            {
                geminiService.SystemMessageContent = PromptInput.text;
            }

            PromptPanel.SetActive(false);
        }

        public void OnChangePromptDropdown()
        {
            if (PromptDropdown.value == 0)
            {
                PromptInput.text = chatGPTService.SystemMessageContent;
            }
            else if (PromptDropdown.value == 1)
            {
                PromptInput.text = claudeService.SystemMessageContent;
            }
            else if (PromptDropdown.value == 2)
            {
                PromptInput.text = geminiService.SystemMessageContent;
            }
        }
    }
}

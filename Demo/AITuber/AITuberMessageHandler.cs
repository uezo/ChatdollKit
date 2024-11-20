using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using VRM;
using ChatdollKit.IO;
using ChatdollKit.Model;
using ChatdollKit.Dialog;
using ChatdollKit.LLM;
using ChatdollKit.LLM.ChatGPT;
using ChatdollKit.LLM.Claude;
using ChatdollKit.LLM.Gemini;
using ChatdollKit.SpeechSynthesizer;
using ChatdollKit.Extension.VRM;

namespace ChatdollKit.Demo
{
    public class AITuberMessageHandler : MonoBehaviour
    {
        [SerializeField]
        private IExternalInboundMessageHandler handler;

        [SerializeField]
        private GameObject aiAvatarObject;
        private ModelController modelController;
        private ModelRequestBroker modelRequestBroker;
        private DialogProcessor dialogProcessor;
        private DialogPriorityManager dialogPriorityManager;
        private ChatGPTService chatGPTService;
        private ClaudeService claudeService;
        private GeminiService geminiService;
        private VoicevoxSpeechSynthesizer voicevoxSpeechSynthesizer;
        private StyleBertVits2SpeechSynthesizer styleBertVits2SpeechSynthesizer;

        [SerializeField]
        private VRMLoader vrmLoader;
        [SerializeField]
        private Camera mainCamera;

        private Dictionary<(string endpoint, string operation), List<Func<ExternalInboundMessage, UniTask>>> handlers = new();

        private void Awake()
        {
            handler = gameObject.GetComponent<IExternalInboundMessageHandler>();
            handler.OnDataReceived += HandleExternalMessage;
        }

        private void Start()
        {
            modelController = aiAvatarObject.GetComponent<ModelController>();
            modelRequestBroker = aiAvatarObject.GetComponent<ModelRequestBroker>();

            dialogProcessor = aiAvatarObject.GetComponent<DialogProcessor>();
            dialogPriorityManager = aiAvatarObject.GetComponent<DialogPriorityManager>();

            chatGPTService = aiAvatarObject.GetComponent<ChatGPTService>();
            claudeService = aiAvatarObject.GetComponent<ClaudeService>();
            geminiService = aiAvatarObject.GetComponent<GeminiService>();

            voicevoxSpeechSynthesizer = aiAvatarObject.GetComponent<VoicevoxSpeechSynthesizer>();
            styleBertVits2SpeechSynthesizer = aiAvatarObject.GetComponent<StyleBertVits2SpeechSynthesizer>();
        }

        private async UniTask HandleExternalMessage(ExternalInboundMessage message)
        {
            // Execute registered handlers
            var registeredHandlers = GetHandlers(message.Endpoint, message.Operation);
            foreach (var handler in registeredHandlers)
            {
                await handler(message);
            }

            // Built-in handlers

            // Model
            if (message.Endpoint == "model")
            {
                if (message.Operation == "perform")
                {
                    modelRequestBroker.SetRequest(message.Text);
                }
                else if (message.Operation == "load")
                {
                    await vrmLoader.LoadCharacterAsync(message.Text);
                    var vrmLookAtHead = modelController.AvatarModel.GetComponent<VRMLookAtHead>();
                    vrmLookAtHead.Target = mainCamera.transform;
                    vrmLookAtHead.UpdateType = UpdateType.LateUpdate;
                }

                else if (message.Operation == "appearance")
                {
                    var positionX = (float)(double)message.Payloads["position_x"];
                    var rotationY = (float)(double)message.Payloads["rotation_y"];
                    modelController.AvatarModel.transform.position = new Vector3(positionX, 0, 0);
                    modelController.AvatarModel.transform.rotation = Quaternion.Euler(0, rotationY, 0);

                    var cameraPositionY = (float)(double)message.Payloads["camera_position_y"];
                    var cameraRotationX = (float)(double)message.Payloads["camera_rotation_x"];
                    var cameraFieldOfView = (float)(double)message.Payloads["camera_field_of_view"];
                    mainCamera.transform.position = new Vector3(0, cameraPositionY, 2);
                    mainCamera.transform.rotation = Quaternion.Euler(cameraRotationX, 180, 0);
                    mainCamera.fieldOfView = cameraFieldOfView;
                }
            }

            // Dialog
            else if (message.Endpoint == "dialog")
            {
                if (message.Operation == "process")
                {
                    dialogPriorityManager.SetRequest(message.Text, message.Payloads, message.Priority);
                }
                else if (message.Operation == "clear_request_queue")
                {
                    dialogPriorityManager.ClearDialogRequestQueue(message.Priority);
                }
                else if (message.Operation == "clear_context")
                {
                    dialogProcessor.ClearContext();
                }
            }

            // Speech Synthesizer
            else if (message.Endpoint == "speech_synthesizer")
            {
                if (message.Operation == "activate")
                {

                }
                var speechSynthesizerName = ((string)message.Payloads["name"]).ToLower();

                if (speechSynthesizerName == "voicevox")
                {
                    styleBertVits2SpeechSynthesizer.IsEnabled = false;
                    voicevoxSpeechSynthesizer.IsEnabled = true;
                    voicevoxSpeechSynthesizer.EndpointUrl = (string)message.Payloads["url"];
                    voicevoxSpeechSynthesizer.Speaker = int.Parse($"{message.Payloads["voicevox_speaker"]}");
                    modelController.SpeechSynthesizerFunc = voicevoxSpeechSynthesizer.GetAudioClipAsync;
                }
                else if (speechSynthesizerName == "style-bert-vits2")
                {
                    voicevoxSpeechSynthesizer.IsEnabled = false;
                    styleBertVits2SpeechSynthesizer.IsEnabled = true;
                    styleBertVits2SpeechSynthesizer.EndpointUrl = (string)message.Payloads["url"];
                    styleBertVits2SpeechSynthesizer.ModelId = int.Parse($"{message.Payloads["sbv2_model_id"]}");
                    styleBertVits2SpeechSynthesizer.SpeakerId = int.Parse($"{message.Payloads["sbv2_speaker_id"]}");
                    modelController.SpeechSynthesizerFunc = styleBertVits2SpeechSynthesizer.GetAudioClipAsync;
                }
            }

            // LLM
            else if (message.Endpoint == "llm")
            {
                if (message.Operation == "activate")
                {
                    var name = ((string)message.Payloads["name"]).ToLower();
                    var apiKey = message.Payloads.ContainsKey("api_key") ?  (string)message.Payloads["api_key"] : null;
                    var model = message.Payloads.ContainsKey("model") ?  (string)message.Payloads["model"] : null;
                    var temperature = message.Payloads.ContainsKey("temperature") ?  (float)(double)message.Payloads["temperature"] : -1;

                    if (name == "chatgpt")
                    {
                        dialogProcessor.SelectLLMService(chatGPTService);
                        if (apiKey != null) chatGPTService.ApiKey = apiKey;
                        if (model != null) chatGPTService.Model = model;
                        if (temperature >= 0) chatGPTService.Temperature = temperature;
                    }
                    else if (name == "claude")
                    {
                        dialogProcessor.SelectLLMService(claudeService);
                        if (apiKey != null) claudeService.ApiKey = apiKey;
                        if (model != null) claudeService.Model = model;
                        if (temperature >= 0) claudeService.Temperature = temperature;
                    }
                    else if (name == "gemini")
                    {
                        dialogProcessor.SelectLLMService(geminiService);
                        if (apiKey != null) geminiService.ApiKey = apiKey;
                        if (model != null) geminiService.Model = model;
                        if (temperature >= 0) geminiService.Temperature = temperature;
                    }
                }
                else if (message.Operation == "system_prompt")
                {
                    ((LLMServiceBase)dialogProcessor.LLMService).SystemMessageContent = (string)message.Payloads["system_prompt"];
                }
                else if (message.Operation == "debug")
                {
                    ((LLMServiceBase)dialogProcessor.LLMService).DebugMode = (bool)message.Payloads["debug_mode"];
                }

                dialogProcessor.ClearContext();
            }
        }

        public List<Func<ExternalInboundMessage, UniTask>> GetHandlers(string endpoint, string operation)
        {
            return handlers.TryGetValue((endpoint, operation), out var handlerList) ? handlerList : new List<Func<ExternalInboundMessage, UniTask>>();
        }

        public void AddHandler(string endpoint, string operation, Func<ExternalInboundMessage, UniTask> handlerFunc)
        {
            var key = (endpoint, operation);
            if (!handlers.ContainsKey(key))
            {
                handlers[key] = new List<Func<ExternalInboundMessage, UniTask>>();
            }
            handlers[key].Add(handlerFunc);
        }
    }
}

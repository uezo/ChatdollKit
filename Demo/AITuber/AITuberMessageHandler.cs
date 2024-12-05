using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using VRM;
using Newtonsoft.Json.Linq;
using ChatdollKit.Dialog;
using ChatdollKit.IO;
using ChatdollKit.LLM;
using ChatdollKit.LLM.ChatGPT;
using ChatdollKit.LLM.Claude;
using ChatdollKit.LLM.Gemini;
using ChatdollKit.LLM.Dify;
using ChatdollKit.Model;
using ChatdollKit.Network;
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
        private DifyService difyService;
        private VoicevoxSpeechSynthesizer voicevoxSpeechSynthesizer;
        private StyleBertVits2SpeechSynthesizer styleBertVits2SpeechSynthesizer;
        private LLMContentProcessor llmContentProcessor;
        private SocketClient socketClient;

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
            difyService = aiAvatarObject.GetComponent<DifyService>();

            voicevoxSpeechSynthesizer = aiAvatarObject.GetComponent<VoicevoxSpeechSynthesizer>();
            styleBertVits2SpeechSynthesizer = aiAvatarObject.GetComponent<StyleBertVits2SpeechSynthesizer>();

            llmContentProcessor = aiAvatarObject.GetComponent<LLMContentProcessor>();
            socketClient = aiAvatarObject.GetComponent<SocketClient>();
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
                    var positionX = Convert.ToSingle(message.Payloads["position_x"]);
                    var rotationY = Convert.ToSingle(message.Payloads["rotation_y"]);
                    modelController.AvatarModel.transform.position = new Vector3(positionX, 0, 0);
                    modelController.AvatarModel.transform.rotation = Quaternion.Euler(0, rotationY, 0);

                    var cameraPositionY = Convert.ToSingle(message.Payloads["camera_position_y"]);
                    var cameraRotationX = Convert.ToSingle(message.Payloads["camera_rotation_x"]);
                    var cameraFieldOfView = Convert.ToSingle(message.Payloads["camera_field_of_view"]);
                    mainCamera.transform.position = new Vector3(0, cameraPositionY, 2);
                    mainCamera.transform.rotation = Quaternion.Euler(cameraRotationX, 180, 0);
                    mainCamera.fieldOfView = cameraFieldOfView;

                    if (message.Payloads.ContainsKey("camera_background_color"))
                    {
                        var bgString = (string)message.Payloads["camera_background_color"];
                        if (ColorUtility.TryParseHtmlString(bgString.StartsWith("#") ? bgString : "#" + bgString, out Color color))
                        {
                            color.a = 1.0f;
                            mainCamera.backgroundColor = color;
                        }
                    }
                }
            }

            // Dialog
            else if (message.Endpoint == "dialog")
            {
                if (message.Operation == "process")
                {
                    dialogPriorityManager.SetRequest(message.Text, message.Payloads, message.Priority);
                }
                else if (message.Operation == "append_next")
                {
                    dialogPriorityManager.SetRequestToAppendNext(message.Text);
                }
                else if (message.Operation == "clear_request_queue")
                {
                    dialogPriorityManager.ClearDialogRequestQueue(message.Priority);
                }
                else if (message.Operation == "clear_context")
                {
                    dialogProcessor.ClearContext();
                }
                else if (message.Operation == "connect_to_aiavatar")
                {
                    socketClient.Disconnect();
                    var address = (string)message.Payloads["address"];
                    var port = int.Parse($"{message.Payloads["port"]}");
                    socketClient.Connect(address, port);
                }
                else if (message.Operation == "disconnect_from_aiavatar")
                {
                    socketClient.Disconnect();
                }
            }

            // Speech Synthesizer
            else if (message.Endpoint == "speech_synthesizer")
            {
                if (message.Operation == "activate")
                {
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
                else if (message.Operation == "styles")
                {
                    if (styleBertVits2SpeechSynthesizer.IsEnabled)
                    {
                        var styles = ((JObject)message.Payloads["styles"]).ToObject<Dictionary<string, string>>();
                        styleBertVits2SpeechSynthesizer.VoiceStyles.Clear();
                        foreach (var style in styles)
                        {
                            styleBertVits2SpeechSynthesizer.VoiceStyles.Add(
                                new StyleBertVits2SpeechSynthesizer.VoiceStyle() {
                                    VoiceStyleValue = style.Key,
                                    StyleBertVITSStyle = style.Value
                                }
                            );
                        }
                    }
                    else if (voicevoxSpeechSynthesizer.IsEnabled)
                    {
                        var styles = ((JObject)message.Payloads["styles"]).ToObject<Dictionary<string, int>>();
                        voicevoxSpeechSynthesizer.VoiceStyles.Clear();
                        foreach (var style in styles)
                        {
                            voicevoxSpeechSynthesizer.VoiceStyles.Add(
                                new VoicevoxSpeechSynthesizer.VoiceStyle() {
                                    VoiceStyleValue = style.Key,
                                    VoiceVoxSpeaker = style.Value
                                }
                            );
                        }
                    }
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
                    var temperature = message.Payloads.ContainsKey("temperature") ?  Convert.ToSingle(message.Payloads["temperature"]) : -1;
                    var url = message.Payloads.ContainsKey("url") ?  (string)message.Payloads["url"] : null;
                    var user = message.Payloads.ContainsKey("user") ?  (string)message.Payloads["user"] : null;

                    if (name == "chatgpt")
                    {
                        dialogProcessor.SelectLLMService(chatGPTService);
                        if (apiKey != null) chatGPTService.ApiKey = apiKey;
                        if (model != null) chatGPTService.Model = model;
                        if (temperature >= 0) chatGPTService.Temperature = temperature;
                        if (url != null) chatGPTService.ChatCompletionUrl = url;
                    }
                    else if (name == "claude")
                    {
                        dialogProcessor.SelectLLMService(claudeService);
                        if (apiKey != null) claudeService.ApiKey = apiKey;
                        if (model != null) claudeService.Model = model;
                        if (temperature >= 0) claudeService.Temperature = temperature;
                        if (url != null) claudeService.CreateMessageUrl = url;
                    }
                    else if (name == "gemini")
                    {
                        dialogProcessor.SelectLLMService(geminiService);
                        if (apiKey != null) geminiService.ApiKey = apiKey;
                        if (model != null) geminiService.Model = model;
                        if (temperature >= 0) geminiService.Temperature = temperature;
                        if (url != null) geminiService.GenerateContentUrl = url;
                    }
                    else if (name == "dify")
                    {
                        dialogProcessor.SelectLLMService(difyService);
                        if (apiKey != null) difyService.ApiKey = apiKey;
                        if (url != null) difyService.BaseUrl = url;
                        if (user != null) difyService.User = user;
                    }
                }
                else if (message.Operation == "system_prompt")
                {
                    ((LLMServiceBase)dialogProcessor.LLMService).SystemMessageContent = (string)message.Payloads["system_prompt"];
                }
                else if (message.Operation == "cot_tag")
                {
                    llmContentProcessor.ThinkTag = (string)message.Payloads["cot_tag"];
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

﻿using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.IO;
using ChatdollKit.Model;

namespace ChatdollKit.Dialog
{
    public class DialogController : MonoBehaviour
    {
        [Header("Wake Word and Cancel Word")]
        [SerializeField] protected string WakeWord;
        [SerializeField] protected string CancelWord;

        [Header("Prompt")]
        [SerializeField] protected string PromptVoice;
        [SerializeField] protected VoiceSource PromptVoiceType;
        [SerializeField] protected string PromptFace;
        [SerializeField] protected string PromptAnimation;

        [Header("Error")]
        [SerializeField] protected string ErrorVoice;
        [SerializeField] protected VoiceSource ErrorVoiceType;
        [SerializeField] protected string ErrorFace;
        [SerializeField] protected string ErrorAnimation;

        [Header("Message Window")]
        public MessageWindowBase MessageWindow;

        [Header("Camera")]
        public ChatdollCamera ChatdollCamera;

        public bool IsChatting { get; private set; }
        public bool IsError { get; private set; }

        public WakeWordListenerBase WakeWordListener { get; set; }
        public Dictionary<RequestType, IRequestProvider> RequestProviders { get; private set; } = new Dictionary<RequestType, IRequestProvider>();
        private IRequestProcessor requestProcessor { get; set; }
        private ModelController modelController { get; set; }
        private CancellationTokenSource dialogTokenSource { get; set; }

        // Actions for each status
        public Func<string> GetClientId { get; set; }
        public Action OnComponentsReady { get; set; }
        public Func<WakeWord, UniTask> OnWakeAsync { get; set; }
        public Func<DialogRequest, CancellationToken, UniTask> OnPromptAsync { get; set; }
        public Func<Request, CancellationToken, UniTask> OnRequestAsync { get; set; }
        public Func<Response, CancellationToken, UniTask> OnResponseAsync { get; set; }
        public Func<Request, Exception, CancellationToken, UniTask> OnErrorAsync { get; set; }

        private void Awake()
        {
            // Get components
            WakeWordListener = GetComponent<WakeWordListenerBase>();
            requestProcessor = GetComponent<IRequestProcessor>();
            modelController = GetComponent<ModelController>();
            var attachedRequestProviders = GetComponents<IRequestProvider>();
            var userStore = GetComponent<IUserStore>();
            var stateStore = GetComponent<IStateStore>();
            var skillRouter = GetComponent<ISkillRouter>();
            var skills = GetComponents<ISkill>();

            // Make instances if not set
            MessageWindow = MessageWindow != null ? MessageWindow : InstantiateMessageWindow();
            ChatdollCamera = ChatdollCamera != null ? ChatdollCamera : InstantiateCamera();

            // Request providers
            var cameraRequestProvider = GetComponent<CameraRequestProvider>() ?? gameObject.AddComponent<CameraRequestProvider>();
            cameraRequestProvider.ChatdollCamera = ChatdollCamera;
            RequestProviders.Add(RequestType.Camera, cameraRequestProvider);

            var qrCodeRequestProvider = GetComponent<QRCodeRequestProvider>() ?? gameObject.AddComponent<QRCodeRequestProvider>();
            qrCodeRequestProvider.ChatdollCamera = ChatdollCamera;
            RequestProviders.Add(RequestType.QRCode, qrCodeRequestProvider);

            foreach(var rp in GetComponents<VoiceRequestProviderBase>())
            {
                if (rp.enabled)
                {
                    rp.MessageWindow = MessageWindow;
                    if (!string.IsNullOrEmpty(CancelWord))
                    {
                        // Register cancel word to VoiceRequestProvider
                        rp.CancelWords.Add(CancelWord);
                    }
                    RequestProviders.Add(RequestType.Voice, rp);
                    break;
                }
            }
            if (RequestProviders.Count == 0)
            {
                Debug.LogWarning("Request providers are missing");
            }

            OnComponentsReady?.Invoke();

            // Setup RequestProcessor
            if (requestProcessor == null)
            {
                // Create local request processor with components
                Debug.Log("Use LocalRequestProcessor");
                requestProcessor = new LocalRequestProcessor(
                    userStore, stateStore, skillRouter, skills
                );
            }
            else if (requestProcessor is RemoteRequestProcessor)
            {
                Debug.Log("Use RemoteRequestProcessor");
                OnPromptAsync = ((RemoteRequestProcessor)requestProcessor).PromptAsync;
            }

            // Wakeword Listener
            if (WakeWordListener != null)
            {
                // Register wakeword
                if (WakeWordListener.WakeWords.Count == 0)
                {
                    if (!string.IsNullOrEmpty(WakeWord))
                    {
                        WakeWordListener.WakeWords.Add(new WakeWord() { Text = WakeWord, Intent = string.Empty });
                    }
                }

                // Register cancel word
                if (WakeWordListener.CancelWords.Count == 0)
                {
                    if (!string.IsNullOrEmpty(CancelWord))
                    {
                        WakeWordListener.CancelWords.Add(CancelWord);
                    }
                }

                // Awake
                WakeWordListener.OnWakeAsync = async (wakeword) =>
                {
                    if (OnWakeAsync != null)
                    {
                        await OnWakeAsync(wakeword);
                    }
                    else
                    {
                        await OnWakeAsyncDefault(wakeword);
                    }
                };

                // Cancel
#pragma warning disable CS1998
                WakeWordListener.OnCancelAsync = async () => { StopDialog(); };
#pragma warning restore CS1998

                // Raise voice detection threshold when chatting
                WakeWordListener.ShouldRaiseThreshold = () => { return IsChatting; };
            }
        }

        // OnDestroy
        private void OnDestroy()
        {
            // Stop async operations
            dialogTokenSource?.Cancel();
        }

        // ClientId
        private string GetClientIdDefault()
        {
            return "_";
        }

        // OnWake
        private async UniTask OnWakeAsyncDefault(WakeWord wakeword)
        {
            var skipPrompt = false;

            if (wakeword.RequestType != RequestType.None
                || !string.IsNullOrEmpty(wakeword.Intent)
                || !string.IsNullOrEmpty(wakeword.InlineRequestText))
            {
                if (!string.IsNullOrEmpty(wakeword.InlineRequestText))
                {
                    skipPrompt = true;
                }
            }

            // Invoke chat
            await StartDialogAsync(
                new DialogRequest(
                    GetClientId == null ? GetClientIdDefault() : GetClientId(),
                    wakeword, skipPrompt
                )
            );
        }

        // OnPrompt
        private async UniTask OnPromptAsyncDefault(CancellationToken token)
        {
            var PromptAnimatedVoiceRequest = new AnimatedVoiceRequest() { StartIdlingOnEnd = false };

            if (!string.IsNullOrEmpty(PromptVoice))
            {
                if (PromptVoiceType == VoiceSource.Local)
                {
                    PromptAnimatedVoiceRequest.AddVoice(PromptVoice);
                }
                else if (PromptVoiceType == VoiceSource.Web)
                {
                    PromptAnimatedVoiceRequest.AddVoiceWeb(PromptVoice);
                }
                else if (PromptVoiceType == VoiceSource.TTS)
                {
                    PromptAnimatedVoiceRequest.AddVoiceTTS(PromptVoice);
                }
            }
            if (!string.IsNullOrEmpty(PromptFace))
            {
                PromptAnimatedVoiceRequest.AddFace(PromptFace);
            }
            if (!string.IsNullOrEmpty(PromptAnimation))
            {
                PromptAnimatedVoiceRequest.AddAnimation(PromptAnimation);
            }

            await modelController.AnimatedSay(PromptAnimatedVoiceRequest, token);
        }

        // OnError
        private async UniTask OnErrorAsyncDefault(CancellationToken token)
        {
            var ErrorAnimatedVoiceRequest = new AnimatedVoiceRequest();

            if (!string.IsNullOrEmpty(ErrorVoice))
            {
                if (ErrorVoiceType == VoiceSource.Local)
                {
                    ErrorAnimatedVoiceRequest.AddVoice(ErrorVoice);
                }
                else if (ErrorVoiceType == VoiceSource.Web)
                {
                    ErrorAnimatedVoiceRequest.AddVoiceWeb(ErrorVoice);
                }
                else if (ErrorVoiceType == VoiceSource.TTS)
                {
                    ErrorAnimatedVoiceRequest.AddVoiceTTS(ErrorVoice);
                }
            }
            if (!string.IsNullOrEmpty(ErrorFace))
            {
                ErrorAnimatedVoiceRequest.AddFace(ErrorFace);
            }
            if (!string.IsNullOrEmpty(ErrorAnimation))
            {
                ErrorAnimatedVoiceRequest.AddAnimation(ErrorAnimation);
            }

            await modelController.AnimatedSay(ErrorAnimatedVoiceRequest, token);
        }

        // Start chatting loop
        public async UniTask StartDialogAsync(DialogRequest dialogRequest = null)
        {
            if (dialogRequest == null)
            {
                dialogRequest = new DialogRequest(GetClientId == null ? GetClientIdDefault() : GetClientId());
            }

            // Get cancellation token
            StopDialog(true, false);
            var token = GetDialogToken();

            // Request
            Request request = null;

            try
            {
                IsChatting = true;

                // Prompt
                if (!dialogRequest.SkipPrompt)
                {
                    if (OnPromptAsync != null)
                    {
                        await OnPromptAsync(dialogRequest, token);
                    }
                    else
                    {
                        await OnPromptAsyncDefault(token);
                    }
                }

                // Set RequestType for the first turn
                var requestType = RequestType.Voice;
                if (dialogRequest.WakeWord != null)
                {
                    requestType = dialogRequest.WakeWord.RequestType;
                }

                // Convert DialogRequest to Request before the first turn
                request = dialogRequest.ToRequest();

                // Chat loop. Exit when session ends, canceled or error occures
                while (true)
                {
                    if (token.IsCancellationRequested) { return; }

                    if (request == null)
                    {
                        // Get request (microphone / camera / QR code, etc)
                        var requestProvider = RequestProviders[requestType];
                        request = await requestProvider.GetRequestAsync(token);
                        request.ClientId = dialogRequest.ClientId;
                        request.Tokens = dialogRequest.Tokens;
                    }

                    if (!request.IsSet())
                    {
                        break;
                    }

                    // Process request
                    if (OnRequestAsync != null)
                    {
                        await OnRequestAsync(request, token);
                    }
                    var skillResponse = await requestProcessor.ProcessRequestAsync(request, token);
                    if (OnResponseAsync != null)
                    {
                        await OnResponseAsync(skillResponse, token);
                    }

                    // Controll conversation loop
                    if (skillResponse == null || skillResponse.EndConversation)
                    {
                        break;
                    }
                    else
                    {
                        requestType = skillResponse.NextTurnRequestType;
                    }

                    request = null;
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    IsError = true;
                    Debug.LogError($"Error occured in processing chat: {ex.Message}\n{ex.StackTrace}");
                    // Stop running animation and voice then get new token to say error
                    StopDialog(true, false);
                    token = GetDialogToken();
                    if (OnErrorAsync != null)
                    {
                        await OnErrorAsync(request, ex, token);
                    }
                    else
                    {
                        await OnErrorAsyncDefault(token);
                    }
                }
            }
            finally
            {
                IsError = false;
                IsChatting = false;

                if (!token.IsCancellationRequested)
                {
                    // NOTE: Cancel is triggered not only when just canceled but when invoked another chat session
                    // Restart idling animation and reset face expression
                    _ = modelController?.StartIdlingAsync();
                    _ = modelController?.SetDefaultFace();
                }
            }
        }

        // Stop chat
        public void StopDialog(bool waitVoice = false, bool startIdling = true)
        {
            // Cancel the tasks and dispose the token source
            if (dialogTokenSource != null)
            {
                dialogTokenSource.Cancel();
                dialogTokenSource.Dispose();
                dialogTokenSource = null;
            }

            // Stop speaking immediately if not wait
            if (!waitVoice)
            {
                modelController?.StopSpeech();
            }

            if (startIdling)
            {
                // Start idling, default face and blink. `startIdling` is true when no successive animated voice
                _ = modelController?.StartIdlingAsync();
                _ = modelController?.SetDefaultFace();
                _ = modelController?.StartBlinkAsync();
            }
        }

        // Get cancellation token for tasks invoked in chat
        private CancellationToken GetDialogToken()
        {
            // Create new TokenSource and return its token
            dialogTokenSource = new CancellationTokenSource();
            return dialogTokenSource.Token;
        }

        // Make instance of MessageWindow
        private MessageWindowBase InstantiateMessageWindow()
        {
            // Create instance of SimpleMessageWindow
            var messageWindowGameObject = Resources.Load<GameObject>("Prefabs/SimpleMessageWindow");
            if (messageWindowGameObject != null)
            {
                var messageWindowGameObjectInstance = Instantiate(messageWindowGameObject);
                messageWindowGameObjectInstance.name = messageWindowGameObject.name;
                return messageWindowGameObjectInstance.GetComponent<SimpleMessageWindow>();
            }

            return null;
        }

        // Make instance of Camera
        private ChatdollCamera InstantiateCamera()
        {
            var cameraGameObject = Resources.Load<GameObject>("Prefabs/ChatdollCamera");
            if (cameraGameObject != null)
            {
                var cameraGameObjectInstance = Instantiate(cameraGameObject);
                cameraGameObjectInstance.name = cameraGameObject.name;
                return cameraGameObjectInstance.GetComponent<ChatdollCamera>();
            }

            return null;
        }
    }
}

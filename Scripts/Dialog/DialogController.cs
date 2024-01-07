using System;
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
        [SerializeField] protected VoiceSource PromptVoiceType = VoiceSource.TTS;
        [SerializeField] protected string PromptFace;
        [SerializeField] protected string PromptAnimationParamKey;
        [SerializeField] protected int PromptAnimationParamValue;

        [Header("Error")]
        [SerializeField] protected string ErrorVoice;
        [SerializeField] protected VoiceSource ErrorVoiceType = VoiceSource.TTS;
        [SerializeField] protected string ErrorFace;
        [SerializeField] protected string ErrorAnimationParamKey;
        [SerializeField] protected int ErrorAnimationParamValue;

        [Header("Request Processing")]
        public bool UseRemoteServer = false;
        public string BaseUrl = string.Empty;

        [Header("Message Windows")]
        public MessageWindowBase UserMessageWindow;
        public MessageWindowBase CharacterMessageWindow;

        [Header("I/O")]
        public ChatdollCamera ChatdollCamera;
        private ChatdollMicrophone chatdollMicrophone;
        [SerializeField]
        private bool isMuted = false;
        private bool isPreviousMuted = false;
        public bool IsMuted
        {
            get { return isMuted; }
            set { isMuted = value; }
        }

        // Dialog Status
        public enum DialogStatus
        {
            Idling,
            Initializing,
            Prompting,
            PreparingFirstTurn,
            TurnStarted,
            Listening,
            Processing,
            Responding,
            Finalizing
        }
        public DialogStatus Status { get; private set; }
        public string DialogSessionId { get; private set; }

        public bool IsChatting { get; private set; }
        public bool IsError { get; private set; }

        public IWakeWordListener WakeWordListener { get; set; }
        public Dictionary<RequestType, IRequestProvider> RequestProviders { get; private set; } = new Dictionary<RequestType, IRequestProvider>();
        private IRequestProcessor requestProcessor { get; set; }
        private ModelController modelController { get; set; }
        private CancellationTokenSource dialogTokenSource { get; set; }

        // Actions for each status
        public Func<string> GetClientId { get; set; }
        public Func<WakeWord, UniTask> OnWakeAsync { get; set; }
        public Func<UniTask> OnDialogStartAsync { get; set; }
        public Func<DialogRequest, CancellationToken, UniTask> OnPromptAsync { get; set; }
        public Func<Request, CancellationToken, UniTask> OnRequestAsync { get; set; }
        public Func<Request, CancellationToken, UniTask> OnStartShowingWaitingAnimationAsync
        {
            set
            {
                if (requestProcessor is LocalRequestProcessor)
                {
                    ((LocalRequestProcessor)requestProcessor).OnStartShowingWaitingAnimationAsync = value;
                }
            }
        }
        public Func<Response, CancellationToken, UniTask> OnStartShowingResponseAsync
        {
            set
            {
                if (requestProcessor is LocalRequestProcessor)
                {
                    ((LocalRequestProcessor)requestProcessor).OnStartShowingResponseAsync = async (response, token) =>
                    {
                        Status = DialogStatus.Responding;
                        await value(response, token);
                    };
                }
            }
        }
        public Func<Response, CancellationToken, UniTask> OnResponseAsync { get; set; }
        public Func<Request, Exception, CancellationToken, UniTask> OnErrorAsync { get; set; }

        private void Awake()
        {
            // Get components
            var wakeWordListeners = GetComponents<IWakeWordListener>();
            modelController = GetComponent<ModelController>();
            var attachedRequestProviders = GetComponents<IRequestProvider>();

            // Components for LocalRequestProcessor
            var userStore = GetComponent<IUserStore>();
            var stateStore = GetComponent<IStateStore>();
            var skillRouter = GetComponent<ISkillRouter>();

            // Search message windows
            foreach (var messageWindow in GetComponentsInChildren<MessageWindowBase>(true))
            {
                if (messageWindow.name == "UserMessageWindow" && UserMessageWindow == null)
                {
                    UserMessageWindow = messageWindow;
                }
                else if (messageWindow.name == "CharacterMessageWindow" && CharacterMessageWindow == null)
                {
                    CharacterMessageWindow = messageWindow;
                }
            }

            // User message window
            if (UserMessageWindow == null)
            {
                Debug.LogWarning("UserMessageWindow is not set.");
            }
            if (!UserMessageWindow.IsInstance)
            {
                // Create MessageWindow instance
                UserMessageWindow = Instantiate(UserMessageWindow);
            }

            // Character message window
            if (CharacterMessageWindow == null)
            {
                Debug.LogWarning("CharacterMessageWindow is not set.");
            }
            // Synchronize speech and character message window
            modelController.OnSayStart = (text, token) =>
            {
                if (!string.IsNullOrEmpty(text))
                {
                    _ = CharacterMessageWindow?.ShowMessageAsync(text, token);
                }
            };
            modelController.OnSayEnd = () =>
            {
                CharacterMessageWindow?.Hide();
            };

            // Microphone
            chatdollMicrophone = GetComponent<ChatdollMicrophone>();

            // Create ChatdollCamera instance
            ChatdollCamera = Instantiate(ChatdollCamera);

            // Request providers
            var cameraRequestProvider = GetComponent<CameraRequestProvider>() ?? gameObject.AddComponent<CameraRequestProvider>();
            cameraRequestProvider.ChatdollCamera = ChatdollCamera;
            RequestProviders.Add(RequestType.Camera, cameraRequestProvider);

            var qrCodeRequestProvider = GetComponent<QRCodeRequestProvider>() ?? gameObject.AddComponent<QRCodeRequestProvider>();
            qrCodeRequestProvider.ChatdollCamera = ChatdollCamera;
            RequestProviders.Add(RequestType.QRCode, qrCodeRequestProvider);

            foreach (var rp in GetComponents<IVoiceRequestProvider>())
            {
                if (((MonoBehaviour)rp).enabled)
                {
                    rp.SetMessageWindow(UserMessageWindow);
                    if (!string.IsNullOrEmpty(CancelWord))
                    {
                        // Register cancel word to VoiceRequestProvider
                        rp.SetCancelWord(CancelWord);
                    }

                    if (rp is VoiceRecorderBase)
                    {
                        ((VoiceRecorderBase)rp).UnmuteOnListeningStart = () => { return !IsMuted; };
                    }
                    RequestProviders.Add(RequestType.Voice, rp);
                    break;
                }
            }
            if (RequestProviders.Count == 0)
            {
                Debug.LogWarning("Request providers are missing");
            }

            // Setup RequestProcessor
            if (UseRemoteServer)
            {
                // Remote
                Debug.Log($"Use RemoteRequestProcessor: {BaseUrl}");
                requestProcessor = GetComponent<RemoteRequestProcessor>() ?? gameObject.AddComponent<RemoteRequestProcessor>();
                ((RemoteRequestProcessor)requestProcessor).BaseUrl = BaseUrl;
            }
            else
            {
                // Local
                requestProcessor = GetComponent<IRequestProcessor>();
                if (requestProcessor == null)
                {
                    // Create local request processor with components
                    Debug.Log("Use LocalRequestProcessor");
                    if (skillRouter == null)
                    {
                        skillRouter = gameObject.AddComponent<StaticSkillRouter>();
                    }
                    requestProcessor = new LocalRequestProcessor(
                        userStore, stateStore, skillRouter
                    );
                }
                else
                {
                    Debug.Log($"Use attached request processor: {requestProcessor.GetType()}");
                }
            }

            // Prompter
            if (requestProcessor is IRequestProcessorWithPrompt)
            {
                OnPromptAsync = ((IRequestProcessorWithPrompt)requestProcessor).PromptAsync;
            }
            else
            {
                OnPromptAsync = OnPromptAsyncDefault;
            }

            // Wakeword Listener
            foreach (var wwl in wakeWordListeners)
            {
                if (((MonoBehaviour)wwl).enabled)
                {
                    WakeWordListener = wwl;
                    break;
                }
            }
            if (WakeWordListener != null)
            {
                // Register wakeword
                if (!string.IsNullOrEmpty(WakeWord))
                {
                    WakeWordListener.SetWakeWord(new WakeWord() { Text = WakeWord, Intent = string.Empty });
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

                // Raise voice detection threshold when chatting
                WakeWordListener.ShouldRaiseThreshold = () => { return IsChatting; };
            }

            Status = DialogStatus.Idling;
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
#pragma warning disable CS1998
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

            // Invoke chat (Do not await to prevent blocking wakeword listener)
            _ = StartDialogAsync(
                new DialogRequest(
                    GetClientId == null ? GetClientIdDefault() : GetClientId(),
                    wakeword, skipPrompt
                )
            );
        }

        private async UniTask OnDialogStartAsyncDefault()
        {
            // Reset idling mode before conversation
            await modelController.ChangeIdlingModeAsync();
        }
#pragma warning restore CS1998

        // OnPrompt
        private async UniTask OnPromptAsyncDefault(DialogRequest dialogRequest, CancellationToken token)
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
            if (!string.IsNullOrEmpty(PromptAnimationParamKey))
            {
                PromptAnimatedVoiceRequest.AddAnimation(PromptAnimationParamKey, PromptAnimationParamValue, 5.0f);
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
            if (!string.IsNullOrEmpty(ErrorAnimationParamKey))
            {
                ErrorAnimatedVoiceRequest.AddAnimation(ErrorAnimationParamKey, ErrorAnimationParamValue, 5.0f);
            }

            await modelController.AnimatedSay(ErrorAnimatedVoiceRequest, token);
        }

        // Start chatting loop
        public async UniTask StartDialogAsync(DialogRequest dialogRequest = null)
        {
            Status = DialogStatus.Initializing;
            DialogSessionId = Guid.NewGuid().ToString();
            var currentDialogSessionId = DialogSessionId;

            try
            {
                await (OnDialogStartAsync == null ? OnDialogStartAsyncDefault() : OnDialogStartAsync());

                if (dialogRequest == null)
                {
                    dialogRequest = new DialogRequest(GetClientId == null ? GetClientIdDefault() : GetClientId());
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error at initializing dialog: {ex}\n{ex.StackTrace}");
                Status = DialogStatus.Idling;
                return;
            }

            // Stop running dialog and get cancellation token
            StopDialog(true, false);
            var token = GetDialogToken();

            // Stop WakeWordListener and microphone
            WakeWordListener.StopListening();

            // Request
            Request request = null;

            try
            {
                IsChatting = true;

                // Prompt
                if (!dialogRequest.SkipPrompt)
                {
                    Status = DialogStatus.Prompting;
                    await OnPromptAsync(dialogRequest, token);
                }

                // Set RequestType for the first turn
                Status = DialogStatus.PreparingFirstTurn;
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
                    Status = DialogStatus.TurnStarted;

                    if (token.IsCancellationRequested) { return; }

                    if (request == null)
                    {
                        // Get request (microphone / camera / QR code, etc)
                        var requestProvider = RequestProviders[requestType];
                        Status = DialogStatus.Listening;
                        request = await requestProvider.GetRequestAsync(token);
                        Status = DialogStatus.Processing;
                        request.ClientId = dialogRequest.ClientId;
                        request.Tokens = dialogRequest.Tokens;
                    }
                    else
                    {
                        Status = DialogStatus.Processing;
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
                    Status = DialogStatus.Finalizing;
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
                // Reset flags
                IsError = false;
                IsChatting = false;

                if (!token.IsCancellationRequested)
                {
                    // NOTE: Cancel is triggered not only when just canceled but when invoked another chat session
                    // Restart idling animation and reset face expression
                    modelController?.StartIdling();
                }

                if (currentDialogSessionId == DialogSessionId)
                {
                    // Reset status and start WakeWordListener when another session is not started
                    Status = DialogStatus.Idling;
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
                // Start idling. `startIdling` is true when no successive animated voice
                modelController?.StartIdling();
            }
        }

        // Get cancellation token for tasks invoked in chat
        public CancellationToken GetDialogToken()
        {
            // Create new TokenSource and return its token
            dialogTokenSource = new CancellationTokenSource();
            return dialogTokenSource.Token;
        }

        private void Update()
        {
            // Control mute
            if (isMuted != isPreviousMuted)
            {
                IsMuted = isMuted;
            }
            isPreviousMuted = isMuted;

            // Sync DialogController mute/unmute with Microphone mute/unmute
            if (!chatdollMicrophone.IsMuted && IsMuted)
            {
                // Mute microphone anyway
                chatdollMicrophone.IsMuted = true;
            }
            else if (chatdollMicrophone.IsMuted && !IsMuted)
            {
                // Unmute when VoiceRequestProvider is based on VoiceRecorder and it is listening
                if (RequestProviders[RequestType.Voice] is VoiceRecorderBase)
                {
                    if (((IVoiceRequestProvider)RequestProviders[RequestType.Voice]).IsListening)
                    {
                        chatdollMicrophone.IsMuted = false;
                    }
                }
            }

            // Sync DialogController mute/unmute with NonVoiceRecordingRequestProvider
            if (RequestProviders[RequestType.Voice] is NonRecordingVoiceRequestProviderBase)
            {
                var vrp = (NonRecordingVoiceRequestProviderBase)RequestProviders[RequestType.Voice];
                if (vrp.IsMuted != IsMuted)
                {
                    // Sync IsMuted
                    vrp.IsMuted = IsMuted;
                }
            }

            // Control WakeWordListener
            if ((Status != DialogStatus.Idling || IsMuted) && WakeWordListener.IsListening)
            {
                WakeWordListener.StopListening();
            }
            else if (Status == DialogStatus.Idling && !IsMuted && !WakeWordListener.IsListening)
            {
                WakeWordListener.StartListening();
            }
        }
    }
}

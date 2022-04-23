using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.IO;
using ChatdollKit.Model;

namespace ChatdollKit
{
    [RequireComponent(typeof(ModelController))]
    [RequireComponent(typeof(CameraRequestProvider))]
    [RequireComponent(typeof(QRCodeRequestProvider))]
    public class ChatdollApplication : MonoBehaviour
    {
        protected ModelController modelController;
        protected DialogController dialogController;
        protected IRequestProvider[] requestProviders;
        protected VoiceRequestProviderBase voiceRequestProvider;
        protected CameraRequestProvider cameraRequestProvider;
        protected QRCodeRequestProvider qrcodeRequestProvider;
        protected WakeWordListenerBase wakeWordListener;
        protected AnimatedVoiceRequest PromptAnimatedVoiceRequest = new AnimatedVoiceRequest() { StartIdlingOnEnd = false };
        protected AnimatedVoiceRequest ErrorAnimatedVoiceRequest = new AnimatedVoiceRequest();

        [Header("Application Identifier")]
        public string ApplicationName;

        [Header("Wake Word and Cancel Word")]
        [SerializeField] protected string WakeWord;
        [SerializeField] protected string CancelWord;

        [Header("Prompt")]
        [SerializeField] protected string PromptVoice;
        [SerializeField] protected VoiceSource PromptVoiceType;
        [SerializeField] protected string PromptFace;
        [SerializeField] protected string PromptAnimation;

        [Header("Message Window")]
        [SerializeField] protected MessageWindowBase MessageWindow;

        [Header("Camera")]
        [SerializeField] protected ChatdollCamera ChatdollCamera;

        protected virtual void Awake()
        {
            // Get components
            modelController = gameObject.GetComponent<ModelController>();
            wakeWordListener = GetComponent<WakeWordListenerBase>();
            requestProviders = gameObject.GetComponents<IRequestProvider>();
            MessageWindow = MessageWindow ?? InstantiateMessageWindow();
            ChatdollCamera = ChatdollCamera ?? InstantiateCamera();

            foreach (var rp in requestProviders)
            {
                if (((MonoBehaviour)rp).enabled)
                {
                    if (rp.RequestType == RequestType.Voice)
                    {
                        voiceRequestProvider = (VoiceRequestProviderBase)rp;
                        voiceRequestProvider.MessageWindow = MessageWindow;
                    }
                    else if (rp.RequestType == RequestType.Camera)
                    {
                        cameraRequestProvider = (CameraRequestProvider)rp;
                        cameraRequestProvider.ChatdollCamera = ChatdollCamera;
                    }
                    else if (rp.RequestType == RequestType.QRCode)
                    {
                        qrcodeRequestProvider = (QRCodeRequestProvider)rp;
                        qrcodeRequestProvider.ChatdollCamera = ChatdollCamera;
                    }
                }
            }

            // Apply configuration to each component if configuration exists
            var config = Resources.Load<ScriptableObject>(GetConfigName());
            OnComponentsReady(config);

            var enabledRequestProviders = new Dictionary<RequestType, IRequestProvider>
            {
                [RequestType.Voice] = voiceRequestProvider,
                [RequestType.Camera] = cameraRequestProvider,
                [RequestType.QRCode] = qrcodeRequestProvider
            };
            if (enabledRequestProviders.Count == 0)
            {
                Debug.LogWarning("Request providers are missing");
            }

            // Register cancel word to VoiceRequestProvider
            if (voiceRequestProvider?.CancelWords.Count == 0)
            {
                if (!string.IsNullOrEmpty(CancelWord))
                {
                    voiceRequestProvider.CancelWords.Add(CancelWord);
                }
            }

            // Setup RequestProcessor
            var requestProcessor = gameObject.GetComponent<IRequestProcessor>();
            if (requestProcessor == null)
            {
                // Create local request processor with components
                Debug.Log("Use LocalRequestProcessor");
                requestProcessor = new LocalRequestProcessor(
                    GetComponent<IUserStore>(), GetComponent<IStateStore>(),
                    GetComponent<ISkillRouter>(), GetComponents<ISkill>()
                );
            }

            // OnPromptAsync
            Func<DialogRequest, CancellationToken, UniTask> onPromptAsync;
            if (requestProcessor is RemoteRequestProcessor)
            {
                onPromptAsync = ((RemoteRequestProcessor)requestProcessor).PromptAsync;
            }
            else
            {
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

                onPromptAsync = async (r, t) =>
                {
                    await modelController.AnimatedSay(PromptAnimatedVoiceRequest, t);
                };
            }

            // OnErrorAsync
            Func<Request, CancellationToken, UniTask> onErrorAsync = async (r, t) =>
            {
                await modelController.AnimatedSay(ErrorAnimatedVoiceRequest, t);
            };

            // Create and set components to DialogController
            dialogController = new DialogController(modelController, enabledRequestProviders, requestProcessor, onPromptAsync, onErrorAsync);

            // Wakeword Listener
            if (wakeWordListener != null)
            {
                // Register wakeword
                if (wakeWordListener.WakeWords.Count == 0)
                {
                    if (!string.IsNullOrEmpty(WakeWord))
                    {
                        wakeWordListener.WakeWords.Add(new WakeWord() { Text = WakeWord, Intent = string.Empty });
                    }
                }

                // Register cancel word
                if (wakeWordListener.CancelWords.Count == 0)
                {
                    if (!string.IsNullOrEmpty(CancelWord))
                    {
                        wakeWordListener.CancelWords.Add(CancelWord);
                    }
                }

                // Awaken
                wakeWordListener.OnWakeAsync = async (wakeword) =>
                {
                    var skipPrompt = false;
                    Request preRequest = null;

                    // Set request type, intent and inline request if set
                    if (wakeword.RequestType != RequestType.None
                        || !string.IsNullOrEmpty(wakeword.Intent)
                        || !string.IsNullOrEmpty(wakeword.InlineRequestText))
                    {
                        preRequest = new Request(wakeword.RequestType);
                        preRequest.Intent = new Intent(wakeword.Intent, wakeword.IntentPriority);
                        if (!string.IsNullOrEmpty(wakeword.InlineRequestText))
                        {
                            preRequest.Text = wakeword.InlineRequestText;
                            skipPrompt = true;
                        }
                    }

                    // Invoke chat
                    await dialogController.StartDialogAsync(new DialogRequest(GetUserId(), wakeword, skipPrompt));
                };

                // Cancel
#pragma warning disable CS1998
                wakeWordListener.OnCancelAsync = async () => { dialogController.StopDialog(); };
#pragma warning restore CS1998

                // Raise voice detection threshold when chatting
                wakeWordListener.ShouldRaiseThreshold = () => { return dialogController.IsChatting; };
            }
        }

        // Called after GetComponent(s) in Awake
        protected virtual void OnComponentsReady(ScriptableObject config)
        {

        }

        // OnDestroy
        protected void OnDestroy()
        {
            dialogController.Dispose();
        }

        protected virtual MessageWindowBase InstantiateMessageWindow()
        {
            if (MessageWindow == null)
            {
                // Create instance of SimpleMessageWindow
                var messageWindowGameObject = Resources.Load<GameObject>("Prefabs/SimpleMessageWindow/SimpleMessageWindow");
                if (messageWindowGameObject != null)
                {
                    var messageWindowGameObjectInstance = Instantiate(messageWindowGameObject);
                    messageWindowGameObjectInstance.name = messageWindowGameObject.name;
                    return messageWindowGameObjectInstance.GetComponent<SimpleMessageWindow>();
                }
            }

            return null;
        }

        protected virtual ChatdollCamera InstantiateCamera()
        {
            if (ChatdollCamera == null)
            {
                var cameraGameObject = Resources.Load<GameObject>("Prefabs/ChatdollCamera");
                if (cameraGameObject != null)
                {
                    var cameraGameObjectInstance = Instantiate(cameraGameObject);
                    cameraGameObjectInstance.name = cameraGameObject.name;
                    return cameraGameObjectInstance.GetComponent<ChatdollCamera>();
                }
            }

            return null;
        }

        public async void StartChatAsync()
        {
            if (dialogController != null)
            {
                await dialogController.StartDialogAsync(new DialogRequest(GetUserId()));
            }
            else
            {
                Debug.LogWarning("Run application before start chatting");
            }
        }

        public void StopChat()
        {
            dialogController?.StopDialog();
        }

        // Send text to WakeWordListener instead of voice
        public virtual void SendWakeWord(string text)
        {
            if (wakeWordListener != null)
            {
                wakeWordListener.TextInput = text;
            }
        }

        // Send text to VoiceRequestProvider instead of voice
        public virtual void SendTextRequest(string text)
        {
            if (voiceRequestProvider != null)
            {
                voiceRequestProvider.TextInput = text;
            }
        }

        public virtual string GetConfigName()
        {
            return string.IsNullOrEmpty(ApplicationName) ? name : ApplicationName;
        }

        public virtual ScriptableObject CreateConfig(ScriptableObject config = null)
        {
            return config ?? ScriptableObject.CreateInstance<ScriptableObject>();
        }

        protected virtual string GetUserId()
        {
            return "user0123456789";
        }
    }
}

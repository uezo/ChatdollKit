using System.Collections.Generic;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.IO;
using ChatdollKit.Model;
using System;

namespace ChatdollKit
{
    [RequireComponent(typeof(ModelController))]
    [RequireComponent(typeof(CameraRequestProvider))]
    [RequireComponent(typeof(QRCodeRequestProvider))]
    public class ChatdollApplication : MonoBehaviour
    {
        protected DialogController dialogController;
        protected ModelController modelController;
        protected IUserStore userStore;
        protected IStateStore stateStore;
        protected IRequestProvider[] requestProviders;
        public VoiceRequestProviderBase voiceRequestProvider { get; protected set; }
        protected CameraRequestProvider cameraRequestProvider;
        protected QRCodeRequestProvider qrcodeRequestProvider;
        protected ISkill[] skills;
        protected ISkillRouter skillRouter;
        protected HttpPrompter httpPrompter;
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
            userStore = gameObject.GetComponent<IUserStore>() ?? gameObject.AddComponent<LocalUserStore>();
            stateStore = gameObject.GetComponent<IStateStore>() ?? gameObject.AddComponent<MemoryStateStore>();
            skills = gameObject.GetComponents<ISkill>();
            skillRouter = gameObject.GetComponent<ISkillRouter>() ?? gameObject.AddComponent<StaticSkillRouter>();
            httpPrompter = gameObject.GetComponent<HttpPrompter>();
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

            // Register skills to router
            if (skills != null)
            {
                foreach (var skill in skills)
                {
                    skillRouter.RegisterSkill(skill);
                    Debug.Log($"Skill '{skill.TopicName}' registered successfully");
                }
            }
            else
            {
                Debug.LogError("Skills are missing");
            }

            // Make request provider set
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

            // Create DialogController with components
            dialogController = new DialogController(userStore, stateStore, skillRouter, enabledRequestProviders, modelController);

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
                    await dialogController.StartChatAsync(GetUserId(), skipPrompt, preRequest);
                };

                // Cancel
#pragma warning disable CS1998
                wakeWordListener.OnCancelAsync = async () => { dialogController.StopChat(); };
#pragma warning restore CS1998

                // Raise voice detection threshold when chatting
                wakeWordListener.ShouldRaiseThreshold = () => { return dialogController.IsChatting; };
            }

            // Prompt
            if (httpPrompter != null)
            {
                dialogController.OnPromptAsync = httpPrompter.OnPromptAsync;
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

                dialogController.OnPromptAsync = async (r, u, c, t) =>
                {
                    await modelController.AnimatedSay(PromptAnimatedVoiceRequest, t);
                };
            }

            // Error
            dialogController.OnErrorAsync = async (r, c, t) =>
            {
                await modelController.AnimatedSay(ErrorAnimatedVoiceRequest, t);
            };
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
                await dialogController.StartChatAsync(GetUserId());
            }
            else
            {
                Debug.LogWarning("Run application before start chatting");
            }
        }

        public void StopChat()
        {
            dialogController?.StopChat();
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

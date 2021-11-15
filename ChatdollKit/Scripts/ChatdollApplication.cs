using System.Collections.Generic;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.IO;
using ChatdollKit.Model;

namespace ChatdollKit
{
    [RequireComponent(typeof(ModelController))]
    [RequireComponent(typeof(CameraRequestProvider))]
    [RequireComponent(typeof(QRCodeRequestProvider))]
    public class ChatdollApplication : MonoBehaviour
    {
        protected DialogController dialogController;
        protected ModelController modelController;
        protected VoiceRequestProviderBase voiceRequestProvider;
        protected CameraRequestProvider cameraRequestProvider;
        protected QRCodeRequestProvider qrcodeRequestProvider;
        protected WakeWordListenerBase wakeWordListener;
        protected AnimatedVoiceRequest PromptAnimatedVoiceRequest = new AnimatedVoiceRequest() { StartIdlingOnEnd = false };
        protected AnimatedVoiceRequest ErrorAnimatedVoiceRequest = new AnimatedVoiceRequest();

        [Header("Wake Word and Cancel Word")]
        [SerializeField] protected string WakeWord;
        [SerializeField] protected string CancelWord;

        [Header("Prompt")]
        [SerializeField] protected string PromptVoice;
        [SerializeField] protected VoiceSource PromptVoiceType;
        [SerializeField] protected string PromptFace;
        [SerializeField] protected string PromptAnimation;

        [Header("Voice Request Provider")]
        [SerializeField] protected MessageWindowBase MessageWindow;

        [Header("Camera")]
        [SerializeField] protected ChatdollCamera ChatdollCamera;

        protected virtual void Awake()
        {
            // Get components
            modelController = gameObject.GetComponent<ModelController>();
            voiceRequestProvider = gameObject.GetComponent<VoiceRequestProviderBase>();
            cameraRequestProvider = gameObject.GetComponent<CameraRequestProvider>();
            qrcodeRequestProvider = gameObject.GetComponent<QRCodeRequestProvider>();

            // Get or add User/State store
            var userStore = gameObject.GetComponent<IUserStore>() ?? gameObject.AddComponent<LocalUserStore>();
            var stateStore = gameObject.GetComponent<IStateStore>() ?? gameObject.AddComponent<MemoryStateStore>();

            // Register request providers for each input type
            var requestProviders = new Dictionary<RequestType, IRequestProvider>();
            var attachedRequestProviders = gameObject.GetComponents<IRequestProvider>();
            if (attachedRequestProviders != null)
            {
                foreach (var rp in attachedRequestProviders)
                {
                    if (((MonoBehaviour)rp).enabled)
                    {
                        requestProviders[rp.RequestType] = rp;
                    }
                }
            }
            else
            {
                Debug.LogError("RequestProviders are missing");
            }

            // Use user defined router or static router
            var skillRouter = gameObject.GetComponent<ISkillRouter>() ?? gameObject.AddComponent<StaticSkillRouter>();

            // Register intents and its processor
            var skills = gameObject.GetComponents<ISkill>();
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

            // Setup DialogController
            dialogController = new DialogController(userStore, stateStore, skillRouter, requestProviders, modelController);

            // Prompt
            var httpPrompter = gameObject.GetComponent<HttpPrompter>();
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

            // Wakeword Listener
            wakeWordListener = GetComponent<WakeWordListenerBase>();
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

            // Voice Request Provider
            if (voiceRequestProvider != null)
            {
                // Set message window
                if (voiceRequestProvider.MessageWindow == null)
                {
                    InstantiateMessageWindos();
                    voiceRequestProvider.MessageWindow = MessageWindow;
                }

                // Register cancel word to request provider
                if (voiceRequestProvider.CancelWords.Count == 0)
                {
                    voiceRequestProvider.CancelWords.Add(CancelWord);
                }
            }

            // Camera and QRCode Request Provider
            if (cameraRequestProvider.ChatdollCamera == null)
            {
                InstantiateCamera();
                cameraRequestProvider.ChatdollCamera = ChatdollCamera;
            }
            if (qrcodeRequestProvider.ChatdollCamera == null)
            {
                InstantiateCamera();
                qrcodeRequestProvider.ChatdollCamera = ChatdollCamera;
            }
        }

        // OnDestroy
        protected void OnDestroy()
        {
            dialogController.Dispose();
        }

        protected virtual void InstantiateMessageWindos()
        {
            if (MessageWindow == null)
            {
                // Create instance of SimpleMessageWindow
                var messageWindowGameObject = Resources.Load<GameObject>("Prefabs/SimpleMessageWindow/SimpleMessageWindow");
                if (messageWindowGameObject != null)
                {
                    var messageWindowGameObjectInstance = Instantiate(messageWindowGameObject);
                    messageWindowGameObjectInstance.name = messageWindowGameObject.name;
                    MessageWindow = messageWindowGameObjectInstance.GetComponent<SimpleMessageWindow>();
                }
            }
        }

        protected virtual void InstantiateCamera()
        {
            if (ChatdollCamera == null)
            {
                var cameraGameObject = Resources.Load<GameObject>("Prefabs/ChatdollCamera");
                if (cameraGameObject != null)
                {
                    var cameraGameObjectInstance = Instantiate(cameraGameObject);
                    cameraGameObjectInstance.name = cameraGameObject.name;
                    ChatdollCamera = cameraGameObjectInstance.GetComponent<ChatdollCamera>();
                }
            }
        }

        public async void StartChatAsync()
        {
            await dialogController.StartChatAsync(GetUserId());
        }

        public void StopChat()
        {
            dialogController.StopChat();
        }

        protected virtual string GetUserId()
        {
            return "user0123456789";
        }
    }
}

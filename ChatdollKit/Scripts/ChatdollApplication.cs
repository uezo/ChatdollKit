using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.IO;
using ChatdollKit.Model;

namespace ChatdollKit
{
    [RequireComponent(typeof(Chatdoll))]
    [RequireComponent(typeof(ModelController))]
    [RequireComponent(typeof(CameraRequestProvider))]
    [RequireComponent(typeof(QRCodeRequestProvider))]
    public class ChatdollApplication : MonoBehaviour
    {
        protected Chatdoll chatdoll;
        protected ModelController modelController;
        protected VoiceRequestProviderBase voiceRequestProvider;
        protected CameraRequestProvider cameraRequestProvider;
        protected QRCodeRequestProvider qrcodeRequestProvider;
        protected WakeWordListenerBase wakeWordListener;
        protected AnimatedVoiceRequest PromptAnimatedVoiceRequest = new AnimatedVoiceRequest() { StartIdlingOnEnd = false };
        protected AnimatedVoiceRequest ErrorAnimatedVoiceRequest = new AnimatedVoiceRequest();

        [Header("Wake Word and Cancel Word")]
        public string WakeWord;
        public string CancelWord;

        [Header("Prompt")]
        public string PromptVoice;
        public VoiceSource PromptVoiceType;
        public string PromptFace;
        public string PromptAnimation;

        [Header("Voice Request Provider")]
        public MessageWindowBase MessageWindow;

        [Header("Camera")]
        public ChatdollCamera ChatdollCamera;

        protected virtual void Awake()
        {
            // Get components
            chatdoll = gameObject.GetComponent<Chatdoll>();
            modelController = gameObject.GetComponent<ModelController>();
            voiceRequestProvider = gameObject.GetComponent<VoiceRequestProviderBase>();
            cameraRequestProvider = gameObject.GetComponent<CameraRequestProvider>();
            qrcodeRequestProvider = gameObject.GetComponent<QRCodeRequestProvider>();
            wakeWordListener = GetComponent<WakeWordListenerBase>();

            // Prompt
            var httpPrompter = gameObject.GetComponent<HttpPrompter>();
            if (httpPrompter != null)
            {
                chatdoll.OnPromptAsync = httpPrompter.OnPromptAsync;
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

                chatdoll.OnPromptAsync = async (r, u, c, t) =>
                {
                    await modelController.AnimatedSay(PromptAnimatedVoiceRequest, t);
                };
            }

            // Error
            chatdoll.OnErrorAsync = async (r, c, t) =>
            {
                await modelController.AnimatedSay(ErrorAnimatedVoiceRequest, t);
            };

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
                        preRequest.Intent = wakeword.Intent;
                        if (!string.IsNullOrEmpty(wakeword.InlineRequestText))
                        {
                            preRequest.Text = wakeword.InlineRequestText;
                            skipPrompt = true;
                        }
                    }

                    // Invoke chat
                    await chatdoll.StartChatAsync(GetUserId(), skipPrompt, preRequest);
                };

                // Cancel
                wakeWordListener.OnCancelAsync = async () => { chatdoll.StopChat(); };

                // Raise voice detection threshold when chatting
                wakeWordListener.ShouldRaiseThreshold = () => { return chatdoll.IsChatting; };
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

        protected virtual string GetUserId()
        {
            return "user0123456789";
        }
    }
}

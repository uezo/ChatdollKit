using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Model;

namespace ChatdollKit
{
    public enum CloudService
    {
        Other,
        Azure,
        Google,
        Watson,
        OpenAI
    }

    [RequireComponent(typeof(ModelController))]
    [RequireComponent(typeof(DialogController))]
    public class ChatdollKit : MonoBehaviour
    {
        [Header("Application Identifier")]
        public string ApplicationName;
        [HideInInspector] public ModelController ModelController;
        [HideInInspector] public DialogController DialogController;
        [HideInInspector]  public CloudService SpeechService = CloudService.Other;

        // Azure
        [HideInInspector] public string AzureApiKey = string.Empty;
        [HideInInspector] public string AzureRegion = string.Empty;
        [HideInInspector] public string AzureLanguage = "ja-JP";
        [HideInInspector] public string AzureGender = "Female";
        [HideInInspector] public string AzureSpeakerName = "ja-JP-AoiNeural";

        // Google
        [HideInInspector] public string GoogleApiKey = string.Empty;
        [HideInInspector] public string GoogleLanguage = "ja-JP";
        [HideInInspector] public string GoogleGender = "FEMALE";
        [HideInInspector] public string GoogleSpeakerName = "ja-JP-Standard-A";

        // OpenAI
        [HideInInspector] public string OpenAIApiKey = string.Empty;
        [HideInInspector] public string OpenAILanguage = string.Empty;
        [HideInInspector] public string OpenAIVoice = "nova";

        // Watson
        [HideInInspector] public string WatsonTTSApiKey = string.Empty;
        [HideInInspector] public string WatsonTTSBaseUrl = string.Empty;
        [HideInInspector] public string WatsonTTSSpeakerName = "ja-JP_EmiV3Voice";
        [HideInInspector] public string WatsonSTTApiKey = string.Empty;
        [HideInInspector] public string WatsonSTTBaseUrl = string.Empty;
        [HideInInspector] public string WatsonSTTModel = "ja-JP_BroadbandModel";
        [HideInInspector] public bool WatsonSTTRemoveWordSeparation = true;

        public Func<WakeWord, UniTask> OnWakeAsync
        {
            get
            {
                return DialogController.OnWakeAsync;
            }
            set
            {
                DialogController.OnWakeAsync = value;
            }
        }
        public Func<DialogRequest, CancellationToken, UniTask> OnPromptAsync
        {
            get
            {
                return DialogController.OnPromptAsync;
            }
            set
            {
                DialogController.OnPromptAsync = value;
            }

        }
        public Func<Request, CancellationToken, UniTask> OnRequestAsync
        {
            get
            {
                return DialogController.OnRequestAsync;
            }
            set
            {
                DialogController.OnRequestAsync = value;
            }

        }
        public Func<Response, CancellationToken, UniTask> OnResponseAsync
        {
            get
            {
                return DialogController.OnResponseAsync;
            }
            set
            {
                DialogController.OnResponseAsync = value;
            }

        }
        public Func<Request, Exception, CancellationToken, UniTask> OnErrorAsync
        {
            get
            {
                return DialogController.OnErrorAsync;
            }
            set
            {
                DialogController.OnErrorAsync = value;
            }

        }

        protected virtual void Awake()
        {
            ModelController = GetComponent<ModelController>();
            DialogController = GetComponent<DialogController>();
        }

        public async UniTask StartChatAsync(DialogRequest dialogRequest = null)
        {
            if (DialogController != null)
            {
                await DialogController.StartDialogAsync(dialogRequest);
            }
            else
            {
                Debug.LogWarning("Run application before start chatting");
            }
        }

        public void StopChat()
        {
            DialogController.StopDialog();
        }

        protected virtual void OnComponentsReady()
        {
        }

        public virtual ChatdollKitConfig LoadConfig()
        {
            return ChatdollKitConfig.Load(this, ApplicationName);
        }

        public virtual ChatdollKitConfig CreateConfig()
        {
            return ChatdollKitConfig.Create(this);
        }

        // Send text to WakeWordListener instead of voice
        public virtual void SendWakeWord(string text)
        {
            if (DialogController.WakeWordListener != null)
            {
                DialogController.WakeWordListener.TextInput = text;
            }
        }

        // Send text to VoiceRequestProvider instead of voice
        public virtual void SendTextRequest(string text)
        {
            if (DialogController.RequestProviders[RequestType.Voice] != null)
            {
                ((IVoiceRequestProvider)DialogController.RequestProviders[RequestType.Voice]).TextInput = text;
            }
        }
    }
}

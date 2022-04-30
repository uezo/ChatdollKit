using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Model;

namespace ChatdollKit
{
    [RequireComponent(typeof(ModelController))]
    [RequireComponent(typeof(DialogController))]
    public class ChatdollApplication : MonoBehaviour
    {
        [Header("Application Identifier")]
        public string ApplicationName;

        public ModelController ModelController;
        public DialogController DialogController;

        public Action OnDialogComponentsReady
        {
            get
            {
                return DialogController.OnComponentsReady;
            }
            set
            {
                DialogController.OnComponentsReady = value;
            }
        }
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
            OnDialogComponentsReady = OnComponentsReady;
        }

        public async UniTask StartChatAsync()
        {
            if (DialogController != null)
            {
                await DialogController.StartDialogAsync();
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

        public virtual ScriptableObject LoadConfig()
        {
            return Resources.Load<ScriptableObject>(ApplicationName);
        }

        public virtual ScriptableObject CreateConfig(ScriptableObject config = null)
        {
            return ScriptableObject.CreateInstance<ScriptableObject>();
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
                ((VoiceRequestProviderBase)DialogController.RequestProviders[RequestType.Voice]).TextInput = text;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public class NonRecordingVoiceRequestProviderBase : MonoBehaviour, IVoiceRequestProvider
    {
        // This provides voice request
        public RequestType RequestType { get; } = RequestType.Voice;
        public virtual string TextInput { get; set; }
        public virtual bool IsListening { get; protected set; }
        public virtual bool IsMuted { get; set; }
        public bool IsDetectingVoice { get; protected set; } = false;

        [Header("Cancellation Settings")]
        public List<string> CancelWords = new List<string>();
        public List<string> IgnoreWords = new List<string>() { "。", "、", "？", "！" };

        [Header("Test and Debug")]
        public bool PrintResult = false;

        [Header("UI")]
        public IMessageWindow MessageWindow;
        [SerializeField]
        protected string listeningMessage = "[ Listening ... ]";

        // Actions for each status
#pragma warning disable CS1998
        public Func<Request, CancellationToken, UniTask> OnStartListeningAsync;
        public Func<string, UniTask> OnRecognizedAsync;
        public Func<Request, CancellationToken, UniTask> OnFinishListeningAsync;
        public Func<Request, CancellationToken, UniTask> OnErrorAsync
            = async (r, t) => { Debug.LogWarning("NonRecordingVoiceRequestProvider.OnErrorAsync is not implemented"); };
#pragma warning restore CS1998

        // Protected members for recording voice and recognize task
        protected string recognitionId = string.Empty;

        public void SetMessageWindow(IMessageWindow messageWindow)
        {
            MessageWindow = messageWindow;
        }

        public void SetCancelWord(string cancelWord)
        {
            foreach (var cw in CancelWords)
            {
                if (cw == cancelWord)
                {
                    return;
                }
            }

            CancelWords.Add(cancelWord);
        }

#pragma warning disable CS1998
        protected virtual async UniTask OnStartListeningDefaultAsync(Request request, CancellationToken token)
        {
            if (MessageWindow != null)
            {
                MessageWindow.Show(listeningMessage);
            }
            else
            {
                Debug.LogWarning("NonRecordingVoiceRequestProvider.OnStartListeningAsync is not implemented");
            }
        }

        protected virtual async UniTask OnFinishListeningDefaultAsync(Request request, CancellationToken token)
        {
            if (MessageWindow != null)
            {
                await MessageWindow.SetMessageAsync(request.Text, token);
            }
            else
            {
                Debug.LogWarning("NonRecordingVoiceRequestProvider.OnFinishListeningAsync is not implemented");
            }
        }

        public virtual async UniTask<Request> GetRequestAsync(CancellationToken token)
        {
            throw new NotImplementedException("GetRequestAsync method should be implemented at the sub class of NonRecordingVoiceRequestProviderBase");
        }
#pragma warning restore CS1998
    }
}

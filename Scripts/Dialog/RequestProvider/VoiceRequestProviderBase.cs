using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.IO;
using ChatdollKit.Network;

namespace ChatdollKit.Dialog
{
    public class VoiceRequestProviderBase : VoiceRecorderBase, IVoiceRequestProvider
    {
        // This provides voice request
        public RequestType RequestType { get; } = RequestType.Voice;

        [Header("Cancellation Settings")]
        public List<string> CancelWords = new List<string>();
        public List<string> IgnoreWords = new List<string>() { "。", "、", "？", "！" };

        [Header("Test and Debug")]
        public bool PrintResult = false;

        [Header("Voice Recorder Settings")]
        public float VoiceDetectionThreshold = 0.1f;
        public float VoiceDetectionMinimumLength = 0.3f;
        public float SilenceDurationToEndRecording = 1.0f;
        public float ListeningTimeout = 20.0f;

        [Header("UI")]
        public IMessageWindow MessageWindow;
        [SerializeField]
        protected string listeningMessage = "[ Listening ... ]";
        public Action OnListeningStart;
        public Action OnListeningStop;
        public Action OnRecordingStart = () => { Debug.Log("Recording voice request started"); };
        public Action<float> OnDetectVoice;
        public Action<AudioClip> OnRecordingEnd = (a) => { Debug.Log("Recording voice request ended"); };
        public Action<Exception> OnError = (e) => { Debug.LogError($"Recording voice request error: {e.Message}\n{e.StackTrace}"); };

        // Actions for each status
#pragma warning disable CS1998
        public Func<Request, CancellationToken, UniTask> OnStartListeningAsync;
        public Func<string, UniTask> OnRecognizedAsync;
        public Func<Request, CancellationToken, UniTask> OnFinishListeningAsync;
        public Func<Request, CancellationToken, UniTask> OnErrorAsync
            = async (r, t) => { Debug.LogWarning("VoiceRequestProvider.OnErrorAsync is not implemented"); };
#pragma warning restore CS1998

        // Protected members for recording voice and recognize task
        protected string recognitionId = string.Empty;
        protected ChatdollHttp client = new ChatdollHttp();

        public new bool IsListening
        {
            get { return base.IsListening; }
        }

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
                Debug.LogWarning("VoiceRequestProvider.OnStartListeningAsync is not implemented");
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
                Debug.LogWarning("VoiceRequestProvider.OnFinishListeningAsync is not implemented");
            }
        }
#pragma warning restore CS1998

        // Create request using voice recognition
        public virtual async UniTask<Request> GetRequestAsync(CancellationToken token)
        {
            var request = new Request(RequestType);

            voiceDetectionThreshold = VoiceDetectionThreshold;
            voiceDetectionMinimumLength = VoiceDetectionMinimumLength;
            silenceDurationToEndRecording = SilenceDurationToEndRecording;
            onListeningStart = OnListeningStart;
            onListeningStop = OnListeningStop;
            onRecordingStart = OnRecordingStart;
            onDetectVoice = OnDetectVoice;
            onRecordingEnd = OnRecordingEnd;
            onError = OnError;

            StartListening();

            try
            {
                // Update RecognitionId
                var currentRecognitionId = Guid.NewGuid().ToString();
                recognitionId = currentRecognitionId;

                // Invoke action before start recognition
                await (OnStartListeningAsync ?? OnStartListeningDefaultAsync).Invoke(request, token);

                // Get voice from recorder
                var voiceRecorderResponse = await GetVoiceAsync(ListeningTimeout, token);

                // Exit if RecognitionId is updated by another request
                if (recognitionId != currentRecognitionId)
                {
                    Debug.Log($"Id was updated by another request: Current {currentRecognitionId} / Global {recognitionId}");
                }
                else if (voiceRecorderResponse != null)
                {
                    if (!string.IsNullOrEmpty(voiceRecorderResponse.Text))
                    {
                        request.Text = voiceRecorderResponse.Text;
                        if (PrintResult)
                        {
                            Debug.Log($"Text input(VoiceRequestProvider): {request.Text}");
                        }
                    }
                    else if (voiceRecorderResponse.Voice != null)
                    {
                        request.Text = await RecognizeSpeechAsync(voiceRecorderResponse);
                        if (OnRecognizedAsync != null)
                        {
                            await OnRecognizedAsync(request.Text);
                        }
                        if (PrintResult)
                        {
                            Debug.Log($"Recognized(VoiceRequestProvider): {request.Text}");
                        }
                    }
                }

                if (!request.IsSet())
                {
                    Debug.LogWarning("No speech recognized");
                }

                // Clean up to check cancel
                var text = request.Text;
                foreach (var iw in IgnoreWords)
                {
                    text = text.Replace(iw, string.Empty);
                }

                // Check cancellation
                if (CancelWords.Contains(text))
                {
                    Debug.LogWarning("Request canceled");
                    request.IsCanceled = true;
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Canceled during recognizing speech");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in recognizing speech: {ex.Message}\n{ex.StackTrace}");
                await OnErrorAsync(request, token);
            }
            finally
            {
                StopListening();
                // Invoke action after recognition
                await (OnFinishListeningAsync ?? OnFinishListeningDefaultAsync).Invoke(request, token);
            }

            return request;
        }

#pragma warning disable CS1998
        protected virtual async UniTask<string> RecognizeSpeechAsync(VoiceRecorderResponse recordedVoice)
        {
            throw new NotImplementedException("RecognizeSpeechAsync method should be implemented at the sub class of VoiceRequestProviderBase");
        }
#pragma warning restore CS1998
    }
}

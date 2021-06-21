using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.IO;
using ChatdollKit.Network;
using System.Collections.Generic;


namespace ChatdollKit.Dialog
{
    public class VoiceRequestProviderBase : VoiceRecorderBase, IRequestProvider
    {
        // This provides voice request
        public RequestType RequestType { get; } = RequestType.Voice;

        [Header("Cancellation Settings")]
        public List<string> CancelWords = new List<string>();
        public List<string> IgnoreWords = new List<string>() { "。", "、", "？", "！" };

        [Header("Test and Debug")]
        public bool UseDummy = false;
        public string DummyText = string.Empty;
        public bool PrintResult = false;

        [Header("Voice Recorder Settings")]
        public float VoiceDetectionThreshold = 0.1f;
        public float VoiceDetectionMinimumLength = 0.3f;
        public float SilenceDurationToEndRecording = 1.0f;
        public float ListeningTimeout = 20.0f;

        [Header("UI")]
        public MessageWindowBase MessageWindow;

        public Action OnListeningStart;
        public Action OnListeningStop;
        public Action OnRecordingStart = () => { Debug.Log("Recording voice request started"); };
        public Action<float> OnDetectVoice;
        public Action<AudioClip> OnRecordingEnd = (a) => { Debug.Log("Recording voice request ended"); };
        public Action<Exception> OnError = (e) => { Debug.LogError($"Recording voice request error: {e.Message}\n{e.StackTrace}"); };

        // Actions for each status
#pragma warning disable CS1998
        public Func<Request, State, CancellationToken, Task> OnStartListeningAsync;
        public Func<string, Task> OnRecognizedAsync;
        public Func<Request, State, CancellationToken, Task> OnFinishListeningAsync;
        public Func<Request, State, CancellationToken, Task> OnErrorAsync
            = async (r, c, t) => { Debug.LogWarning("VoiceRequestProvider.OnErrorAsync is not implemented"); };
#pragma warning restore CS1998

        // Private and protected members for recording voice and recognize task
        private string recognitionId = string.Empty;
        protected ChatdollHttp client = new ChatdollHttp();

#pragma warning disable CS1998
        private async Task OnStartListeningDefaultAsync(Request request, State state, CancellationToken token)
        {
            if (MessageWindow != null)
            {
                MessageWindow.Show("(Listening...)");
            }
            else
            {
                Debug.LogWarning("VoiceRequestProvider.OnStartListeningAsync is not implemented");
            }
        }

        private async Task OnFinishListeningDefaultAsync(Request request, State state, CancellationToken token)
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
        public async Task<Request> GetRequestAsync(User user, State state, CancellationToken token, Request preRequest = null)
        {
            if (preRequest != null && !string.IsNullOrEmpty(preRequest.Text))
            {
                preRequest.User = user;
                return preRequest;
            }

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

            var request = preRequest ?? new Request(RequestType);
            request.User = user;

            try
            {
                // Update RecognitionId
                var currentRecognitionId = Guid.NewGuid().ToString();
                recognitionId = currentRecognitionId;

                // Invoke action before start recognition
                await (OnStartListeningAsync ?? OnStartListeningDefaultAsync).Invoke(request, state, token);

                // For debugging and testing
                if (UseDummy)
                {
                    await Task.Delay(1000);
                    request.Text = DummyText;
                }
                else
                {
                    var voiceRecorderResponse = await GetVoiceAsync(ListeningTimeout, token);

                    // Exit if RecognitionId is updated by another request
                    if (recognitionId != currentRecognitionId)
                    {
                        Debug.Log($"Id was updated by another request: Current {currentRecognitionId} / Global {recognitionId}");
                    }
                    else if (voiceRecorderResponse != null && voiceRecorderResponse.Voice != null)
                    {
                        request.Text = await RecognizeSpeechAsync(voiceRecorderResponse.Voice);
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
            catch (TaskCanceledException)
            {
                Debug.Log("Canceled during recognizing speech");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in recognizing speech: {ex.Message}\n{ex.StackTrace}");
                await OnErrorAsync(request, state, token);
            }
            finally
            {
                StopListening();
                // Invoke action after recognition
                await (OnFinishListeningAsync ?? OnFinishListeningDefaultAsync).Invoke(request, state, token);
            }

            return request;
        }

#pragma warning disable CS1998
        protected virtual async Task<string> RecognizeSpeechAsync(AudioClip recordedVoice)
        {
            throw new NotImplementedException("RecognizeSpeechAsync method should be implemented at the sub class of VoiceRequestProviderBase");
        }
#pragma warning restore CS1998
    }
}

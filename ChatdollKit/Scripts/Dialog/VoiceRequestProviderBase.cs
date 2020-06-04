using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.IO;
using ChatdollKit.Network;


namespace ChatdollKit.Dialog
{
    public class VoiceRequestProviderBase : VoiceRecorderBase, IRequestProvider
    {
        // This provides voice request
        public RequestType RequestType { get; } = RequestType.Voice;

        [Header("Test and Debug")]
        public bool UseDummy = false;
        public string DummyText = string.Empty;
        public bool PrintResult = false;

        [Header("Voice Recorder Settings")]
        public float VoiceDetectionThreshold = 0.1f;
        public float VoiceDetectionMinimumLength = 0.3f;
        public float SilenceDurationToEndRecording = 1.0f;
        public float ListeningTimeout = 20.0f;

        public Action OnListeningStart;
        public Action OnListeningStop;
        public Action OnRecordingStart = () => { Debug.Log("Recording voice request started"); };
        public Action<float> OnDetectVoice;
        public Action<AudioClip> OnRecordingEnd = (a) => { Debug.Log("Recording voice request ended"); };
        public Action<Exception> OnError = (e) => { Debug.LogError($"Recording voice request error: {e.Message}\n{e.StackTrace}"); };

        // Actions for each status
        public Func<Request, Context, CancellationToken, Task> OnStartListeningAsync
            = async (r, c, t) => { Debug.LogWarning("VoiceRequestProvider.OnStartListeningAsync is not implemented"); };
        public Func<string, Task> OnRecognizedAsync;
        public Func<Request, Context, CancellationToken, Task> OnFinishListeningAsync
            = async(r, c, t) => { Debug.LogWarning("VoiceRequestProvider.OnFinishListeningAsync is not implemented"); };
        public Func<Request, Context, CancellationToken, Task> OnErrorAsync
            = async (r, c, t) => { Debug.LogWarning("VoiceRequestProvider.OnErrorAsync is not implemented"); };

        // Private and protected members for recording voice and recognize task
        private string recognitionId = string.Empty;
        protected ChatdollHttp client = new ChatdollHttp();

        // Create request using voice recognition
        public async Task<Request> GetRequestAsync(User user, Context context, CancellationToken token)
        {
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

            var request = new Request(RequestType, user)
            {
                Text = string.Empty
            };

            try
            {
                // Update RecognitionId
                var currentRecognitionId = Guid.NewGuid().ToString();
                recognitionId = currentRecognitionId;

                // Invoke action before start recognition
                await OnStartListeningAsync(request, context, token);

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
            }
            catch (TaskCanceledException)
            {
                Debug.Log("Canceled during recognizing speech");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in recognizing speech: {ex.Message}\n{ex.StackTrace}");
                await OnErrorAsync(request, context, token);
            }
            finally
            {
                // Invoke action after recognition
                await OnFinishListeningAsync(request, context, token);
            }

            return request;
        }

        protected virtual async Task<string> RecognizeSpeechAsync(AudioClip recordedVoice)
        {
            throw new NotImplementedException("RecognizeSpeechAsync method should be implemented at the sub class of VoiceRequestProviderBase");
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.IO;
using ChatdollKit.Network;


namespace ChatdollKit.Dialog
{
    [RequireComponent(typeof(VoiceRecorder))]
    public class VoiceRequestProviderBase : MonoBehaviour, IRequestProvider
    {
        // This provides voice request
        public RequestType RequestType { get; } = RequestType.Voice;

        [Header("Test and Debug")]
        public bool UseDummy = false;
        public string DummyText = string.Empty;
        public bool PrintResult = false;

        [Header("Voice Recorder Settings")]
        public int SamplingFrequency = 16000;
        public float VoiceDetectionThreshold = 0.1f;
        public float VoiceDetectionMinimumLength = 0.3f;
        public float SilenceDurationToEndRecording = 1.0f;
        public float RecordingStartTimeout = 0.0f;
        public float ListeningTimeout = 20.0f;

        public Action OnListeningStart;
        public Action OnListeningStop;
        public Action OnRecordingStart = () => { Debug.Log("Recording voice request started"); };
        public Action OnDetectVoice;
        public Action<AudioClip> OnRecordingEnd = (a) => { Debug.Log("Recording voice request ended"); };
        public Action<Exception> OnError = (e) => { Debug.LogError($"Recording voice request error: {e.Message}\n{e.StackTrace}"); };

        // Actions for each status
        public Func<Request, Context, CancellationToken, Task> OnStartListeningAsync
            = async (r, c, t) => { Debug.LogWarning("VoiceRequestProvider.OnStartListeningAsync is not implemented"); };
        public Func<Request, Context, CancellationToken, Task> OnFinishListeningAsync
            = async(r, c, t) => { Debug.LogWarning("VoiceRequestProvider.OnFinishListeningAsync is not implemented"); };
        public Func<Request, Context, CancellationToken, Task> OnErrorAsync
            = async (r, c, t) => { Debug.LogWarning("VoiceRequestProvider.OnErrorAsync is not implemented"); };

        // Private and protected members for recording voice and recognize task
        private VoiceRecorder voiceRecorder;
        private string recognitionId = string.Empty;
        protected ChatdollHttp client = new ChatdollHttp();

        private void Awake()
        {
            voiceRecorder = gameObject.GetComponent<VoiceRecorder>();
        }

        // Create request using voice recognition
        public async Task<Request> GetRequestAsync(User user, Context context, CancellationToken token)
        {
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
                    var voiceRecorderRequest = new VoiceRecorderRequest()
                    {
                        SamplingFrequency = SamplingFrequency,
                        VoiceDetectionThreshold = VoiceDetectionThreshold,
                        VoiceDetectionMinimumLength = VoiceDetectionMinimumLength,
                        SilenceDurationToEndRecording = SilenceDurationToEndRecording,
                        OnListeningStart = OnListeningStart,
                        OnListeningStop = OnListeningStop,
                        OnRecordingStart = OnRecordingStart,
                        OnDetectVoice = OnDetectVoice,
                        OnRecordingEnd = OnRecordingEnd,
                        OnError = OnError,
                    };
                    var voiceRecorderResponse = await voiceRecorder.GetVoiceAsync(voiceRecorderRequest, token);

                    // Exit if RecognitionId is updated by another request
                    if (recognitionId != currentRecognitionId)
                    {
                        Debug.Log($"Id was updated by another request: Current {currentRecognitionId} / Global {recognitionId}");
                    }
                    else if (voiceRecorderResponse != null && voiceRecorderResponse.Voice != null)
                    {
                        request.Text = await RecognizeSpeechAsync(voiceRecorderResponse.Voice);
                        if (PrintResult)
                        {
                            Debug.Log($"Recognized: {request.Text}");
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

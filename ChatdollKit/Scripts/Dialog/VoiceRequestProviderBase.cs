using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.IO;
using ChatdollKit.Network;


namespace ChatdollKit.Dialog
{
    public class VoiceRequestProviderBase : MonoBehaviour, IRequestProvider
    {
        // This provides voice request
        public RequestType RequestType { get; } = RequestType.Voice;

        // Dummy for test
        public bool UseDummy = false;
        public string DummyText = string.Empty;

        // General configuration
        public bool PrintResult = false;

        // Actions for each status
        public Func<Request, Context, CancellationToken, Task> OnStartListeningAsync
            = async (r, c, t) => { Debug.LogWarning("VoiceRequestProvider.OnStartListeningAsync is not implemented"); };
        public Func<Request, Context, CancellationToken, Task> OnFinishListeningAsync
            = async(r, c, t) => { Debug.LogWarning("VoiceRequestProvider.OnFinishListeningAsync is not implemented"); };
        public Func<Request, Context, CancellationToken, Task> OnErrorAsync
            = async (r, c, t) => { Debug.LogWarning("VoiceRequestProvider.OnErrorAsync is not implemented"); };

        // Private and protected members for recognize task
        private string recognitionId = string.Empty;
        protected ChatdollHttp client = new ChatdollHttp();

        // Create request using voice recognition
        public async Task<Request> GetRequestAsync(User user, Context context, CancellationToken token)
        {
            var request = new Request(RequestType, user)
            {
                Text = string.Empty
            };

            // Listen voice
            try
            {
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
                    var recordedVoice = await GetVoiceAsync();
                    if (recordedVoice != null)
                    {
                        request.Text = await RecognizeSpeechAsync(recordedVoice);
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

        private async Task<AudioClip> GetVoiceAsync()
        {
            // Update RecognitionId
            var currentRecognitionId = Guid.NewGuid().ToString();
            recognitionId = currentRecognitionId;
            AudioClip recordedVoice = null;

            // Setup VoiceRecorder
            var voiceRecorder = gameObject.GetComponent<VoiceRecorder>();
            voiceRecorder.StopListeningOnDetectionEnd = true;
            voiceRecorder.OnRecordingEnd = (a) => { recordedVoice = a; };

            // Start recording
            voiceRecorder.StartRecorder();

            // Wait for voice or timeout
            while (recordedVoice == null)
            {
                if (voiceRecorder.Status == VoiceRecorderStatus.NotWorking)
                {
                    Debug.Log($"VoiceRecorder timeout");
                    return null;
                }
                await Task.Delay(10);
            }

            // Exit if RecognitionId is updated by another request
            if (recognitionId != currentRecognitionId)
            {
                Debug.Log($"Id was updated by another request: Current {currentRecognitionId} / Global {recognitionId}");
                return null;
            }

            // Exit if audio clip to recognize is empty
            if (recordedVoice == null)
            {
                Debug.LogWarning("No voice to recognize");
                return null;
            }

            return recordedVoice;
        }

        protected virtual async Task<string> RecognizeSpeechAsync(AudioClip recordedVoice)
        {
            throw new NotImplementedException("RecognizeSpeechAsync method should be implemented at the sub class of VoiceRequestProviderBase");
        }
    }
}

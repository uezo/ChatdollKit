using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using FrostweepGames.Plugins.GoogleCloud.SpeechRecognition;
using ChatdollKit.Dialog;


namespace ChatdollKit.Extension
{
    public class GoogleCloudSpeechRequestProvider : MonoBehaviour, IRequestProvider
    {
        // This provides voice request
        public RequestType RequestType { get; } = RequestType.Voice;

        // GCSR configurations
        public string ApiKey;
        public string Language = "ja-JP";
        public float Timeout = 6.0f;

        // Dummy for test
        public bool UseDummy = false;
        public string DummyText = string.Empty;

        // Actions for each status
        public Func<Request, Context, CancellationToken, Task> OnStartListeningAsync;
        public Func<Request, Context, CancellationToken, Task> OnFinishListeningAsync;
        public Func<Request, Context, CancellationToken, Task> OnErrorAsync;

        // GCSpeechRecognition
        private GCSpeechRecognition speechRecognition;

        // Private members for recognize task
        private string recognitionId = string.Empty;
        private AudioClip recordedClip { get; set; }
        private float[] recordedRawData { get; set; }
        private bool nowRecording = false;


        private void Start()
        {
            // Configure GCSpeechRecognition
            speechRecognition = GCSpeechRecognition.Instance;
            speechRecognition.RecordFailedEvent += RecordFailedEventHandler;
            speechRecognition.EndTalkigEvent += EndTalkigEventHandler;

            if (!string.IsNullOrEmpty(ApiKey))
            {
                speechRecognition.apiKey = ApiKey;
            }

            // Select the first microphone device
            if (speechRecognition.HasConnectedMicrophoneDevices())
            {
                speechRecognition.SetMicrophoneDevice(speechRecognition.GetMicrophoneDevices()[0]);
            }
        }

        private void OnDestroy()
        {
            // Detach event handlers
            speechRecognition.RecordFailedEvent -= RecordFailedEventHandler;
            speechRecognition.EndTalkigEvent -= EndTalkigEventHandler;
        }

        // Create request using voice recognition
        public async Task<Request> GetRequestAsync(User user, Context context, CancellationToken token)
        {
            var request = new Request(RequestType, user);

            // Listen voice
            try
            {
                // Invoke action before start recognition
                await OnStartListeningAsync?.Invoke(request, context, token);

                // Recognize speech
                request.Text = await RecognizeOnceAsync();
                if (request.IsSet())
                {
                    Debug.Log(request.Text);
                }
                else
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
                await OnErrorAsync?.Invoke(request, context, token);
            }
            finally
            {
                // Invoke action after recognition
                await OnFinishListeningAsync?.Invoke(request, context, token);
            }

            return request;
        }

        // Recognize speech by GCSR
        public async Task<string> RecognizeOnceAsync()
        {
            // Update RecognitionId
            var currentRecognitionId = Guid.NewGuid().ToString();
            recognitionId = currentRecognitionId;

            // For debugging and testing
            if (UseDummy)
            {
                await Task.Delay(1000);
                return DummyText;
            }

            try
            {
                // Start recording
                speechRecognition.StartRecord(true);
                nowRecording = true;

                // Wait for talking ends or timeout
                var startTime = Time.time;
                while (nowRecording)
                {
                    if (Time.time - startTime > Timeout)
                    {
                        Debug.Log($"Recording timeout");
                        return string.Empty;
                    }
                    await Task.Delay(50);
                }

                // Stop recording just after voice detected
                speechRecognition.StopRecord();

                // Exit if RecognitionId is updated by another request
                if (recognitionId != currentRecognitionId)
                {
                    Debug.Log($"Id was updated by another request: Current {currentRecognitionId} / Global {recognitionId}");
                    return string.Empty;
                }

                // Exit if audio clip to recognize is empty
                if (recordedClip == null)
                {
                    Debug.LogError("No audio clip to recognize");
                    return string.Empty;
                }

                // Set config for each request
                var config = RecognitionConfig.GetDefault();
                config.languageCode = Language;
                config.audioChannelCount = recordedClip.channels;

                // Compose request
                var recognitionRequest = new GeneralRecognitionRequest()
                {
                    audio = new RecognitionAudioContent()
                    {
                        content = recordedRawData.ToBase64()
                    },
                    config = config
                };
                string postData = JsonConvert.SerializeObject(recognitionRequest);
                var content = new StringContent(postData, Encoding.UTF8, "application/json");

                // Post to recognize
                using (var client = new HttpClient())
                {
                    var response = await client.PostAsync("https://speech.googleapis.com/v1/speech:recognize?key=" + speechRecognition.apiKey, content);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"Error occured while calling GCSR ({response.StatusCode.ToString()})");
                    }
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var recognitionResponse = JsonConvert.DeserializeObject<RecognitionResponse>(responseContent);

                    // Return empty when nothing recognized
                    if (recognitionResponse.results.Length == 0 || recognitionResponse.results[0].alternatives.Length == 0)
                    {
                        Debug.Log("Nothing recognized by GCSR");
                        return string.Empty;
                    }

                    // Return recognized text
                    return recognitionResponse.results[0].alternatives[0].transcript;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in recognizing by GCSR: {ex.Message}\n{ex.StackTrace}");
            }

            return string.Empty;
        }

        // Handler for recording error
        private void RecordFailedEventHandler()
        {
            Debug.LogError("Recording failed. Make sure that the microphone is available");
        }

        // Handler when talking ends
        public void EndTalkigEventHandler(AudioClip clip, float[] raw)
        {
            recordedClip = clip;
            recordedRawData = raw;
            nowRecording = false;
        }
    }
}
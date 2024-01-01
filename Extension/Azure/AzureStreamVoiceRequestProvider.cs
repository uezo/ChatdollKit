using System;
using System.Threading;
using UnityEngine;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;

namespace ChatdollKit.Extension.Azure
{
    public class AzureStreamVoiceRequestProvider : NonRecordingVoiceRequestProviderBase
    {
        [Header("Azure Settings")]
        public string ApiKey;
        public string Region;
        public string Language = "ja-JP";

        [Header("Speech Recognition Settings")]
        public float SilenceDurationToEndRecording = 1.0f;
        public float ListeningTimeout = 20.0f;

        private SpeechRecognizer recognizer { get; set; }
        private string recognizedTextBuffer { get; set; }

        private string textInput { get; set; }
        public override string TextInput
        {
            get
            {
                return textInput;
            }
            set
            {
                recognizedTextBuffer = value;   // To show at message window
                textInput = value;
            }
        }

        private bool isMuted = false;
        public bool IsMuted {
            get
            {
                return isMuted;
            }
            set
            {
                isMuted = value;
                if (isMuted)
                {
                    // Mute
                    if (IsListening)
                    {
                        _ = StopSpeechRecognizerAsync();
                    }
                }
                else
                {
                    // Unmute
                    if (IsListening)
                    {
                        _ = StartSpeechRecognizerAsync();
                    }
                }
            }
        }

        public bool IsListening { get; set; } = false;

        private EventHandler<SpeechRecognitionEventArgs> OnRecognizing { get; set; }
        private EventHandler<SpeechRecognitionEventArgs> OnRecognized { get; set; }

        private void Awake()
        {
            InitSpeechRecognitionHandlers();
        }

        private void InitSpeechRecognitionHandlers()
        {
            OnRecognizing = (sender, e) =>
            {
                recognizedTextBuffer = e.Result.Text;
            };
            OnRecognized = (sender, e) =>
            {
                Debug.Log("Stop on recognizer.Recognized");
                _ = StopSpeechRecognizerAsync();
                TextInput = e.Result.Text;

                // No text recognized when timeout
                if (string.IsNullOrEmpty(TextInput))
                {
                    IsListening = false;
                }
            };
        }

        public async UniTask StartSpeechRecognizerAsync()
        {
            // Stop speech recognizer if already running
            if (recognizer != null)
            {
                await StopSpeechRecognizerAsync();
            }

            // Configuration
            var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
            var speechConfig = SpeechConfig.FromSubscription(ApiKey, Region);
            if (!string.IsNullOrEmpty(Language))
            {
                speechConfig.SpeechRecognitionLanguage = Language;
            }
            speechConfig.SetProperty(
                PropertyId.Speech_SegmentationSilenceTimeoutMs,
                ((int)(SilenceDurationToEndRecording * 1000)).ToString()
            );
            speechConfig.SetProperty(
                PropertyId.Conversation_Initial_Silence_Timeout,
                ((int)(ListeningTimeout * 1000)).ToString()
            );

            // Initialize recognizer
            recognizer = new SpeechRecognizer(speechConfig, audioConfig);
            recognizer.Recognizing += OnRecognizing;
            recognizer.Recognized += OnRecognized;

            // Start recognizer
            recognizedTextBuffer = string.Empty;
            await recognizer.StartContinuousRecognitionAsync();

            Debug.Log("Azure Speech SDK recognizer started");
        }

        public async UniTask StopSpeechRecognizerAsync()
        {
            if (recognizer != null)
            {
                try
                {
                    recognizer.Recognizing -= OnRecognizing;
                    recognizer.Recognized -= OnRecognized;
                    await recognizer.StopContinuousRecognitionAsync();
                    recognizer.Dispose();
                    Debug.Log("Azure Speech SDK recognizer stopped");
                }
                catch (ObjectDisposedException)
                {
                    // Do nothing
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error occured at StopSpeechRecognizerAsync: {ex.Message}\n{ex.StackTrace}");
                }
                finally
                {
                    recognizer = null;
                }
            }
        }

        public override async UniTask<Request> GetRequestAsync(CancellationToken token)
        {
            var request = new Request(RequestType);
            IsListening = true;
            TextInput = string.Empty;
            recognizedTextBuffer = string.Empty;

            try
            {
                // Update RecognitionId
                var currentRecognitionId = Guid.NewGuid().ToString();
                recognitionId = currentRecognitionId;

                // Invoke action before start recognition
                await (OnStartListeningAsync ?? OnStartListeningDefaultAsync).Invoke(request, token);

                // Start Azure Speech Recognizer if not muted
                if (!IsMuted)
                {
                    await StartSpeechRecognizerAsync(); // Will stop at OnRecognized or exception handlers
                }

                // Show message window
                _ = ((SimpleMessageWindow)MessageWindow).SetMessageStreamAsync(() => {
                    return new StreamMessageRequest() { Text = recognizedTextBuffer, IsEnd = !string.IsNullOrEmpty(TextInput) || !IsListening };
                }, token);

                // Wait for input
                while (IsListening && string.IsNullOrEmpty(TextInput))
                {
                    await UniTask.Delay(50, cancellationToken: token);
                }
                IsListening = false;

                if (PrintResult)
                {
                    Debug.Log($"Voice request: (AzureStreamVoiceRequestProvider): {TextInput}");
                }

                // Set text to request
                request.Text = TextInput;

                if (!request.IsSet())
                {
                    Debug.Log("No speech recognized");
                }

                if (OnRecognizedAsync != null)
                {
                    await OnRecognizedAsync(request.Text);
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
                    Debug.Log($"Request canceled by cancel word: {text}");
                    request.IsCanceled = true;
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Token canceled during recognizing speech");
                await CancelRequestAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured at GetRequestAsync: {ex.Message}\n{ex.StackTrace}");
                await OnErrorAsync(request, token);
                await CancelRequestAsync();
            }
            finally
            {
                // Invoke action after recognition
                await (OnFinishListeningAsync ?? OnFinishListeningDefaultAsync).Invoke(request, token);
            }

            return request;
        }

        public async UniTask CancelRequestAsync()
        {
            await StopSpeechRecognizerAsync();
            IsListening = false;
        }

#pragma warning disable CS1998
        protected override async UniTask OnFinishListeningDefaultAsync(Request request, CancellationToken token)
        {
            // Do nothing (In default settings, the entire recognized text is displayed.)
        }
#pragma warning restore CS1998
    }

}

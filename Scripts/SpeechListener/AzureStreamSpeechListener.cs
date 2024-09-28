using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace ChatdollKit.SpeechListener
{
    public class AzureStreamSpeechListener : MonoBehaviour, ISpeechListener
    {
        [Header("Voice Recorder Settings")]
        public float SilenceDurationThreshold = 0.3f;
        public float ListeningTimeout = 30.0f;  // Session timeout. Start new recognition session after silence for ListeningTimeout.
        public bool PrintResult = false;

        [Header("Azure Settings")]
        public string ApiKey = string.Empty;
        public string Region = string.Empty;
        public string Language = "ja-JP";

        // Recognizer
        public Func<string, UniTask> OnRecognized { get; set; }
        private SpeechRecognizer recognizer { get; set; }
        private EventHandler<SpeechRecognitionEventArgs> OnRecognizerRecognizing { get; set; }
        private EventHandler<SpeechRecognitionEventArgs> OnRecognizerRecognized { get; set; }
        private Queue<string> recognizedTextQueue = new Queue<string>();
        public string RecognizedTextBuffer { get; private set; }

        public void StartListening(bool stopBeforeStart = false)
        {
            _ = StartRecognizerAsync(stopBeforeStart);
        }

        public void StopListening()
        {
            _ = StopRecognizerAsync();
        }

        private void Awake()
        {
            OnRecognizerRecognizing = (sender, e) =>
            {
                RecognizedTextBuffer = e.Result.Text;
            };
            OnRecognizerRecognized = (sender, e) =>
            {
                if (PrintResult)
                {
                    Debug.Log($"Speech recognized: {e.Result.Text}");
                }

                if (!string.IsNullOrEmpty(e.Result.Text))
                {
                    // Just enqueue to process OnRecognized in main thread
                    recognizedTextQueue.Enqueue(e.Result.Text);
                }
            };
        }

        private void Update()
        {
            if (recognizedTextQueue.Count > 0)
            {
                var recognizedText = recognizedTextQueue.Dequeue();
                recognizedTextQueue.Clear();
                // Execute in main thread
                RecognizedTextBuffer = string.Empty;
                OnRecognized?.Invoke(recognizedText);
            }
        }

        protected void OnDestroy()
        {
            _ = StopRecognizerAsync();
        }

        private async UniTask StartRecognizerAsync(bool stopBeforeStart)
        {
            // Stop speech recognizer if already running
            if (recognizer != null && stopBeforeStart)
            {
                await StopRecognizerAsync();
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
                ((int)(SilenceDurationThreshold * 1000)).ToString()
            );
            speechConfig.SetProperty(
                PropertyId.Conversation_Initial_Silence_Timeout,
                ((int)(ListeningTimeout * 1000)).ToString()
            );

            // Initialize recognizer
            recognizer = new SpeechRecognizer(speechConfig, audioConfig);
            recognizer.Recognizing += OnRecognizerRecognizing;
            recognizer.Recognized += OnRecognizerRecognized;

            // Start recognizer
            RecognizedTextBuffer = string.Empty;
            await recognizer.StartContinuousRecognitionAsync();

            Debug.Log("Azure Speech SDK recognizer started");
        }

        private async UniTask StopRecognizerAsync()
        {
            if (recognizer == null) return;

            try
            {
                recognizer.Recognizing -= OnRecognizerRecognizing;
                recognizer.Recognized -= OnRecognizerRecognized;
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
                Debug.LogError($"Error occured at StopRecognizerAsync: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                recognizer = null;
            }
        }

        public void ChangeSessionConfig(float silenceDurationThreshold = float.MinValue, float minRecordingDuration = float.MinValue, float maxRecordingDuration = float.MinValue)
        {
            if (recognizer == null) return;

            
            recognizer.Properties.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, ((int)(silenceDurationThreshold * 1000)).ToString());
        }
    }   
}

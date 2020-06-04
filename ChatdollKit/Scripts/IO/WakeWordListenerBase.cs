using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Network;


namespace ChatdollKit.IO
{
    public class WakeWordListenerBase : VoiceRecorderBase
    {
        public List<string> WakeWords;
        public Func<string, Task> OnRecognizedAsync;
        public Func<Task> OnWakeAsync;

        [Header("Test and Debug")]
        public bool PrintResult = false;

        [Header("Voice Recorder Settings")]
        public float VoiceDetectionThreshold = 0.1f;
        public float VoiceDetectionMinimumLength = 0.2f;
        public float SilenceDurationToEndRecording = 0.3f;

        public Action OnListeningStart;
        public Action OnListeningStop;
        public Action OnRecordingStart = () => { Debug.Log("Recording wakeword started"); };
        public Action<float> OnDetectVoice;
        public Action<AudioClip> OnRecordingEnd = (a) => { Debug.Log("Recording wakeword ended"); };
        public Action<Exception> OnError = (e) => { Debug.LogError($"Recording wakeword error: {e.Message}\n{e.StackTrace}"); };

        // Private and protected members for recording voice and recognize task
        private CancellationTokenSource cancellationTokenSource;
        protected ChatdollHttp client = new ChatdollHttp();

        protected override void OnDestroy()
        {
            base.OnDestroy();
            cancellationTokenSource?.Cancel();
        }

        public async Task StartListeningAsync()
        {
            StartListening();   // Start recorder here to asure that GetVoiceAsync will be called after recorder started

            if (OnWakeAsync == null)
            {
                Debug.LogError("OnWakeAsync must be set");
                return;
            }

            cancellationTokenSource = new CancellationTokenSource();
            var token = cancellationTokenSource.Token;

            while (!token.IsCancellationRequested)
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

                var voiceRecorderResponse = await GetVoiceAsync(0.0f, token);
                if (voiceRecorderResponse != null && voiceRecorderResponse.Voice != null)
                {
                    _ = ProcessVoiceAsync(voiceRecorderResponse.Voice); // Do not await to continue listening
                }
            }
        }

        private async Task ProcessVoiceAsync(AudioClip voice)
        {
            // Recognize speech
            var recognizedText = await RecognizeSpeechAsync(voice);
            if (OnRecognizedAsync != null)
            {
                await OnRecognizedAsync(recognizedText);
            }
            if (PrintResult)
            {
                Debug.Log($"Recognized(WakeWordListener): {recognizedText}");
            }
            foreach (var ww in WakeWords)
            {
                if (recognizedText.Contains(ww))
                {
                    try
                    {
                        await OnWakeAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"OnWakeAsync failed: {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }
        }

        protected virtual async Task<string> RecognizeSpeechAsync(AudioClip recordedVoice)
        {
            throw new NotImplementedException("RecognizeSpeechAsync method should be implemented at the sub class of WakeWordListenerBase");
        }
    }
}

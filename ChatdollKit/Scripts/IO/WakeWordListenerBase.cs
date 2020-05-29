using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Network;


namespace ChatdollKit.IO
{
    [RequireComponent(typeof(VoiceRecorder))]
    public class WakeWordListenerBase : MonoBehaviour
    {
        public List<string> WakeWords;
        public Func<Task> OnWakeAsync;

        [Header("Test and Debug")]
        public bool PrintResult = false;

        [Header("Voice Recorder Settings")]
        public int SamplingFrequency = 16000;
        public float VoiceDetectionThreshold = 0.1f;
        public float VoiceDetectionMinimumLength = 0.2f;
        public float SilenceDurationToEndRecording = 0.3f;

        public Action OnListeningStart;
        public Action OnListeningStop;
        public Action OnRecordingStart = () => { Debug.Log("Recording wakeword started"); };
        public Action OnDetectVoice;
        public Action<AudioClip> OnRecordingEnd = (a) => { Debug.Log("Recording wakeword ended"); };
        public Action<Exception> OnError = (e) => { Debug.LogError($"Recording wakeword error: {e.Message}\n{e.StackTrace}"); };

        // Private and protected members for recording voice and recognize task
        private VoiceRecorder voiceRecorder;
        private CancellationTokenSource cancellationTokenSource;
        protected ChatdollHttp client = new ChatdollHttp();


        private void Awake()
        {
            voiceRecorder = gameObject.GetComponent<VoiceRecorder>();
        }

        private void OnDestroy()
        {
            cancellationTokenSource?.Cancel();
        }

        public async Task StartListeningAsync()
        {
            if (OnWakeAsync == null)
            {
                Debug.LogError("OnWakeAsync must be set");
                return;
            }

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

            cancellationTokenSource = new CancellationTokenSource();
            var token = cancellationTokenSource.Token;

            while (!token.IsCancellationRequested)
            {
                var voiceRecorderResponse = await voiceRecorder.GetVoiceAsync(voiceRecorderRequest, token);
                if (voiceRecorderResponse != null && voiceRecorderResponse.Voice != null)
                {
                    // Recognize speech
                    var recognizedText = await RecognizeSpeechAsync(voiceRecorderResponse.Voice);
                    if (PrintResult)
                    {
                        Debug.Log($"Voice detected: {recognizedText}");
                    }
                    foreach (var ww in WakeWords)
                    {
                        if (recognizedText.Contains(ww))
                        {
                            await OnWakeAsync();
                        }
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

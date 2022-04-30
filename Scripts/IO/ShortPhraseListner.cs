using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.IO
{
    public class ShortPhraseListener : VoiceRecorderBase
    {
        public bool AutoStart = true;
        public Func<string, UniTask> OnRecognizedAsync;

        [Header("Test and Debug")]
        public bool PrintResult = false;

        [Header("Voice Recorder Settings")]
        public float VoiceDetectionThreshold = 0.1f;
        public float VoiceDetectionRaisedThreshold = 0.5f;
        public float VoiceDetectionMinimumLength = 0.2f;
        public float SilenceDurationToEndRecording = 0.3f;
        public float VoiceRecognitionMaximumLength = 3.0f;

        public Action OnListeningStart;
        public Action OnListeningStop;
        public Action OnRecordingStart;
        public Action<float> OnDetectVoice;
        public Action<AudioClip> OnRecordingEnd;
        public Action<Exception> OnError = (e) => { Debug.LogError($"Recording wakeword error: {e.Message}\n{e.StackTrace}"); };
        public Func<bool> ShouldRaiseThreshold = () => { return false; };

        // Protected members for recording voice and recognize task
        protected CancellationTokenSource cancellationTokenSource;

        protected virtual void Start()
        {
            if (AutoStart)
            {
#pragma warning disable CS4014
                StartListeningAsync();
#pragma warning restore CS4014
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            cancellationTokenSource?.Cancel();
        }

        protected virtual void Update()
        {
            // Observe which threshold should be applied in every frames
            voiceDetectionThreshold = ShouldRaiseThreshold() ? VoiceDetectionRaisedThreshold : VoiceDetectionThreshold;
        }

        protected virtual async UniTask StartListeningAsync()
        {
            if (IsListening)
            {
                Debug.LogWarning("WakeWordListener is already listening");
                return;
            }

            StartListening();   // Start recorder here to asure that GetVoiceAsync will be called after recorder started

            try
            {
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
                    if (voiceRecorderResponse != null)
                    {
                        if (!string.IsNullOrEmpty(voiceRecorderResponse.Text) ||
                            (voiceRecorderResponse.Voice != null && voiceRecorderResponse.Voice.length <= VoiceRecognitionMaximumLength)
                        )
                        {
#pragma warning disable CS4014
                            ProcessVoiceAsync(voiceRecorderResponse); // Do not await to continue listening
#pragma warning restore CS4014
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in listening wakeword: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                StopListening();
            }
        }

#pragma warning disable CS1998
        protected virtual async UniTask ProcessVoiceAsync(VoiceRecorderResponse voiceRecorderResponse)
        {

        }
#pragma warning restore CS1998
    }
}

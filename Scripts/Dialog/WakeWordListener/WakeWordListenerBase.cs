using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.IO;
using ChatdollKit.Network;

namespace ChatdollKit.Dialog
{
    public class WakeWordListenerBase : VoiceRecorderBase, IWakeWordListener
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

        // Protected members for recording voice and recognize task
        protected CancellationTokenSource cancellationTokenSource;

        [Header("WakeWord Settings")]
        public List<WakeWord> WakeWords;
        public List<string> CancelWords;
        public List<string> IgnoreWords = new List<string>() { "。", "、", "？", "！" };

        public Func<string, WakeWord> ExtractWakeWord { get; set; }
        public Func<string, string> ExtractCancelWord { get; set; }
        public Func<WakeWord, UniTask> OnWakeAsync { get; set; }
        public Func<UniTask> OnCancelAsync { get; set; }
        public Func<bool> ShouldRaiseThreshold { get; set; } = () => { return false; };

        public new bool IsListening
        {
            get { return base.IsListening; }
        }

        protected ChatdollHttp client = new ChatdollHttp();

        protected virtual void Start()
        {
            if (AutoStart)
            {
#pragma warning disable CS4014
                StartListeningAsync();
#pragma warning restore CS4014
            }
        }

        protected virtual void Update()
        {
            // Observe which threshold should be applied in every frames
            voiceDetectionThreshold = ShouldRaiseThreshold() ? VoiceDetectionRaisedThreshold : VoiceDetectionThreshold;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            cancellationTokenSource?.Cancel();
        }

        public void SetWakeWord(WakeWord wakeWord)
        {
            foreach (var ww in WakeWords)
            {
                if (ww.Text == wakeWord.Text)
                {
                    return;
                }
            }

            WakeWords.Add(wakeWord);
        }

        public void SetCancelWord(string cancelWord)
        {
            foreach (var cw in CancelWords)
            {
                if (cw == cancelWord)
                {
                    return;
                }
            }

            CancelWords.Add(cancelWord);
        }

        public virtual new void StartListening()
        {
            _ = StartListeningAsync();
        }

        public virtual new void StopListening()
        {
            cancellationTokenSource.Cancel();
        }

        protected virtual async UniTask StartListeningAsync()
        {
            if (IsListening)
            {
                Debug.LogWarning("WakeWordListener is already listening");
                return;
            }

            if (OnWakeAsync == null)
            {
                Debug.LogError("Start WakeWordListener failed. OnWakeAsync must be set.");
#pragma warning disable CS1998
                OnWakeAsync = async (ww) => { Debug.LogWarning("Nothing is invoked by wakeword. Set Func to OnWakeAsync."); };
#pragma warning restore CS1998
            }

            base.StartListening();   // Start recorder here to asure that GetVoiceAsync will be called after recorder started

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
                base.StopListening();
            }
        }

        protected async UniTask ProcessVoiceAsync(VoiceRecorderResponse voiceRecorderResponse)
        {
            var recognizedText = string.Empty;
            if (!string.IsNullOrEmpty(voiceRecorderResponse.Text))
            {
                recognizedText = voiceRecorderResponse.Text;
                if (PrintResult)
                {
                    Debug.Log($"Text input(WakeWordListener): {recognizedText}");
                }
            }
            else
            {
                recognizedText = await RecognizeSpeechAsync(voiceRecorderResponse);
                if (OnRecognizedAsync != null)
                {
                    await OnRecognizedAsync(recognizedText);
                }
                if (PrintResult)
                {
                    Debug.Log($"Recognized(WakeWordListener): {recognizedText}");
                }
            }

            var extractedWakeWord = (ExtractWakeWord ?? ExtractWakeWordDefault).Invoke(recognizedText);
            if (extractedWakeWord != null)
            {
                try
                {
                    await OnWakeAsync(extractedWakeWord);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"OnWakeAsync failed: {ex.Message}\n{ex.StackTrace}");
                }
            }
            else if (!string.IsNullOrEmpty((ExtractCancelWord ?? ExtractCancelWordDefault).Invoke(recognizedText)))
            {
                try
                {
                    if (OnCancelAsync != null)
                    {
                        await OnCancelAsync();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"OnCancelAsync failed: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

#pragma warning disable CS1998
        protected virtual async UniTask<string> RecognizeSpeechAsync(VoiceRecorderResponse recordedVoice)
        {
            throw new NotImplementedException("RecognizeSpeechAsync method should be implemented at the sub class of WakeWordListenerBase");
        }
#pragma warning restore CS1998

        public WakeWord ExtractWakeWordDefault(string text)
        {
            var textLower = text.ToLower();
            foreach (var iw in IgnoreWords)
            {
                textLower = textLower.Replace(iw.ToLower(), string.Empty);
            }

            foreach (var ww in WakeWords)
            {
                var wwText = ww.Text.ToLower();
                if (textLower.Contains(wwText))
                {
                    var prefix = textLower.Substring(0, textLower.IndexOf(wwText));
                    var suffix = textLower.Substring(textLower.IndexOf(wwText) + wwText.Length);

                    if (prefix.Length <= ww.PrefixAllowance && suffix.Length <= ww.SuffixAllowance)
                    {
                        return ww.CloneWithRecognizedText(text);
                    }
                }
            }

            return null;
        }

        public string ExtractCancelWordDefault(string text)
        {
            var textLower = text.ToLower();
            foreach (var iw in IgnoreWords)
            {
                textLower = textLower.Replace(iw.ToLower(), string.Empty);
            }

            foreach (var cw in CancelWords)
            {
                if (textLower == cw.ToLower())
                {
                    return cw;
                }
            }

            return string.Empty;
        }
    }
}

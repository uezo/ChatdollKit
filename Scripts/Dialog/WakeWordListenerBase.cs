using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.IO;
using ChatdollKit.Network;

namespace ChatdollKit.Dialog
{
    public class WakeWordListenerBase : ShortPhraseListener
    {
        [Header("WakeWord Settings")]
        public List<WakeWord> WakeWords;
        public List<string> CancelWords;
        public List<string> IgnoreWords = new List<string>() { "。", "、", "？", "！" };
        public Func<string, WakeWord> ExtractWakeWord;
        public Func<string, string> ExtractCancelWord;
        public Func<WakeWord, UniTask> OnWakeAsync;
        public Func<UniTask> OnCancelAsync;

        protected ChatdollHttp client = new ChatdollHttp();

        protected override void Start()
        {
            if (AutoStart)
            {
                if (OnWakeAsync == null)
                {
                    Debug.LogError("OnWakeAsync must be set");
#pragma warning disable CS1998
                    OnWakeAsync = async (ww) => { Debug.LogWarning("Nothing is invoked by wakeword. Set Func to OnWakeAsync."); };
#pragma warning restore CS1998
                }

#pragma warning disable CS4014
                StartListeningAsync();
#pragma warning restore CS4014
            }
        }

        protected override async UniTask ProcessVoiceAsync(VoiceRecorderResponse voiceRecorderResponse)
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

#pragma warning disable CS1998
        protected virtual async UniTask<string> RecognizeSpeechAsync(VoiceRecorderResponse recordedVoice)
        {
            throw new NotImplementedException("RecognizeSpeechAsync method should be implemented at the sub class of WakeWordListenerBase");
        }
#pragma warning restore CS1998
    }

    [Serializable]
    public class WakeWord
    {
        public string Text;
        public int PrefixAllowance = 4;
        public int SuffixAllowance = 4;
        public string Intent;
        public Priority IntentPriority = Priority.Normal;
        public RequestType RequestType = RequestType.Voice;
        public int InlineRequestMinimumLength = 0;
        public string RecognizedText { get; private set; }
        public string InlineRequestText { get; private set; }

        public WakeWord CloneWithRecognizedText(string recognizedText)
        {
            var ww = new WakeWord
            {
                Text = Text,
                PrefixAllowance = PrefixAllowance,
                SuffixAllowance = SuffixAllowance,
                Intent = Intent,
                IntentPriority = IntentPriority,
                RequestType = RequestType,
                InlineRequestMinimumLength = InlineRequestMinimumLength,
                RecognizedText = recognizedText,
                InlineRequestText = string.Empty
            };

            if (InlineRequestMinimumLength > 0 && RecognizedText != null)
            {
                var requestText = RecognizedText.Substring(RecognizedText.IndexOf(Text) + Text.Length);
                if (requestText.Length >= InlineRequestMinimumLength)
                {
                    InlineRequestText = requestText;
                }
            }

            return ww;
        }
    }
}

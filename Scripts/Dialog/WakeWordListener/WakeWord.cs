using System;

namespace ChatdollKit.Dialog
{
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
        public bool SkipPrompt = false;

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
                InlineRequestText = string.Empty,
                SkipPrompt = SkipPrompt
            };

            if (SkipPrompt)
            {
                ww.InlineRequestText = ww.RecognizedText;
            }

            return ww;
        }
    }
}

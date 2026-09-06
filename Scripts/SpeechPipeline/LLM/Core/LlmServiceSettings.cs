using System;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.LLM
{
    /// <summary>Inspector-owned options. Tool definitions, hooks, guardrails, initial messages,
    /// and extra request parameters configured in code are preserved when applying these settings.</summary>
    [Serializable]
    public sealed class LlmServiceSettings
    {
        public string Model = "gpt-5.6-terra";
        [TextArea(3, 10)] public string SystemPrompt;
        public bool UseTemperature;
        public float Temperature = 1;
        public string ReasoningEffort;
        public bool UseMaxOutputTokens;
        [Min(1)] public int MaxOutputTokens = 4096;
        [Min(0.01f)] public float TimeoutSeconds = 60;
        public string[] SplitChars = { "。", "？", "！", ". ", "?", "!", "\n" };
        public string[] OptionSplitChars = { "、", ", " };
        [Min(0)] public int OptionSplitThreshold = 50;
        public bool SplitOnControlTags = true;
        public string[] VoiceTextTags = Array.Empty<string>();
        public string TerminalVoiceTextTag;
        [Min(1)] public int MaxToolRounds = 8;

        public void ApplyTo(LlmServiceOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Model = Model;
            options.SystemPrompt = SystemPrompt;
            options.Temperature = UseTemperature ? (double?)Temperature : null;
            options.ReasoningEffort = string.IsNullOrEmpty(ReasoningEffort) ? null : ReasoningEffort;
            options.MaxOutputTokens = UseMaxOutputTokens ? (int?)MaxOutputTokens : null;
            options.TimeoutSeconds = TimeoutSeconds;
            options.SplitChars = SplitChars == null ? null : (string[])SplitChars.Clone();
            options.OptionSplitChars = OptionSplitChars == null ? null : (string[])OptionSplitChars.Clone();
            options.OptionSplitThreshold = OptionSplitThreshold;
            options.SplitOnControlTags = SplitOnControlTags;
            options.VoiceTextTags = VoiceTextTags == null ? Array.Empty<string>() : (string[])VoiceTextTags.Clone();
            options.TerminalVoiceTextTag = string.IsNullOrEmpty(TerminalVoiceTextTag) ? null : TerminalVoiceTextTag;
            options.MaxToolRounds = MaxToolRounds;
        }
    }
}

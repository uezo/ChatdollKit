using System;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.LLM
{
    public class LlmServiceOptions
    {
        public string ApiKey { get; set; }
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public string Model { get; set; } = "gpt-5.6-terra";
        public string SystemPrompt { get; set; }
        public JArray InitialMessages { get; set; } = new JArray();
        public double? Temperature { get; set; }
        public string ReasoningEffort { get; set; }
        public JObject ExtraBody { get; set; }
        public int? MaxOutputTokens { get; set; }
        public double TimeoutSeconds { get; set; } = 60;
        public bool EnablePreviousResponseFallback { get; set; } = true;
        public string[] SplitChars { get; set; } = new[] { "。", "？", "！", ". ", "?", "!", "\n" };
        public string[] OptionSplitChars { get; set; } = new[] { "、", ", " };
        public int OptionSplitThreshold { get; set; } = 50;
        public bool SplitOnControlTags { get; set; } = true;
        public string[] VoiceTextTags { get; set; } = Array.Empty<string>();
        public string TerminalVoiceTextTag { get; set; }
        /// <summary>Provider-native tool definitions. Individual tool implementations are not included.</summary>
        public JArray ToolDefinitions { get; set; }
        public LlmTool[] Tools { get; set; } = Array.Empty<LlmTool>();
        /// <summary>Maximum rounds of tool execution; a final generation may follow the last round.</summary>
        public int MaxToolRounds { get; set; } = 8;
        public ILlmGuardrail[] Guardrails { get; set; } = Array.Empty<ILlmGuardrail>();
        public Action<JObject, LlmRequest> EditRequestParameters { get; set; }

        public virtual LlmServiceOptions Copy()
        {
            var copy = (LlmServiceOptions)MemberwiseClone();
            copy.InitialMessages = InitialMessages == null ? new JArray() : (JArray)InitialMessages.DeepClone();
            copy.ExtraBody = (JObject)ExtraBody?.DeepClone();
            copy.ToolDefinitions = (JArray)ToolDefinitions?.DeepClone();
            copy.Tools = Tools == null ? Array.Empty<LlmTool>() : Array.ConvertAll(Tools, tool => tool?.Copy());
            copy.Guardrails = Guardrails == null ? Array.Empty<ILlmGuardrail>() : (ILlmGuardrail[])Guardrails.Clone();
            copy.SplitChars = SplitChars == null ? null : (string[])SplitChars.Clone();
            copy.OptionSplitChars = OptionSplitChars == null ? null : (string[])OptionSplitChars.Clone();
            copy.VoiceTextTags = VoiceTextTags == null ? Array.Empty<string>() : (string[])VoiceTextTags.Clone();
            return copy;
        }

        public virtual void Validate()
        {
            if (string.IsNullOrWhiteSpace(ApiKey) || ApiKey.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("An API key without line breaks is required.", nameof(ApiKey));
            if (string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("A model is required.", nameof(Model));
            if (double.IsNaN(TimeoutSeconds) || double.IsInfinity(TimeoutSeconds) || TimeoutSeconds <= 0 || TimeoutSeconds > int.MaxValue / 1000.0)
                throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds));
            if (Temperature.HasValue && (double.IsNaN(Temperature.Value) || double.IsInfinity(Temperature.Value)))
                throw new ArgumentOutOfRangeException(nameof(Temperature));
            if (MaxOutputTokens.HasValue && MaxOutputTokens.Value < 1) throw new ArgumentOutOfRangeException(nameof(MaxOutputTokens));
            if (OptionSplitThreshold < 0) throw new ArgumentOutOfRangeException(nameof(OptionSplitThreshold));
            if (MaxToolRounds < 1) throw new ArgumentOutOfRangeException(nameof(MaxToolRounds));
            var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var tool in Tools ?? Array.Empty<LlmTool>())
                if (tool == null || string.IsNullOrWhiteSpace(tool.Name) || !names.Add(tool.Name) || tool.Parameters == null)
                    throw new ArgumentException("Tools require unique names and parameter schemas.", nameof(Tools));
            foreach (var guardrail in Guardrails ?? Array.Empty<ILlmGuardrail>())
                if (guardrail == null) throw new ArgumentException("Guardrails cannot contain null.", nameof(Guardrails));
            ValidateStrings(SplitChars, nameof(SplitChars));
            ValidateStrings(OptionSplitChars, nameof(OptionSplitChars));
            ValidateStrings(VoiceTextTags, nameof(VoiceTextTags));
            if (TerminalVoiceTextTag != null && Array.IndexOf(VoiceTextTags ?? Array.Empty<string>(), TerminalVoiceTextTag) < 0)
                throw new ArgumentException("TerminalVoiceTextTag must be included in VoiceTextTags.", nameof(TerminalVoiceTextTag));
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Use an absolute HTTPS base URL without credentials, query or fragment (HTTP is allowed for loopback tests).");
        }

        private static void ValidateStrings(string[] values, string name)
        {
            if (values == null) return;
            foreach (var value in values) if (string.IsNullOrEmpty(value)) throw new ArgumentException("Entries must not be empty.", name);
        }
    }
}

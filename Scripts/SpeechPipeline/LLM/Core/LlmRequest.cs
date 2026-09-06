using System;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.LLM
{
    /// <summary>Conversation state is supplied by the caller and is never stored by the service.</summary>
    public sealed class LlmRequest
    {
        public string ContextId { get; set; } = "default";
        public string UserId { get; set; }
        public string SessionId { get; set; }
        public string Channel { get; set; }
        public string Text { get; set; }
        public string[] ImageUrls { get; set; } = Array.Empty<string>();
        /// <summary>Optional provider-native current input. When set, replaces Text/ImageUrls composition.</summary>
        public JArray Input { get; set; }
        /// <summary>Prior messages in the selected API's format, excluding InitialMessages and current input.
        /// Responses uses this for recovery; Chat Completions sends it on every turn.</summary>
        public JArray History { get; set; } = new JArray();
        public string PreviousResponseId { get; set; }
        /// <summary>First-generation parameters, merged after service settings. Not reapplied to tool continuations.</summary>
        public JObject Parameters { get; set; }
        public JObject SystemPromptParameters { get; set; }

        public LlmRequest Copy()
        {
            var copy = (LlmRequest)MemberwiseClone();
            copy.ImageUrls = ImageUrls == null ? Array.Empty<string>() : (string[])ImageUrls.Clone();
            copy.Input = (JArray)Input?.DeepClone();
            copy.History = History == null ? new JArray() : (JArray)History.DeepClone();
            copy.Parameters = (JObject)Parameters?.DeepClone();
            copy.SystemPromptParameters = (JObject)SystemPromptParameters?.DeepClone();
            return copy;
        }
    }
}

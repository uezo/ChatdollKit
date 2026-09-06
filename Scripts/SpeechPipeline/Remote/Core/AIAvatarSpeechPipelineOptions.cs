using System;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.Remote
{
    /// <summary>Connection settings for one AIAvatarKit WebSocket conversation.</summary>
    public sealed class AIAvatarSpeechPipelineOptions
    {
        public string Url { get; set; } = "ws://localhost:48001/ws";
        public string ApiKey { get; set; }
        public string SessionId { get; set; }
        public string UserId { get; set; }
        public string ContextId { get; set; }
        public JObject Metadata { get; set; }
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(120);
        public int MaxPendingRequests { get; set; } = 16;
        public int MaxResponseAudioBytes { get; set; } = 16 * 1024 * 1024;

        public AIAvatarSpeechPipelineOptions Copy()
        {
            var copy = (AIAvatarSpeechPipelineOptions)MemberwiseClone();
            copy.Metadata = (JObject)Metadata?.DeepClone();
            return copy;
        }

        internal void Validate()
        {
            if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
                throw new ArgumentException("Url must be an absolute ws:// or wss:// URL.", nameof(Url));
            if (SessionId != null && string.IsNullOrWhiteSpace(SessionId))
                throw new ArgumentException("SessionId must be nonempty when supplied.", nameof(SessionId));
            if (ContextId != null && string.IsNullOrWhiteSpace(ContextId))
                throw new ArgumentException("ContextId must be nonempty when supplied.", nameof(ContextId));
            if (ConnectTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ConnectTimeout));
            if (RequestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
            if (MaxPendingRequests <= 0) throw new ArgumentOutOfRangeException(nameof(MaxPendingRequests));
            if (MaxResponseAudioBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxResponseAudioBytes));
        }
    }
}

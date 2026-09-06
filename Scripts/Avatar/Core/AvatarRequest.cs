using System;
using System.Collections.Generic;

namespace ChatdollKit.Avatar
{
    /// <summary>Audio, text and resolved avatar controls. Independent of Unity, LLMs and pipeline response formats.</summary>
    public sealed class AvatarRequest
    {
        public string SessionId { get; set; }
        public string ContextId { get; set; }
        public string TransactionId { get; set; }
        public string Text { get; set; }
        public string VoiceText { get; set; }
        public string Language { get; set; }
        /// <summary>Complete synthesized integer PCM WAV file bytes.</summary>
        public byte[] AudioData { get; set; }
        public IReadOnlyList<AvatarControl> Controls { get; set; } = Array.Empty<AvatarControl>();

        public AvatarRequest Copy()
        {
            var copy = (AvatarRequest)MemberwiseClone();
            copy.AudioData = (byte[])AudioData?.Clone();
            copy.Controls = new List<AvatarControl>(Controls ?? Array.Empty<AvatarControl>()).AsReadOnly();
            return copy;
        }
    }
}

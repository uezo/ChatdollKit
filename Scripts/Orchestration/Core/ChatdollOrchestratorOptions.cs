using System;

namespace ChatdollKit.Orchestration
{
    [Serializable]
    public sealed class ChatdollOrchestratorOptions
    {
        /// <summary>Allow microphone input during response generation and playback.</summary>
        public bool AllowBargeIn
        {
            get => !MuteMicrophoneDuringResponse;
            set => MuteMicrophoneDuringResponse = !value;
        }

        public int MaxPendingAudioFrames = 100;
        public int MaxPendingPresentations = 100;
        // Serialized storage keeps existing scene/prefab values and their meaning.
        // Application code and the Inspector use AllowBargeIn.
        public bool MuteMicrophoneDuringResponse;

        public ChatdollOrchestratorOptions Copy() => (ChatdollOrchestratorOptions)MemberwiseClone();
        public void Validate()
        {
            if (MaxPendingAudioFrames < 1) throw new ArgumentOutOfRangeException(nameof(MaxPendingAudioFrames));
            if (MaxPendingPresentations < 1) throw new ArgumentOutOfRangeException(nameof(MaxPendingPresentations));
        }
    }
}

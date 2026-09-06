using System.Collections.Generic;

namespace ChatdollKit.SpeechPipeline.VAD.TurnEndGates
{
    /// <summary>A detached snapshot of a session's current gate hold.</summary>
    public sealed class TurnEndGateState
    {
        public bool IsActive { get; }
        public double? Timeout { get; }
        public IReadOnlyList<string> Reasons { get; }

        internal TurnEndGateState(bool isActive, double? timeout, IList<string> reasons)
        {
            IsActive = isActive;
            Timeout = timeout;
            Reasons = new List<string>(reasons).AsReadOnly();
        }
    }
}

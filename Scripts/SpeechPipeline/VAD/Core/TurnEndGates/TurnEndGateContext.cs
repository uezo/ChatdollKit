using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ChatdollKit.SpeechPipeline.VAD.TurnEndGates
{
    public sealed class TurnEndGateContext
    {
        private readonly Dictionary<string, TurnEndDecision> decisions;
        public IReadOnlyDictionary<string, TurnEndDecision> Decisions { get; }

        public TurnEndGateContext() : this(new Dictionary<string, TurnEndDecision>()) { }

        private TurnEndGateContext(Dictionary<string, TurnEndDecision> decisions)
        {
            this.decisions = decisions;
            Decisions = new ReadOnlyDictionary<string, TurnEndDecision>(decisions);
        }

        public void AddDecision(string gateName, TurnEndDecision decision) => decisions[gateName] = decision;
        public TurnEndDecision GetDecision(string gateName) => decisions.TryGetValue(gateName, out var decision) ? decision : null;
        public bool IsWaiting(string gateName) => GetDecision(gateName)?.ShouldEnd == false;
        public TurnEndGateContext Snapshot() => new TurnEndGateContext(new Dictionary<string, TurnEndDecision>(decisions));
    }
}

using System;
using ChatdollKit.SpeechPipeline;

namespace ChatdollKit.Orchestration
{
    /// <summary>An owned response snapshot with the run and generation that accepted it.
    /// Reading Response returns a copy so observers cannot modify this event or each other's data.</summary>
    public sealed class OrchestratorResponseEvent
    {
        private readonly SpeechPipelineResponse response;
        public ChatdollOrchestratorEngine Source { get; }
        public long Generation { get; }
        /// <summary>Authoritative start order within Source; zero before the turn starts.</summary>
        public long TurnOrder { get; }
        public SpeechPipelineResponse Response => response.Copy();

        public OrchestratorResponseEvent(ChatdollOrchestratorEngine source, long generation, SpeechPipelineResponse response,
            long turnOrder = 0)
        {
            Source = source;
            Generation = generation;
            TurnOrder = turnOrder;
            this.response = response?.Copy() ?? throw new ArgumentNullException(nameof(response));
        }

        public OrchestratorResponseEvent Copy() => new OrchestratorResponseEvent(Source, Generation, response, TurnOrder);
    }
}

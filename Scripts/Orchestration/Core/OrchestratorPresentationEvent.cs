using ChatdollKit.Avatar;

namespace ChatdollKit.Orchestration
{
    /// <summary>A dispatched presentation with its original turn identity, safe across delayed notifications.</summary>
    public sealed class OrchestratorPresentationEvent
    {
        public ChatdollOrchestratorEngine Source { get; }
        public long Generation { get; }
        public long TurnOrder { get; }
        private readonly AvatarRequest request;
        public AvatarRequest Request => request.Copy();
        public OrchestratorPresentationEvent(ChatdollOrchestratorEngine source, long generation, long turnOrder, AvatarRequest request)
        {
            Source = source; Generation = generation; TurnOrder = turnOrder; this.request = request.Copy();
        }
        public OrchestratorPresentationEvent Copy() => new OrchestratorPresentationEvent(Source, Generation, TurnOrder, request);
    }
}

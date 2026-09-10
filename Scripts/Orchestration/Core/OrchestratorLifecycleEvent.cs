namespace ChatdollKit.Orchestration
{
    public enum OrchestratorLifecycleKind
    {
        Started,
        Stopped,
        Interrupted,
        Reset
    }

    /// <summary>A run or accepted control boundary. Interrupted/Reset invalidate earlier generations,
    /// even when pipeline control subsequently fails. Stopped invalidates the entire source run.</summary>
    public sealed class OrchestratorLifecycleEvent
    {
        public ChatdollOrchestratorEngine Source { get; }
        public OrchestratorLifecycleKind Kind { get; }
        public string SessionId { get; }
        public string ContextId { get; }
        public long Generation { get; }

        public OrchestratorLifecycleEvent(ChatdollOrchestratorEngine source, OrchestratorLifecycleKind kind,
            string sessionId, long generation, string contextId = null)
        {
            Source = source;
            Kind = kind;
            SessionId = sessionId;
            Generation = generation;
            ContextId = contextId;
        }
    }
}

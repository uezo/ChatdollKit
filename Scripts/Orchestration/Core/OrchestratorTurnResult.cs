namespace ChatdollKit.Orchestration
{
    public enum OrchestratorTurnEndReason
    {
        Completed,
        Canceled,
        Failed
    }

    /// <summary>A terminal turn whose queued and active avatar presentations have finished.</summary>
    public sealed class OrchestratorTurnResult
    {
        public ChatdollOrchestratorEngine Source { get; }
        public string SessionId { get; }
        public string ContextId { get; }
        public string TransactionId { get; }
        public long Generation { get; }
        public OrchestratorTurnEndReason Reason { get; }

        public OrchestratorTurnResult(ChatdollOrchestratorEngine source, string sessionId,
            string contextId, string transactionId, long generation, OrchestratorTurnEndReason reason)
        {
            Source = source;
            SessionId = sessionId;
            ContextId = contextId;
            TransactionId = transactionId;
            Generation = generation;
            Reason = reason;
        }
    }
}

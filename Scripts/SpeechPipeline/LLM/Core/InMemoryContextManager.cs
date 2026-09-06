using System;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.LLM
{
    public enum LlmHistoryFormat { ChatCompletions, Responses }

    /// <summary>An owned copy of one conversation. Editing History never changes the manager.</summary>
    public sealed class LlmContextSnapshot
    {
        public string ContextId { get; }
        public string ResponseId { get; }
        public JArray History { get; }
        internal long Revision { get; }
        internal LlmContextSnapshot(string contextId, string responseId, JArray history, long revision)
        { ContextId = contextId; ResponseId = responseId; History = history; Revision = revision; }
    }

    /// <summary>In-memory history and continuation ID for a single conversation, owned by LlmConversation.</summary>
    public sealed class InMemoryContextManager
    {
        private readonly object sync = new object();
        private readonly JArray history = new JArray();
        private string contextId;
        private string responseId;
        private long revision;

        public LlmContextSnapshot GetSnapshot()
        {
            lock (sync) return new LlmContextSnapshot(contextId, responseId, (JArray)history.DeepClone(), revision);
        }

        /// <summary>Clears history and ID. A generation already in progress cannot repopulate this reset history.</summary>
        public void Reset()
        {
            lock (sync)
            {
                history.Clear();
                contextId = responseId = null;
                revision++;
            }
        }

        internal void ClearResponseId()
        {
            lock (sync) { responseId = null; revision++; }
        }

        internal bool TryCommit(LlmContextSnapshot previous, string nextContextId, JArray input, JArray output,
            string nextResponseId, CancellationToken token)
        {
            // Copy before touching the stored conversation, so a failed copy never commits half a turn.
            var turn = new JArray();
            foreach (var item in input) turn.Add(item.DeepClone());
            foreach (var item in output) turn.Add(item.DeepClone());
            lock (sync)
            {
                token.ThrowIfCancellationRequested();
                if (revision != previous.Revision) return false;
                if (contextId != null && contextId != nextContextId)
                    throw new InvalidOperationException("Reset the conversation before changing ContextId.");
                foreach (var item in turn) history.Add(item);
                contextId = nextContextId;
                responseId = nextResponseId;
                revision++;
                return true;
            }
        }
    }
}

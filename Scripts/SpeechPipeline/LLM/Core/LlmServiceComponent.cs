using ChatdollKit.SpeechPipeline;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.LLM
{
    public abstract class LlmServiceComponent : LiveSpeechComponent
    {
        public ILlmService Service { get; internal set; }
        public abstract LlmServiceOptions BuildOptions(LlmServiceOptions current = null);
        public abstract ILlmService CreateService(LlmServiceOptions options);
        /// <summary>Override for a custom service whose history format cannot be inferred.</summary>
        public virtual LlmHistoryFormat? HistoryFormat => null;
    }
}

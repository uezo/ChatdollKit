using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.VAD.TurnEndGates
{
    public interface ITurnEndGate
    {
        string Name { get; }
        bool RunInBackground { get; }
        double? Timeout { get; }
        bool ShouldRunInBackground(TurnEndGateContext context);
        UniTask<TurnEndDecision> ShouldEndTurnAsync(TurnEndRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Defaults for a gate that evaluates inline without a force timeout.</summary>
    public abstract class TurnEndGateBase : ITurnEndGate
    {
        public virtual string Name => GetType().Name;
        public virtual bool RunInBackground => false;
        public virtual double? Timeout => null;
        public virtual bool ShouldRunInBackground(TurnEndGateContext context) => true;
        public abstract UniTask<TurnEndDecision> ShouldEndTurnAsync(TurnEndRequest request, CancellationToken cancellationToken);
    }
}

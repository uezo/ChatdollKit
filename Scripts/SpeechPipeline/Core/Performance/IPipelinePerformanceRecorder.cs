using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.Performance
{
    public interface IPipelinePerformanceRecorder
    {
        UniTask RecordAsync(PipelinePerformanceRecord record, CancellationToken cancellationToken = default);
    }
}

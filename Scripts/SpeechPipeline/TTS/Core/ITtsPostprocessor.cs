using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.TTS
{
    public interface ITtsPostprocessor
    {
        UniTask<byte[]> ProcessAsync(byte[] audio, SpeechSynthesizerOptions options,
            CancellationToken cancellationToken = default);
        /// <summary>Include all settings that affect output. Return null to disable caching for this processor.</summary>
        JToken GetCacheConfiguration(SpeechSynthesizerOptions options);
    }
}

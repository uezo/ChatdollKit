using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.TTS
{
    public static class SpeechSynthesizer
    {
        /// <summary>Supply only audio generation; the returned synthesizer provides processors, cache, resampling, and lifecycle.
        /// Use a stable cacheIdentity when sharing a persistent cache between instances of the same implementation.</summary>
        public static ISpeechSynthesizer Create(Func<SpeechSynthesisRequest, CancellationToken, UniTask<byte[]>> generate,
            SpeechSynthesizerOptions options = null, string cacheIdentity = null)
            => new DelegateSpeechSynthesizer(generate, options, cacheIdentity);
    }

    public sealed class DelegateSpeechSynthesizer : SpeechSynthesizerBase
    {
        private readonly Func<SpeechSynthesisRequest, CancellationToken, UniTask<byte[]>> generate;
        private readonly string cacheIdentity;
        public DelegateSpeechSynthesizer(Func<SpeechSynthesisRequest, CancellationToken, UniTask<byte[]>> generate,
            SpeechSynthesizerOptions options = null, string cacheIdentity = null) : base(options ?? new SpeechSynthesizerOptions())
        {
            this.generate = generate ?? throw new ArgumentNullException(nameof(generate));
            this.cacheIdentity = cacheIdentity ?? Guid.NewGuid().ToString("N");
        }
        protected override UniTask<byte[]> GenerateCoreAsync(SpeechSynthesisRequest request, SpeechSynthesizerOptions options, CancellationToken token)
            => generate(request, token);
        protected override UniTask<string> MakeSynthesisCacheKeyAsync(SpeechSynthesisRequest request, SpeechSynthesizerOptions options, CancellationToken token)
            => UniTask.FromResult(CreateCacheKey(request, options, new JValue(cacheIdentity)));
    }
}

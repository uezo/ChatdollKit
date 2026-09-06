// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace ChatdollKit.SpeechPipeline.TTS
{
    public sealed class SpeechSynthesizerRouterOptions : SpeechSynthesizerOptions
    {
        public string DefaultRoute { get; set; }
        [JsonIgnore] public Func<SpeechSynthesisRequest, string> Route { get; set; }
        public override void Validate()
        {
            base.Validate();
            if (SampleRate.HasValue || !string.IsNullOrEmpty(CacheDirectory) || Preprocessors.Length != 0 || Postprocessors.Length != 0)
                throw new ArgumentException("Configure processors, cache, and sample rate on the routed synthesizers.");
        }
    }

    /// <summary>Selects a child from the original request and runs that child's complete synthesis flow once.</summary>
    public sealed class SpeechSynthesizerRouter : SpeechSynthesizerBase
    {
        private readonly Dictionary<string, ISpeechSynthesizer> synthesizers;
        private readonly bool ownsSynthesizers;
        public SpeechSynthesizerRouter(IReadOnlyDictionary<string, ISpeechSynthesizer> synthesizers,
            Func<SpeechSynthesisRequest, string> route = null, string defaultRoute = null, bool ownsSynthesizers = false)
            : base(new SpeechSynthesizerRouterOptions { Route = route, DefaultRoute = defaultRoute })
        {
            if (synthesizers == null || synthesizers.Count == 0) throw new ArgumentException("Supply at least one synthesizer.", nameof(synthesizers));
            this.synthesizers = new Dictionary<string, ISpeechSynthesizer>();
            foreach (var pair in synthesizers)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null) throw new ArgumentException("Route keys and synthesizers must be nonempty.", nameof(synthesizers));
                this.synthesizers.Add(pair.Key, pair.Value);
            }
            if (defaultRoute != null && !this.synthesizers.ContainsKey(defaultRoute)) throw new ArgumentException("Unknown default route.", nameof(defaultRoute));
            this.ownsSynthesizers = ownsSynthesizers;
        }

        public Func<SpeechSynthesisRequest, string> Route
        {
            get => ((SpeechSynthesizerRouterOptions)GetOptions()).Route;
            set { var settings = (SpeechSynthesizerRouterOptions)GetOptions(); settings.Route = value; UpdateOptions(settings); }
        }

        protected override UniTask<byte[]> GenerateCoreAsync(SpeechSynthesisRequest request, SpeechSynthesizerOptions options, CancellationToken token)
        {
            var settings = (SpeechSynthesizerRouterOptions)options;
            if (settings.Route == null) throw new InvalidOperationException("A TTS route function is required.");
            var key = settings.Route(request.Copy()) ?? settings.DefaultRoute;
            token.ThrowIfCancellationRequested();
            if (key == null || !synthesizers.TryGetValue(key, out var synthesizer)) throw new ArgumentException("The selected TTS route is not registered.");
            return synthesizer.SynthesizeAsync(request, token);
        }

        protected override async UniTask DisposeResourcesAsync()
        {
            if (!ownsSynthesizers) return;
            var closed = new List<ISpeechSynthesizer>();
            var failures = new List<Exception>();
            foreach (var synthesizer in synthesizers.Values)
            {
                if (closed.Exists(item => ReferenceEquals(item, synthesizer))) continue;
                closed.Add(synthesizer);
                try { await synthesizer.DisposeAsync(); }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count != 0) throw new AggregateException(failures);
        }
    }
}

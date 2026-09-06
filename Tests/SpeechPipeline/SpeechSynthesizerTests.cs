using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.TTS;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SynthesizerFactory = ChatdollKit.SpeechPipeline.TTS.SpeechSynthesizer;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class SpeechSynthesizerTests
    {
        private readonly List<ISpeechSynthesizer> owned = new List<ISpeechSynthesizer>();
        private readonly List<string> directories = new List<string>();
        private static SpeechCompletionSource<bool> Signal() => new SpeechCompletionSource<bool>();
        private static SpeechSynthesisRequest Request(string text = "hello") => new SpeechSynthesisRequest { Text = text };
        private T Track<T>(T synthesizer) where T : ISpeechSynthesizer { owned.Add(synthesizer); return synthesizer; }
        private FakeSynthesizer Create(SpeechSynthesizerOptions options = null) => Track(new FakeSynthesizer(options ?? new TestOptions()));
        private string CacheDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "chatdoll-tts-common-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            directories.Add(directory);
            return directory;
        }

        [TearDown]
        public async NUnitTask Cleanup()
        {
            foreach (var synthesizer in owned) await synthesizer.DisposeAsync();
            owned.Clear();
            foreach (var directory in directories) if (Directory.Exists(directory)) Directory.Delete(directory, true);
            directories.Clear();
        }

        private static async UniTask WaitCancellation(CancellationToken token)
        {
            var completion = Signal();
            using (token.Register(() => completion.TrySetCanceled())) await completion.Task;
        }

        [Test]
        public async NUnitTask CompletedSynthesisReleasesItsTrackingEntriesAndAudio()
        {
            var synthesizer = Create();
            for (var i = 0; i < 3; i++) await synthesizer.SynthesizeAsync(Request());
            SpeechAsyncAssert.NoPendingOperations(synthesizer, "pending");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t\n")]
        public async NUnitTask SynthesizeSkipsEmptyTextWhileGenerateCallsProvider(string text)
        {
            var synth = Create();
            Assert.That(await synth.SynthesizeAsync(Request(text)), Is.Empty);
            Assert.That(synth.Calls, Is.Empty);
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, await synth.GenerateAsync(Request(text)));
            Assert.That(synth.Calls.Count, Is.EqualTo(1));
            Assert.That(synth.Calls[0].Request.Text, Is.EqualTo(text));
        }

        [Test]
        public async NUnitTask ProcessorsRunInOrderWithOwnedRequestOptionsAndAudio()
        {
            var trace = new List<string>();
            var generated = new byte[] { 1 };
            var options = new TestOptions { Voice = "original" };
            options.Preprocessors = new ITtsPreprocessor[]
            {
                new Pre(async (request, settings, token) =>
                {
                    trace.Add("pre1:" + request.Text);
                    request.StyleInfo["value"] = "mutated by pre";
                    ((TestOptions)settings).Voice = "mutated by pre";
                    await UniTask.CompletedTask;
                    return request.Text + "-one";
                }),
                new Pre((request, settings, token) =>
                {
                    trace.Add("pre2:" + request.Text);
                    Assert.That((string)request.StyleInfo["value"], Is.EqualTo("original"));
                    Assert.That(((TestOptions)settings).Voice, Is.EqualTo("original"));
                    return UniTask.FromResult(request.Text + "-two");
                })
            };
            var retainedPostOutput = new byte[] { 3 };
            options.Postprocessors = new ITtsPostprocessor[]
            {
                new Post((audio, settings, token) => { trace.Add("post1:" + audio[0]); audio[0] = 2; return UniTask.FromResult(audio); }),
                new Post((audio, settings, token) => { trace.Add("post2:" + audio[0]); return UniTask.FromResult(retainedPostOutput); })
            };
            var synth = Create(options);
            synth.Generate = (request, settings, token) => { trace.Add("generate:" + request.Text); return UniTask.FromResult(generated); };
            var caller = Request();
            caller.StyleInfo = new JObject { ["value"] = "original" };
            var result = await synth.SynthesizeAsync(caller);
            CollectionAssert.AreEqual(new[] { "pre1:hello", "pre2:hello-one", "generate:hello-one-two", "post1:1", "post2:2" }, trace);
            Assert.That(caller.Text, Is.EqualTo("hello"));
            Assert.That((string)caller.StyleInfo["value"], Is.EqualTo("original"));
            Assert.That(generated[0], Is.EqualTo(1));
            result[0] = 9;
            Assert.That(retainedPostOutput[0], Is.EqualTo(3));
        }

        [Test]
        public async NUnitTask PreprocessorCanSkipGenerationWithEmptyText()
        {
            var synth = Create(new TestOptions { Preprocessors = new ITtsPreprocessor[] { new Pre((request, settings, token) => UniTask.FromResult(" ")) } });
            Assert.That(await synth.SynthesizeAsync(Request()), Is.Empty);
            Assert.That(synth.Calls, Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async NUnitTask MissingProviderAudioSkipsPostprocessingAndCache(bool nullAudio)
        {
            var directory = CacheDirectory();
            int posts = 0;
            var synth = Create(new TestOptions
            {
                CacheDirectory = directory,
                Postprocessors = new ITtsPostprocessor[] { new Post((audio, settings, token) => { posts++; return UniTask.FromResult(audio); }) }
            });
            synth.Generate = (request, settings, token) => UniTask.FromResult(nullAudio ? null : Array.Empty<byte>());
            Assert.That(await synth.SynthesizeAsync(Request()), Is.Empty);
            Assert.That(posts, Is.Zero);
            Assert.That(Directory.GetFiles(directory), Is.Empty);
        }

        [Test]
        public async NUnitTask CacheUsesProcessedTextAndStoresFinalAudioWithoutRepeatingPostprocessing()
        {
            var directory = CacheDirectory();
            int pres = 0;
            int posts = 0;
            var synth = Create(new TestOptions
            {
                CacheDirectory = directory,
                Preprocessors = new ITtsPreprocessor[] { new Pre((request, settings, token) => { pres++; return UniTask.FromResult(request.Text.ToUpperInvariant()); }) },
                Postprocessors = new ITtsPostprocessor[] { new Post((audio, settings, token) => { posts++; return UniTask.FromResult(new byte[] { 8, 9 }); }) }
            });
            var first = await synth.SynthesizeAsync(Request("hello"));
            first[0] = 0;
            CollectionAssert.AreEqual(new byte[] { 8, 9 }, await synth.SynthesizeAsync(Request("HELLO")));
            Assert.That(synth.Calls.Count, Is.EqualTo(1));
            Assert.That(synth.Calls[0].Request.Text, Is.EqualTo("HELLO"));
            Assert.That(pres, Is.EqualTo(2));
            Assert.That(posts, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(directory, "*.wav").Length, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }

        [Test]
        public async NUnitTask CacheDistinguishesLanguageStyleAndProviderOptionsButIgnoresTimeout()
        {
            var options = new TestOptions { CacheDirectory = CacheDirectory(), Voice = "one" };
            var synth = Create(options);
            var request = Request();
            request.Language = "ja";
            request.StyleInfo = new JObject { ["styled_text"] = "calm" };
            await synth.SynthesizeAsync(request);
            await synth.SynthesizeAsync(request);
            request.Language = "en";
            await synth.SynthesizeAsync(request);
            request.StyleInfo["styled_text"] = "happy";
            await synth.SynthesizeAsync(request);
            var replacement = (TestOptions)synth.GetOptions();
            replacement.Voice = "two";
            synth.UpdateOptions(replacement);
            await synth.SynthesizeAsync(request);
            replacement.TimeoutSeconds = 20;
            synth.UpdateOptions(replacement);
            await synth.SynthesizeAsync(request);
            Assert.That(synth.Calls.Count, Is.EqualTo(4));
        }

        [Test]
        public async NUnitTask PostprocessorConfigurationChangesInvalidateCache()
        {
            int value = 1;
            var post = new Post((audio, settings, token) => UniTask.FromResult(new[] { (byte)value }), settings => new JObject { ["gain"] = value });
            var synth = Create(new TestOptions { CacheDirectory = CacheDirectory(), Postprocessors = new ITtsPostprocessor[] { post } });
            Assert.That((await synth.SynthesizeAsync(Request()))[0], Is.EqualTo(1));
            value = 2;
            Assert.That((await synth.SynthesizeAsync(Request()))[0], Is.EqualTo(2));
            Assert.That((await synth.SynthesizeAsync(Request()))[0], Is.EqualTo(2));
            Assert.That(synth.Calls.Count, Is.EqualTo(2));
        }

        [Test]
        public async NUnitTask NullPostprocessorCacheConfigurationDisablesCaching()
        {
            var directory = CacheDirectory();
            var synth = Create(new TestOptions
            {
                CacheDirectory = directory,
                Postprocessors = new ITtsPostprocessor[] { new Post((audio, settings, token) => UniTask.FromResult(audio), settings => null) }
            });
            await synth.SynthesizeAsync(Request());
            await synth.SynthesizeAsync(Request());
            Assert.That(synth.Calls.Count, Is.EqualTo(2));
            Assert.That(Directory.GetFiles(directory), Is.Empty);
        }

        [Test]
        public async NUnitTask GenerateBypassesProcessorsResamplingAndCache()
        {
            var directory = CacheDirectory();
            var raw = Pcm16Audio.WriteWave(new byte[] { 0, 0, 100, 0, 200, 0, 100, 0 }, 8000);
            int pres = 0;
            int posts = 0;
            var synth = Create(new TestOptions
            {
                CacheDirectory = directory, SampleRate = 16000,
                Preprocessors = new ITtsPreprocessor[] { new Pre((request, settings, token) => { pres++; return UniTask.FromResult(request.Text); }) },
                Postprocessors = new ITtsPostprocessor[] { new Post((audio, settings, token) => { posts++; return UniTask.FromResult(audio); }) }
            });
            synth.Generate = (request, settings, token) => UniTask.FromResult(raw);
            var generated = await synth.GenerateAsync(Request());
            Assert.That(WavResampler.ReadWave(generated).SampleRate, Is.EqualTo(8000));
            Assert.That(pres + posts, Is.Zero);
            Assert.That(Directory.GetFiles(directory), Is.Empty);
            var processed = await synth.SynthesizeAsync(Request());
            Assert.That(WavResampler.ReadWave(processed).SampleRate, Is.EqualTo(16000));
            var cached = await synth.SynthesizeAsync(Request());
            CollectionAssert.AreEqual(processed, cached);
            Assert.That(pres, Is.EqualTo(2));
            Assert.That(posts, Is.EqualTo(1));
            Assert.That(synth.Calls.Count, Is.EqualTo(2));
            generated[0] = 0;
            Assert.That(raw[0], Is.EqualTo((byte)'R'));
        }

        [Test]
        public async NUnitTask QueuedRequestsCaptureTheirInputAndOptionsBeforeWaiting()
        {
            var entered = Signal();
            var release = Signal();
            var options = new TestOptions { Voice = "old", StyleMapper = new Dictionary<string, string> { ["happy"] = "old-style" } };
            var synth = Create(options);
            synth.Generate = async (request, settings, token) =>
            {
                if (request.Text == "first") { entered.TrySetResult(true); using (token.Register(() => release.TrySetCanceled())) await release.Task; }
                return new byte[] { 1 };
            };
            var first = synth.SynthesizeAsync(Request("first"));
            await entered.Task;
            var secondRequest = Request("second");
            secondRequest.StyleInfo = new JObject { ["nested"] = new JObject { ["value"] = "old" } };
            secondRequest.Language = "ja";
            var second = synth.SynthesizeAsync(secondRequest);
            secondRequest.Text = "mutated";
            secondRequest.Language = "en";
            secondRequest.StyleInfo["nested"]["value"] = "mutated";
            synth.UpdateOptions(new TestOptions { Voice = "new", StyleMapper = new Dictionary<string, string> { ["happy"] = "new-style" } });
            Assert.That(synth.Calls.Count, Is.EqualTo(1));
            release.TrySetResult(true);
            await UniTask.WhenAll(first, second);
            await synth.SynthesizeAsync(Request("third"));
            Assert.That(synth.Calls[1].Request.Text, Is.EqualTo("second"));
            Assert.That(synth.Calls[1].Request.Language, Is.EqualTo("ja"));
            Assert.That((string)synth.Calls[1].Request.StyleInfo["nested"]["value"], Is.EqualTo("old"));
            Assert.That(((TestOptions)synth.Calls[1].Options).Voice, Is.EqualTo("old"));
            Assert.That(synth.Calls[1].Options.StyleMapper["happy"], Is.EqualTo("old-style"));
            Assert.That(((TestOptions)synth.Calls[2].Options).Voice, Is.EqualTo("new"));
        }

        [Test]
        public async NUnitTask CancelingAQueuedRequestDoesNotEnterGeneration()
        {
            var entered = Signal();
            var release = Signal();
            var synth = Create();
            synth.Generate = async (request, settings, token) => { entered.TrySetResult(true); using (token.Register(() => release.TrySetCanceled())) await release.Task; return new byte[] { 1 }; };
            var first = synth.GenerateAsync(Request("first"));
            await entered.Task;
            using (var cancellation = new CancellationTokenSource())
            {
                var queued = synth.GenerateAsync(Request("queued"), cancellation.Token);
                cancellation.Cancel();
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await queued);
            }
            Assert.That(synth.Calls.Count, Is.EqualTo(1));
            release.TrySetResult(true);
            await first;
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask ProcessorCancellationDoesNotPublishCache(bool afterGeneration)
        {
            var entered = Signal();
            var directory = CacheDirectory();
            var options = new TestOptions { CacheDirectory = directory };
            if (afterGeneration) options.Postprocessors = new ITtsPostprocessor[] { new Post(async (audio, settings, token) => { entered.TrySetResult(true); await WaitCancellation(token); return audio; }) };
            else options.Preprocessors = new ITtsPreprocessor[] { new Pre(async (request, settings, token) => { entered.TrySetResult(true); await WaitCancellation(token); return request.Text; }) };
            var synth = Create(options);
            using (var cancellation = new CancellationTokenSource())
            {
                var active = synth.SynthesizeAsync(Request(), cancellation.Token);
                await entered.Task;
                cancellation.Cancel();
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await active);
            }
            Assert.That(synth.Calls.Count, Is.EqualTo(afterGeneration ? 1 : 0));
            Assert.That(Directory.GetFiles(directory), Is.Empty);
        }

        [Test]
        public async NUnitTask DisposeCancelsActiveAndQueuedWorkAndWaitsForCleanup()
        {
            var entered = Signal();
            var canceled = Signal();
            var releaseCleanup = Signal();
            var synth = Create();
            synth.Generate = async (request, settings, token) =>
            {
                entered.TrySetResult(true);
                try { await WaitCancellation(token); return new byte[] { 1 }; }
                finally { canceled.TrySetResult(true); await releaseCleanup.Task; }
            };
            var active = synth.SynthesizeAsync(Request("active"));
            await entered.Task;
            var queued = synth.GenerateAsync(Request("queued"));
            UniTask disposal = default;
            try
            {
                disposal = synth.DisposeAsync();
                Assert.That(synth.DisposeAsync(), Is.EqualTo(disposal));
                await canceled.Task;
                Assert.That(disposal.Status.IsCompleted(), Is.False);
                Assert.That(synth.DisposeCount, Is.Zero);
                Assert.Throws<ObjectDisposedException>(() => synth.SynthesizeAsync(Request()));
                Assert.Throws<ObjectDisposedException>(() => synth.UpdateOptions(new TestOptions()));
            }
            finally { releaseCleanup.TrySetResult(true); }
            await disposal;
            await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await active);
            await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await queued);
            Assert.That(synth.Calls.Count, Is.EqualTo(1));
            Assert.That(synth.DisposeCount, Is.EqualTo(1));
        }

        [TestCase("pre")]
        [TestCase("post")]
        [TestCase("generate")]
        public async NUnitTask ReentrantSynthesisGenerationAndDisposalAreRejected(string location)
        {
            FakeSynthesizer synth = null;
            int checks = 0;
            Action check = () =>
            {
                Assert.Throws<InvalidOperationException>(() => synth.SynthesizeAsync(Request()));
                Assert.Throws<InvalidOperationException>(() => synth.GenerateAsync(Request()));
                Assert.Throws<InvalidOperationException>(() => synth.DisposeAsync());
                checks++;
            };
            var options = new TestOptions();
            if (location == "pre") options.Preprocessors = new ITtsPreprocessor[] { new Pre((request, settings, token) => { check(); return UniTask.FromResult(request.Text); }) };
            if (location == "post") options.Postprocessors = new ITtsPostprocessor[] { new Post((audio, settings, token) => { check(); return UniTask.FromResult(audio); }) };
            synth = Create(options);
            if (location == "generate") synth.Generate = (request, settings, token) => { check(); return UniTask.FromResult(new byte[] { 1 }); };
            await synth.SynthesizeAsync(Request());
            Assert.That(checks, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask DelegateSynthesizerCacheIdentityControlsCrossInstanceReuse(bool sharedIdentity)
        {
            var options = new SpeechSynthesizerOptions { CacheDirectory = CacheDirectory() };
            int secondCalls = 0;
            var first = Track(SynthesizerFactory.Create((request, token) => UniTask.FromResult(new byte[] { 1 }), options, sharedIdentity ? "stable-engine" : null));
            var second = Track(SynthesizerFactory.Create((request, token) => { secondCalls++; return UniTask.FromResult(new byte[] { 2 }); }, options, sharedIdentity ? "stable-engine" : null));
            Assert.That((await first.SynthesizeAsync(Request()))[0], Is.EqualTo(1));
            Assert.That((await second.SynthesizeAsync(Request()))[0], Is.EqualTo(sharedIdentity ? 1 : 2));
            Assert.That(secondCalls, Is.EqualTo(sharedIdentity ? 0 : 1));
        }

        [Test]
        public void OptionsAreCopiedWithTheirDerivedTypeAndRejectDifferentTypes()
        {
            var options = new TestOptions { Voice = "original", StyleMapper = new Dictionary<string, string> { ["happy"] = "one" } };
            var synth = Create(options);
            options.Voice = "mutated";
            options.StyleMapper["happy"] = "mutated";
            var snapshot = (TestOptions)synth.GetOptions();
            Assert.That(snapshot.Voice, Is.EqualTo("original"));
            Assert.That(snapshot.StyleMapper["happy"], Is.EqualTo("one"));
            snapshot.StyleMapper.Clear();
            Assert.That(synth.GetOptions().StyleMapper.Count, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => synth.UpdateOptions(new SpeechSynthesizerOptions()));
        }

        [Test]
        public async NUnitTask RouterSelectsFromOriginalInputAndRunsChildPreprocessingExactlyOnce()
        {
            var child = Create(new TestOptions { Preprocessors = new ITtsPreprocessor[] { new Pre((request, settings, token) => UniTask.FromResult(request.Text + "-processed")) } });
            var registrations = new Dictionary<string, ISpeechSynthesizer> { ["ja"] = child };
            int routes = 0;
            var router = Track(new SpeechSynthesizerRouter(registrations, request =>
            {
                routes++;
                Assert.That(request.Text, Is.EqualTo("original"));
                request.Text = "route mutation";
                return request.Language;
            }));
            registrations.Clear();
            var request = Request("original");
            request.Language = "ja";
            await router.SynthesizeAsync(request);
            Assert.That(routes, Is.EqualTo(1));
            Assert.That(child.Calls.Single().Request.Text, Is.EqualTo("original-processed"));
            Assert.That(request.Text, Is.EqualTo("original"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask RouterDefaultAppliesOnlyWhenTheSelectorReturnsNull(bool unknownRoute)
        {
            var child = new StubSynthesizer();
            var router = Track(new SpeechSynthesizerRouter(new Dictionary<string, ISpeechSynthesizer> { ["default"] = child },
                request => unknownRoute ? "missing" : null, "default"));
            if (unknownRoute)
            {
                await SpeechAsyncAssert.ThrowsAsync<ArgumentException>(async () => await router.SynthesizeAsync(Request()));
                Assert.That(child.SynthesizeCount, Is.Zero);
            }
            else
            {
                await router.SynthesizeAsync(Request());
                Assert.That(child.SynthesizeCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async NUnitTask RouterRequiresASelectorEvenWhenADefaultRouteExists()
        {
            var router = Track(new SpeechSynthesizerRouter(new Dictionary<string, ISpeechSynthesizer> { ["default"] = new StubSynthesizer() }, defaultRoute: "default"));
            await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(async () => await router.SynthesizeAsync(Request()));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask RouterGenerateAlsoRunsTheChildSynthesisFlow(bool generate)
        {
            var child = new StubSynthesizer();
            var router = Track(new SpeechSynthesizerRouter(new Dictionary<string, ISpeechSynthesizer> { ["child"] = child }, request => "child"));
            if (generate) await router.GenerateAsync(Request()); else await router.SynthesizeAsync(Request());
            Assert.That(child.SynthesizeCount, Is.EqualTo(1));
            Assert.That(child.GenerateCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask RouterDisposesEachSharedChildOnceOnlyWhenItOwnsChildren(bool ownsChildren)
        {
            var child = new StubSynthesizer();
            var router = Track(new SpeechSynthesizerRouter(new Dictionary<string, ISpeechSynthesizer> { ["a"] = child, ["b"] = child }, request => "a", ownsSynthesizers: ownsChildren));
            await router.DisposeAsync();
            await router.DisposeAsync();
            Assert.That(child.DisposeCount, Is.EqualTo(ownsChildren ? 1 : 0));
        }

        [Test]
        public async NUnitTask RouterAttemptsAllOwnedChildDisposalsEvenAfterOneFails()
        {
            var failure = new InvalidOperationException("child cleanup failed");
            var first = new StubSynthesizer { DisposalError = failure };
            var second = new StubSynthesizer();
            var router = new SpeechSynthesizerRouter(new Dictionary<string, ISpeechSynthesizer> { ["first"] = first, ["alias"] = first, ["second"] = second }, request => "first", ownsSynthesizers: true);
            var actual = await SpeechAsyncAssert.ThrowsAsync<AggregateException>(async () => await router.DisposeAsync());
            Assert.That(actual.InnerExceptions.Single(), Is.SameAs(failure));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
        }

        private sealed class TestOptions : SpeechSynthesizerOptions { public string Voice { get; set; } = "voice"; }
        private sealed class CapturedCall { public SpeechSynthesisRequest Request; public SpeechSynthesizerOptions Options; }
        private sealed class FakeSynthesizer : SpeechSynthesizerBase
        {
            public readonly List<CapturedCall> Calls = new List<CapturedCall>();
            public Func<SpeechSynthesisRequest, SpeechSynthesizerOptions, CancellationToken, UniTask<byte[]>> Generate = (request, options, token) => UniTask.FromResult(new byte[] { 1, 2 });
            public int DisposeCount;
            public FakeSynthesizer(SpeechSynthesizerOptions options) : base(options) { }
            protected override UniTask<byte[]> GenerateCoreAsync(SpeechSynthesisRequest request, SpeechSynthesizerOptions options, CancellationToken token)
            {
                Calls.Add(new CapturedCall { Request = request.Copy(), Options = options.Copy() });
                return Generate(request, options, token);
            }
            protected override UniTask DisposeResourcesAsync() { DisposeCount++; return UniTask.CompletedTask; }
        }
        private sealed class Pre : ITtsPreprocessor
        {
            private readonly Func<SpeechSynthesisRequest, SpeechSynthesizerOptions, CancellationToken, UniTask<string>> process;
            public Pre(Func<SpeechSynthesisRequest, SpeechSynthesizerOptions, CancellationToken, UniTask<string>> process) { this.process = process; }
            public UniTask<string> ProcessAsync(SpeechSynthesisRequest request, SpeechSynthesizerOptions options, CancellationToken token = default) => process(request, options, token);
        }
        private sealed class Post : ITtsPostprocessor
        {
            private readonly Func<byte[], SpeechSynthesizerOptions, CancellationToken, UniTask<byte[]>> process;
            private readonly Func<SpeechSynthesizerOptions, JToken> config;
            public Post(Func<byte[], SpeechSynthesizerOptions, CancellationToken, UniTask<byte[]>> process, Func<SpeechSynthesizerOptions, JToken> config = null)
            { this.process = process; this.config = config ?? (_ => new JObject()); }
            public UniTask<byte[]> ProcessAsync(byte[] audio, SpeechSynthesizerOptions options, CancellationToken token = default) => process(audio, options, token);
            public JToken GetCacheConfiguration(SpeechSynthesizerOptions options) => config(options);
        }
        private sealed class StubSynthesizer : ISpeechSynthesizer
        {
            public int SynthesizeCount;
            public int GenerateCount;
            public int DisposeCount;
            public Exception DisposalError;
            public UniTask<byte[]> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken token = default) { SynthesizeCount++; return UniTask.FromResult(new byte[] { 1 }); }
            public UniTask<byte[]> GenerateAsync(SpeechSynthesisRequest request, CancellationToken token = default) { GenerateCount++; return UniTask.FromResult(new byte[] { 2 }); }
            public SpeechSynthesizerOptions GetOptions() => new SpeechSynthesizerOptions();
            public void UpdateOptions(SpeechSynthesizerOptions options) { }
            public UniTask DisposeAsync() { DisposeCount++; return DisposalError == null ? UniTask.CompletedTask : UniTask.FromException(DisposalError); }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnitTask = System.Threading.Tasks.Task;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.STT;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class SpeechRecognizerBaseTests
    {
        private readonly List<ProbeRecognizer> recognizers = new List<ProbeRecognizer>();
        private readonly List<Action> releases = new List<Action>();

        [TearDown]
        public async NUnitTask Cleanup()
        {
            foreach (var release in releases) release();
            foreach (var recognizer in recognizers)
            {
                var disposing = recognizer.DisposeAsync();
                await Settled(disposing);
                try { await disposing; } catch { /* Some tests deliberately fail resource disposal. */ }
            }
            releases.Clear();
            recognizers.Clear();
        }

        [Test]
        public void OptionsAreCopiedIncludingAlternativeLanguagesAndDerivedFields()
        {
            var options = new MarkerOptions { Language = "ja", AlternativeLanguages = new[] { "en" }, Marker = "initial" };
            var recognizer = Own(options);
            options.Language = "changed";
            options.AlternativeLanguages[0] = "changed";
            var first = (MarkerOptions)recognizer.GetOptions();
            Assert.That(first.Language, Is.EqualTo("ja"));
            CollectionAssert.AreEqual(new[] { "en" }, first.AlternativeLanguages);
            Assert.That(first.Marker, Is.EqualTo("initial"));
            first.AlternativeLanguages[0] = "changed again";
            CollectionAssert.AreEqual(new[] { "en" }, recognizer.GetOptions().AlternativeLanguages);
            Assert.That(first.SampleRate, Is.EqualTo(16000));
            Assert.That(first.TimeoutSeconds, Is.EqualTo(10));
            Assert.That(first.MaxAttempts, Is.EqualTo(2));
        }

        [Test]
        public async NUnitTask RecognitionRunsHooksInOrderWithTransformedAudioAndMetadata()
        {
            var recognizer = Own();
            var steps = new List<string>();
            recognizer.PreprocessAsync = (id, audio, token) =>
            {
                steps.Add("pre:" + id);
                return UniTask.FromResult(new SpeechPreprocessResult
                {
                    Audio = new byte[] { 2 }, Metadata = new Dictionary<string, object> { ["pre"] = "metadata" }
                });
            };
            recognizer.Handler = (audio, options, token) =>
            {
                steps.Add("transcribe:" + audio[0]);
                return UniTask.FromResult("raw text");
            };
            recognizer.PostprocessAsync = (id, text, audio, metadata, token) =>
            {
                steps.Add("post:" + id);
                Assert.That(text, Is.EqualTo("raw text"));
                Assert.That(audio, Is.EqualTo(new byte[] { 2 }));
                Assert.That(metadata["pre"], Is.EqualTo("metadata"));
                return UniTask.FromResult(new SpeechPostprocessResult
                {
                    Text = "final text", Metadata = new Dictionary<string, object> { ["post"] = "metadata" }
                });
            };
            var result = await recognizer.RecognizeAsync("session", new byte[] { 1 });
            CollectionAssert.AreEqual(new[] { "pre:session", "transcribe:2", "post:session" }, steps);
            Assert.That(result.Text, Is.EqualTo("final text"));
            Assert.That(result.PreprocessMetadata["pre"], Is.EqualTo("metadata"));
            Assert.That(result.PostprocessMetadata["post"], Is.EqualTo("metadata"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask EmptyPreprocessedAudioRetainsMetadataAndSkipsRemainingStages(bool nullAudio)
        {
            var recognizer = Own();
            recognizer.PreprocessAsync = (id, audio, token) => UniTask.FromResult(new SpeechPreprocessResult
            {
                Audio = nullAudio ? null : Array.Empty<byte>(),
                Metadata = new Dictionary<string, object> { ["reason"] = "filtered" }
            });
            recognizer.PostprocessAsync = (id, text, audio, metadata, token) => throw new Exception("Postprocess must be skipped.");
            var result = await recognizer.RecognizeAsync("session", new byte[] { 1 });
            Assert.That(result.Text, Is.Null);
            Assert.That(result.PreprocessMetadata["reason"], Is.EqualTo("filtered"));
            Assert.That(result.PostprocessMetadata, Is.Null);
            Assert.That(recognizer.Calls, Is.Empty);
        }

        [Test]
        public async NUnitTask MissingTranscriptStillRunsPostprocessing()
        {
            var recognizer = Own();
            recognizer.Handler = (audio, options, token) => UniTask.FromResult<string>(null);
            recognizer.PostprocessAsync = (id, text, audio, metadata, token) =>
            {
                Assert.That(text, Is.Null);
                return UniTask.FromResult(new SpeechPostprocessResult { Text = "replacement" });
            };
            Assert.That((await recognizer.RecognizeAsync("session", new byte[] { 1 })).Text, Is.EqualTo("replacement"));
        }

        [Test]
        public async NUnitTask DirectTranscriptionBypassesHooksAndCanBeCalledFromPreprocessing()
        {
            var recognizer = Own();
            var preCalls = 0;
            recognizer.Handler = (audio, options, token) => UniTask.FromResult("core:" + audio[0]);
            recognizer.PreprocessAsync = async (id, audio, token) =>
            {
                preCalls++;
                var direct = await recognizer.TranscribeAsync(new byte[] { 2 }, token);
                return new SpeechPreprocessResult { Audio = audio, Metadata = new Dictionary<string, object> { ["direct"] = direct } };
            };
            recognizer.PostprocessAsync = (id, text, audio, metadata, token) =>
                UniTask.FromResult(new SpeechPostprocessResult { Text = text + ":post" });
            Assert.That(await recognizer.TranscribeAsync(new byte[] { 3 }), Is.EqualTo("core:3"));
            var result = await recognizer.RecognizeAsync("session", new byte[] { 1 });
            Assert.That(preCalls, Is.EqualTo(1));
            Assert.That(result.Text, Is.EqualTo("core:1:post"));
            Assert.That(result.PreprocessMetadata["direct"], Is.EqualTo("core:2"));
            Assert.That(recognizer.Calls.Count, Is.EqualTo(3));
        }

        [Test]
        public async NUnitTask ActiveCallsRetainAudioOptionsAndBothHookSnapshots()
        {
            var recognizer = Own(new SpeechRecognizerOptions { Language = "initial", AlternativeLanguages = new[] { "en" } });
            var entered = Signal();
            var release = ReleaseSignal();
            recognizer.PreprocessAsync = async (id, audio, token) =>
            {
                entered.TrySetResult(true);
                await release.Task;
                return new SpeechPreprocessResult { Audio = audio };
            };
            recognizer.PostprocessAsync = (id, text, audio, metadata, token) =>
                UniTask.FromResult(new SpeechPostprocessResult { Text = text + ":old-post" });
            recognizer.Handler = (audio, options, token) => UniTask.FromResult(options.Language + ":" + options.AlternativeLanguages[0] + ":" + audio[0]);
            var originalAudio = new byte[] { 1 };
            var first = recognizer.RecognizeAsync("first", originalAudio);
            await Within(entered.Task);
            originalAudio[0] = 99;
            var updated = new SpeechRecognizerOptions { Language = "updated", AlternativeLanguages = new[] { "fr" } };
            recognizer.UpdateOptions(updated);
            updated.AlternativeLanguages[0] = "changed externally";
            recognizer.PreprocessAsync = (id, audio, token) => UniTask.FromResult(new SpeechPreprocessResult { Audio = new byte[] { 2 } });
            recognizer.PostprocessAsync = (id, text, audio, metadata, token) =>
                UniTask.FromResult(new SpeechPostprocessResult { Text = text + ":new-post" });
            var second = await recognizer.RecognizeAsync("second", new byte[] { 8 });
            release.TrySetResult(true);
            Assert.That((await first).Text, Is.EqualTo("initial:en:1:old-post"));
            Assert.That(second.Text, Is.EqualTo("updated:fr:2:new-post"));
        }

        [Test]
        public async NUnitTask ConcurrentCallsHaveIndependentSessionMetadataAndProviderState()
        {
            var recognizer = Own();
            var bothEntered = Signal();
            var release = ReleaseSignal();
            var count = 0;
            recognizer.PreprocessAsync = async (id, audio, token) =>
            {
                if (Interlocked.Increment(ref count) == 2) bothEntered.TrySetResult(true);
                await release.Task;
                return new SpeechPreprocessResult { Audio = audio, Metadata = new Dictionary<string, object> { ["session"] = id } };
            };
            recognizer.Handler = (audio, options, token) =>
            {
                Assert.That(options.Language, Is.Null);
                options.Language = audio[0].ToString();
                return UniTask.FromResult(options.Language);
            };
            recognizer.PostprocessAsync = (id, text, audio, metadata, token) => UniTask.FromResult(new SpeechPostprocessResult
            {
                Text = metadata["session"] + ":" + text,
                Metadata = new Dictionary<string, object> { ["session"] = id }
            });
            var first = recognizer.RecognizeAsync("one", new byte[] { 1 });
            var second = recognizer.RecognizeAsync("two", new byte[] { 2 });
            await Within(bothEntered.Task);
            release.TrySetResult(true);
            Assert.That((await first).Text, Is.EqualTo("one:1"));
            Assert.That((await second).Text, Is.EqualTo("two:2"));
            Assert.That((await first).PreprocessMetadata, Is.Not.SameAs((await second).PreprocessMetadata));
            Assert.That(recognizer.GetOptions().Language, Is.Null);
        }

        [Test]
        public async NUnitTask PreprocessingOutputCanBeReusedWithoutChangingAnActiveRequest()
        {
            var recognizer = Own();
            var output = new byte[] { 7 };
            var entered = Signal();
            var release = ReleaseSignal();
            recognizer.PreprocessAsync = (id, audio, token) => UniTask.FromResult(new SpeechPreprocessResult { Audio = output });
            recognizer.Handler = async (audio, options, token) =>
            {
                entered.TrySetResult(true);
                await release.Task;
                return audio[0].ToString();
            };
            var request = recognizer.RecognizeAsync("session", new byte[] { 1 });
            await Within(entered.Task);
            output[0] = 99;
            release.TrySetResult(true);
            Assert.That((await request).Text, Is.EqualTo("7"));
        }

        [Test]
        public async NUnitTask AlreadyCancelledInputDoesNotInvokeHooksOrProvider()
        {
            var recognizer = Own();
            var hooks = 0;
            recognizer.PreprocessAsync = (id, audio, token) =>
            {
                hooks++;
                return UniTask.FromResult(new SpeechPreprocessResult { Audio = audio });
            };
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                await Failed<OperationCanceledException>(Invoke(() => recognizer.RecognizeAsync("session", new byte[] { 1 }, cancellation.Token)));
            }
            Assert.That(hooks, Is.Zero);
            Assert.That(recognizer.Calls, Is.Empty);
        }

        [TestCase("pre")]
        [TestCase("core")]
        [TestCase("post")]
        public async NUnitTask StageExceptionsPropagateToTheRequest(string stage)
        {
            var recognizer = Own();
            var failure = new InvalidOperationException("stage failure");
            if (stage == "pre") recognizer.PreprocessAsync = (id, audio, token) => throw failure;
            if (stage == "core") recognizer.Handler = (audio, options, token) => throw failure;
            if (stage == "post") recognizer.PostprocessAsync = (id, text, audio, metadata, token) => throw failure;
            Assert.That(await Failed<InvalidOperationException>(recognizer.RecognizeAsync("session", new byte[] { 1 })), Is.SameAs(failure));
            Assert.That(recognizer.Calls.Count, Is.EqualTo(stage == "pre" ? 0 : 1));
        }

        [TestCase("pre")]
        [TestCase("core")]
        [TestCase("post")]
        public async NUnitTask CancellationPropagatesThroughEveryAsynchronousStage(string stage)
        {
            var recognizer = Own();
            var entered = Signal();
            var wait = ReleaseSignal();
            Func<CancellationToken, UniTask> block = async token =>
            {
                using (token.Register(() => wait.TrySetCanceled()))
                {
                    entered.TrySetResult(true);
                    await wait.Task;
                }
            };
            if (stage == "pre") recognizer.PreprocessAsync = async (id, audio, token) => { await block(token); return new SpeechPreprocessResult { Audio = audio }; };
            if (stage == "core") recognizer.Handler = async (audio, options, token) => { await block(token); return "unused"; };
            if (stage == "post") recognizer.PostprocessAsync = async (id, text, audio, metadata, token) => { await block(token); return new SpeechPostprocessResult { Text = text }; };
            using (var cancellation = new CancellationTokenSource())
            {
                var request = recognizer.RecognizeAsync("session", new byte[] { 1 }, cancellation.Token);
                await Within(entered.Task);
                cancellation.Cancel();
                await Failed<OperationCanceledException>(request);
                Assert.That(request.Status.IsCanceled(), Is.True);
            }
        }

        [Test]
        public async NUnitTask DisposeCancelsAndAwaitsConcurrentHooksBeforeDisposingResources()
        {
            var recognizer = Own();
            var first = new ControlledStage();
            var second = new ControlledStage();
            releases.Add(first.Release);
            releases.Add(second.Release);
            recognizer.PreprocessAsync = async (id, audio, token) =>
            {
                await (id == "one" ? first : second).RunAsync(token);
                return new SpeechPreprocessResult { Audio = audio };
            };
            var one = recognizer.RecognizeAsync("one", new byte[] { 1 });
            var two = recognizer.RecognizeAsync("two", new byte[] { 2 });
            await Within(UniTask.WhenAll(first.Entered.Task, second.Entered.Task));
            var disposing = recognizer.DisposeAsync();
            Assert.That(recognizer.DisposeAsync(), Is.EqualTo(disposing));
            await Within(UniTask.WhenAll(first.CleanupStarted.Task, second.CleanupStarted.Task));
            Assert.That(recognizer.DisposeCount, Is.Zero);
            Assert.That(disposing.Status.IsCompleted(), Is.False);
            await Failed<ObjectDisposedException>(Invoke(() => recognizer.RecognizeAsync("new", new byte[] { 1 })));
            await Failed<ObjectDisposedException>(Invoke(() => recognizer.TranscribeAsync(new byte[] { 1 })));
            Assert.Throws<ObjectDisposedException>(() => recognizer.UpdateOptions(new SpeechRecognizerOptions()));
            first.Release();
            await Failed<OperationCanceledException>(one);
            Assert.That(disposing.Status.IsCompleted(), Is.False);
            second.Release();
            await Failed<OperationCanceledException>(two);
            await Within(disposing);
            Assert.That(recognizer.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask DisposeWaitsForAProviderThatCompletesAfterCancellation()
        {
            var recognizer = Own();
            var entered = Signal();
            var release = ReleaseSignal();
            recognizer.Handler = async (audio, options, token) => { entered.TrySetResult(true); await release.Task; return "late"; };
            var request = recognizer.TranscribeAsync(new byte[] { 1 });
            await Within(entered.Task);
            var disposing = recognizer.DisposeAsync();
            Assert.That(disposing.Status.IsCompleted(), Is.False);
            Assert.That(recognizer.DisposeCount, Is.Zero);
            release.TrySetResult(true);
            await Failed<OperationCanceledException>(request);
            await Within(disposing);
            Assert.That(recognizer.DisposeCount, Is.EqualTo(1));
        }

        [TestCase("pre")]
        [TestCase("core")]
        [TestCase("post")]
        public async NUnitTask DisposalFromOwnedWorkFailsWithoutDeadlocking(string stage)
        {
            var recognizer = Own();
            if (stage == "pre") recognizer.PreprocessAsync = async (id, audio, token) => { await recognizer.DisposeAsync(); return new SpeechPreprocessResult(); };
            if (stage == "core") recognizer.Handler = async (audio, options, token) => { await recognizer.DisposeAsync(); return "unused"; };
            if (stage == "post") recognizer.PostprocessAsync = async (id, text, audio, metadata, token) => { await recognizer.DisposeAsync(); return new SpeechPostprocessResult(); };
            await Failed<InvalidOperationException>(recognizer.RecognizeAsync("session", new byte[] { 1 }));
            await Within(recognizer.DisposeAsync());
            Assert.That(recognizer.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask CancellationObserverFailureStillDisposesResourcesAfterActiveWork()
        {
            var recognizer = Own();
            var entered = Signal();
            var wait = ReleaseSignal();
            recognizer.Handler = async (audio, options, token) =>
            {
                using (token.Register(() => { wait.TrySetCanceled(); throw new InvalidOperationException("cancel observer failed"); }))
                {
                    entered.TrySetResult(true);
                    await wait.Task;
                    return "unused";
                }
            };
            var request = recognizer.TranscribeAsync(new byte[] { 1 });
            await Within(entered.Task);
            await Failed<AggregateException>(recognizer.DisposeAsync());
            await Failed<OperationCanceledException>(request);
            Assert.That(recognizer.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask ResourceDisposalFailureIsReturnedIdempotently()
        {
            var recognizer = Own();
            var failure = new InvalidOperationException("resource disposal failed");
            recognizer.DisposeFailure = failure;
            var disposing = recognizer.DisposeAsync();
            Assert.That(await Failed<InvalidOperationException>(disposing), Is.SameAs(failure));
            Assert.That(recognizer.DisposeAsync(), Is.EqualTo(disposing));
            Assert.That(recognizer.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void InvalidUpdatesLeaveTheOriginalConfigurationIntact()
        {
            var recognizer = Own(new MarkerOptions { Language = "original" });
            Assert.Throws<ArgumentException>(() => recognizer.UpdateOptions(new SpeechRecognizerOptions()));
            Assert.Throws<ArgumentOutOfRangeException>(() => recognizer.UpdateOptions(new MarkerOptions { MaxAttempts = 0 }));
            Assert.That(recognizer.GetOptions().Language, Is.EqualTo("original"));
        }

        private ProbeRecognizer Own(SpeechRecognizerOptions options = null)
        {
            var recognizer = new ProbeRecognizer(options);
            recognizers.Add(recognizer);
            return recognizer;
        }
        private SpeechCompletionSource<bool> ReleaseSignal()
        {
            var signal = Signal();
            releases.Add(() => signal.TrySetResult(true));
            return signal;
        }
        private static SpeechCompletionSource<bool> Signal() => new SpeechCompletionSource<bool>();
        private static async UniTask Settled(UniTask task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!task.Status.IsCompleted() && DateTime.UtcNow < deadline) await SpeechAsync.Yield();
            Assert.That(task.Status.IsCompleted(), Is.True, "Recognizer operation timed out.");
        }
        private static async UniTask Within(UniTask task) { await Settled(task); await task; }
        private static async UniTask Invoke(Func<UniTask> action) { await action(); }
        private static async UniTask<T> Failed<T>(UniTask task) where T : Exception
        {
            await Settled(task);
            try { await task; } catch (T exception) { return exception; }
            Assert.Fail("Expected " + typeof(T).Name);
            return null;
        }

        private sealed class MarkerOptions : SpeechRecognizerOptions { public string Marker { get; set; } }
        private sealed class ProbeRecognizer : SpeechRecognizerBase
        {
            public readonly ConcurrentQueue<byte[]> Calls = new ConcurrentQueue<byte[]>();
            public Func<byte[], SpeechRecognizerOptions, CancellationToken, UniTask<string>> Handler = (audio, options, token) => UniTask.FromResult("recognized");
            public int DisposeCount;
            public Exception DisposeFailure;
            public ProbeRecognizer(SpeechRecognizerOptions options) : base(options) { }
            protected override UniTask<string> TranscribeCoreAsync(byte[] audio, SpeechRecognizerOptions options, CancellationToken cancellationToken)
            {
                Calls.Enqueue(audio);
                return Handler(audio, options, cancellationToken);
            }
            protected override void DisposeResources()
            {
                DisposeCount++;
                if (DisposeFailure != null) throw DisposeFailure;
            }
        }
        private sealed class ControlledStage
        {
            private readonly SpeechCompletionSource<bool> cancellation = Signal();
            private readonly SpeechCompletionSource<bool> cleanup = Signal();
            public readonly SpeechCompletionSource<bool> Entered = Signal();
            public readonly SpeechCompletionSource<bool> CleanupStarted = Signal();
            public async UniTask RunAsync(CancellationToken token)
            {
                using (token.Register(() => cancellation.TrySetCanceled()))
                {
                    Entered.TrySetResult(true);
                    try { await cancellation.Task; }
                    finally { CleanupStarted.TrySetResult(true); await cleanup.Task; }
                }
            }
            public void Release() { cancellation.TrySetResult(true); cleanup.TrySetResult(true); }
        }
    }
}

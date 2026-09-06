using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.Avatar;
using ChatdollKit.Orchestration;
using ChatdollKit.SpeechPipeline;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChatdollKit.Tests.Orchestration
{
    public class ChatdollOrchestratorEngineTests
    {
        private readonly List<Rig> rigs = new List<Rig>();
        private readonly List<SpeechCompletionSource<bool>> releases = new List<SpeechCompletionSource<bool>>();
        private static SpeechCompletionSource<bool> Signal() => new SpeechCompletionSource<bool>();
        private SpeechCompletionSource<bool> Release() { var signal = Signal(); releases.Add(signal); return signal; }
        private Rig Create(ChatdollOrchestratorOptions options = null, bool ownsPipeline = false)
        {
            var rig = new Rig();
            rig.Orchestrator = new ChatdollOrchestratorEngine(rig.Pipeline, rig.Avatar, options, ownsPipeline);
            rig.Orchestrator.ResponseReceived += response => rig.Responses.Enqueue(response.Copy());
            rig.Orchestrator.PresentationStarted += presentation => rig.Started.Enqueue(presentation.Copy());
            rig.Orchestrator.PresentationCompleted += presentation => rig.Completed.Enqueue(presentation.Copy());
            rig.Orchestrator.Error += error => rig.Errors.Enqueue(error);
            rigs.Add(rig); return rig;
        }

        [TearDown]
        public async NUnitTask Cleanup()
        {
            foreach (var signal in releases) signal.TrySetResult(true);
            foreach (var rig in rigs) await Within(rig.Orchestrator.DisposeAsync());
            releases.Clear(); rigs.Clear();
        }
        // Only a deadlock guard; all ordering in these tests is controlled by signals.
        private static async UniTask Within(UniTask task)
        {
            task = SpeechAsync.Share(task);
            Assert.That(await UniTask.WhenAny(task, SpeechAsync.Delay(TimeSpan.FromSeconds(5), CancellationToken.None)), Is.EqualTo(0), "Operation did not complete after its gate was released.");
            await task;
        }
        private static async UniTask<T> Within<T>(UniTask<T> task) { task = SpeechAsync.Share(task); await Within((UniTask)task); return await task; }
        private static async UniTask WaitForCancellation(CancellationToken token)
        {
            var canceled = Signal();
            using (token.Register(() => canceled.TrySetCanceled())) await canceled.Task;
        }
        private static SpeechPipelineResponse Response(SpeechPipelineResponseType type, string id, string text = null, bool block = false)
        {
            return new SpeechPipelineResponse
            {
                Type = type, SessionId = "session", ContextId = "context", TransactionId = id, Text = text,
                VoiceText = type == SpeechPipelineResponseType.Chunk ? text : null,
                AudioData = type == SpeechPipelineResponseType.Chunk ? new byte[] { 1, 2 } : null,
                Metadata = type == SpeechPipelineResponseType.Accepted ? new JObject { ["block_barge_in"] = block } : null
            };
        }
        private static async UniTask Begin(Rig rig, string id, bool block = false)
        {
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Accepted, id, block: block));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Start, id));
        }

        [Test]
        public async NUnitTask FinalCompletesInvocationBeforeAvatarPlaybackCompletes()
        {
            var rig = Create(); var entered = Signal(); var release = Release();
            rig.Avatar.Handler = async (presentation, token) => { entered.TrySetResult(true); await release.Task; };
            var final = await Within(rig.Orchestrator.SendTextAsync("hello"));
            await Within(entered.Task);
            Assert.That(final.Type, Is.EqualTo(SpeechPipelineResponseType.Final));
            Assert.That(rig.Pipeline.Invocations.Single().Text, Is.EqualTo("hello"));
            Assert.That(rig.Orchestrator.IsPresenting, Is.True);
            Assert.That(rig.Completed, Is.Empty);
            release.TrySetResult(true); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Orchestrator.IsPresenting, Is.False);
            Assert.That(rig.Completed.Count, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask WaitInQueuePreservesEarlierPlaybackAndAppendsTheNextTurn()
        {
            var rig = Create(); var firstEntered = Signal(); var releaseFirst = Release();
            rig.Avatar.Handler = async (presentation, token) =>
            { if (presentation.TransactionId == "A") { firstEntered.TrySetResult(true); await releaseFirst.Task; } };
            await rig.Orchestrator.InvokeAsync(new SpeechPipelineRequest { Text = "first", TransactionId = "A" });
            await Within(firstEntered.Task);
            await rig.Orchestrator.InvokeAsync(new SpeechPipelineRequest { Text = "second", TransactionId = "B", WaitInQueue = true });
            Assert.That(rig.Pipeline.Invocations.Last().WaitInQueue, Is.True);
            Assert.That(rig.Avatar.Presentations.Select(item => item.TransactionId), Is.EqualTo(new[] { "A" }));
            Assert.That(rig.Avatar.CancellationTokens.First().IsCancellationRequested, Is.False);
            releaseFirst.TrySetResult(true); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Avatar.Presentations.Select(item => item.TransactionId), Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public async NUnitTask StopUsesStartOrderEvenWhenAcceptedArrivesInAnotherOrder()
        {
            var rig = Create(); var enteredA = Signal();
            rig.Avatar.Handler = async (presentation, token) =>
            { if (presentation.TransactionId == "A") { enteredA.TrySetResult(true); await WaitForCancellation(token); } };
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Accepted, "B"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Accepted, "A"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Start, "A"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "A", "old A"));
            await Within(enteredA.Task);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "A"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Start, "B"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "B", "old B"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "B"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Stop, "B"));
            await Begin(rig, "C");
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "C", "new C"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "C"));
            await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Avatar.Presentations.Select(item => item.TransactionId), Is.EqualTo(new[] { "A", "C" }));
            Assert.That(rig.Avatar.CancellationTokens.First().IsCancellationRequested, Is.True);
        }

        [Test]
        public async NUnitTask LateStopForOldTransactionCannotCancelNewerPlayback()
        {
            var rig = Create(); var enteredA = Signal(); var enteredB = Signal(); var releaseB = Release();
            rig.Avatar.Handler = async (presentation, token) =>
            {
                if (presentation.TransactionId == "A") { enteredA.TrySetResult(true); await WaitForCancellation(token); }
                else { enteredB.TrySetResult(true); await releaseB.Task; }
            };
            await Begin(rig, "A"); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "A", "A")); await Within(enteredA.Task);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Stop, "A"));
            await Begin(rig, "B"); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "B", "B")); await Within(enteredB.Task);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Stop, "A"));
            Assert.That(rig.Avatar.CancellationTokens.Last().IsCancellationRequested, Is.False);
            releaseB.TrySetResult(true); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "B"));
            await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Completed.Any(item => item.TransactionId == "B"), Is.True);
        }

        [Test]
        public async NUnitTask NullStopCutoffIsCapturedBeforeSubsequentStarts()
        {
            var rig = Create(); var entered = Signal();
            rig.Avatar.Handler = async (presentation, token) =>
            { if (presentation.TransactionId == "A") { entered.TrySetResult(true); await WaitForCancellation(token); } };
            await Begin(rig, "A"); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "A", "A")); await Within(entered.Task);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Stop, null));
            await Begin(rig, "B"); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "B", "B"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "B"));
            await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Avatar.Presentations.Last().TransactionId, Is.EqualTo("B"));
            Assert.That(rig.Completed.Any(item => item.TransactionId == "B"), Is.True);
        }

        [Test]
        public async NUnitTask UnknownNonNullStopDoesNotStopTheCurrentTurn()
        {
            var rig = Create(); var entered = Signal(); var release = Release();
            rig.Avatar.Handler = async (presentation, token) => { entered.TrySetResult(true); await release.Task; };
            await Begin(rig, "active"); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "active", "active")); await Within(entered.Task);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Stop, "never-started"));
            Assert.That(rig.Avatar.CancellationTokens.Single().IsCancellationRequested, Is.False);
            release.TrySetResult(true); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "active"));
            await Within(rig.Orchestrator.DrainAsync());
        }

        [Test]
        public async NUnitTask BlockBargeInRemainsHeldAfterFinalUntilItsAudioCompletes()
        {
            var rig = Create(); var entered = Signal(); var release = Release();
            rig.Avatar.Handler = async (presentation, token) => { entered.TrySetResult(true); await release.Task; };
            await Begin(rig, "blocking", true);
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.True);
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }), Is.False);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "blocking", "answer")); await Within(entered.Task);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "blocking"));
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.True);
            release.TrySetResult(true); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.False);
        }

        [Test]
        public async NUnitTask NonblockingAcceptedCannotReleaseAnotherTransactionsBlock()
        {
            var rig = Create();
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Accepted, "blocking", block: true));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Accepted, "other", block: false));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Canceled, "other"));
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.True);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Canceled, "blocking"));
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.False);
        }

        [TestCase(SpeechPipelineResponseType.Final)]
        [TestCase(SpeechPipelineResponseType.Canceled)]
        [TestCase(SpeechPipelineResponseType.Error)]
        public async NUnitTask TerminalWithoutAudioReleasesItsOwnBlock(SpeechPipelineResponseType terminal)
        {
            var rig = Create();
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Accepted, "first", block: true));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Accepted, "second", block: true));
            await rig.Pipeline.EmitAsync(Response(terminal, "first"));
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.True);
            await rig.Pipeline.EmitAsync(Response(terminal, "second"));
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask AllowBargeInControlsInputDuringGenerationAndPlayback(bool allowBargeIn)
        {
            var rig = Create(new ChatdollOrchestratorOptions { AllowBargeIn = allowBargeIn });
            var entered = Signal(); var release = Release();
            rig.Avatar.Handler = async (presentation, token) => { entered.TrySetResult(true); await release.Task; };
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Accepted, "turn"));
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.EqualTo(!allowBargeIn));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Start, "turn"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "turn", "answer")); await Within(entered.Task);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "turn"));
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.EqualTo(!allowBargeIn));
            release.TrySetResult(true); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask AllowBargeInCanChangeDuringGenerationAndAfterFinal(bool initiallyAllowed)
        {
            var rig = Create(new ChatdollOrchestratorOptions { AllowBargeIn = initiallyAllowed });
            var entered = Signal(); var release = Release();
            rig.Avatar.Handler = async (presentation, token) => { entered.TrySetResult(true); await release.Task; };
            await Begin(rig, "turn");

            rig.Orchestrator.AllowBargeIn = !initiallyAllowed;
            Assert.That(rig.Orchestrator.AllowBargeIn, Is.EqualTo(!initiallyAllowed));
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.EqualTo(initiallyAllowed));
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }), Is.EqualTo(!initiallyAllowed));
            await Within(rig.Orchestrator.DrainAsync());

            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "turn", "answer"));
            await Within(entered.Task);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "turn"));
            rig.Orchestrator.AllowBargeIn = initiallyAllowed;
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.EqualTo(!initiallyAllowed));
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 2, 0 }), Is.EqualTo(initiallyAllowed));
            Assert.That(rig.Avatar.CancellationTokens.Single().IsCancellationRequested, Is.False);

            release.TrySetResult(true); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.False);
            Assert.That(rig.Pipeline.Audio.Select(bytes => bytes[0]), Is.EqualTo(new byte[] { initiallyAllowed ? (byte)2 : (byte)1 }));
            Assert.That(rig.Completed.Count, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask EnablingBargeInCannotReleaseTheCurrentRequestsExplicitBlock()
        {
            var rig = Create(new ChatdollOrchestratorOptions { AllowBargeIn = false });
            var entered = Signal(); var release = Release();
            rig.Avatar.Handler = async (presentation, token) => { entered.TrySetResult(true); await release.Task; };
            await Begin(rig, "blocking", true);
            rig.Orchestrator.AllowBargeIn = true;
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.True);
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }), Is.False);

            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "blocking", "answer"));
            await Within(entered.Task);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "blocking"));
            rig.Orchestrator.AllowBargeIn = false;
            rig.Orchestrator.AllowBargeIn = true;
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.True);
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 2, 0 }), Is.False);

            release.TrySetResult(true); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.False);
            Assert.That(rig.Pipeline.Audio, Is.Empty);
        }

        [Test]
        public async NUnitTask EnablingBargeInCannotOverrideExplicitlyDisabledInput()
        {
            var rig = Create(new ChatdollOrchestratorOptions { AllowBargeIn = false });
            rig.Orchestrator.InputEnabled = false;
            await Begin(rig, "turn");
            rig.Orchestrator.AllowBargeIn = true;
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.True);
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }), Is.False);
            rig.Orchestrator.InputEnabled = true;
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.False);
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 2, 0 }), Is.True);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "turn"));
            await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Pipeline.Audio.Single()[0], Is.EqualTo(2));
        }

        [Test]
        public async NUnitTask DisablingBargeInDiscardsQueuedAudioWithoutCancelingSubmittedAudio()
        {
            var rig = Create(); var entered = Signal(); var release = Release(); var count = 0;
            var activeToken = CancellationToken.None;
            rig.Pipeline.AudioHandler = async (audio, token) =>
            {
                if (Interlocked.Increment(ref count) == 1)
                { activeToken = token; entered.TrySetResult(true); await release.Task; }
            };
            await Begin(rig, "turn");
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }), Is.True); await Within(entered.Task);
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 2, 0 }), Is.True);

            rig.Orchestrator.AllowBargeIn = false;
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.True);
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 3, 0 }), Is.False);
            Assert.That(activeToken.IsCancellationRequested, Is.False);
            rig.Orchestrator.AllowBargeIn = true;
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 4, 0 }), Is.True);
            release.TrySetResult(true);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "turn"));
            await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Pipeline.Audio.Select(bytes => bytes[0]), Is.EqualTo(new byte[] { 1, 4 }));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async NUnitTask AllowBargeInCanChangeDuringControlWithoutReopeningInput(bool reset, bool allowedAfterControl)
        {
            var rig = Create(new ChatdollOrchestratorOptions { AllowBargeIn = allowedAfterControl });
            var entered = Signal(); var release = Release();
            Func<CancellationToken, UniTask> controlHandler = async token =>
            { entered.TrySetResult(true); await release.Task; };
            rig.Pipeline.InterruptHandler = controlHandler;
            rig.Pipeline.ResetHandler = (context, token) => controlHandler(token);
            var control = reset ? rig.Orchestrator.ResetAsync("next-context") : rig.Orchestrator.InterruptAsync();
            await Within(entered.Task);

            foreach (var allowBargeIn in new[] { !allowedAfterControl, allowedAfterControl })
            {
                rig.Orchestrator.AllowBargeIn = allowBargeIn;
                Assert.That(rig.Orchestrator.AllowBargeIn, Is.EqualTo(allowBargeIn));
                Assert.That(rig.Orchestrator.IsInputSuppressed, Is.True);
                Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }), Is.False);
                Assert.That(control.Status.IsCompleted(), Is.False);
            }

            release.TrySetResult(true); await Within(control);
            Assert.That(rig.Orchestrator.AllowBargeIn, Is.EqualTo(allowedAfterControl));
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.False);
            await Begin(rig, "next-turn");
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.EqualTo(!allowedAfterControl));
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 2, 0 }), Is.EqualTo(allowedAfterControl));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "next-turn"));
            await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Pipeline.Audio.Select(bytes => bytes[0]), Is.EqualTo(allowedAfterControl ? new byte[] { 2 } : Array.Empty<byte>()));
        }

        [Test]
        public async NUnitTask DefaultAllowsAudioWhileANonblockingResponseIsPresenting()
        {
            var rig = Create(); var entered = Signal(); var release = Release();
            rig.Avatar.Handler = async (presentation, token) => { entered.TrySetResult(true); await release.Task; };
            await Begin(rig, "turn"); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "turn", "answer")); await Within(entered.Task);
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.False);
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }), Is.True);
            release.TrySetResult(true); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "turn"));
            await Within(rig.Orchestrator.DrainAsync()); Assert.That(rig.Pipeline.Audio.Count, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask SubmittedAudioIsCopiedAndProcessedInOrder()
        {
            var rig = Create(); var entered = Signal(); var release = Release(); var count = 0;
            rig.Pipeline.AudioHandler = async (audio, token) =>
            { if (Interlocked.Increment(ref count) == 1) { entered.TrySetResult(true); await release.Task; } };
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }), Is.True); await Within(entered.Task);
            var audio = new byte[] { 2, 0 }; Assert.That(rig.Orchestrator.SubmitAudio(audio), Is.True); audio[0] = 99;
            release.TrySetResult(true); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Pipeline.Audio.Select(bytes => bytes[0]), Is.EqualTo(new byte[] { 1, 2 }));
        }

        [Test]
        public async NUnitTask ExplicitInputDisableRejectsSamplesWithoutSendingThem()
        {
            var rig = Create(); rig.Orchestrator.InputEnabled = false;
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }), Is.False);
            rig.Orchestrator.InputEnabled = true;
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 2, 0 }), Is.True);
            await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Pipeline.Audio.Single(), Is.EqualTo(new byte[] { 2, 0 }));
        }

        [Test]
        public async NUnitTask AudioOverflowDiscardsWaitingFramesAndRequiresExplicitResume()
        {
            var rig = Create(new ChatdollOrchestratorOptions { MaxPendingAudioFrames = 1 });
            var entered = Signal(); var release = Release(); var count = 0;
            rig.Pipeline.AudioHandler = async (audio, token) =>
            { if (Interlocked.Increment(ref count) == 1) { entered.TrySetResult(true); await release.Task; } };
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }), Is.True); await Within(entered.Task);
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 2, 0 }), Is.True);
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 3, 0 }), Is.False);
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.True);
            release.TrySetResult(true); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Pipeline.Audio.Select(bytes => bytes[0]), Is.EqualTo(new byte[] { 1 }));
            Assert.That(rig.Errors.Count, Is.EqualTo(1));
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 4, 0 }), Is.False);
            rig.Orchestrator.ResumeAudioInput();
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 5, 0 }), Is.True);
            await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Pipeline.Audio.Select(bytes => bytes[0]), Is.EqualTo(new byte[] { 1, 5 }));
        }

        [Test]
        public async NUnitTask AudioFailurePausesForwardingUntilExplicitResume()
        {
            var rig = Create(); var error = new InvalidOperationException("input failed");
            rig.Pipeline.AudioHandler = (audio, token) => throw error;
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }), Is.True);
            await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.True); Assert.That(rig.Errors, Does.Contain(error));
            rig.Pipeline.AudioHandler = null; rig.Orchestrator.ResumeAudioInput();
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 2, 0 }), Is.True);
            await Within(rig.Orchestrator.DrainAsync());
        }

        [Test]
        public async NUnitTask SuppressionDiscardsQueuedMicrophoneFramesBeforeResuming()
        {
            var rig = Create(); var entered = Signal(); var release = Release(); var count = 0;
            rig.Pipeline.AudioHandler = async (audio, token) =>
            { if (Interlocked.Increment(ref count) == 1) { entered.TrySetResult(true); await release.Task; } };
            rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }); await Within(entered.Task);
            rig.Orchestrator.SubmitAudio(new byte[] { 2, 0 });
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Accepted, "blocking", block: true));
            release.TrySetResult(true); await Within(rig.Orchestrator.DrainAsync());
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Canceled, "blocking"));
            rig.Orchestrator.SubmitAudio(new byte[] { 3, 0 }); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Pipeline.Audio.Select(bytes => bytes[0]), Is.EqualTo(new byte[] { 1, 3 }));
        }

        [Test]
        public async NUnitTask PresentationOverflowDropsOnlyTheAffectedTurn()
        {
            var rig = Create(new ChatdollOrchestratorOptions { MaxPendingPresentations = 1 }); var entered = Signal();
            rig.Avatar.Handler = async (presentation, token) =>
            { if (presentation.TransactionId == "overflow") { entered.TrySetResult(true); await WaitForCancellation(token); } };
            await Begin(rig, "overflow", true);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "overflow", "playing")); await Within(entered.Task);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "overflow", "queued"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "overflow", "too many"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "overflow"));
            await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Errors.Count, Is.EqualTo(1)); Assert.That(rig.Orchestrator.IsInputSuppressed, Is.False);
            await Begin(rig, "next"); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "next", "next"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "next")); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Avatar.Presentations.Select(item => item.Text), Is.EqualTo(new[] { "playing", "next" }));
        }

        [Test]
        public async NUnitTask PresentationFailureAbandonsItsRemainingChunksAndReportsTheCause()
        {
            var rig = Create(); var entered = Signal(); var release = Release(); var failure = new InvalidOperationException("avatar failed");
            rig.Avatar.Handler = async (presentation, token) => { entered.TrySetResult(true); await release.Task; throw failure; };
            await Begin(rig, "bad"); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "bad", "first")); await Within(entered.Task);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "bad", "queued"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "bad")); release.TrySetResult(true);
            await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Errors, Does.Contain(failure)); Assert.That(rig.Avatar.Presentations.Count, Is.EqualTo(1));
            Assert.That(rig.Completed, Is.Empty);
        }

        [Test]
        public async NUnitTask InvalidAudioFrameIsRejectedBeforeReachingPipeline()
        {
            var rig = Create();
            Assert.Throws<ArgumentNullException>(() => rig.Orchestrator.SubmitAudio(null));
            Assert.Throws<ArgumentException>(() => rig.Orchestrator.SubmitAudio(new byte[] { 1 }));
            Assert.That(rig.Orchestrator.SubmitAudio(Array.Empty<byte>()), Is.True);
            await Within(rig.Orchestrator.DrainAsync()); Assert.That(rig.Pipeline.Audio, Is.Empty);
        }

        [Test]
        public async NUnitTask BeforeRequestCanCustomizeAnOwnedSnapshotBeforeSubmission()
        {
            var rig = Create(); var entered = Signal(); var release = Release();
            rig.Orchestrator.BeforeRequestAsync = async (request, token) =>
            { entered.TrySetResult(true); await release.Task; request.Text += " customized"; };
            var original = new SpeechPipelineRequest { Text = "original", Metadata = new JObject { ["value"] = "original" } };
            var result = rig.Orchestrator.InvokeAsync(original); await Within(entered.Task);
            original.Text = "changed"; original.Metadata["value"] = "changed"; release.TrySetResult(true);
            await Within(result); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Pipeline.Invocations.Single().Text, Is.EqualTo("original customized"));
            Assert.That((string)rig.Pipeline.Invocations.Single().Metadata["value"], Is.EqualTo("original"));
        }

        [Test]
        public async NUnitTask QueuedInvocationCannotPassAnEarlierAsynchronousBeforeRequest()
        {
            var rig = Create(); var beforeFirst = Signal(); var releaseFirst = Release();
            rig.Orchestrator.BeforeRequestAsync = async (request, token) =>
            { if (request.Text == "first") { beforeFirst.TrySetResult(true); await releaseFirst.Task; } };
            var first = rig.Orchestrator.InvokeAsync(new SpeechPipelineRequest { Text = "first", TransactionId = "first" });
            await Within(beforeFirst.Task);
            var second = rig.Orchestrator.InvokeAsync(new SpeechPipelineRequest { Text = "second", TransactionId = "second", WaitInQueue = true });
            Assert.That(rig.Pipeline.Invocations, Is.Empty, "Neither invocation may reach the pipeline while the first input is still being prepared.");
            releaseFirst.TrySetResult(true);
            await Within(UniTask.WhenAll(first, second)); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Pipeline.Invocations.Select(request => request.Text), Is.EqualTo(new[] { "first", "second" }));
        }

        [Test]
        public async NUnitTask CancelingAMiddleSubmissionPreservesTheEarlierPreparationFence()
        {
            var rig = Create(); var beforeFirst = Signal(); var releaseFirst = Release();
            rig.Orchestrator.BeforeRequestAsync = async (request, token) =>
            { if (request.Text == "first") { beforeFirst.TrySetResult(true); await releaseFirst.Task; } };
            var first = rig.Orchestrator.SendTextAsync("first"); await Within(beforeFirst.Task);
            using (var cancel = new CancellationTokenSource())
            {
                var middle = rig.Orchestrator.InvokeAsync(new SpeechPipelineRequest { Text = "canceled", WaitInQueue = true }, cancel.Token);
                cancel.Cancel();
                await ExpectExceptionAsync<OperationCanceledException>(async () => await Within(middle));
            }
            var last = rig.Orchestrator.InvokeAsync(new SpeechPipelineRequest { Text = "last", WaitInQueue = true });
            Assert.That(rig.Pipeline.Invocations, Is.Empty);
            releaseFirst.TrySetResult(true);
            await Within(UniTask.WhenAll(first, last)); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Pipeline.Invocations.Select(request => request.Text), Is.EqualTo(new[] { "first", "last" }));
        }

        [Test]
        public async NUnitTask ObserverExceptionsAreReportedWithoutStoppingPresentationOrOtherObservers()
        {
            var rig = Create(); var error = new InvalidOperationException("observer failed"); var later = 0;
            rig.Orchestrator.ResponseReceived += response => { if (response.Type == SpeechPipelineResponseType.Chunk) throw error; };
            rig.Orchestrator.ResponseReceived += response => { if (response.Type == SpeechPipelineResponseType.Chunk) Interlocked.Increment(ref later); };
            await rig.Orchestrator.SendTextAsync("hello"); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Errors, Does.Contain(error)); Assert.That(later, Is.EqualTo(1)); Assert.That(rig.Completed.Count, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask ResponsesWithoutAnAcceptedStartCannotCreatePresentationWork()
        {
            var rig = Create();
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Start, "unknown"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "unknown", "ignored"));
            await Begin(rig, "valid"); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "valid"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Start, "valid"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "valid", "late"));
            await Within(rig.Orchestrator.DrainAsync()); Assert.That(rig.Avatar.Presentations, Is.Empty);
        }

        [Test]
        public async NUnitTask QueuedPresentationOwnsItsTextAudioAndMetadataSnapshot()
        {
            var rig = Create(); var entered = Signal(); var release = Release(); var count = 0;
            rig.Avatar.Handler = async (presentation, token) =>
            { if (Interlocked.Increment(ref count) == 1) { entered.TrySetResult(true); await release.Task; } };
            await Begin(rig, "turn"); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "turn", "first")); await Within(entered.Task);
            var response = Response(SpeechPipelineResponseType.Chunk, "turn", "original"); response.Metadata = new JObject { ["value"] = "original" };
            await rig.Pipeline.EmitAsync(response);
            response.Text = "changed"; response.AudioData[0] = 99; response.Metadata["value"] = "changed";
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "turn"));
            release.TrySetResult(true); await Within(rig.Orchestrator.DrainAsync());
            var snapshot = rig.Avatar.Presentations.Last();
            Assert.That(snapshot.Text, Is.EqualTo("original")); Assert.That(snapshot.AudioData[0], Is.EqualTo(1));
            Assert.That((string)rig.Responses.Last(item => item.Type == SpeechPipelineResponseType.Chunk).Metadata["value"], Is.EqualTo("original"));
        }

        [Test]
        public async NUnitTask ResponseMapsToAnOwnedAvatarRequestWithoutRecomputingVoiceText()
        {
            var rig = Create();
            await Begin(rig, "transaction");
            var response = Response(SpeechPipelineResponseType.Chunk, "transaction",
                "<ack>[face:raw]Display text</ack><answer>Answer</answer>");
            response.VoiceText = "Chosen pronunciation"; response.Language = "ja-JP";
            response.AudioData = new byte[] { 1, 2, 3 };
            response.Metadata = JObject.Parse("{\"control_tags\":[{\"name\":\"face\",\"attributes\":{\"name\":\"resolved\",\"duration\":2}}],\"nested\":{\"value\":1}}");
            await rig.Pipeline.EmitAsync(response);
            response.AudioData[0] = 99; response.VoiceText = "changed"; response.Metadata["nested"]["value"] = 99;
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "transaction"));
            await Within(rig.Orchestrator.DrainAsync());

            var request = rig.Avatar.Presentations.Single();
            Assert.That(request.SessionId, Is.EqualTo("session"));
            Assert.That(request.ContextId, Is.EqualTo("context"));
            Assert.That(request.TransactionId, Is.EqualTo("transaction"));
            Assert.That(request.Text, Is.EqualTo("<ack>[face:raw]Display text</ack><answer>Answer</answer>"));
            Assert.That(request.VoiceText, Is.EqualTo("Chosen pronunciation"));
            Assert.That(request.Language, Is.EqualTo("ja-JP"));
            Assert.That(request.AudioData, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(request.Controls.Single().Name, Is.EqualTo("resolved"));
            Assert.That(request.Controls[0].Kind, Is.EqualTo(AvatarControlKind.Face));
            Assert.That(request.Controls[0].DurationSeconds, Is.EqualTo(2));
            var received = rig.Responses.Single(item => item.Type == SpeechPipelineResponseType.Chunk);
            Assert.That((int)received.Metadata["nested"]["value"], Is.EqualTo(1));

            var mutableControls = request.Controls.ToList(); request.Controls = mutableControls;
            var copy = request.Copy();
            request.AudioData[0] = 7; request.VoiceText = "changed again"; mutableControls.Clear();
            Assert.That(copy.AudioData, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(copy.VoiceText, Is.EqualTo("Chosen pronunciation"));
            Assert.That(copy.Controls.Single().Name, Is.EqualTo("resolved"));
            Assert.That(received.AudioData[0], Is.EqualTo(1));
            Assert.That(rig.Started.Single().AudioData[0], Is.EqualTo(1));
            Assert.That(rig.Completed.Single().AudioData[0], Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask AvatarAndPresentationObserversCannotMutateEachOthersRequests()
        {
            var rig = Create();
            var laterStarted = new ConcurrentQueue<AvatarRequest>();
            var laterCompleted = new ConcurrentQueue<AvatarRequest>();
            Action<AvatarRequest> mutate = request =>
            {
                request.TransactionId = "changed"; request.Text = "changed"; request.VoiceText = "changed";
                request.AudioData[0] = 99; request.Controls = Array.Empty<AvatarControl>();
            };
            rig.Avatar.Handler = (request, token) => { mutate(request); return UniTask.CompletedTask; };
            rig.Orchestrator.PresentationStarted += mutate;
            rig.Orchestrator.PresentationStarted += laterStarted.Enqueue;
            rig.Orchestrator.PresentationCompleted += mutate;
            rig.Orchestrator.PresentationCompleted += laterCompleted.Enqueue;
            await Begin(rig, "turn");
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "turn", "[face:smile]Original"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "turn"));
            await Within(rig.Orchestrator.DrainAsync());

            foreach (var request in rig.Started.Concat(rig.Completed).Concat(laterStarted).Concat(laterCompleted))
            {
                Assert.That(request.TransactionId, Is.EqualTo("turn"));
                Assert.That(request.Text, Is.EqualTo("[face:smile]Original"));
                Assert.That(request.VoiceText, Is.EqualTo("[face:smile]Original"));
                Assert.That(request.AudioData[0], Is.EqualTo(1));
                Assert.That(request.Controls.Single().Name, Is.EqualTo("smile"));
            }
            Assert.That(laterStarted.Count, Is.EqualTo(1));
            Assert.That(laterCompleted.Count, Is.EqualTo(1));
            Assert.That(rig.Responses.Single(item => item.Type == SpeechPipelineResponseType.Chunk).AudioData[0], Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask DisposeStopsPresentationAndDisposesOnlyAnOwnedPipeline(bool owns)
        {
            var rig = Create(ownsPipeline: owns); var entered = Signal();
            rig.Avatar.Handler = async (presentation, token) => { entered.TrySetResult(true); await WaitForCancellation(token); };
            await Begin(rig, "turn"); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "turn", "answer")); await Within(entered.Task);
            await Within(rig.Orchestrator.DisposeAsync()); await rig.Orchestrator.DisposeAsync();
            Assert.That(rig.Avatar.CancellationTokens.Single().IsCancellationRequested, Is.True);
            Assert.That(rig.Avatar.StopCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(rig.Pipeline.DisposeCount, Is.EqualTo(owns ? 1 : 0));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "late", "late"));
            Assert.That(rig.Avatar.Presentations.Count, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask InterruptClearsPlaybackAndIgnoresLateChunksFromInterruptedTurn()
        {
            var rig = Create(); var entered = Signal();
            rig.Avatar.Handler = async (presentation, token) =>
            { if (presentation.TransactionId == "old") { entered.TrySetResult(true); await WaitForCancellation(token); } };
            await Begin(rig, "old", true); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "old", "old")); await Within(entered.Task);
            await Within(rig.Orchestrator.InterruptAsync());
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Start, "old"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "old", "late"));
            await Begin(rig, "new"); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "new", "new"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Final, "new"));
            await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Pipeline.InterruptCount, Is.EqualTo(1));
            Assert.That(rig.Avatar.Presentations.Select(item => item.Text), Is.EqualTo(new[] { "old", "new" }));
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.False);
        }

        [Test]
        public async NUnitTask CancelingInterruptWaitDoesNotResumeInputBeforeItsBoundaryFinishes()
        {
            var rig = Create(); var entered = Signal(); var release = Release();
            rig.Pipeline.InterruptHandler = async token => { entered.TrySetResult(true); await release.Task; };
            using (var cancel = new CancellationTokenSource())
            {
                var control = rig.Orchestrator.InterruptAsync(cancel.Token); await Within(entered.Task); cancel.Cancel();
                await ExpectExceptionAsync<OperationCanceledException>(async () => await Within(control));
                Assert.That(rig.Orchestrator.IsInputSuppressed, Is.True);
                Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }), Is.False);
                release.TrySetResult(true); await Within(rig.Orchestrator.DrainAsync());
            }
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.False);
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 2, 0 }), Is.True);
            await Within(rig.Orchestrator.DrainAsync()); Assert.That(rig.Pipeline.Audio.Single()[0], Is.EqualTo(2));
        }

        [Test]
        public async NUnitTask ResetForwardsContextAndRemovesOldPresentationWork()
        {
            var rig = Create(); var entered = Signal();
            rig.Avatar.Handler = async (presentation, token) => { entered.TrySetResult(true); await WaitForCancellation(token); };
            await Begin(rig, "old", true); await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "old", "old")); await Within(entered.Task);
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "old", "queued"));
            await Within(rig.Orchestrator.ResetAsync("new-context"));
            await rig.Pipeline.EmitAsync(Response(SpeechPipelineResponseType.Chunk, "old", "late"));
            await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Pipeline.ResetContexts, Is.EqualTo(new[] { "new-context" }));
            Assert.That(rig.Avatar.Presentations.Count, Is.EqualTo(1));
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.False);
        }

        [Test]
        public async NUnitTask FailedResetReportsFailureAndKeepsMicrophonePaused()
        {
            var rig = Create(); var error = new InvalidOperationException("reset failed");
            rig.Pipeline.ResetHandler = (context, token) => throw error;
            await ExpectExceptionAsync<InvalidOperationException>(async () => await Within(rig.Orchestrator.ResetAsync("new-context")));
            Assert.That(rig.Orchestrator.IsInputSuppressed, Is.True);
            Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }), Is.False);
            rig.Pipeline.ResetHandler = null;
            await Within(rig.Orchestrator.ResetAsync("retry-context"));
            rig.Orchestrator.ResumeAudioInput(); await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Errors, Does.Contain(error)); Assert.That(rig.Orchestrator.IsInputSuppressed, Is.False);
        }

        [Test]
        public async NUnitTask FailedPipelineControlStillWaitsForAvatarStopBeforeEndingTheBoundary()
        {
            var rig = Create(); var attemptedReset = Signal(); var enteredStop = Signal(); var releaseStop = Release();
            var failure = new InvalidOperationException("pipeline reset failed");
            rig.Pipeline.ResetHandler = (context, token) => { attemptedReset.TrySetResult(true); throw failure; };
            rig.Avatar.StopHandler = token => { enteredStop.TrySetResult(true); return releaseStop.Task; };
            var reset = rig.Orchestrator.ResetAsync("next-context");
            try
            {
                await Within(UniTask.WhenAll(attemptedReset.Task, enteredStop.Task));
                Assert.That(reset.Status.IsCompleted(), Is.False, "A failed pipeline call does not make an unfinished avatar stop safe to bypass.");
                Assert.Throws<InvalidOperationException>(() => rig.Orchestrator.ResumeAudioInput());
                Assert.That(rig.Orchestrator.SubmitAudio(new byte[] { 1, 0 }), Is.False);
            }
            finally { releaseStop.TrySetResult(true); rig.Pipeline.ResetHandler = null; }
            await ExpectExceptionAsync<InvalidOperationException>(async () => await Within(reset));
            await Within(rig.Orchestrator.DrainAsync());
            Assert.That(rig.Errors, Does.Contain(failure));
        }

        [Test]
        public async NUnitTask AvatarStopFailureFailsTheControlTaskInsteadOfLeavingItPending()
        {
            var rig = Create(); var error = new InvalidOperationException("stop failed");
            rig.Avatar.StopHandler = token => throw error;
            try
            {
                await ExpectExceptionAsync<InvalidOperationException>(async () => await Within(rig.Orchestrator.InterruptAsync()));
                Assert.That(rig.Orchestrator.IsInputSuppressed, Is.True);
            }
            finally { rig.Avatar.StopHandler = null; }
            await Within(rig.Orchestrator.DrainAsync()); Assert.That(rig.Errors, Does.Contain(error));
        }

        private sealed class Rig
        {
            public ChatdollOrchestratorEngine Orchestrator;
            public readonly FakePipeline Pipeline = new FakePipeline();
            public readonly FakeAvatar Avatar = new FakeAvatar();
            public readonly ConcurrentQueue<SpeechPipelineResponse> Responses = new ConcurrentQueue<SpeechPipelineResponse>();
            public readonly ConcurrentQueue<AvatarRequest> Started = new ConcurrentQueue<AvatarRequest>();
            public readonly ConcurrentQueue<AvatarRequest> Completed = new ConcurrentQueue<AvatarRequest>();
            public readonly ConcurrentQueue<Exception> Errors = new ConcurrentQueue<Exception>();
        }
        private sealed class FakeAvatar : IAvatarController
        {
            public readonly ConcurrentQueue<AvatarRequest> Presentations = new ConcurrentQueue<AvatarRequest>();
            public readonly ConcurrentQueue<CancellationToken> CancellationTokens = new ConcurrentQueue<CancellationToken>();
            public Func<AvatarRequest, CancellationToken, UniTask> Handler;
            public Func<CancellationToken, UniTask> StopHandler;
            public int StopCount;
            public UniTask PresentAsync(AvatarRequest presentation, CancellationToken cancellationToken)
            {
                Presentations.Enqueue(presentation.Copy()); CancellationTokens.Enqueue(cancellationToken);
                return Handler == null ? UniTask.CompletedTask : Handler(presentation, cancellationToken);
            }
            public UniTask StopAsync(CancellationToken cancellationToken = default)
            { Interlocked.Increment(ref StopCount); return StopHandler == null ? UniTask.CompletedTask : StopHandler(cancellationToken); }
        }
        private sealed class FakePipeline : ISpeechPipeline
        {
            public string SessionId => "session";
            public event Func<SpeechPipelineResponse, UniTask> ResponseReceived;
            public event Action<Exception> Error;
            public readonly ConcurrentQueue<SpeechPipelineRequest> Invocations = new ConcurrentQueue<SpeechPipelineRequest>();
            public readonly ConcurrentQueue<byte[]> Audio = new ConcurrentQueue<byte[]>();
            public readonly ConcurrentQueue<string> ResetContexts = new ConcurrentQueue<string>();
            public Func<byte[], CancellationToken, UniTask> AudioHandler;
            public Func<CancellationToken, UniTask> InterruptHandler;
            public Func<string, CancellationToken, UniTask> ResetHandler;
            public int InterruptCount, DisposeCount;
            public async UniTask EmitAsync(SpeechPipelineResponse response)
            {
                var handlers = ResponseReceived;
                if (handlers != null)
                    foreach (Func<SpeechPipelineResponse, UniTask> handler in handlers.GetInvocationList()) await handler(response);
            }
            public void RaiseError(Exception error) => Error?.Invoke(error);
            public async UniTask<SpeechPipelineResponse> InvokeAsync(SpeechPipelineRequest request, CancellationToken cancellationToken = default)
            {
                var copy = request.Copy(); Invocations.Enqueue(copy); cancellationToken.ThrowIfCancellationRequested();
                await EmitAsync(Response(SpeechPipelineResponseType.Accepted, copy.TransactionId, block: copy.BlockBargeIn));
                await EmitAsync(Response(SpeechPipelineResponseType.Start, copy.TransactionId));
                await EmitAsync(Response(SpeechPipelineResponseType.Chunk, copy.TransactionId, copy.Text));
                var final = Response(SpeechPipelineResponseType.Final, copy.TransactionId, copy.Text);
                await EmitAsync(final); return final;
            }
            public UniTask ProcessAudioSamplesAsync(byte[] samples, CancellationToken cancellationToken = default)
            { Audio.Enqueue((byte[])samples.Clone()); return AudioHandler == null ? UniTask.CompletedTask : AudioHandler(samples, cancellationToken); }
            public UniTask InterruptAsync(CancellationToken cancellationToken = default)
            { Interlocked.Increment(ref InterruptCount); return InterruptHandler == null ? UniTask.CompletedTask : InterruptHandler(cancellationToken); }
            public UniTask ResetAsync(string contextId = null, CancellationToken cancellationToken = default)
            { ResetContexts.Enqueue(contextId); return ResetHandler == null ? UniTask.CompletedTask : ResetHandler(contextId, cancellationToken); }
            public UniTask DrainAsync() => UniTask.CompletedTask;
            public UniTask DisposeAsync() { Interlocked.Increment(ref DisposeCount); return UniTask.CompletedTask; }
        }
        private static async UniTask<T> ExpectExceptionAsync<T>(Func<UniTask> operation) where T : Exception
        {
            try { await operation(); }
            catch (Exception error)
            {
                Assert.That(error, Is.InstanceOf<T>());
                return (T)error;
            }
            Assert.Fail("Expected " + typeof(T).Name);
            return null;
        }

    }
}

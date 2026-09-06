using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnitTask = System.Threading.Tasks.Task;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.VAD.TurnEndGates;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class TurnEndGateTests
    {
        private sealed class TestGate : TurnEndGateBase
        {
            public string GateName = "gate";
            public bool Background;
            public double? PendingTimeout;
            public int Calls;
            public Func<TurnEndGateContext, bool> BackgroundCondition;
            public Func<TurnEndRequest, CancellationToken, UniTask<TurnEndDecision>> Evaluate;
            public override string Name => GateName;
            public override bool RunInBackground => Background;
            public override double? Timeout => PendingTimeout;
            public override bool ShouldRunInBackground(TurnEndGateContext context) => BackgroundCondition?.Invoke(context) ?? true;
            public override UniTask<TurnEndDecision> ShouldEndTurnAsync(TurnEndRequest request, CancellationToken cancellationToken)
            {
                Calls++;
                return Evaluate(request, cancellationToken);
            }
        }

        private static TurnEndRequest Request(double silence = 1, string session = "session") => new TurnEndRequest
        {
            Audio = new byte[] { 1, 0 }, SampleRate = 16000, Channels = 1,
            RecordedDuration = 2, SilenceDuration = silence, SessionId = session, Text = "hello"
        };

        private static TurnEndDecision Pass() => new TurnEndDecision { ShouldEnd = true };
        private static TurnEndDecision Hold(double? timeout, string reason = "wait") => new TurnEndDecision { Timeout = timeout, Reason = reason };
        private static SpeechCompletionSource<TurnEndDecision> Completion() => new SpeechCompletionSource<TurnEndDecision>();

        [Test]
        public async NUnitTask NoGatesAllowsTurnEnd()
        {
            using (var manager = new TurnEndGateManager(null))
            {
                Assert.That(manager.HasGates, Is.False);
                Assert.That(await manager.ShouldEndTurnAsync(Request(), 1), Is.True);
            }
        }

        [Test]
        public async NUnitTask OrderedDecisionsLatchAndUseLongestAudioSilenceTimeout()
        {
            var order = new List<string>();
            var first = new TestGate
            {
                GateName = "first",
                Evaluate = (request, token) =>
                {
                    order.Add("first");
                    Assert.That(request.Context.Decisions, Is.Empty);
                    return UniTask.FromResult(Hold(2, "first reason"));
                }
            };
            var second = new TestGate
            {
                GateName = "second",
                Evaluate = (request, token) =>
                {
                    order.Add("second");
                    Assert.That(request.Context.IsWaiting("first"), Is.True);
                    return UniTask.FromResult(Hold(4, "second reason"));
                }
            };
            using (var manager = new TurnEndGateManager(new[] { first, second }))
            {
                Assert.That(await manager.ShouldEndTurnAsync(Request(1), 1), Is.False);
                var snapshot = manager.GetSessionState("session");
                Assert.That(snapshot.IsActive, Is.True);
                Assert.That(snapshot.Timeout, Is.EqualTo(4));
                CollectionAssert.AreEquivalent(new[] { "first reason", "second reason" }, snapshot.Reasons);
                Assert.That(await manager.ShouldEndTurnAsync(Request(4.9), 1), Is.False);
                Assert.That(await manager.ShouldEndTurnAsync(Request(5), 1), Is.True);
                CollectionAssert.AreEqual(new[] { "first", "second" }, order);
                Assert.That(manager.GetSessionState("session").IsActive, Is.False);
                Assert.That(snapshot.IsActive, Is.True, "Previously returned state must be detached.");
            }
        }

        [Test]
        public async NUnitTask AnInfiniteWaitDisablesFiniteForceTimeouts()
        {
            var finite = new TestGate { GateName = "finite", Evaluate = (request, token) => UniTask.FromResult(Hold(2)) };
            var infinite = new TestGate { GateName = "infinite", Evaluate = (request, token) => UniTask.FromResult(Hold(null)) };
            using (var manager = new TurnEndGateManager(new[] { finite, infinite }))
            {
                Assert.That(await manager.ShouldEndTurnAsync(Request(), 1), Is.False);
                Assert.That(manager.GetSessionState("session").Timeout, Is.Null);
                Assert.That(await manager.ShouldEndTurnAsync(Request(100), 1), Is.False);
                Assert.That(finite.Calls, Is.EqualTo(1));
                Assert.That(infinite.Calls, Is.EqualTo(1));
            }
        }

        [Test]
        public async NUnitTask BackgroundGateGetsPrecedingContextSnapshotAndCanReleaseHold()
        {
            var completion = Completion();
            TurnEndGateContext backgroundContext = null;
            var first = new TestGate { GateName = "first", Evaluate = (request, token) => UniTask.FromResult(Pass()) };
            var background = new TestGate
            {
                GateName = "background", Background = true,
                Evaluate = (request, token) => { backgroundContext = request.Context; return completion.Task; }
            };
            var last = new TestGate
            {
                GateName = "last",
                Evaluate = (request, token) =>
                {
                    Assert.That(request.Context.GetDecision("background").Pending, Is.True);
                    return UniTask.FromResult(Pass());
                }
            };
            using (var manager = new TurnEndGateManager(new[] { first, background, last }))
            {
                Assert.That(await manager.ShouldEndTurnAsync(Request(), 1), Is.False);
                Assert.That(backgroundContext.GetDecision("first"), Is.Not.Null);
                Assert.That(backgroundContext.GetDecision("background"), Is.Null);
                Assert.That(backgroundContext.GetDecision("last"), Is.Null);
                var drain = manager.DrainAsync();
                Assert.That(drain.Status.IsCompleted(), Is.False);
                completion.SetResult(Pass());
                await drain;
                Assert.That(await manager.ShouldEndTurnAsync(Request(), 1), Is.True);
            }
        }

        [Test]
        public async NUnitTask BackgroundResultCanExtendLatchedTimeout()
        {
            var completion = Completion();
            var background = new TestGate
            {
                Background = true, PendingTimeout = 2,
                Evaluate = (request, token) => completion.Task
            };
            using (var manager = new TurnEndGateManager(new[] { background }))
            {
                Assert.That(await manager.ShouldEndTurnAsync(Request(), 1), Is.False);
                Assert.That(manager.GetSessionState("session").Timeout, Is.EqualTo(2));
                completion.SetResult(Hold(4, "extended"));
                await manager.DrainAsync();
                Assert.That(await manager.ShouldEndTurnAsync(Request(3), 1), Is.False);
                Assert.That(manager.GetSessionState("session").Timeout, Is.EqualTo(4));
                Assert.That(await manager.ShouldEndTurnAsync(Request(5), 1), Is.True);
            }
        }

        [Test]
        public async NUnitTask BackgroundConditionCanRequireInlineEvaluation()
        {
            var completion = Completion();
            var background = new TestGate
            {
                Background = true, BackgroundCondition = context => false,
                Evaluate = (request, token) => completion.Task
            };
            using (var manager = new TurnEndGateManager(new[] { background }))
            {
                var evaluation = manager.ShouldEndTurnAsync(Request(), 1);
                Assert.That(evaluation.Status.IsCompleted(), Is.False);
                completion.SetResult(Pass());
                Assert.That(await evaluation, Is.True);
                await manager.DrainAsync();
            }
        }

        [Test]
        public async NUnitTask InlineFailureFallsBackAndReportsOnce()
        {
            var errors = new List<Exception>();
            var failing = new TestGate { Evaluate = (request, token) => throw new InvalidOperationException("test") };
            using (var manager = new TurnEndGateManager(new[] { failing }, errors.Add))
            {
                Assert.That(await manager.ShouldEndTurnAsync(Request(), 1), Is.True);
                await manager.DrainAsync();
                Assert.That(errors.Count, Is.EqualTo(1));
                Assert.That(manager.GetSessionState("session").IsActive, Is.False);
            }
        }

        [Test]
        public async NUnitTask BackgroundFailureDoesNotKeepTurnHeld()
        {
            var errors = new List<Exception>();
            var completion = Completion();
            var failing = new TestGate { Background = true, Evaluate = (request, token) => completion.Task };
            using (var manager = new TurnEndGateManager(new[] { failing }, errors.Add))
            {
                Assert.That(await manager.ShouldEndTurnAsync(Request(), 1), Is.False);
                completion.SetException(new InvalidOperationException("test"));
                await manager.DrainAsync();
                Assert.That(await manager.ShouldEndTurnAsync(Request(), 1), Is.True);
                Assert.That(errors.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public async NUnitTask ResetCancelsPendingWorkAndIsIdempotent()
        {
            CancellationToken gateToken = default;
            var pending = new TestGate
            {
                Background = true,
                Evaluate = async (request, token) =>
                {
                    gateToken = token;
                    await SpeechAsync.Delay(Timeout.InfiniteTimeSpan, token);
                    return Pass();
                }
            };
            using (var manager = new TurnEndGateManager(new[] { pending }))
            {
                Assert.That(await manager.ShouldEndTurnAsync(Request(), 1), Is.False);
                manager.ResetSession("session");
                manager.ResetSession("session");
                await manager.DrainAsync();
                Assert.That(gateToken.IsCancellationRequested, Is.True);
                Assert.That(manager.GetSessionState("session").IsActive, Is.False);
            }
        }

        [Test]
        public async NUnitTask ResetRejectsLateInlineResultWithoutRemovingReplacementSession()
        {
            var oldCompletion = Completion();
            var newCompletion = Completion();
            var calls = 0;
            var gate = new TestGate { Evaluate = (request, token) => ++calls == 1 ? oldCompletion.Task : newCompletion.Task };
            using (var manager = new TurnEndGateManager(new[] { gate }))
            {
                var oldEvaluation = manager.ShouldEndTurnAsync(Request(), 1);
                manager.ResetSession("session");
                var newEvaluation = manager.ShouldEndTurnAsync(Request(), 1);
                newCompletion.SetResult(Hold(4, "new session"));
                Assert.That(await newEvaluation, Is.False);
                oldCompletion.SetResult(Pass());
                Assert.That(await oldEvaluation, Is.False);
                await manager.DrainAsync();
                CollectionAssert.AreEqual(new[] { "new session" }, manager.GetSessionState("session").Reasons);
            }
        }

        [Test]
        public async NUnitTask SessionHoldsRemainIndependent()
        {
            var gate = new TestGate { Evaluate = (request, token) => UniTask.FromResult(Hold(request.SessionId == "a" ? 2 : 4)) };
            using (var manager = new TurnEndGateManager(new[] { gate }))
            {
                Assert.That(await manager.ShouldEndTurnAsync(Request(1, "a"), 1), Is.False);
                Assert.That(await manager.ShouldEndTurnAsync(Request(1, "b"), 1), Is.False);
                Assert.That(await manager.ShouldEndTurnAsync(Request(3, "a"), 1), Is.True);
                Assert.That(await manager.ShouldEndTurnAsync(Request(3, "b"), 1), Is.False);
                Assert.That(await manager.ShouldEndTurnAsync(Request(5, "b"), 1), Is.True);
            }
        }

        [Test]
        public async NUnitTask CallerCancellationCancelsInlineEvaluationAndClearsState()
        {
            var gate = new TestGate
            {
                Evaluate = async (request, token) => { await SpeechAsync.Delay(Timeout.InfiniteTimeSpan, token); return Pass(); }
            };
            using (var manager = new TurnEndGateManager(new[] { gate }))
            using (var cancellation = new CancellationTokenSource())
            {
                var evaluation = manager.ShouldEndTurnAsync(Request(), 1, cancellation.Token);
                cancellation.Cancel();
                try
                {
                    await evaluation;
                    Assert.Fail("Cancellation must propagate to the caller.");
                }
                catch (OperationCanceledException) { }
                await manager.DrainAsync();
                Assert.That(manager.GetSessionState("session").IsActive, Is.False);
            }
        }

        [Test]
        public async NUnitTask DisposeCancelsBackgroundWorkAndCanBeRepeated()
        {
            CancellationToken gateToken = default;
            var gate = new TestGate
            {
                Background = true,
                Evaluate = async (request, token) => { gateToken = token; await SpeechAsync.Delay(Timeout.InfiniteTimeSpan, token); return Pass(); }
            };
            var manager = new TurnEndGateManager(new[] { gate });
            Assert.That(await manager.ShouldEndTurnAsync(Request(), 1), Is.False);
            manager.Dispose();
            manager.Dispose();
            await manager.DrainAsync();
            Assert.That(gateToken.IsCancellationRequested, Is.True);
        }
    }
}

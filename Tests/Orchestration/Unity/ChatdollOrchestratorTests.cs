using ChatdollKit.SpeechPipeline.LLM;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.Avatar;
using ChatdollKit.Orchestration;
using ChatdollKit.SpeechListener;
using ChatdollKit.SpeechPipeline;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace ChatdollKit.Tests.Orchestration.Unity
{
    public class ChatdollOrchestratorTests
    {
        private GameObject gameObject;
        private ChatdollOrchestrator orchestrator;
        private readonly List<SpeechCompletionSource<ISpeechPipeline>> factories = new List<SpeechCompletionSource<ISpeechPipeline>>();
        private readonly List<AudioClip> microphoneClips = new List<AudioClip>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return new EnterPlayMode();
            gameObject = new GameObject("Orchestrator test");
            orchestrator = gameObject.AddComponent<ChatdollOrchestrator>();
            orchestrator.ConfigureAvatar(new FakeAvatar());
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                foreach (var factory in factories) factory.TrySetResult(new FakePipeline());
                factories.Clear();
                if (orchestrator != null) yield return Wait(orchestrator.StopAsync());
            }
            finally
            {
                if (gameObject != null) UnityEngine.Object.Destroy(gameObject);
                foreach (var clip in microphoneClips)
                    if (clip != null) UnityEngine.Object.Destroy(clip);
                microphoneClips.Clear();
            }
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator ConfigurationDoesNotStartUntilExplicitStart()
        {
            var pipeline = new FakePipeline();
            orchestrator.ConfigurePipeline(pipeline);
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var hookThread = 0;
            orchestrator.BeforeRequestAsync = async (request, token) =>
            {
                await SpeechAsync.Yield();
                hookThread = Thread.CurrentThread.ManagedThreadId;
                request.Text = "configured " + request.Text;
            };
            yield return null;
            Assert.That(orchestrator.AutoStart, Is.False);
            Assert.That(orchestrator.IsRunning, Is.False);
            yield return Wait(orchestrator.StartAsync());
            yield return Wait(orchestrator.SendTextAsync("input"));
            Assert.That(pipeline.LastText, Is.EqualTo("configured input"));
            Assert.That(hookThread, Is.EqualTo(mainThread));
        }

        [UnityTest]
        public IEnumerator StoppingFromStartedObserverPreservesLifecycleOrderForLaterObservers()
        {
            orchestrator.ConfigurePipeline(new FakePipeline());
            var boundaries = new List<OrchestratorLifecycleKind>();
            UniTask? stop = null;
            orchestrator.LifecycleChanged += boundary =>
            {
                if (boundary.Kind == OrchestratorLifecycleKind.Started) stop = orchestrator.StopAsync();
            };
            orchestrator.LifecycleChanged += boundary => boundaries.Add(boundary.Kind);
            yield return Wait(orchestrator.StartAsync());
            Assert.That(boundaries, Is.EqualTo(new[] { OrchestratorLifecycleKind.Started, OrchestratorLifecycleKind.Stopped }),
                "A nested stop must reach later subscribers after their Started notification, without waiting for Update.");
            Assert.That(stop.HasValue, Is.True);
            yield return Wait(stop.Value);
            Assert.That(orchestrator.IsRunning, Is.False);
            Assert.That(boundaries.Count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator LifecycleBoundariesReachMainThreadAndDisableDeliversStopImmediately()
        {
            var pipeline = new FakePipeline();
            orchestrator.ConfigurePipeline(pipeline);
            var boundaries = new List<OrchestratorLifecycleEvent>();
            var threads = new List<int>();
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            orchestrator.LifecycleChanged += boundary =>
            { boundaries.Add(boundary); threads.Add(Thread.CurrentThread.ManagedThreadId); };
            yield return Wait(orchestrator.StartAsync());
            var first = orchestrator.Orchestrator;
            Assert.That(boundaries.Count, Is.EqualTo(1));
            Assert.That(boundaries[0].Kind, Is.EqualTo(OrchestratorLifecycleKind.Started));
            yield return Wait(orchestrator.InterruptAsync());
            yield return Wait(orchestrator.DrainAsync());
            yield return null;
            Assert.That(boundaries[1].Kind, Is.EqualTo(OrchestratorLifecycleKind.Interrupted));
            Assert.That(boundaries[1].Generation, Is.EqualTo(1));
            orchestrator.enabled = false;
            Assert.That(boundaries.Count, Is.EqualTo(3), "OnDisable must publish stop without requiring another Update.");
            Assert.That(boundaries[2].Kind, Is.EqualTo(OrchestratorLifecycleKind.Stopped));
            Assert.That(boundaries[2].Source, Is.SameAs(first));
            yield return Wait(orchestrator.StopAsync());
            Assert.That(boundaries.Count, Is.EqualTo(3));
            orchestrator.enabled = true;
            yield return Wait(orchestrator.StartAsync());
            Assert.That(boundaries.Count, Is.EqualTo(4));
            Assert.That(boundaries[3].Kind, Is.EqualTo(OrchestratorLifecycleKind.Started));
            Assert.That(boundaries[3].Source, Is.Not.SameAs(first));
            Assert.That(threads, Is.All.EqualTo(mainThread));
        }

        [UnityTest]
        public IEnumerator TurnEndIsForwardedAfterFinalOnMainThreadAndObserversAreIsolated()
        {
            var pipeline = new FakePipeline();
            orchestrator.ConfigurePipeline(pipeline);
            var order = new List<string>();
            var threads = new List<int>();
            var errors = new List<Exception>();
            var failure = new InvalidOperationException("observer failed");
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            orchestrator.Error += errors.Add;
            orchestrator.LifecycleChanged += boundary => throw failure;
            orchestrator.LifecycleChanged += boundary => order.Add(boundary.Kind.ToString());
            orchestrator.ResponseReceived += response =>
            { if (response.Type == SpeechPipelineResponseType.Final) order.Add("Final"); };
            orchestrator.ResponseObserved += observed =>
            {
                if (observed.Response.Type != SpeechPipelineResponseType.Final) return;
                Assert.That(observed.Source, Is.SameAs(orchestrator.Orchestrator));
                Assert.That(observed.Generation, Is.Zero);
                order.Add("Observed"); threads.Add(Thread.CurrentThread.ManagedThreadId);
            };
            orchestrator.TurnEnded += result => throw failure;
            orchestrator.TurnEnded += result =>
            {
                Assert.That(result.Reason, Is.EqualTo(OrchestratorTurnEndReason.Completed));
                Assert.That(result.Source, Is.SameAs(orchestrator.Orchestrator));
                order.Add("Ended"); threads.Add(Thread.CurrentThread.ManagedThreadId);
            };
            yield return Wait(orchestrator.StartAsync());
            yield return Wait(Background(async () =>
            {
                foreach (var type in new[] { SpeechPipelineResponseType.Accepted, SpeechPipelineResponseType.Start, SpeechPipelineResponseType.Final })
                    await pipeline.EmitAsync(new SpeechPipelineResponse
                    { Type = type, SessionId = "default", TransactionId = "text-only" });
            }));
            yield return Wait(orchestrator.DrainAsync());
            yield return null;
            Assert.That(order, Is.EqualTo(new[] { "Started", "Final", "Observed", "Ended" }));
            Assert.That(threads, Is.EqualTo(new[] { mainThread, mainThread }));
            Assert.That(errors, Is.EqualTo(new[] { failure, failure }));
        }

        [UnityTest]
        public IEnumerator BorrowedPipelineCanBeReusedAfterStop()
        {
            var pipeline = new FakePipeline();
            orchestrator.ConfigurePipeline(pipeline);
            yield return Wait(orchestrator.StartAsync());
            yield return Wait(orchestrator.StopAsync());
            Assert.That(pipeline.DisposeCount, Is.Zero);
            Assert.That(pipeline.InterruptCount, Is.EqualTo(1));
            yield return Wait(orchestrator.StartAsync());
            Assert.That(orchestrator.Pipeline, Is.SameAs(pipeline));
            yield return Wait(orchestrator.StopAsync());
            Assert.That(pipeline.DisposeCount, Is.Zero);
            Assert.That(pipeline.InterruptCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator UiBargeInToggleImmediatelyControlsMicrophoneDuringGenerationWithoutRestarting()
        {
            var microphone = ConfigureManualMicrophone();
            var pipeline = new FakePipeline();
            var factoryCalls = 0;
            orchestrator.ConfigurePipelineFactory(token =>
            {
                factoryCalls++;
                return UniTask.FromResult<ISpeechPipeline>(pipeline);
            }, ownsPipeline: false, samplesPerMessage: 2);
            yield return Wait(orchestrator.StartAsync());
            var core = orchestrator.Orchestrator;
            yield return Wait(pipeline.EmitAsync(new SpeechPipelineResponse
            { Type = SpeechPipelineResponseType.Accepted, SessionId = "default", TransactionId = "generating" }));

            var toggle = new UnityEvent<bool>();
            toggle.AddListener(orchestrator.SetAllowBargeIn);
            // Other Inspector edits remain pending until restart, even when they are temporarily invalid.
            orchestrator.Options.MaxPendingAudioFrames = 0;
            orchestrator.Options.MaxPendingPresentations = -1;
            toggle.Invoke(false);
            Assert.That(core.IsInputSuppressed, Is.True, "The UI callback must take effect before the next Update.");
            Assert.That(orchestrator.AllowBargeIn, Is.False);
            Assert.That(orchestrator.Options.AllowBargeIn, Is.False);
            RaiseSamples(microphone, new[] { 0.25f, 0.5f });
            yield return Wait(orchestrator.DrainAsync());
            Assert.That(pipeline.Audio.Count, Is.Zero);

            toggle.Invoke(true);
            Assert.That(core.IsInputSuppressed, Is.False);
            Assert.That(orchestrator.Options.AllowBargeIn, Is.True);
            RaiseSamples(microphone, new[] { -0.25f, -0.5f });
            yield return Wait(orchestrator.DrainAsync());
            Assert.That(pipeline.Audio.Count, Is.EqualTo(1));
            CollectionAssert.AreEqual(new byte[] { 0, 224, 0, 192 }, pipeline.Audio[0]);
            Assert.That(core.GetOptions().MaxPendingAudioFrames, Is.EqualTo(100));
            Assert.That(core.GetOptions().MaxPendingPresentations, Is.EqualTo(100));
            Assert.That(orchestrator.Orchestrator, Is.SameAs(core));
            Assert.That(orchestrator.Pipeline, Is.SameAs(pipeline));
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(pipeline.InterruptCount, Is.Zero);
            Assert.That(pipeline.DisposeCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator BargeInPropertyCanChangeWhileAvatarPlaybackContinuesAfterFinal()
        {
            var pipeline = new FakePipeline();
            var avatar = new HeldAvatar();
            orchestrator.ConfigurePipeline(pipeline);
            orchestrator.ConfigureAvatar(avatar);
            yield return Wait(orchestrator.StartAsync());
            yield return Wait(pipeline.EmitAsync(new SpeechPipelineResponse
            { Type = SpeechPipelineResponseType.Accepted, SessionId = "default", TransactionId = "playing" }));
            yield return Wait(pipeline.EmitAsync(new SpeechPipelineResponse
            { Type = SpeechPipelineResponseType.Start, SessionId = "default", TransactionId = "playing" }));
            yield return Wait(pipeline.EmitAsync(new SpeechPipelineResponse
            {
                Type = SpeechPipelineResponseType.Chunk, SessionId = "default", TransactionId = "playing",
                Text = "Still speaking", VoiceText = "Still speaking"
            }));
            yield return Wait(avatar.Entered.Task);
            yield return Wait(pipeline.EmitAsync(new SpeechPipelineResponse
            { Type = SpeechPipelineResponseType.Final, SessionId = "default", TransactionId = "playing" }));

            orchestrator.AllowBargeIn = false;
            Assert.That(orchestrator.Orchestrator.IsInputSuppressed, Is.True);
            Assert.That(orchestrator.Orchestrator.IsPresenting, Is.True);
            orchestrator.AllowBargeIn = true;
            Assert.That(orchestrator.Orchestrator.IsInputSuppressed, Is.False);
            Assert.That(orchestrator.Orchestrator.IsPresenting, Is.True);
            orchestrator.AllowBargeIn = false;
            avatar.Release.TrySetResult(true);
            yield return Wait(orchestrator.DrainAsync());
            Assert.That(orchestrator.Orchestrator.IsInputSuppressed, Is.False);
            Assert.That(orchestrator.AllowBargeIn, Is.False, "Completing playback must not change the chosen policy.");
        }

        [UnityTest]
        public IEnumerator UiAndInspectorBargeInChangesDuringInterruptionApplyToTheNextTurn()
        {
            var entered = new SpeechCompletionSource<bool>();
            var release = new SpeechCompletionSource<bool>();
            var pipeline = new FakePipeline
            {
                InterruptHandler = () => { entered.TrySetResult(true); return release.Task; }
            };
            orchestrator.ConfigurePipeline(pipeline);
            yield return Wait(orchestrator.StartAsync());
            var interruption = orchestrator.InterruptAsync();
            try
            {
                yield return Wait(entered.Task);
                Assert.DoesNotThrow(() => orchestrator.SetAllowBargeIn(false));
                Assert.That(orchestrator.Orchestrator.AllowBargeIn, Is.False);
                Assert.That(orchestrator.Orchestrator.IsInputSuppressed, Is.True);
                Assert.DoesNotThrow(() => orchestrator.SetAllowBargeIn(true));
                Assert.That(orchestrator.Orchestrator.AllowBargeIn, Is.True);
                Assert.That(orchestrator.Orchestrator.IsInputSuppressed, Is.True,
                    "Enabling barge-in must not release input while interruption is still running.");

                var serialized = new SerializedObject(orchestrator);
                serialized.FindProperty("Options.MuteMicrophoneDuringResponse").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                yield return null;
                yield return null;
                Assert.That(orchestrator.Orchestrator.AllowBargeIn, Is.False);
                Assert.That(orchestrator.Orchestrator.IsInputSuppressed, Is.True);
                Assert.That(interruption.Status.IsCompleted(), Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                pipeline.InterruptHandler = null;
            }

            yield return Wait(interruption);
            Assert.That(orchestrator.AllowBargeIn, Is.False);
            Assert.That(orchestrator.Orchestrator.IsInputSuppressed, Is.False,
                "Input resumes after interruption while there is no response in progress.");
            yield return Wait(pipeline.EmitAsync(new SpeechPipelineResponse
            { Type = SpeechPipelineResponseType.Accepted, SessionId = "default", TransactionId = "after-interruption" }));
            Assert.That(orchestrator.Orchestrator.IsInputSuppressed, Is.True,
                "The next turn must use the policy chosen during interruption.");
        }

        [UnityTest]
        public IEnumerator BargeInChangedDuringAsyncStartupAppliesBeforeStartCompletes()
        {
            var entered = new SpeechCompletionSource<bool>();
            var result = new SpeechCompletionSource<ISpeechPipeline>();
            factories.Add(result);
            orchestrator.AllowBargeIn = true;
            orchestrator.ConfigurePipelineFactory(token => { entered.TrySetResult(true); return result.Task; });
            var starting = orchestrator.StartAsync();
            yield return Wait(entered.Task);
            orchestrator.SetAllowBargeIn(false);

            // Observe completion synchronously so a later Update cannot hide a stale startup snapshot.
            var policyAtCompletion = SpeechAsync.Share(CaptureBargeInAfterStartAsync(starting));
            result.SetResult(new FakePipeline());
            yield return Wait(policyAtCompletion);
            Assert.That(policyAtCompletion.GetAwaiter().GetResult(), Is.False);
            Assert.That(orchestrator.AllowBargeIn, Is.False);
            Assert.That(orchestrator.Options.AllowBargeIn, Is.False);
        }

        [UnityTest]
        public IEnumerator BargeInChoicesBeforeStartAndAfterStopAreRetainedAcrossRuns()
        {
            var pipeline = new FakePipeline();
            orchestrator.ConfigurePipeline(pipeline);
            orchestrator.AllowBargeIn = false;
            yield return Wait(orchestrator.StartAsync());
            Assert.That(orchestrator.Orchestrator.AllowBargeIn, Is.False);
            yield return Wait(orchestrator.StopAsync());
            Assert.That(orchestrator.AllowBargeIn, Is.False);
            orchestrator.SetAllowBargeIn(true);
            yield return Wait(orchestrator.StartAsync());
            Assert.That(orchestrator.Orchestrator.AllowBargeIn, Is.True);
            orchestrator.AllowBargeIn = false;
            yield return Wait(orchestrator.RestartAsync());
            Assert.That(orchestrator.Orchestrator.AllowBargeIn, Is.False);
            Assert.That(orchestrator.Pipeline, Is.SameAs(pipeline));
            Assert.That(pipeline.DisposeCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator InspectorBargeInEditSynchronizesOnUpdateWithoutApplyingPendingQueueEdits()
        {
            var pipeline = new FakePipeline();
            orchestrator.Options.MaxPendingAudioFrames = 7;
            orchestrator.Options.MaxPendingPresentations = 9;
            orchestrator.ConfigurePipeline(pipeline);
            yield return Wait(orchestrator.StartAsync());
            var core = orchestrator.Orchestrator;
            yield return Wait(pipeline.EmitAsync(new SpeechPipelineResponse
            { Type = SpeechPipelineResponseType.Accepted, SessionId = "default", TransactionId = "inspector" }));

            var serialized = new SerializedObject(orchestrator);
            serialized.FindProperty("Options.MuteMicrophoneDuringResponse").boolValue = true;
            serialized.FindProperty("Options.MaxPendingAudioFrames").intValue = 0;
            serialized.FindProperty("Options.MaxPendingPresentations").intValue = -1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            yield return null;
            Assert.That(core.IsInputSuppressed, Is.True);
            Assert.That(core.AllowBargeIn, Is.False);
            Assert.That(core.GetOptions().MaxPendingAudioFrames, Is.EqualTo(7));
            Assert.That(core.GetOptions().MaxPendingPresentations, Is.EqualTo(9));

            serialized.Update();
            serialized.FindProperty("Options.MuteMicrophoneDuringResponse").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            yield return null;
            Assert.That(core.IsInputSuppressed, Is.False);
            Assert.That(core.AllowBargeIn, Is.True);
            Assert.That(orchestrator.Orchestrator, Is.SameAs(core));
            Assert.That(orchestrator.Pipeline, Is.SameAs(pipeline));
            Assert.That(pipeline.InterruptCount, Is.Zero);
            Assert.That(pipeline.DisposeCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FactoryCreatesAndDisposesOneOwnedPipelinePerRun()
        {
            var pipelines = new List<FakePipeline>();
            orchestrator.ConfigurePipelineFactory(token =>
            {
                var pipeline = new FakePipeline(); pipelines.Add(pipeline);
                return UniTask.FromResult<ISpeechPipeline>(pipeline);
            });
            yield return Wait(orchestrator.StartAsync());
            yield return Wait(orchestrator.StartAsync());
            Assert.That(pipelines.Count, Is.EqualTo(1));
            yield return Wait(orchestrator.StopAsync());
            yield return Wait(orchestrator.StopAsync());
            Assert.That(pipelines[0].DisposeCount, Is.EqualTo(1));
            yield return Wait(orchestrator.StartAsync());
            Assert.That(pipelines.Count, Is.EqualTo(2));
            yield return Wait(orchestrator.StopAsync());
            Assert.That(pipelines[1].DisposeCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OwnedPipelineDisposalCallbackCannotAwaitItsOrchestratorStop()
            => VerifyShutdownCallbackReentry(true);

        [UnityTest]
        public IEnumerator BorrowedPipelineInterruptionCallbackCannotAwaitItsOrchestratorStop()
            => VerifyShutdownCallbackReentry(false);

        private IEnumerator VerifyShutdownCallbackReentry(bool ownsPipeline)
        {
            var pipeline = new FakePipeline();
            Exception reentryFailure = null;
            Func<UniTask> callback = async () =>
            {
                try { await orchestrator.StopAsync(); }
                catch (Exception error) { reentryFailure = error; }
            };
            if (ownsPipeline) pipeline.DisposeHandler = callback;
            else pipeline.InterruptHandler = callback;
            orchestrator.ConfigurePipeline(pipeline, ownsPipeline);
            yield return Wait(orchestrator.StartAsync());
            yield return Wait(orchestrator.StopAsync());
            Assert.That(reentryFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(orchestrator.IsRunning, Is.False);
            Assert.That(ownsPipeline ? pipeline.DisposeCount : pipeline.InterruptCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator StopCancelsStartupAndDisposesALateFactoryResult()
        {
            var entered = new SpeechCompletionSource<bool>();
            var result = new SpeechCompletionSource<ISpeechPipeline>();
            factories.Add(result);
            CancellationToken factoryToken = default;
            orchestrator.ConfigurePipelineFactory(token => { factoryToken = token; entered.TrySetResult(true); return result.Task; });
            var starting = orchestrator.StartAsync();
            yield return Wait(entered.Task);
            var stopping = orchestrator.StopAsync();
            Assert.That(factoryToken.IsCancellationRequested, Is.True);
            Assert.That(stopping.Status.IsCompleted(), Is.False);
            var late = new FakePipeline();
            result.SetResult(late);
            yield return Wait(stopping);
            Assert.That(starting.Status.IsCanceled(), Is.True);
            Assert.That(orchestrator.IsRunning, Is.False);
            Assert.That(late.DisposeCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator LifecycleReentryFromFactoryFailsPromptly()
        {
            Exception failure = null;
            orchestrator.ConfigurePipelineFactory(async token =>
            {
                try { await orchestrator.StopAsync(); } catch (Exception error) { failure = error; }
                return new FakePipeline();
            });
            yield return Wait(orchestrator.StartAsync());
            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            yield return Wait(orchestrator.StopAsync());
        }

        [UnityTest]
        public IEnumerator PipelineNotificationsAreForwardedOnUnityMainThread()
        {
            var pipeline = new FakePipeline();
            orchestrator.ConfigurePipeline(pipeline);
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var delivered = new SpeechCompletionSource<int>();
            orchestrator.ResponseReceived += response => delivered.TrySetResult(Thread.CurrentThread.ManagedThreadId);
            yield return Wait(orchestrator.StartAsync());
            yield return Wait(Background(() => pipeline.EmitAsync(new SpeechPipelineResponse
            { Type = SpeechPipelineResponseType.Accepted, TransactionId = "notification", SessionId = "default" })));
            yield return Wait(delivered.Task);
            Assert.That(delivered.Task.GetAwaiter().GetResult(), Is.EqualTo(mainThread));
        }

        [UnityTest]
        public IEnumerator MainThreadSubscribersReceiveIndependentRequestsAndRawResponses()
        {
            var pipeline = new FakePipeline();
            orchestrator.ConfigurePipeline(pipeline);
            var started = new SpeechCompletionSource<AvatarRequest>();
            var completed = new SpeechCompletionSource<AvatarRequest>();
            var received = new SpeechCompletionSource<SpeechPipelineResponse>();
            Action<AvatarRequest> mutate = request =>
            {
                request.Text = "changed"; request.VoiceText = "changed"; request.TransactionId = "changed";
                request.AudioData[0] = 99; request.Controls = Array.Empty<AvatarControl>();
            };
            orchestrator.PresentationStarted += mutate;
            orchestrator.PresentationStarted += request => started.TrySetResult(request);
            orchestrator.PresentationCompleted += mutate;
            orchestrator.PresentationCompleted += request => completed.TrySetResult(request);
            orchestrator.ResponseReceived += response =>
            {
                if (response.Type != SpeechPipelineResponseType.Chunk) return;
                response.Text = "changed"; response.AudioData[0] = 99;
                response.Metadata["nested"]["value"] = 99;
            };
            orchestrator.ResponseReceived += response =>
            { if (response.Type == SpeechPipelineResponseType.Chunk) received.TrySetResult(response); };
            yield return Wait(orchestrator.StartAsync());
            yield return Wait(pipeline.EmitAsync(new SpeechPipelineResponse
            { Type = SpeechPipelineResponseType.Accepted, SessionId = "default", TransactionId = "turn" }));
            yield return Wait(pipeline.EmitAsync(new SpeechPipelineResponse
            { Type = SpeechPipelineResponseType.Start, SessionId = "default", TransactionId = "turn" }));
            yield return Wait(pipeline.EmitAsync(new SpeechPipelineResponse
            {
                Type = SpeechPipelineResponseType.Chunk, SessionId = "default", TransactionId = "turn",
                Text = "[face:smile]Original", VoiceText = "Chosen pronunciation", AudioData = new byte[] { 1, 2 },
                Metadata = Newtonsoft.Json.Linq.JObject.Parse("{\"nested\":{\"value\":1}}")
            }));
            yield return Wait(pipeline.EmitAsync(new SpeechPipelineResponse
            { Type = SpeechPipelineResponseType.Final, SessionId = "default", TransactionId = "turn" }));
            yield return Wait(orchestrator.DrainAsync());
            yield return Wait(UniTask.WhenAll(started.Task, completed.Task, received.Task));

            foreach (var request in new[] { started.Task.GetAwaiter().GetResult(), completed.Task.GetAwaiter().GetResult() })
            {
                Assert.That(request.TransactionId, Is.EqualTo("turn"));
                Assert.That(request.Text, Is.EqualTo("[face:smile]Original"));
                Assert.That(request.VoiceText, Is.EqualTo("Chosen pronunciation"));
                Assert.That(request.AudioData[0], Is.EqualTo(1));
                Assert.That(request.Controls.Count, Is.EqualTo(1));
                Assert.That(request.Controls[0].Name, Is.EqualTo("smile"));
            }
            Assert.That(received.Task.GetAwaiter().GetResult().Text, Is.EqualTo("[face:smile]Original"));
            Assert.That(received.Task.GetAwaiter().GetResult().AudioData[0], Is.EqualTo(1));
            Assert.That((int)received.Task.GetAwaiter().GetResult().Metadata["nested"]["value"], Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DisablingCancelsAQueuedHookWithoutRequiringOrchestratorUpdate()
        {
            var pipeline = new FakePipeline();
            var calls = 0;
            orchestrator.ConfigurePipeline(pipeline);
            orchestrator.BeforeRequestAsync = (request, token) => { calls++; return UniTask.CompletedTask; };
            yield return Wait(orchestrator.StartAsync());
            var dispatch = orchestrator.Orchestrator.BeforeRequestAsync;
            var queued = new SpeechCompletionSource<bool>();
            orchestrator.Orchestrator.BeforeRequestAsync = (request, token) =>
            {
                var pending = dispatch(request, token);
                // Disable after enqueueing and before the Behaviour's Update can dispatch the Unity hook.
                orchestrator.enabled = false;
                queued.TrySetResult(true);
                return pending;
            };
            var invocation = orchestrator.SendTextAsync("queued hook");
            yield return Wait(queued.Task);
            yield return Wait(orchestrator.StopAsync());
            Assert.That(invocation.Status.IsCanceled(), Is.True);
            Assert.That(calls, Is.Zero);
            Assert.That(pipeline.LastText, Is.Null);
        }

        [UnityTest]
        public IEnumerator RequestHookCannotReenterOrchestratorOrCoreControl()
        {
            Exception orchestratorFailure = null, coreFailure = null;
            orchestrator.ConfigurePipeline(new FakePipeline());
            orchestrator.BeforeRequestAsync = async (request, token) =>
            {
                try { await orchestrator.StopAsync(); } catch (Exception error) { orchestratorFailure = error; }
                try { await orchestrator.Orchestrator.ResetAsync(); } catch (Exception error) { coreFailure = error; }
            };
            yield return Wait(orchestrator.StartAsync());
            yield return Wait(orchestrator.SendTextAsync("hook"));
            Assert.That(orchestratorFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(coreFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(orchestrator.IsRunning, Is.True);
        }

        [UnityTest]
        public IEnumerator ManualCaptureHasAProviderBeforeTheFirstUpdate()
        {
            var microphone = gameObject.AddComponent<MicrophoneManager>();
            microphone.AutoStart = false;
            microphone.enabled = false;
            orchestrator.Microphone = microphone;
            orchestrator.ConfigurePipeline(new FakePipeline());
            var starting = orchestrator.StartAsync();
            Assert.That(microphone.MicrophoneProvider, Is.TypeOf<UnityMicrophoneProvider>());
            yield return Wait(starting);
        }

        [UnityTest]
        public IEnumerator RequestHookCanDisableItsOrchestratorWithoutPreventingAutomaticCleanup()
        {
            var pipeline = new FakePipeline();
            orchestrator.ConfigurePipeline(pipeline);
            orchestrator.BeforeRequestAsync = (request, token) => { orchestrator.enabled = false; return UniTask.CompletedTask; };
            yield return Wait(orchestrator.StartAsync());
            var invocation = orchestrator.SendTextAsync("disable");
            yield return Wait(IgnoreFailureAsync(invocation));
            Assert.That(invocation.Status.IsCanceled(), Is.True);
            yield return Wait(orchestrator.StopAsync());
            Assert.That(orchestrator.IsRunning, Is.False);
            Assert.That(pipeline.InterruptCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OrchestratorDoesNotStartOrStopTheApplicationMicrophoneProvider()
        {
            var microphone = gameObject.AddComponent<MicrophoneManager>();
            microphone.AutoStart = false;
            microphone.enabled = false;
            var provider = new FakeMicrophoneProvider(microphoneClips);
            microphone.MicrophoneProvider = provider;
            orchestrator.Microphone = microphone;
            orchestrator.ConfigurePipeline(new FakePipeline());
            yield return Wait(orchestrator.StartAsync());
            yield return Wait(orchestrator.StopAsync());
            Assert.That(microphone.MicrophoneProvider, Is.SameAs(provider));
            Assert.That(provider.StartCount, Is.Zero);
            Assert.That(provider.EndCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ManualCaptureContinuesAcrossOrchestratorStopAndRestart()
        {
            var microphone = ConfigureManualMicrophone(startCapture: false);
            var provider = (FakeMicrophoneProvider)microphone.MicrophoneProvider;
            var pipeline = new FakePipeline();
            orchestrator.ConfigurePipeline(pipeline, samplesPerMessage: 2);
            microphone.StartMicrophone();

            yield return Wait(orchestrator.StartAsync());
            RaiseSamples(microphone, new[] { 0.25f, 0.5f });
            yield return Wait(orchestrator.DrainAsync());
            Assert.That(pipeline.Audio.Count, Is.EqualTo(1));
            yield return Wait(orchestrator.StopAsync());
            Assert.That(provider.StartCount, Is.EqualTo(1));
            Assert.That(provider.EndCount, Is.Zero);
            Assert.That(provider.IsRecording(microphone.MicrophoneDevice), Is.True);
            RaiseSamples(microphone, new[] { 1f, 1f });
            yield return null;
            Assert.That(pipeline.Audio.Count, Is.EqualTo(1));

            yield return Wait(orchestrator.RestartAsync());
            RaiseSamples(microphone, new[] { -0.25f, -0.5f });
            yield return Wait(orchestrator.DrainAsync());
            Assert.That(pipeline.Audio.Count, Is.EqualTo(2));
            yield return Wait(orchestrator.StopAsync());
            Assert.That(provider.StartCount, Is.EqualTo(1));
            Assert.That(provider.EndCount, Is.Zero);
            Assert.That(provider.IsRecording(microphone.MicrophoneDevice), Is.True);

            microphone.StopMicrophone();
            Assert.That(provider.EndCount, Is.EqualTo(1));
            Assert.That(provider.IsRecording(microphone.MicrophoneDevice), Is.False);
        }

        [UnityTest]
        public IEnumerator OrchestratorAcceptsMicrophoneAutoStartWithoutControllingCapture()
        {
            var microphone = ConfigureManualMicrophone(startCapture: false);
            microphone.AutoStart = true;
            var provider = (FakeMicrophoneProvider)microphone.MicrophoneProvider;
            orchestrator.ConfigurePipeline(new FakePipeline());

            // The microphone stays disabled so its own Start method cannot run in this test.
            yield return Wait(orchestrator.StartAsync());
            Assert.That(orchestrator.IsRunning, Is.True);
            Assert.That(microphone.AutoStart, Is.True);
            yield return Wait(orchestrator.StopAsync());
            Assert.That(provider.StartCount, Is.Zero);
            Assert.That(provider.EndCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator MicrophoneSubscriptionConvertsPcmAndDetachesOnStop()
        {
            var microphone = ConfigureManualMicrophone();
            var pipeline = new FakePipeline();
            orchestrator.ConfigurePipeline(pipeline, samplesPerMessage: 2);
            yield return Wait(orchestrator.StartAsync());
            RaiseSamples(microphone, new[] { 0.5f, -0.5f });
            yield return Wait(orchestrator.DrainAsync());
            Assert.That(pipeline.Audio.Count, Is.EqualTo(1));
            CollectionAssert.AreEqual(new byte[] { 0, 64, 0, 192 }, pipeline.Audio[0]);
            yield return Wait(orchestrator.StopAsync());
            RaiseSamples(microphone, new[] { 1f, 1f });
            yield return null;
            Assert.That(pipeline.Audio.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator StereoCaptureStartedAfterOrchestratorUsesTheClipChannelCount()
        {
            var microphone = ConfigureManualMicrophone(startCapture: false, channels: 2);
            var provider = (FakeMicrophoneProvider)microphone.MicrophoneProvider;
            var pipeline = new FakePipeline();
            orchestrator.ConfigurePipeline(pipeline, samplesPerMessage: 2);
            Assert.That(microphone.Channels, Is.Zero);

            yield return Wait(orchestrator.StartAsync());
            Assert.That(orchestrator.IsRunning, Is.True);
            Assert.That(provider.StartCount, Is.Zero);
            microphone.StartMicrophone();
            Assert.That(microphone.Channels, Is.EqualTo(2));
            FeedMicrophoneClip(microphone, new[] { 0.75f, -0.25f, -0.25f, -0.75f });
            yield return Wait(orchestrator.DrainAsync());

            Assert.That(pipeline.Audio.Count, Is.EqualTo(1));
            CollectionAssert.AreEqual(new byte[] { 0, 32, 0, 192 }, pipeline.Audio[0]);
            microphone.StopMicrophone();
            Assert.That(microphone.Channels, Is.Zero);
            yield return Wait(orchestrator.StopAsync());
            Assert.That(provider.StartCount, Is.EqualTo(1));
            Assert.That(provider.EndCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ChangingCaptureChannelsStopsTheOrchestratorBeforeSendingMisinterpretedAudio()
        {
            var microphone = ConfigureManualMicrophone();
            var provider = (FakeMicrophoneProvider)microphone.MicrophoneProvider;
            var pipeline = new FakePipeline();
            Exception failure = null;
            orchestrator.Error += error => failure = error;
            orchestrator.ConfigurePipeline(pipeline, samplesPerMessage: 2);
            yield return Wait(orchestrator.StartAsync());
            FeedMicrophoneClip(microphone, new[] { 0.25f, 0.5f });
            yield return Wait(orchestrator.DrainAsync());
            Assert.That(pipeline.Audio.Count, Is.EqualTo(1));

            microphone.StopMicrophone();
            provider.Channels = 2;
            microphone.StartMicrophone();
            FeedMicrophoneClip(microphone, new[] { 0.5f, 0.5f, -0.5f, -0.5f });
            Assert.That(orchestrator.IsRunning, Is.False, "The format error must stop accepting audio automatically.");
            yield return Wait(orchestrator.StopAsync());

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(orchestrator.IsRunning, Is.False);
            Assert.That(pipeline.Audio.Count, Is.EqualTo(1));
            Assert.That(provider.StartCount, Is.EqualTo(2));
            Assert.That(provider.EndCount, Is.EqualTo(1));
            Assert.That(provider.IsRecording(microphone.MicrophoneDevice), Is.True);
        }

        [UnityTest]
        public IEnumerator InjectedPipelineAudioFormatDrivesMicrophoneConversion()
        {
            var microphone = ConfigureManualMicrophone();
            var pipeline = new FakePipeline();
            orchestrator.ConfigurePipeline(pipeline, inputSampleRate: 8000, samplesPerMessage: 2);
            yield return Wait(orchestrator.StartAsync());
            yield return VerifyDownsampledMessages(microphone, pipeline);
        }

        [UnityTest]
        public IEnumerator InjectedFactoryAudioFormatDrivesMicrophoneConversion()
        {
            var microphone = ConfigureManualMicrophone();
            var pipeline = new FakePipeline();
            orchestrator.ConfigurePipelineFactory(token => UniTask.FromResult<ISpeechPipeline>(pipeline),
                inputSampleRate: 8000, samplesPerMessage: 2);
            yield return Wait(orchestrator.StartAsync());
            yield return VerifyDownsampledMessages(microphone, pipeline);
        }

        [UnityTest]
        public IEnumerator ComponentLeaseAudioFormatDrivesMicrophoneConversionAndOwnsItsPipeline()
        {
            var microphone = ConfigureManualMicrophone();
            var pipeline = new FakePipeline();
            var component = gameObject.AddComponent<TestPipelineComponent>();
            component.Lease = new SpeechPipelineLease(pipeline, inputSampleRate: 8000, samplesPerMessage: 2);
            orchestrator.PipelineComponent = component;

            yield return Wait(orchestrator.StartAsync());
            Assert.That(orchestrator.Pipeline, Is.SameAs(pipeline));
            yield return VerifyDownsampledMessages(microphone, pipeline);
            yield return Wait(orchestrator.StopAsync());
            yield return Wait(orchestrator.StopAsync());
            Assert.That(pipeline.DisposeCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator InvalidAudioConfigurationPreservesThePreviouslyConfiguredPipelineAndFormat()
        {
            var microphone = ConfigureManualMicrophone();
            var original = new FakePipeline();
            var replacement = new FakePipeline();
            var factoryCalls = 0;
            orchestrator.ConfigurePipeline(original, inputSampleRate: 8000, samplesPerMessage: 2);
            Func<CancellationToken, UniTask<ISpeechPipeline>> factory = token =>
            {
                factoryCalls++;
                return UniTask.FromResult<ISpeechPipeline>(replacement);
            };

            foreach (var invalid in new[] { 0, -1 })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => orchestrator.ConfigurePipeline(
                    replacement, ownsPipeline: true, inputSampleRate: invalid, samplesPerMessage: 4));
                Assert.Throws<ArgumentOutOfRangeException>(() => orchestrator.ConfigurePipeline(
                    replacement, ownsPipeline: true, inputSampleRate: 16000, samplesPerMessage: invalid));
                Assert.Throws<ArgumentOutOfRangeException>(() => orchestrator.ConfigurePipelineFactory(
                    factory, inputSampleRate: invalid, samplesPerMessage: 4));
                Assert.Throws<ArgumentOutOfRangeException>(() => orchestrator.ConfigurePipelineFactory(
                    factory, inputSampleRate: 16000, samplesPerMessage: invalid));
            }

            yield return Wait(orchestrator.StartAsync());
            Assert.That(orchestrator.Pipeline, Is.SameAs(original));
            Assert.That(factoryCalls, Is.Zero);
            yield return VerifyDownsampledMessages(microphone, original);
            yield return Wait(orchestrator.StopAsync());
            Assert.That(original.DisposeCount, Is.Zero, "Invalid configuration must not change ownership either.");
            Assert.That(replacement.DisposeCount, Is.Zero);
        }

        private MicrophoneManager ConfigureManualMicrophone(bool startCapture = true, int channels = 1)
        {
            var microphone = gameObject.AddComponent<MicrophoneManager>();
            microphone.AutoStart = false;
            microphone.enabled = false;
            microphone.SampleRate = 16000;
            microphone.MicrophoneProvider = new FakeMicrophoneProvider(microphoneClips) { Channels = channels };
            orchestrator.Microphone = microphone;
            if (startCapture) microphone.StartMicrophone();
            return microphone;
        }

        private static void FeedMicrophoneClip(MicrophoneManager microphone, float[] interleavedSamples)
        {
            var provider = (FakeMicrophoneProvider)microphone.MicrophoneProvider;
            Assert.That(provider.Clip.SetData(interleavedSamples, provider.Position), Is.True);
            provider.Position += interleavedSamples.Length / provider.Channels;
            // Exercise the real clip reader and OnSamplesReceived event while automatic updates stay disabled.
            typeof(MicrophoneManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(microphone, null);
        }

        private IEnumerator VerifyDownsampledMessages(MicrophoneManager microphone, FakePipeline pipeline)
        {
            // At 8 kHz, retain input frames 0 and 2. A two-sample message spans both callbacks.
            RaiseSamples(microphone, new[] { 0.25f });
            yield return Wait(orchestrator.DrainAsync());
            Assert.That(pipeline.Audio.Count, Is.Zero);
            RaiseSamples(microphone, new[] { 0.375f, -0.5f, 1f });
            yield return Wait(orchestrator.DrainAsync());
            Assert.That(pipeline.Audio.Count, Is.EqualTo(1));
            CollectionAssert.AreEqual(new byte[] { 0, 32, 0, 192 }, pipeline.Audio[0]);
        }

        private static void RaiseSamples(MicrophoneManager microphone, float[] samples) =>
            ((Action<float[]>)typeof(MicrophoneManager).GetField("OnSamplesReceived", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(microphone))?.Invoke(samples);

        private async UniTask<bool> CaptureBargeInAfterStartAsync(UniTask starting)
        {
            await starting;
            return orchestrator.Orchestrator.AllowBargeIn;
        }
        private static async UniTask IgnoreFailureAsync(UniTask operation)
        {
            try { await operation; } catch (Exception) { }
        }

        private static IEnumerator Wait(UniTask task)
        {
            var until = Time.realtimeSinceStartupAsDouble + 3;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartupAsDouble < until) yield return null;
            Assert.That(task.Status.IsCompleted(), Is.True, "Orchestrator operation timed out.");
            if (task.Status.IsFaulted()) task.GetAwaiter().GetResult();
            Assert.That(task.Status.IsCanceled(), Is.False, "Orchestrator operation was unexpectedly cancelled.");
        }

        private sealed class FakeAvatar : IAvatarController
        {
            public UniTask PresentAsync(AvatarRequest presentation, CancellationToken cancellationToken) => UniTask.CompletedTask;
            public UniTask StopAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        }

        private sealed class HeldAvatar : IAvatarController
        {
            public readonly SpeechCompletionSource<bool> Entered = new SpeechCompletionSource<bool>();
            public readonly SpeechCompletionSource<bool> Release = new SpeechCompletionSource<bool>();
            public async UniTask PresentAsync(AvatarRequest presentation, CancellationToken cancellationToken)
            {
                using (cancellationToken.Register(() => Release.TrySetCanceled()))
                {
                    Entered.TrySetResult(true);
                    await Release.Task;
                }
            }
            public UniTask StopAsync(CancellationToken cancellationToken = default)
            {
                Release.TrySetResult(true);
                return UniTask.CompletedTask;
            }
        }

        public sealed class TestPipelineComponent : SpeechPipelineComponent
        {
            public SpeechPipelineLease Lease;
            public override UniTask<SpeechPipelineLease> CreatePipelineAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.FromResult(Lease);
            }
        }

        private sealed class FakeMicrophoneProvider : IMicrophoneProvider
        {
            public int StartCount, EndCount;
            public int Channels = 1, Position;
            public AudioClip Clip;
            private readonly List<AudioClip> clips;
            public FakeMicrophoneProvider(List<AudioClip> clips) { this.clips = clips; }
            public string[] devices => new[] { "test microphone" };
            public bool IsRecording(string deviceName) => StartCount > EndCount;
            public AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency)
            {
                StartCount++;
                Position = 0;
                Clip = AudioClip.Create("Test microphone capture", frequency * lengthSec, Channels, frequency, false);
                clips.Add(Clip);
                return Clip;
            }
            public void End(string deviceName) { EndCount++; }
            public int GetPosition(string deviceName) => Position;
        }

        private sealed class FakePipeline : ISpeechPipeline
        {
            public string SessionId => "default";
            public int DisposeCount, InterruptCount;
            public Func<UniTask> InterruptHandler, DisposeHandler;
            public string LastText;
            public readonly List<byte[]> Audio = new List<byte[]>();
            public event Func<SpeechPipelineResponse, UniTask> ResponseReceived;
            public event Action<Exception> Error { add { } remove { } }
            public UniTask<SpeechPipelineResponse> InvokeAsync(SpeechPipelineRequest request, CancellationToken cancellationToken = default)
            {
                LastText = request.Text;
                return UniTask.FromResult(new SpeechPipelineResponse { Type = SpeechPipelineResponseType.Final, SessionId = SessionId, TransactionId = "result" });
            }
            public UniTask ProcessAudioSamplesAsync(byte[] samples, CancellationToken cancellationToken = default)
            { Audio.Add((byte[])samples.Clone()); return UniTask.CompletedTask; }
            public UniTask InterruptAsync(CancellationToken cancellationToken = default)
            { InterruptCount++; return InterruptHandler?.Invoke() ?? UniTask.CompletedTask; }
            public UniTask ResetAsync(string contextId = null, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask DrainAsync() => UniTask.CompletedTask;
            public UniTask DisposeAsync() { DisposeCount++; return DisposeHandler?.Invoke() ?? UniTask.CompletedTask; }
            public async UniTask EmitAsync(SpeechPipelineResponse response)
            {
                var handlers = ResponseReceived;
                if (handlers != null) foreach (Func<SpeechPipelineResponse, UniTask> handler in handlers.GetInvocationList()) await handler(response);
            }
        }
        private static UniTask Background(Func<UniTask> operation)
            => SpeechAsync.Share(SpeechAsync.FromTask(NUnitTask.Run(async () => await operation())));

    }
}

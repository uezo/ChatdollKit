using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ChatdollKit.Avatar;
using ChatdollKit.Orchestration;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.UI.MessageWindow;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Window = ChatdollKit.UI.MessageWindow.MessageWindow;

namespace ChatdollKit.Tests.Orchestration.Unity
{
    public class ConversationEventSourceTests
    {
        private GameObject root;
        private ChatdollOrchestrator orchestrator;
        private FakePipeline pipeline;
        private Window userWindow, assistantWindow;
        private readonly List<ChatdollOrchestrator> orchestrators = new List<ChatdollOrchestrator>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Message source test");
            orchestrator = root.AddComponent<ChatdollOrchestrator>();
            orchestrators.Add(orchestrator);
            pipeline = new FakePipeline();
            orchestrator.ConfigurePipeline(pipeline);
            orchestrator.ConfigureAvatar(new FakeAvatar());
            userWindow = CreateWindow("User", true, false);
            assistantWindow = CreateWindow("Assistant", false, true);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var instance in orchestrators)
                if (instance != null) yield return Wait(instance.StopAsync());
            orchestrators.Clear();
            if (root != null) UnityEngine.Object.Destroy(root);
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator NewUiAssemblyPreservesLegacyMembershipAndDependencyDirection()
        {
            const string uiAssembly = "ChatdollKit.UI.MessageWindow";
            Assert.That(typeof(ChatdollKit.Dialog.SimpleMessageWindow).Assembly.GetName().Name, Is.EqualTo("ChatdollKit"));
            Assert.That(typeof(ChatdollKit.UI.MessageWindowContainer).Assembly.GetName().Name, Is.EqualTo("ChatdollKit"));
            Assert.That(typeof(Window).Assembly.GetName().Name, Is.EqualTo(uiAssembly));
            Assert.That(Array.Exists(typeof(AvatarController).Assembly.GetReferencedAssemblies(), item => item.Name == uiAssembly), Is.False);
            Assert.That(Array.Exists(typeof(SpeechPipelineResponse).Assembly.GetReferencedAssemblies(), item => item.Name == uiAssembly), Is.False);
            Assert.That(Array.Exists(typeof(ChatdollOrchestrator).Assembly.GetReferencedAssemblies(), item => item.Name == uiAssembly), Is.False);
            Assert.That(Array.Exists(typeof(Window).Assembly.GetReferencedAssemblies(), item =>
                item.Name == "ChatdollKit.Orchestration" || item.Name == "ChatdollKit.Avatar" || item.Name == "ChatdollKit.SpeechPipeline"), Is.False);
            Assert.That(typeof(IConversationEventSource).IsAssignableFrom(typeof(ChatdollOrchestrator)), Is.True);
            const string eventsAssembly = "ChatdollKit.Orchestration.Events";
            Assert.That(typeof(IConversationEventSource).Assembly.GetName().Name, Is.EqualTo(eventsAssembly));
            Assert.That(Array.Exists(typeof(ChatdollOrchestrator).Assembly.GetReferencedAssemblies(), item => item.Name == eventsAssembly), Is.True);
            Assert.That(Array.Exists(typeof(Window).Assembly.GetReferencedAssemblies(), item => item.Name == eventsAssembly), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ConversationPrefabNeedsOnlySourceAndKeepsReceivingAfterClickToInterrupt()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ChatdollKit/Prefabs/Runtime/Orchestration/MessageWindow.prefab");
            Assert.That(prefab, Is.Not.Null);
            var instance = UnityEngine.Object.Instantiate(prefab, root.transform);
            var window = instance.GetComponent<Window>();
            var interruptButton = instance.GetComponentInChildren<Button>(true);
            Assert.That(window, Is.Not.Null);
            foreach (var component in instance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Assert.That(component, Is.Not.Null, "The prefab must have no missing scripts.");
                Assert.That(component.GetType().Name, Is.Not.EqualTo("MessageWindowPresenter"));
            }
            Assert.That(window.Source, Is.Null);
            Assert.That(interruptButton, Is.Not.Null);
            Assert.That(interruptButton.onClick.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(interruptButton.onClick.GetPersistentTarget(0), Is.SameAs(window));
            Assert.That(interruptButton.onClick.GetPersistentMethodName(0), Is.EqualTo(nameof(Window.Interrupt)));

            // This is the only connection a scene author needs to make on the prefab instance.
            window.Source = orchestrator;
            yield return Wait(orchestrator.StartAsync());
            yield return null;
            Assert.That(window.IsVisible, Is.False);
            Assert.That(window.ContentRoot.activeSelf, Is.False);
            Assert.That(window.isActiveAndEnabled, Is.True);

            yield return Wait(pipeline.EmitAsync(Start("prefab-first", "Question")));
            yield return Until(() => window.MessageText.text == "Question");
            Assert.That(window.SpeakerText.text, Is.EqualTo("User"));
            yield return Wait(pipeline.EmitAsync(Chunk("prefab-first", "First answer")));
            yield return Until(() => window.MessageText.text == "First answer");
            Assert.That(window.SpeakerText.text, Is.EqualTo("AI"));
            interruptButton.onClick.Invoke();
            yield return Until(() => pipeline.InterruptCount > 0 && !window.IsVisible);
            Assert.That(window.ContentRoot.activeSelf, Is.False);
            Assert.That(window.isActiveAndEnabled, Is.True);

            yield return EmitCompleteTurn(pipeline, orchestrator, "prefab-next", "Next question", "Next answer");
            yield return Until(() => window.MessageText.text == "Next answer");
            Assert.That(window.IsVisible, Is.True);
        }

        [UnityTest]
        public IEnumerator CustomAvatarOverrideUsesResponseFallbackEvenWithUnusedConcreteAvatarAssigned()
        {
            var unusedAvatar = root.AddComponent<AvatarController>();
            orchestrator.Avatar = unusedAvatar;
            var unusedStarts = 0;
            unusedAvatar.PresentationStarted += request => unusedStarts++;
            yield return Wait(orchestrator.StartAsync());
            yield return EmitCompleteTurn(pipeline, orchestrator, "overridden", "Question", "Caption through custom avatar");
            yield return Until(() => assistantWindow.MessageText.text == "Caption through custom avatar");
            Assert.That(unusedStarts, Is.Zero);
            Assert.That(orchestrator.CurrentConversation.DisplayAssistantUtterance.IsComplete, Is.True);
        }

        [UnityTest]
        public IEnumerator FallbackWithoutConcreteAvatarKeepsCumulativeCaptionUntilWholeTurnEnds()
        {
            var avatar = new HeldAvatar();
            orchestrator.ConfigureAvatar(avatar);
            assistantWindow.Options.AutoHideAssistant = true;
            assistantWindow.Options.AssistantHoldSeconds = 0f;
            yield return Wait(orchestrator.StartAsync());
            yield return Wait(pipeline.EmitAsync(Start("turn", "User question")));
            yield return Wait(pipeline.EmitAsync(Chunk("turn", "Hello ")));
            yield return Wait(avatar.Entered.Task);
            yield return Wait(pipeline.EmitAsync(Chunk("turn", "world")));
            yield return Wait(pipeline.EmitAsync(Final("turn", "Hello world")));
            yield return Until(() => assistantWindow.MessageText.text == "Hello world");
            var id = assistantWindow.MessageId;
            yield return null;
            Assert.That(orchestrator.Orchestrator.IsPresenting, Is.True);
            Assert.That(orchestrator.CurrentConversation.DisplayAssistantUtterance.IsComplete, Is.False);
            Assert.That(userWindow.MessageText.text, Is.EqualTo("User question"));
            Assert.That(assistantWindow.IsVisible, Is.True, "Final must not hide a caption while playback is active.");
            Assert.That(assistantWindow.MessageId, Is.EqualTo(id));

            avatar.Release.TrySetResult(true);
            yield return Wait(orchestrator.DrainAsync());
            yield return Until(() => !assistantWindow.IsVisible);
            Assert.That(userWindow.IsVisible, Is.True, "The two windows have separate lifetimes.");
        }

        [UnityTest]
        public IEnumerator InterruptAndResetClearCompletedButRetainedDisplays()
        {
            yield return Wait(orchestrator.StartAsync());
            yield return EmitCompleteTurn(pipeline, orchestrator, "first", "Question", "Answer");
            yield return Until(() => assistantWindow.MessageText.text == "Answer");
            yield return Wait(orchestrator.InterruptAsync());
            yield return Until(() => !userWindow.IsVisible && !assistantWindow.IsVisible);
            Assert.That(pipeline.InterruptCount, Is.GreaterThan(0));

            yield return EmitCompleteTurn(pipeline, orchestrator, "second", "Another question", "Another answer");
            yield return Until(() => assistantWindow.MessageText.text == "Another answer");
            yield return Wait(orchestrator.ResetAsync("fresh-context"));
            yield return Until(() => !userWindow.IsVisible && !assistantWindow.IsVisible);
            Assert.That(pipeline.LastResetContext, Is.EqualTo("fresh-context"));
        }

        [UnityTest]
        public IEnumerator DisabledWindowCatchesUpToCurrentConversationWhenReenabled()
        {
            yield return Wait(orchestrator.StartAsync());
            yield return EmitCompleteTurn(pipeline, orchestrator, "first", "Question", "Answer");
            yield return Until(() => assistantWindow.MessageText.text == "Answer");
            userWindow.enabled = assistantWindow.enabled = false;
            Assert.That(userWindow.IsVisible || assistantWindow.IsVisible, Is.False);
            yield return EmitCompleteTurn(pipeline, orchestrator, "disabled", "While hidden", "Latest answer");
            yield return null;
            Assert.That(userWindow.MessageText.text, Is.Empty);
            Assert.That(assistantWindow.MessageText.text, Is.Empty);
            userWindow.enabled = assistantWindow.enabled = true;
            Assert.That(userWindow.MessageText.text, Is.EqualTo("While hidden"));
            Assert.That(assistantWindow.MessageText.text, Is.EqualTo("Latest answer"));
        }

        [UnityTest]
        public IEnumerator SharedWindowRetainsPreviousCaptionWhenSourceFiltersInternalRequest()
        {
            var shared = CreateWindow("Shared", true, true);
            yield return Wait(orchestrator.StartAsync());
            yield return EmitCompleteTurn(pipeline, orchestrator, "shared", "Question", "Answer");
            yield return Until(() => shared.MessageText.text == "Answer");
            var displayedId = shared.MessageId;
            yield return Wait(pipeline.EmitAsync(Start("internal", "$Speak naturally")));
            yield return null;
            Assert.That(shared.MessageId, Is.EqualTo(displayedId));
            Assert.That(shared.MessageText.text, Is.EqualTo("Answer"));
            Assert.That(orchestrator.CurrentConversation.DisplayUserUtterance.Text, Is.EqualTo("Question"));
            yield return Wait(pipeline.EmitAsync(Chunk("internal", "Internal instruction response")));
            yield return Wait(pipeline.EmitAsync(Final("internal", "Internal instruction response")));
            yield return Wait(orchestrator.DrainAsync());
            yield return Until(() => shared.MessageText.text == "Internal instruction response");
            Assert.That(shared.SpeakerText.text, Is.EqualTo("AI"));
        }

        [UnityTest]
        public IEnumerator RebindingToAnotherRunningOrchestratorIgnoresOldSourceEvents()
        {
            yield return Wait(orchestrator.StartAsync());
            yield return EmitCompleteTurn(pipeline, orchestrator, "first", "Old question", "Old answer");
            yield return Until(() => assistantWindow.MessageText.text == "Old answer");
            var nextHost = new GameObject("Next orchestrator");
            nextHost.transform.SetParent(root.transform, false);
            var nextOrchestrator = nextHost.AddComponent<ChatdollOrchestrator>();
            orchestrators.Add(nextOrchestrator);
            var nextPipeline = new FakePipeline();
            nextOrchestrator.ConfigurePipeline(nextPipeline);
            nextOrchestrator.ConfigureAvatar(new FakeAvatar());
            yield return Wait(nextOrchestrator.StartAsync());
            userWindow.Source = assistantWindow.Source = nextOrchestrator;
            yield return Until(() => !assistantWindow.IsVisible);
            yield return EmitCompleteTurn(pipeline, orchestrator, "old-source", "Ignore user", "Ignore answer");
            yield return null;
            Assert.That(userWindow.IsVisible || assistantWindow.IsVisible, Is.False);
            yield return EmitCompleteTurn(nextPipeline, nextOrchestrator, "new-source", "New question", "New answer");
            yield return Until(() => assistantWindow.MessageText.text == "New answer");
            Assert.That(userWindow.MessageText.text, Is.EqualTo("New question"));
            yield return Wait(orchestrator.StopAsync());
            yield return null;
            Assert.That(assistantWindow.MessageText.text, Is.EqualTo("New answer"));
        }

        [UnityTest]
        public IEnumerator ConcreteAvatarUsesPreparedVoiceTextOnceAndDoesNotRegressToUserCaption()
        {
            var avatar = root.AddComponent<AvatarController>();
            orchestrator.Avatar = avatar;
            orchestrator.ConfigureAvatar(avatar);
            var shared = CreateWindow("Shared", true, true);
            var started = 0;
            avatar.PresentationStarted += request => started++;
            yield return Wait(orchestrator.StartAsync());
            yield return Wait(pipeline.EmitAsync(Start("prepared", "Question")));
            var chunk = Chunk("prepared", "Spoken caption");
            chunk.Text = "[face:Joy]Spoken caption";
            yield return Wait(pipeline.EmitAsync(chunk));
            yield return Wait(pipeline.EmitAsync(Final("prepared", "Spoken caption")));
            yield return Wait(orchestrator.DrainAsync());
            yield return Until(() => shared.MessageText.text == "Spoken caption");
            yield return null;
            Assert.That(started, Is.EqualTo(1));
            Assert.That(shared.MessageText.text, Is.EqualTo("Spoken caption"));
            Assert.That(shared.SpeakerText.text, Is.EqualTo("AI"));
        }

        [UnityTest]
        public IEnumerator CommonRecognitionUpdatesReplacePartialConfirmSameMessageAndClearOnCancelOrInterrupt()
        {
            var events = new List<ConversationEvent>();
            orchestrator.ConversationEventReceived += value => events.Add(value);
            yield return Wait(orchestrator.StartAsync());
            pipeline.Recognize("speech-1", null, SpeechRecognitionUpdateKind.Started);
            yield return Until(() => userWindow.IsVisible && userWindow.MessageText.text == "…");
            var id = userWindow.MessageId;
            var started = events.Single(value => value.Kind == ConversationEventKind.UserSpeechStarted);
            Assert.That(started.RecognitionId, Is.EqualTo("speech-1"));
            Assert.That(started.Utterance.Id, Is.EqualTo(id));
            Assert.That(started.Utterance.IsAwaitingRecognition, Is.True);
            Assert.That(started.Utterance.IsComplete, Is.False);
            Assert.That(started.Text, Is.Null.Or.Empty, "The UI prompt must not become recognized speech in the event stream.");
            Assert.That(started.Utterance.Text, Is.Null.Or.Empty);
            Assert.That(orchestrator.CurrentConversation.DisplayUserUtterance.IsAwaitingRecognition, Is.True);
            Assert.That(assistantWindow.IsVisible, Is.False);

            pipeline.Recognize("speech-1", "I would", SpeechRecognitionUpdateKind.Partial);
            yield return Until(() => userWindow.MessageText.text == "I would");
            Assert.That(userWindow.MessageId, Is.EqualTo(id));
            Assert.That(orchestrator.CurrentConversation.DisplayUserUtterance.IsPartial, Is.True);
            Assert.That(orchestrator.CurrentConversation.DisplayUserUtterance.IsAwaitingRecognition, Is.False);
            pipeline.Recognize("speech-1", "I would like tea", SpeechRecognitionUpdateKind.Partial);
            yield return Until(() => userWindow.MessageText.text == "I would like tea");
            Assert.That(userWindow.MessageId, Is.EqualTo(id));
            pipeline.Recognize("speech-1", "I would like tea", SpeechRecognitionUpdateKind.Confirmed, "recognition-turn");
            yield return Until(() => orchestrator.CurrentConversation.DisplayUserUtterance?.IsPartial == false);
            Assert.That(orchestrator.CurrentConversation.DisplayUserUtterance.IsAwaitingRecognition, Is.False);
            yield return Wait(pipeline.EmitAsync(Start("recognition-turn", "I would like tea")));
            yield return null;
            Assert.That(userWindow.MessageId, Is.EqualTo(id));
            var recognitionEvents = events.Where(value => value.RecognitionId == "speech-1" &&
                (value.Kind == ConversationEventKind.UserSpeechStarted || value.Kind == ConversationEventKind.UserSpeechPartial ||
                    value.Kind == ConversationEventKind.UserSpeechConfirmed)).ToArray();
            Assert.That(recognitionEvents.Select(value => value.Kind), Is.EqualTo(new[]
            {
                ConversationEventKind.UserSpeechStarted, ConversationEventKind.UserSpeechPartial,
                ConversationEventKind.UserSpeechPartial, ConversationEventKind.UserSpeechConfirmed
            }));
            Assert.That(recognitionEvents.All(value => value.Utterance.Id == id), Is.True);
            yield return Wait(orchestrator.InterruptAsync());
            yield return Until(() => !userWindow.IsVisible);

            pipeline.Recognize("speech-2", "Never mind", SpeechRecognitionUpdateKind.Partial);
            yield return Until(() => userWindow.MessageText.text == "Never mind");
            pipeline.Recognize("speech-2", null, SpeechRecognitionUpdateKind.Canceled);
            yield return Until(() => !userWindow.IsVisible);
        }

        [UnityTest]
        public IEnumerator CancelingSpeechBeforeAnyPartialClearsTheAwaitingRecognitionPrompt()
        {
            var events = new List<ConversationEvent>();
            orchestrator.ConversationEventReceived += value => events.Add(value);
            yield return Wait(orchestrator.StartAsync());
            pipeline.Recognize("canceled-before-partial", null, SpeechRecognitionUpdateKind.Started);
            yield return Until(() => userWindow.IsVisible && userWindow.MessageText.text == "…");
            var id = userWindow.MessageId;
            pipeline.Recognize("canceled-before-partial", null, SpeechRecognitionUpdateKind.Canceled);
            yield return Until(() => !userWindow.IsVisible);
            Assert.That(userWindow.MessageText.text, Is.Empty);
            Assert.That(orchestrator.CurrentConversation.DisplayUserUtterance, Is.Null);
            var recognitionEvents = events.Where(value => value.RecognitionId == "canceled-before-partial").ToArray();
            Assert.That(recognitionEvents.Select(value => value.Kind), Is.EqualTo(new[]
            { ConversationEventKind.UserSpeechStarted, ConversationEventKind.UserSpeechCanceled }));
            var canceled = recognitionEvents.Last();
            Assert.That(canceled.Utterance.Id, Is.EqualTo(id));
            Assert.That(canceled.Utterance.IsCanceled, Is.True);
            Assert.That(canceled.Utterance.IsAwaitingRecognition, Is.False);
            yield return null;
            Assert.That(userWindow.IsVisible, Is.False, "An unrelated refresh must not restore the canceled prompt.");
        }

        [UnityTest]
        public IEnumerator InterruptingSpeechBeforeAnyPartialClearsTheAwaitingRecognitionPrompt()
        {
            yield return Wait(orchestrator.StartAsync());
            pipeline.Recognize("interrupted-before-partial", null, SpeechRecognitionUpdateKind.Started);
            yield return Until(() => userWindow.IsVisible && userWindow.MessageText.text == "…");
            yield return Wait(orchestrator.InterruptAsync());
            yield return Until(() => pipeline.InterruptCount > 0 && !userWindow.IsVisible);
            Assert.That(userWindow.MessageText.text, Is.Empty);
            Assert.That(orchestrator.CurrentConversation.DisplayUserUtterance, Is.Null);
            Assert.That(orchestrator.CurrentConversation.LatestUserUtterance, Is.Null);
            yield return null;
            Assert.That(userWindow.IsVisible, Is.False, "An unrelated refresh must not restore the interrupted prompt.");
        }

        [UnityTest]
        public IEnumerator QueuedRecognitionIsDiscardedWhenResponseSuppressesInputBeforeDelivery()
        {
            yield return VerifyQueuedRecognitionDelivery(false);
        }

        [UnityTest]
        public IEnumerator QueuedRecognitionIsDeliveredDuringResponseWhenBargeInIsAllowed()
        {
            yield return VerifyQueuedRecognitionDelivery(true);
        }

        private IEnumerator VerifyQueuedRecognitionDelivery(bool allowBargeIn)
        {
            orchestrator.SetAllowBargeIn(allowBargeIn);
            var events = new List<ConversationEvent>();
            orchestrator.ConversationEventReceived += value => events.Add(value);
            yield return Wait(orchestrator.StartAsync());
            pipeline.Recognize("open-before-accept", null, SpeechRecognitionUpdateKind.Started,
                active: true, duration: 0.032, observedAt: 10);
            yield return Until(() => events.Any(value => value.Kind == ConversationEventKind.UserSpeechStarted &&
                value.RecognitionId == "open-before-accept"));
            var awaiting = orchestrator.CurrentConversation.LatestUserUtterance;
            events.Clear();
            var generation = orchestrator.Orchestrator.Generation;
            Assert.That(orchestrator.Orchestrator.IsInputSuppressed, Is.False);

            // Do not yield until Accepted has changed the core's input policy. All updates
            // pass the provider-callback gate but remain queued for the next Unity Update.
            pipeline.Recognize("queued-start", null, SpeechRecognitionUpdateKind.Started);
            pipeline.Recognize("queued-partial", "New user speech", SpeechRecognitionUpdateKind.Partial);
            pipeline.Recognize("open-before-accept", null, SpeechRecognitionUpdateKind.Activity,
                active: true, duration: 0.032, observedAt: 10.032);
            Assert.That(events.Any(value => value.RecognitionId != null), Is.False);
            var accepted = pipeline.EmitAsync(new SpeechPipelineResponse
            {
                Type = SpeechPipelineResponseType.Accepted,
                SessionId = "default", TransactionId = "accepted-before-recognition-delivery"
            });
            Assert.That(accepted.Status.IsCompleted(), Is.True, "Accepted must reach the core without advancing a Unity frame.");
            accepted.GetAwaiter().GetResult();
            Assert.That(orchestrator.Orchestrator.HasActiveTurns, Is.True);
            Assert.That(orchestrator.Orchestrator.IsInputSuppressed, Is.EqualTo(!allowBargeIn));
            Assert.That(orchestrator.Orchestrator.Generation, Is.EqualTo(generation),
                "This regression occurs within the same generation, without an interrupt or reset.");
            var duringAcceptance = orchestrator.CurrentConversation;
            Assert.That(duringAcceptance.LatestUserUtterance.Id, Is.EqualTo(awaiting.Id));
            Assert.That(duringAcceptance.LatestUserUtterance.IsCanceled, Is.False);
            if (allowBargeIn)
                Assert.That(duringAcceptance.DisplayUserUtterance.Id, Is.EqualTo(awaiting.Id));
            else
                Assert.That(duringAcceptance.DisplayUserUtterance, Is.Null,
                    "Input suppression removes an already projected onset without canceling the raw recognition.");

            // Cancellation is intentionally admitted even during suppression. Receiving this
            // later notification proves Update has already consumed all queued updates.
            pipeline.Recognize("delivery-barrier", null, SpeechRecognitionUpdateKind.Canceled);
            yield return Until(() => events.Any(value => value.Kind == ConversationEventKind.UserSpeechCanceled &&
                value.RecognitionId == "delivery-barrier"));
            var delivered = events.Where(value => value.RecognitionId == "queued-start" ||
                value.RecognitionId == "queued-partial" || value.RecognitionId == "open-before-accept").ToArray();
            var snapshot = orchestrator.CurrentConversation;
            if (allowBargeIn)
            {
                Assert.That(delivered.Select(value => value.Kind), Is.EqualTo(new[]
                    { ConversationEventKind.UserSpeechStarted, ConversationEventKind.UserSpeechPartial,
                        ConversationEventKind.UserSpeechActivity }));
                Assert.That(delivered[0].Utterance.IsAwaitingRecognition, Is.True);
                Assert.That(delivered[0].Text, Is.Null.Or.Empty);
                Assert.That(delivered[2].IsSpeechActive, Is.True);
                Assert.That(delivered[2].AudioDurationSeconds, Is.EqualTo(0.032));
                Assert.That(delivered[2].ObservedAtSeconds, Is.EqualTo(10.032));
                Assert.That(delivered[2].Utterance.Id, Is.EqualTo(awaiting.Id));
                Assert.That(snapshot.LatestUserUtterance.RecognitionId, Is.EqualTo("queued-partial"));
                Assert.That(snapshot.DisplayUserUtterance.RecognitionId, Is.EqualTo("queued-partial"));
                Assert.That(userWindow.IsVisible, Is.True);
                Assert.That(userWindow.MessageText.text, Is.EqualTo("New user speech"));
            }
            else
            {
                Assert.That(delivered, Is.Empty);
                Assert.That(snapshot.LatestUserUtterance.Id, Is.EqualTo(awaiting.Id));
                Assert.That(snapshot.LatestUserUtterance.IsAwaitingRecognition, Is.True);
                Assert.That(snapshot.LatestUserUtterance.IsCanceled, Is.False);
                Assert.That(snapshot.DisplayUserUtterance, Is.Null);
                Assert.That(userWindow.IsVisible, Is.False);
                Assert.That(userWindow.MessageText.text, Is.Empty);
            }
            Assert.That(orchestrator.Orchestrator.HasActiveTurns, Is.True);
        }

        [UnityTest]
        public IEnumerator SourceObserversReceiveIndependentSnapshotsAndFailuresDoNotBlockDisplay()
        {
            var errors = new List<Exception>();
            orchestrator.Error += errors.Add;
            orchestrator.ConversationEventReceived += conversationEvent =>
            {
                if (conversationEvent.Kind != ConversationEventKind.AssistantGenerated || conversationEvent.Utterance == null) return;
                conversationEvent.Utterance.Text = "Mutated by another observer";
                conversationEvent.Utterance.DisplayText = "Mutated by another observer";
                throw new InvalidOperationException("Observer failed");
            };
            ConversationUtterance observed = null;
            orchestrator.ConversationEventReceived += conversationEvent =>
            {
                if (conversationEvent.Kind == ConversationEventKind.AssistantGenerated)
                    observed = conversationEvent.Utterance;
            };
            yield return Wait(orchestrator.StartAsync());
            yield return EmitCompleteTurn(pipeline, orchestrator, "observers", "Question", "Original answer");
            yield return Until(() => assistantWindow.MessageText.text == "Original answer");
            Assert.That(orchestrator.CurrentConversation.DisplayAssistantUtterance.DisplayText, Is.EqualTo("Original answer"));
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed.Text, Is.EqualTo("Original answer"));
            Assert.That(errors.Count, Is.GreaterThan(0));
            Assert.That(errors.TrueForAll(error => error.Message == "Observer failed"), Is.True);
        }

        [UnityTest]
        public IEnumerator ReentrantInterruptFromObserverPublishesClearedStateToWindows()
        {
            var interrupted = false;
            orchestrator.ConversationEventReceived += conversationEvent =>
            {
                if (interrupted || conversationEvent.Kind != ConversationEventKind.AssistantGenerated) return;
                interrupted = true;
                orchestrator.Interrupt();
            };
            yield return Wait(orchestrator.StartAsync());
            yield return Wait(pipeline.EmitAsync(Start("interrupt-observer", "Question")));
            yield return Wait(pipeline.EmitAsync(Chunk("interrupt-observer", "Answer")));
            yield return Until(() => interrupted && pipeline.InterruptCount > 0 && !assistantWindow.IsVisible && !userWindow.IsVisible);
            Assert.That(orchestrator.CurrentConversation.DisplayAssistantUtterance, Is.Null);
            yield return null;
            Assert.That(assistantWindow.IsVisible || userWindow.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator HiddenRequestsRemainInConversationEventsAndDoNotDisplaceVisibleMessages()
        {
            var events = new List<ConversationEvent>();
            orchestrator.ConversationEventReceived += value => events.Add(value);
            yield return Wait(orchestrator.StartAsync());
            yield return EmitCompleteTurn(pipeline, orchestrator, "visible", "Visible question", "Visible answer");
            yield return Until(() => assistantWindow.MessageText.text == "Visible answer");
            yield return Wait(pipeline.EmitAsync(Start("internal", "$Internal direction")));
            yield return Until(() => events.Any(item => item.Kind == ConversationEventKind.UserSpeechConfirmed && item.TransactionId == "internal"));
            var hidden = events.Single(item => item.Kind == ConversationEventKind.UserSpeechConfirmed && item.TransactionId == "internal");
            Assert.That(hidden.Utterance.Text, Is.EqualTo("$Internal direction"));
            Assert.That(hidden.Utterance.IsDisplayAllowed, Is.False);
            Assert.That(orchestrator.CurrentConversation.LatestUserUtterance.Id, Is.EqualTo(hidden.Utterance.Id));
            Assert.That(userWindow.MessageText.text, Is.EqualTo("Visible question"));
            Assert.That(assistantWindow.MessageText.text, Is.EqualTo("Visible answer"));
            var chunk = Chunk("internal", "Spoken answer");
            chunk.Text = "[face:Joy]Spoken answer";
            yield return Wait(pipeline.EmitAsync(chunk));
            yield return Wait(pipeline.EmitAsync(Final("internal", "Spoken answer")));
            yield return Wait(orchestrator.DrainAsync());
            yield return Until(() => events.Any(item => item.Kind == ConversationEventKind.TurnEnded && item.TransactionId == "internal"));
            var generated = events.Single(item => item.Kind == ConversationEventKind.AssistantGenerated && item.TransactionId == "internal");
            Assert.That(generated.Text, Is.EqualTo("[face:Joy]Spoken answer"));
            Assert.That(generated.VoiceText, Is.EqualTo("Spoken answer"));
            Assert.That(assistantWindow.MessageText.text, Is.EqualTo("Spoken answer"));
            Assert.That(events.Any(item => item.Kind == ConversationEventKind.AssistantSpeechStarted && item.TransactionId == "internal"), Is.True);
            Assert.That(events.Any(item => item.Kind == ConversationEventKind.AssistantSpeechCompleted && item.TransactionId == "internal"), Is.True);
            Assert.That(events.Select(item => item.Sequence), Is.Ordered.And.Unique);
        }

        [UnityTest]
        public IEnumerator LifecycleEventsArePublishedWithoutAnyMessagesAndKeepOrderAcrossRestart()
        {
            var events = new List<ConversationEvent>();
            orchestrator.ConversationEventReceived += value => events.Add(value);
            yield return Wait(orchestrator.StartAsync());
            var firstRun = orchestrator.CurrentConversation.RunId;
            yield return Wait(orchestrator.InterruptAsync());
            yield return Wait(orchestrator.ResetAsync("next-context"));
            yield return Wait(orchestrator.DrainAsync());
            yield return Until(() => events.Any(item => item.Kind == ConversationEventKind.Reset));
            yield return Wait(orchestrator.StopAsync());
            Assert.That(orchestrator.CurrentConversation.IsRunning, Is.False);
            yield return Wait(orchestrator.StartAsync());
            Assert.That(orchestrator.CurrentConversation.RunId, Is.Not.EqualTo(firstRun));
            var boundaries = events.Where(item => item.Kind == ConversationEventKind.Started ||
                item.Kind == ConversationEventKind.Interrupted || item.Kind == ConversationEventKind.Reset ||
                item.Kind == ConversationEventKind.Stopped).ToArray();
            Assert.That(boundaries.Select(item => item.Kind), Is.EqualTo(new[] {
                ConversationEventKind.Started, ConversationEventKind.Interrupted,
                ConversationEventKind.Reset, ConversationEventKind.Stopped, ConversationEventKind.Started }));
            Assert.That(boundaries[2].ContextId, Is.EqualTo("next-context"));
            Assert.That(events.Select(item => item.Sequence), Is.Ordered.And.Unique);
        }

        [UnityTest]
        public IEnumerator FilterCanInterruptWithoutPublishingAStaleDisplaySnapshot()
        {
            var interrupted = false;
            var events = new List<ConversationEvent>();
            orchestrator.ConversationEventReceived += value => events.Add(value);
            orchestrator.ShouldShowMessage = context =>
            {
                if (!interrupted && context.Speaker == ConversationSpeaker.Assistant)
                {
                    interrupted = true;
                    orchestrator.Interrupt();
                }
                return true;
            };
            yield return Wait(orchestrator.StartAsync());
            yield return Wait(pipeline.EmitAsync(Start("filter-interrupt", "Question")));
            yield return Wait(pipeline.EmitAsync(Chunk("filter-interrupt", "Answer")));
            yield return Until(() => interrupted && events.Any(item => item.Kind == ConversationEventKind.Interrupted));
            Assert.That(orchestrator.CurrentConversation.DisplayUserUtterance, Is.Null);
            Assert.That(orchestrator.CurrentConversation.DisplayAssistantUtterance, Is.Null);
            Assert.That(userWindow.IsVisible || assistantWindow.IsVisible, Is.False);
            var fact = events.First(item => item.Kind == ConversationEventKind.AssistantGenerated);
            var boundary = events.First(item => item.Kind == ConversationEventKind.Interrupted);
            Assert.That(fact.Generation, Is.LessThan(boundary.Generation));
            Assert.That(events.Select(item => item.Sequence), Is.Ordered.And.Unique);
        }

        private Window CreateWindow(string name, bool showUser, bool showAssistant)
        {
            var host = new GameObject(name + " window");
            host.transform.SetParent(root.transform, false);
            var window = host.AddComponent<Window>();
            window.MessageText = CreateText(host, "Message");
            window.SpeakerText = CreateText(host, "Speaker");
            window.IsTextAnimated = false;
            window.Options.AutoHideUser = window.Options.AutoHideAssistant = false;
            // These source integration tests assert delivery independently of UI smoothing.
            window.Options.UserSpeechPromptDelay = 0;
            window.Options.ShowUserMessages = showUser;
            window.Options.ShowAssistantMessages = showAssistant;
            window.Source = orchestrator;
            return window;
        }

        private static Text CreateText(GameObject parent, string name)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            host.transform.SetParent(parent.transform, false);
            return host.GetComponent<Text>();
        }

        private static SpeechPipelineResponse Start(string id, string text) => new SpeechPipelineResponse
        {
            Type = SpeechPipelineResponseType.Start, SessionId = "default", TransactionId = id,
            Metadata = new JObject { ["recognized_text"] = text }
        };
        private static SpeechPipelineResponse Chunk(string id, string text) => new SpeechPipelineResponse
        {
            Type = SpeechPipelineResponseType.Chunk, SessionId = "default", TransactionId = id, Text = text, VoiceText = text
        };
        private static SpeechPipelineResponse Final(string id, string text) => new SpeechPipelineResponse
        {
            Type = SpeechPipelineResponseType.Final, SessionId = "default", TransactionId = id, Text = text, VoiceText = text
        };
        private static IEnumerator EmitCompleteTurn(FakePipeline source, ChatdollOrchestrator target, string id, string user, string assistant)
        {
            yield return Wait(source.EmitAsync(Start(id, user)));
            yield return Wait(source.EmitAsync(Chunk(id, assistant)));
            yield return Wait(source.EmitAsync(Final(id, assistant)));
            yield return Wait(target.DrainAsync());
        }
        private static IEnumerator Until(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(condition(), Is.True, "Timed out waiting for message source.");
        }
        private static IEnumerator Wait(UniTask operation)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (!operation.Status.IsCompleted() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(operation.Status.IsCompleted(), Is.True, "Orchestrator operation timed out.");
            operation.GetAwaiter().GetResult();
        }
        private sealed class FakeAvatar : IAvatarController
        {
            public UniTask PresentAsync(AvatarRequest request, CancellationToken cancellationToken) => UniTask.CompletedTask;
            public UniTask StopAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        }
        private sealed class HeldAvatar : IAvatarController
        {
            public readonly SpeechCompletionSource<bool> Entered = new SpeechCompletionSource<bool>();
            public readonly SpeechCompletionSource<bool> Release = new SpeechCompletionSource<bool>();
            public async UniTask PresentAsync(AvatarRequest request, CancellationToken cancellationToken)
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
        private sealed class FakePipeline : ISpeechPipeline, ISpeechRecognitionSource
        {
            public string SessionId => "default";
            public int InterruptCount;
            public string LastResetContext;
            public event Func<SpeechPipelineResponse, UniTask> ResponseReceived;
            public event Action<SpeechRecognitionUpdate> RecognitionUpdated;
            public event Action<Exception> Error { add { } remove { } }
            public UniTask<SpeechPipelineResponse> InvokeAsync(SpeechPipelineRequest request, CancellationToken cancellationToken = default)
                => UniTask.FromResult(Final("invoke", request.Text));
            public UniTask ProcessAudioSamplesAsync(byte[] samples, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask ResetSpeechInputAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask InterruptAsync(CancellationToken cancellationToken = default)
            { InterruptCount++; return UniTask.CompletedTask; }
            public UniTask ResetAsync(string contextId = null, CancellationToken cancellationToken = default)
            { LastResetContext = contextId; return UniTask.CompletedTask; }
            public UniTask DrainAsync() => UniTask.CompletedTask;
            public UniTask DisposeAsync() => UniTask.CompletedTask;
            public void Recognize(string id, string text, SpeechRecognitionUpdateKind kind, string transaction = null,
                bool? active = null, double? duration = null, double observedAt = 0)
                => RecognitionUpdated?.Invoke(new SpeechRecognitionUpdate
                {
                    RecognitionId = id, Text = text, Kind = kind, TransactionId = transaction, SessionId = SessionId,
                    IsSpeechActive = active, AudioDurationSeconds = duration, ObservedAtSeconds = observedAt
                });
            public async UniTask EmitAsync(SpeechPipelineResponse response)
            {
                if (response.Type == SpeechPipelineResponseType.Start)
                {
                    var accepted = response.Copy();
                    accepted.Type = SpeechPipelineResponseType.Accepted;
                    await EmitAsync(accepted);
                }
                var handlers = ResponseReceived;
                if (handlers != null)
                    foreach (Func<SpeechPipelineResponse, UniTask> handler in handlers.GetInvocationList()) await handler(response);
            }
        }
    }
}

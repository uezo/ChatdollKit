using System;
using System.Linq;
using System.Reflection;
using ChatdollKit.Avatar;
using ChatdollKit.Orchestration;
using ChatdollKit.SpeechPipeline;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChatdollKit.Tests.Orchestration
{
    public class OrchestratorConversationStateTests
    {
        [Test]
        public void RecognizedTextTakesPrecedenceOverDecoratedRequestText()
        {
            var r = new Rig(); r.Start("turn", "actual utterance", "$internal decorated prompt");
            Assert.That(r.User.Text, Is.EqualTo("actual utterance"));
            Assert.That(r.User.Speaker, Is.EqualTo(ConversationSpeaker.User));
        }

        [Test]
        public void InternalRequestKeepsThePreviousDisplayUserUtteranceAndItsAnswerIsEligible()
        {
            var r = new Rig(); r.Start("visible", "keep this text"); var id = r.User.Id;
            r.Start("internal", "$update state", "decorated request");
            Assert.That(r.User.Id, Is.EqualTo(id));
            Assert.That(r.User.Text, Is.EqualTo("keep this text"));
            r.Present("internal", "The result is ready.");
            Assert.That(r.Assistant.DisplayText, Is.EqualTo("The result is ready."));
        }

        [Test]
        public void MissingRecognitionFallsBackToRequestTextButEmptyRecognitionDoesNot()
        {
            var r = new Rig(); r.Start("fallback", null, "typed request");
            Assert.That(r.User.Text, Is.EqualTo("typed request"));
            r.Start("empty", "", "do not expose decorated prompt");
            r.Start("internal", null, "$internal request");
            Assert.That(r.User.Text, Is.EqualTo("typed request"));
        }

        [TestCase("IsWakeword")]
        [TestCase("is_wakeword")]
        public void WakewordFilteringUsesExplicitMetadata(string key)
        {
            var r = new Rig(); r.Options.ShowWakewordMessages = false;
            var response = Response(SpeechPipelineResponseType.Start, "wake");
            response.Metadata = new JObject { ["recognized_text"] = "hello avatar", [key] = true };
            r.State.OnResponse(response, 1);
            Assert.That(r.User, Is.Null);
            r.Start("ordinary", "hello avatar", order: 2);
            Assert.That(r.User.Text, Is.EqualTo("hello avatar"));
        }

        [Test]
        public void AssistantOnlyUsesVoiceTextAndControlChunksDoNotChangeTheSnapshot()
        {
            var r = new Rig();
            r.State.OnPresentationStarted(new AvatarRequest
            { TransactionId = "turn", Text = "<think>private</think>[face:smile]display", VoiceText = "spoken text" }, 1);
            var before = r.State.Current;
            Assert.That(r.State.OnPresentationStarted(new AvatarRequest
            { TransactionId = "turn", Text = "[face:smile]<think>private</think>", VoiceText = "" }, 1), Is.True);
            Assert.That(r.Assistant.DisplayText, Is.EqualTo("spoken text"));
            Assert.That(r.Assistant.Id, Is.EqualTo(before.DisplayAssistantUtterance.Id));
            Assert.That(r.State.Current.Sequence, Is.GreaterThan(before.Sequence));
        }

        [Test]
        public void SameTurnAssistantHasPriorityOverDelayedUserStart()
        {
            var r = new Rig(); r.Present("turn", "already speaking", 2);
            r.Start("turn", "delayed user text", order: 2);
            Assert.That(r.Assistant.Order, Is.GreaterThan(r.User.Order));
        }

        [Test]
        public void OlderUnseenTurnCannotReplaceNewerCaptionEvenAfterTheNewerTurnEnds()
        {
            var r = new Rig(); r.Present("new", "new response", 2);
            r.State.OnTurnEnded("new", OrchestratorTurnEndReason.Completed);
            r.Start("old", "older input", order: 1); r.Present("old", "older response", 1);
            Assert.That(r.User, Is.Null);
            Assert.That(r.Assistant.DisplayText, Is.EqualTo("new response"));
            Assert.That(r.Assistant.IsComplete, Is.True);
        }

        [Test]
        public void OlderKnownTurnCannotReplaceNewerAssistantAfterCleanup()
        {
            var r = new Rig(); r.Start("old", "old request", order: 1);
            r.Present("new", "new answer", 2);
            r.State.OnTurnEnded("new", OrchestratorTurnEndReason.Canceled);
            r.Present("old", "delayed answer", 1);
            Assert.That(r.Assistant, Is.Null);
        }

        [Test]
        public void StartSuppliesAuthoritativeOrderingToAnEarlierUnorderedConfirmation()
        {
            var r = new Rig();
            r.Recognition("speech", "new question", SpeechRecognitionUpdateKind.Confirmed, "new");
            r.Start("new", "new question", order: 2);
            r.State.OnTurnEnded("new", OrchestratorTurnEndReason.Completed);
            r.Start("old", "delayed older question", order: 1);
            Assert.That(r.User.Text, Is.EqualTo("new question"));
        }

        [Test]
        public void FinalDoesNotDuplicateTextOrCompletePresentation()
        {
            var r = new Rig(); r.Present("turn", "first "); r.Present("turn", "second");
            r.State.OnResponse(Response(SpeechPipelineResponseType.Final, "turn", "first second"), 1);
            Assert.That(r.Assistant.DisplayText, Is.EqualTo("first second"));
            Assert.That(r.Assistant.IsComplete, Is.False);
            r.State.OnTurnEnded("turn", OrchestratorTurnEndReason.Completed);
            Assert.That(r.Assistant.IsComplete, Is.True);
        }

        [TestCase(OrchestratorTurnEndReason.Canceled)]
        [TestCase(OrchestratorTurnEndReason.Failed)]
        public void AbnormalEndInvalidatesOnlyItsOwnMessages(OrchestratorTurnEndReason reason)
        {
            var r = new Rig(); r.Start("old", "old request", order: 1); r.Present("old", "unfinished", 1);
            r.Start("new", "new question", order: 2);
            r.State.OnTurnEnded("old", reason);
            Assert.That(r.User.Text, Is.EqualTo("new question"));
            Assert.That(r.Assistant, Is.Null);
        }

        [Test]
        public void ClearInvalidatesMessagesAndListeningAndAllowsANewRun()
        {
            var r = new Rig(); r.Start("same", "old request", order: 10); r.Present("same", "old answer", 10);
            r.State.SetCanListen(true); var revision = r.State.Current.Sequence; var id = r.User.Id;
            Assert.That(r.State.OnLifecycle(ConversationEventKind.Reset, "run", "session", 1), Is.True);
            Assert.That(r.State.Current.Sequence, Is.GreaterThan(revision));
            Assert.That(r.State.Current.CanListen, Is.False);
            Assert.That(r.User, Is.Null); Assert.That(r.Assistant, Is.Null);
            r.Start("same", "new run request", order: 1);
            Assert.That(r.User.Id, Is.Not.EqualTo(id));
        }

        [Test]
        public void ResponseArrivalModeDoesNotAlsoAppendPlaybackNotifications()
        {
            var r = new Rig(); r.Options.AssistantTiming = AssistantMessageTiming.ResponseReceived;
            r.State.OnResponse(Response(SpeechPipelineResponseType.Chunk, "turn", "received "), 1);
            r.Present("turn", "duplicate playback");
            r.State.OnResponse(Response(SpeechPipelineResponseType.Chunk, "turn", "answer"), 1);
            r.State.OnResponse(Response(SpeechPipelineResponseType.Final, "turn", "received answer"), 1);
            Assert.That(r.Assistant.DisplayText, Is.EqualTo("received answer"));
        }

        [Test]
        public void MissingConcreteAvatarCanUseResponseArrivalFallback()
        {
            var r = new Rig();
            r.State.OnResponse(Response(SpeechPipelineResponseType.Chunk, "turn", "fallback"), 1, useResponseText: true);
            Assert.That(r.Assistant.DisplayText, Is.EqualTo("fallback"));
        }

        [Test]
        public void TextOnlyChunksAccumulateUnderOneId()
        {
            var r = new Rig(); r.Present("turn", "text "); var id = r.Assistant.Id;
            r.Present("turn", "only");
            Assert.That(r.Assistant.DisplayText, Is.EqualTo("text only"));
            Assert.That(r.Assistant.Id, Is.EqualTo(id));
        }

        [Test]
        public void ReplaceModeStartsAFreshMessageForEachChunk()
        {
            var r = new Rig(); r.Options.AssistantMode = AssistantMessageMode.Replace;
            r.Present("turn", "first"); var id = r.Assistant.Id; r.Present("turn", "second");
            Assert.That(r.Assistant.DisplayText, Is.EqualTo("second"));
            Assert.That(r.Assistant.Id, Is.Not.EqualTo(id));
        }

        [Test]
        public void CustomFilterReceivesMetadataCopiesAndCanExcludeEitherSpeaker()
        {
            var r = new Rig();
            r.State.ShouldShowMessage = context =>
            {
                if (context.Metadata != null) context.Metadata["recognized_text"] = "mutated";
                return !context.Text.Contains("private");
            };
            var response = Response(SpeechPipelineResponseType.Start, "visible");
            response.Metadata = new JObject { ["recognized_text"] = "public input" };
            r.State.OnResponse(response, 1); r.Start("hidden", "private input", order: 2); r.Present("hidden", "private answer", 2);
            Assert.That((string)response.Metadata["recognized_text"], Is.EqualTo("public input"));
            Assert.That(r.User.Text, Is.EqualTo("public input")); Assert.That(r.Assistant, Is.Null);
        }

        [TestCase(SpeechPipelineResponseType.Final)]
        [TestCase(SpeechPipelineResponseType.Canceled)]
        [TestCase(SpeechPipelineResponseType.Error)]
        [TestCase(SpeechPipelineResponseType.Accepted)]
        public void NonDisplayResponsesDoNotChangeListeningOrDisplay(SpeechPipelineResponseType type)
        {
            var r = new Rig(); r.State.SetCanListen(true); var before = r.State.Current.Sequence;
            Assert.That(r.State.OnResponse(Response(type, "unknown"), 1), Is.True);
            Assert.That(r.State.Current.CanListen, Is.True);
            Assert.That(r.State.Current.Sequence, Is.GreaterThan(before));
        }

        [Test]
        public void PartialHypothesesReplaceWholeTextWithoutChangingIdentity()
        {
            var r = new Rig(); r.Recognition("speech", "turn on", SpeechRecognitionUpdateKind.Partial);
            var id = r.User.Id;
            r.Recognition("speech", "turn off the lights", SpeechRecognitionUpdateKind.Partial);
            Assert.That(r.User.Text, Is.EqualTo("turn off the lights"));
            Assert.That(r.User.Id, Is.EqualTo(id)); Assert.That(r.User.IsPartial, Is.True);
            Assert.That(r.User.IsComplete, Is.False);
        }

        [Test]
        public void ConfirmedRecognitionAndStartKeepThePartialIdentity()
        {
            var r = new Rig(); r.Recognition("speech", "question", SpeechRecognitionUpdateKind.Partial);
            var id = r.User.Id;
            r.Recognition("speech", "confirmed question", SpeechRecognitionUpdateKind.Confirmed, "turn");
            Assert.That(r.User.IsComplete, Is.True); Assert.That(r.User.IsPartial, Is.False);
            r.Start("turn", "confirmed question", "$decorated prompt", 1);
            Assert.That(r.User.Id, Is.EqualTo(id));
            Assert.That(r.User.Text, Is.EqualTo("confirmed question"));
            r.Present("turn", "answer");
            Assert.That(r.Assistant.Order, Is.GreaterThan(r.User.Order));
        }

        [Test]
        public void SpeechStartPublishesAnEmptyAwaitingUtteranceWithoutChoosingAPlaceholder()
        {
            var r = new Rig();
            r.Recognition("speech", null, SpeechRecognitionUpdateKind.Started, active: true, duration: 0.032, observedAt: 12);
            Assert.That(r.User.IsDisplayAllowed, Is.True);
            Assert.That(r.User.IsAwaitingRecognition, Is.True);
            Assert.That(r.User.IsPartial, Is.True);
            Assert.That(r.User.IsComplete, Is.False);
            Assert.That(r.User.Text, Is.Empty);
            Assert.That(r.User.VoiceText, Is.Empty);
            Assert.That(r.User.DisplayText, Is.Empty);
            var started = r.State.DrainEvents().Single();
            Assert.That(started.Kind, Is.EqualTo(ConversationEventKind.UserSpeechStarted));
            Assert.That(started.RecognitionId, Is.EqualTo("speech"));
            Assert.That(started.Text, Is.Empty);
            Assert.That(started.IsSpeechActive, Is.True);
            Assert.That(started.AudioDurationSeconds, Is.EqualTo(0.032));
            Assert.That(started.ObservedAtSeconds, Is.EqualTo(12));
            Assert.That(started.Utterance.IsAwaitingRecognition, Is.True);
            started.Utterance.IsAwaitingRecognition = false;
            Assert.That(r.State.Current.LatestUserUtterance.IsAwaitingRecognition, Is.True);
        }

        [Test]
        public void SpeechActivityPublishesFactsWithoutChangingPartialTextIdentityOrOrder()
        {
            var r = new Rig();
            r.Recognition("speech", null, SpeechRecognitionUpdateKind.Started);
            r.Recognition("speech", "recognized text", SpeechRecognitionUpdateKind.Partial);
            var before = r.State.Current;
            r.State.DrainEvents();
            r.Recognition("speech", "not a transcript", SpeechRecognitionUpdateKind.Activity,
                active: false, duration: 0.032, observedAt: 123);
            r.Recognition("speech", null, SpeechRecognitionUpdateKind.Activity, active: true, observedAt: 123.1);
            var activity = r.State.DrainEvents();
            Assert.That(activity.Length, Is.EqualTo(2), "Every activity fact is available immediately, without a UI threshold.");
            Assert.That(activity.All(item => item.Kind == ConversationEventKind.UserSpeechActivity), Is.True);
            Assert.That(activity.All(item => item.Text == null && item.VoiceText == null), Is.True);
            Assert.That(activity.All(item => item.Utterance.Text == "recognized text" && item.Utterance.IsPartial), Is.True);
            Assert.That(activity[0].IsSpeechActive, Is.False);
            Assert.That(activity[0].AudioDurationSeconds, Is.EqualTo(0.032));
            Assert.That(activity[0].ObservedAtSeconds, Is.EqualTo(123));
            Assert.That(activity[1].IsSpeechActive, Is.True);
            Assert.That(activity[1].AudioDurationSeconds, Is.Null);
            Assert.That(activity[1].ObservedAtSeconds, Is.EqualTo(123.1));
            Assert.That(r.User.Id, Is.EqualTo(before.DisplayUserUtterance.Id));
            Assert.That(r.User.Order, Is.EqualTo(before.DisplayUserUtterance.Order));
            Assert.That(r.User.DisplayText, Is.EqualTo("recognized text"));
            Assert.That(r.State.Current.LatestUserUtterance.Text, Is.EqualTo("recognized text"));
            activity[0].Utterance.Text = "observer mutation";
            Assert.That(r.State.Current.LatestUserUtterance.Text, Is.EqualTo("recognized text"));
        }

        [TestCase("confirmed")]
        [TestCase("canceled")]
        [TestCase("turn-ended")]
        public void SpeechActivityCannotCreateAnUtteranceOrReviveATerminalRecognition(string terminal)
        {
            var r = new Rig();
            r.Recognition("unknown", null, SpeechRecognitionUpdateKind.Activity, active: true, duration: 10);
            Assert.That(r.State.DrainEvents(), Is.Empty);
            Assert.That(r.State.Current.LatestUserUtterance, Is.Null);
            r.Recognition("speech", "text", SpeechRecognitionUpdateKind.Partial, "turn");
            if (terminal == "turn-ended") r.State.OnTurnEnded("turn", OrchestratorTurnEndReason.Completed);
            else r.Recognition("speech", "text", terminal == "confirmed"
                ? SpeechRecognitionUpdateKind.Confirmed : SpeechRecognitionUpdateKind.Canceled, "turn");
            r.State.DrainEvents();
            var sequence = r.State.Current.Sequence;
            r.Recognition("speech", null, SpeechRecognitionUpdateKind.Activity, active: true, duration: 10);
            Assert.That(r.State.DrainEvents(), Is.Empty);
            Assert.That(r.State.Current.Sequence, Is.EqualTo(sequence));
        }

        [Test]
        public void SuppressingAwaitingRecognitionPreservesRawFactsAndActivityDoesNotRestoreItsProjection()
        {
            var r = new Rig(); r.Recognition("speech", null, SpeechRecognitionUpdateKind.Started);
            var before = r.User;
            r.State.DrainEvents();
            Assert.That(r.State.SuppressAwaitingRecognition(), Is.True);
            Assert.That(r.State.SuppressAwaitingRecognition(), Is.False);
            Assert.That(r.State.DrainEvents().Single().Kind, Is.EqualTo(ConversationEventKind.StateChanged));
            Assert.That(r.User, Is.Null);
            Assert.That(r.State.Current.LatestUserUtterance.Id, Is.EqualTo(before.Id));
            Assert.That(r.State.Current.LatestUserUtterance.IsCanceled, Is.False);
            r.Recognition("speech", null, SpeechRecognitionUpdateKind.Activity, active: true, duration: 2);
            Assert.That(r.State.DrainEvents().Single().Kind, Is.EqualTo(ConversationEventKind.UserSpeechActivity));
            Assert.That(r.User, Is.Null, "Activity or input resuming must not restore an old awaiting projection.");
            r.Recognition("speech", "recognized now", SpeechRecognitionUpdateKind.Partial);
            Assert.That(r.User.Id, Is.EqualTo(before.Id));
            Assert.That(r.User.Order, Is.EqualTo(before.Order));
            Assert.That(r.User.DisplayText, Is.EqualTo("recognized now"));
            Assert.That(r.State.SuppressAwaitingRecognition(), Is.False, "Recognized text is not an awaiting prompt.");
        }

        [Test]
        public void SpeechStartKeepsItsIdentityThroughPartialConfirmationAndTurnStart()
        {
            var r = new Rig(); r.Recognition("speech", null, SpeechRecognitionUpdateKind.Started);
            var id = r.User.Id;
            r.Recognition("speech", "hello", SpeechRecognitionUpdateKind.Partial);
            Assert.That(r.User.Id, Is.EqualTo(id));
            Assert.That(r.User.IsAwaitingRecognition, Is.False);
            Assert.That(r.User.IsPartial, Is.True);
            Assert.That(r.User.DisplayText, Is.EqualTo("hello"));
            r.Recognition("speech", "hello world", SpeechRecognitionUpdateKind.Confirmed, "turn");
            r.Start("turn", "hello world", "$decorated", 1);
            Assert.That(r.User.Id, Is.EqualTo(id));
            Assert.That(r.User.IsAwaitingRecognition, Is.False);
            Assert.That(r.User.IsComplete, Is.True);
            Assert.That(r.State.DrainEvents().Select(item => item.Kind), Is.EqualTo(new[]
            {
                ConversationEventKind.UserSpeechStarted, ConversationEventKind.UserSpeechPartial,
                ConversationEventKind.UserSpeechConfirmed, ConversationEventKind.TurnStarted
            }));
        }

        [Test]
        public void DuplicateAndLateSpeechStartsNeverRestoreTheAwaitingProjection()
        {
            var r = new Rig(); r.Recognition("speech", null, SpeechRecognitionUpdateKind.Started);
            r.Recognition("speech", null, SpeechRecognitionUpdateKind.Started);
            Assert.That(r.State.DrainEvents().Count(item => item.Kind == ConversationEventKind.UserSpeechStarted), Is.EqualTo(1));
            r.Recognition("speech", "partial text", SpeechRecognitionUpdateKind.Partial);
            r.Recognition("speech", null, SpeechRecognitionUpdateKind.Started);
            Assert.That(r.User.DisplayText, Is.EqualTo("partial text"));
            Assert.That(r.User.IsAwaitingRecognition, Is.False);
            r.Recognition("speech", "confirmed text", SpeechRecognitionUpdateKind.Confirmed, "turn");
            r.Recognition("speech", null, SpeechRecognitionUpdateKind.Started);
            Assert.That(r.User.DisplayText, Is.EqualTo("confirmed text"));
            Assert.That(r.User.IsComplete, Is.True);
            Assert.That(r.State.DrainEvents().Any(item => item.Kind == ConversationEventKind.UserSpeechStarted), Is.False);
        }

        [Test]
        public void SpeechStartFilterCanExcludeAwaitingRecognitionWithoutExcludingRecognizedText()
        {
            var r = new Rig(); r.Start("previous", "keep previous message");
            var awaitingSeen = false;
            r.State.ShouldShowMessage = context =>
            {
                if (!context.IsAwaitingRecognition) return true;
                awaitingSeen = true;
                Assert.That(context.Text, Is.Empty);
                return false;
            };
            r.Recognition("speech", null, SpeechRecognitionUpdateKind.Started);
            Assert.That(awaitingSeen, Is.True);
            Assert.That(r.User.DisplayText, Is.EqualTo("keep previous message"));
            Assert.That(r.State.Current.LatestUserUtterance.IsAwaitingRecognition, Is.True);
            Assert.That(r.State.Current.LatestUserUtterance.IsDisplayAllowed, Is.False);
            r.State.DrainEvents();
            r.Recognition("speech", null, SpeechRecognitionUpdateKind.Activity, active: true, duration: 2);
            var activity = r.State.DrainEvents().Single();
            Assert.That(activity.Kind, Is.EqualTo(ConversationEventKind.UserSpeechActivity));
            Assert.That(activity.Utterance.IsDisplayAllowed, Is.False);
            Assert.That(r.User.DisplayText, Is.EqualTo("keep previous message"));
            r.Recognition("speech", "recognized input", SpeechRecognitionUpdateKind.Partial);
            Assert.That(r.User.DisplayText, Is.EqualTo("recognized input"));
        }

        [Test]
        public void InternalRecognitionRemovesItsAwaitingProjectionWithoutExposingTheInstruction()
        {
            var r = new Rig(); r.Recognition("speech", null, SpeechRecognitionUpdateKind.Started);
            r.Recognition("speech", "$internal request", SpeechRecognitionUpdateKind.Confirmed, "turn");
            Assert.That(r.User, Is.Null);
            Assert.That(r.State.Current.LatestUserUtterance.IsDisplayAllowed, Is.False);
            Assert.That(r.State.Current.LatestUserUtterance.IsAwaitingRecognition, Is.False);
            r.Present("turn", "visible answer");
            Assert.That(r.Assistant.DisplayText, Is.EqualTo("visible answer"));
        }

        [Test]
        public void CanceledSpeechStartCannotBeReopenedByALateStartedNotification()
        {
            var r = new Rig(); r.Recognition("speech", null, SpeechRecognitionUpdateKind.Started);
            r.Recognition("speech", null, SpeechRecognitionUpdateKind.Canceled);
            Assert.That(r.User, Is.Null);
            var canceled = r.State.Current.LatestUserUtterance;
            Assert.That(canceled.IsAwaitingRecognition, Is.False);
            Assert.That(canceled.IsCanceled, Is.True);
            r.State.DrainEvents();
            r.Recognition("speech", null, SpeechRecognitionUpdateKind.Started);
            Assert.That(r.User, Is.Null);
            Assert.That(r.State.DrainEvents(), Is.Empty);
        }

        [TestCase(ConversationEventKind.Interrupted)]
        [TestCase(ConversationEventKind.Reset)]
        [TestCase(ConversationEventKind.Stopped)]
        public void LifecycleBoundaryClearsAwaitingRecognition(ConversationEventKind kind)
        {
            var r = new Rig(); r.State.OnLifecycle(ConversationEventKind.Started, "run", "session", 0);
            r.Recognition("speech", null, SpeechRecognitionUpdateKind.Started);
            r.State.OnLifecycle(kind, "run", "session", 1);
            Assert.That(r.User, Is.Null);
            Assert.That(r.State.Current.LatestUserUtterance, Is.Null);
        }

        [Test]
        public void CancelingRecognitionDoesNotEraseANewerRecognition()
        {
            var r = new Rig(); r.Recognition("old", "older partial", SpeechRecognitionUpdateKind.Partial);
            r.Recognition("new", "newer partial", SpeechRecognitionUpdateKind.Partial);
            r.Recognition("old", null, SpeechRecognitionUpdateKind.Canceled);
            Assert.That(r.User.Text, Is.EqualTo("newer partial"));
            r.Recognition("new", null, SpeechRecognitionUpdateKind.Canceled);
            Assert.That(r.User, Is.Null);
        }

        [Test]
        public void OlderRecognitionUpdateCannotReplaceANewerRecognition()
        {
            var r = new Rig(); r.Recognition("old", "old partial", SpeechRecognitionUpdateKind.Partial);
            r.Recognition("new", "new partial", SpeechRecognitionUpdateKind.Partial);
            r.Recognition("old", "late corrected partial", SpeechRecognitionUpdateKind.Partial);
            Assert.That(r.User.Text, Is.EqualTo("new partial"));
        }

        [Test]
        public void NewPartialKeepsPriorityOverAnOlderOngoingAssistant()
        {
            var r = new Rig(); r.Start("old", "old request", order: 1); r.Present("old", "old answer", 1);
            r.Recognition("speech", "new question", SpeechRecognitionUpdateKind.Partial);
            r.Present("old", " continued", 1);
            Assert.That(r.User.Order, Is.GreaterThan(r.Assistant.Order));
            r.Recognition("speech", "new question", SpeechRecognitionUpdateKind.Confirmed, "new");
            r.Start("new", "new question", order: 2); r.Present("new", "new answer", 2);
            Assert.That(r.Assistant.Order, Is.GreaterThan(r.User.Order));
        }

        [Test]
        public void FilteredConfirmationClearsOnlyItsMatchingPartial()
        {
            var r = new Rig(); r.Recognition("speech", "partial", SpeechRecognitionUpdateKind.Partial);
            r.Recognition("speech", "$internal", SpeechRecognitionUpdateKind.Confirmed, "turn");
            Assert.That(r.User, Is.Null);
        }

        [Test]
        public void StartWakewordMetadataCanInvalidateItsEarlierPartial()
        {
            var r = new Rig(); r.Options.ShowWakewordMessages = false;
            r.Recognition("speech", "hello avatar", SpeechRecognitionUpdateKind.Partial);
            r.Recognition("speech", "hello avatar", SpeechRecognitionUpdateKind.Confirmed, "turn");
            var response = Response(SpeechPipelineResponseType.Start, "turn");
            response.Metadata = new JObject { ["recognized_text"] = "hello avatar", ["is_wakeword"] = true };
            r.State.OnResponse(response, 1);
            Assert.That(r.User, Is.Null);
        }

        [Test]
        public void ConsumersCannotMutateTheSourcesCurrentState()
        {
            var r = new Rig(); r.Start("turn", "question"); r.Present("turn", "answer");
            var snapshot = r.State.Current;
            snapshot.DisplayUserUtterance.Text = "mutated"; snapshot.DisplayAssistantUtterance.IsComplete = true; snapshot.CanListen = true;
            Assert.That(r.User.Text, Is.EqualTo("question")); Assert.That(r.Assistant.IsComplete, Is.False);
            Assert.That(r.State.Current.CanListen, Is.False);
        }

        [Test]
        public void SourcePolicyDoesNotHoldWindowsOrUnityObjects()
        {
            var fields = typeof(OrchestratorConversationState).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fields.Any(field => field.FieldType.Namespace?.StartsWith("ChatdollKit.UI", StringComparison.Ordinal) == true), Is.False);
            Assert.That(fields.Any(field => field.FieldType.Namespace?.StartsWith("UnityEngine", StringComparison.Ordinal) == true), Is.False);
        }

        [Test]
        public void HiddenRequestsRemainInTheGenericStreamAndRawSnapshot()
        {
            var r = new Rig(); r.Start("visible", "show me"); r.State.DrainEvents();
            r.Start("internal", "$refresh memory", order: 2);
            var facts = r.State.DrainEvents();
            var confirmed = facts.Single(item => item.Kind == ConversationEventKind.UserSpeechConfirmed);
            Assert.That(confirmed.Text, Is.EqualTo("$refresh memory"));
            Assert.That(confirmed.Utterance.IsDisplayAllowed, Is.False);
            Assert.That(r.State.Current.LatestUserUtterance.Text, Is.EqualTo("$refresh memory"));
            Assert.That(r.User.Text, Is.EqualTo("show me"));
        }

        [Test]
        public void OlderValidTurnsAreLoggedWithoutRegressingPresentation()
        {
            var r = new Rig(); r.Present("new", "new answer", 2);
            r.State.OnTurnEnded("new", OrchestratorTurnEndReason.Completed); r.State.DrainEvents();
            r.Start("old", "old question", order: 1); r.Present("old", "old answer", 1);
            var facts = r.State.DrainEvents();
            Assert.That(facts.Any(item => item.Kind == ConversationEventKind.UserSpeechConfirmed && item.Text == "old question"), Is.True);
            Assert.That(facts.Any(item => item.Kind == ConversationEventKind.AssistantSpeechStarted && item.VoiceText == "old answer"), Is.True);
            Assert.That(r.Assistant.DisplayText, Is.EqualTo("new answer"));
        }

        [Test]
        public void ResponseFallbackAndCustomAvatarStartPublishBothFactsWithoutDuplicatingCaption()
        {
            var r = new Rig();
            r.State.OnResponse(Response(SpeechPipelineResponseType.Chunk, "turn", "answer"), 1, useResponseText: true);
            r.State.OnPresentationStarted(new AvatarRequest { TransactionId = "turn", Text = "answer", VoiceText = "answer" }, 1, useResponseText: true);
            Assert.That(r.Assistant.DisplayText, Is.EqualTo("answer"));
            r.State.OnTurnEnded("turn", OrchestratorTurnEndReason.Completed);
            var facts = r.State.DrainEvents();
            Assert.That(facts.Select(item => item.Kind), Is.EqualTo(new[]
            { ConversationEventKind.AssistantGenerated, ConversationEventKind.AssistantSpeechStarted,
                ConversationEventKind.AssistantSpeechCompleted, ConversationEventKind.TurnEnded }));
            Assert.That(r.Assistant.IsComplete, Is.True);
        }

        [Test]
        public void GeneratedAndSpokenEventsRetainOriginalTextAndVoiceTextSeparately()
        {
            var r = new Rig();
            var chunk = Response(SpeechPipelineResponseType.Chunk, "turn", "spoken");
            chunk.Text = "<think>private</think>[face:smile]spoken";
            r.State.OnResponse(chunk, 1);
            Assert.That(r.Assistant, Is.Null);
            var generated = r.State.DrainEvents().Single();
            Assert.That(generated.Kind, Is.EqualTo(ConversationEventKind.AssistantGenerated));
            Assert.That(generated.Text, Is.EqualTo(chunk.Text));
            Assert.That(generated.Utterance.Text, Is.EqualTo(chunk.Text));
            Assert.That(generated.Utterance.VoiceText, Is.EqualTo("spoken"));
            r.State.OnPresentationStarted(new AvatarRequest { TransactionId = "turn", Text = chunk.Text, VoiceText = "spoken" }, 1);
            var spoken = r.State.DrainEvents().Single();
            Assert.That(spoken.Kind, Is.EqualTo(ConversationEventKind.AssistantSpeechStarted));
            Assert.That(spoken.Utterance.Id, Is.EqualTo(generated.Utterance.Id));
            Assert.That(r.Assistant.DisplayText, Is.EqualTo("spoken"));
        }

        [Test]
        public void GenerationCompletionIsPublishedWithoutCompletingSpeech()
        {
            var r = new Rig();
            r.State.OnResponse(Response(SpeechPipelineResponseType.Chunk, "turn", "answer"), 1);
            r.Present("turn", "answer"); r.State.DrainEvents();
            r.State.OnResponse(Response(SpeechPipelineResponseType.Final, "turn", "answer"), 1);
            var final = r.State.DrainEvents().Single();
            Assert.That(final.Kind, Is.EqualTo(ConversationEventKind.AssistantGenerationCompleted));
            Assert.That(final.Utterance.IsComplete, Is.False);
            Assert.That(r.Assistant.IsComplete, Is.False);
            r.State.OnTurnEnded("turn", OrchestratorTurnEndReason.Completed);
            var ended = r.State.DrainEvents();
            Assert.That(ended.Select(item => item.Kind), Is.EqualTo(new[]
            { ConversationEventKind.AssistantSpeechCompleted, ConversationEventKind.TurnEnded }));
            Assert.That(ended[0].Utterance.IsComplete, Is.True);
        }

        [Test]
        public void GeneratedContentWithoutPresentationDoesNotPublishSpeechCompletion()
        {
            var r = new Rig();
            r.State.OnResponse(Response(SpeechPipelineResponseType.Chunk, "turn", "answer"), 1);
            r.State.OnResponse(Response(SpeechPipelineResponseType.Final, "turn", "answer"), 1);
            r.State.OnTurnEnded("turn", OrchestratorTurnEndReason.Completed);
            var facts = r.State.DrainEvents();
            Assert.That(facts.Select(item => item.Kind), Is.EqualTo(new[]
            { ConversationEventKind.AssistantGenerated, ConversationEventKind.AssistantGenerationCompleted,
                ConversationEventKind.TurnEnded }));
            Assert.That(facts.Last().EndReason, Is.EqualTo(ConversationTurnEndReason.Completed));
        }

        [Test]
        public void GenerationCompletionRetainsAllGeneratedContentWhenPlaybackHasOnlyStartedTheFirstChunk()
        {
            var r = new Rig();
            var first = Response(SpeechPipelineResponseType.Chunk, "turn", "first ");
            first.Text = "[face:smile]first ";
            r.State.OnResponse(first, 1);
            r.State.OnResponse(Response(SpeechPipelineResponseType.Chunk, "turn", "second"), 1);
            r.State.OnPresentationStarted(new AvatarRequest
            { TransactionId = "turn", Text = first.Text, VoiceText = first.VoiceText }, 1);
            r.State.DrainEvents();
            var finalResponse = Response(SpeechPipelineResponseType.Final, "turn", "original final voice");
            finalResponse.Text = "original final text";
            r.State.OnResponse(finalResponse, 1);
            var final = r.State.DrainEvents().Single();
            Assert.That(final.Kind, Is.EqualTo(ConversationEventKind.AssistantGenerationCompleted));
            Assert.That(final.Utterance.Text, Is.EqualTo("[face:smile]first second"));
            Assert.That(final.Utterance.VoiceText, Is.EqualTo("first second"));
            Assert.That(final.Utterance.IsComplete, Is.False);
            Assert.That(final.Text, Is.EqualTo("original final text"));
            Assert.That(final.VoiceText, Is.EqualTo("original final voice"));
            Assert.That(r.Assistant.DisplayText, Is.EqualTo("first "));
        }

        [Test]
        public void SpeechCompletionContainsPresentedContentWhenNewerGenerationWasTheLastNotification()
        {
            var r = new Rig();
            r.State.OnResponse(Response(SpeechPipelineResponseType.Chunk, "turn", "presented"), 1);
            r.Present("turn", "presented", 1);
            var started = r.State.DrainEvents().Single(item => item.Kind == ConversationEventKind.AssistantSpeechStarted);
            r.State.OnResponse(Response(SpeechPipelineResponseType.Chunk, "turn", " generated later"), 1);
            r.State.OnTurnEnded("turn", OrchestratorTurnEndReason.Completed);
            var completed = r.State.DrainEvents().Single(item => item.Kind == ConversationEventKind.AssistantSpeechCompleted);
            Assert.That(completed.Utterance.Id, Is.EqualTo(started.Utterance.Id));
            Assert.That(completed.Utterance.Text, Is.EqualTo("presented"));
            Assert.That(completed.Utterance.VoiceText, Is.EqualTo("presented"));
            Assert.That(completed.Utterance.IsComplete, Is.True);
            Assert.That(r.State.Current.LatestAssistantUtterance.Text, Is.EqualTo("presented generated later"));
        }

        [Test]
        public void MatchingConfirmedRecognitionAndStartProduceOnlyOneConfirmationFact()
        {
            var r = new Rig();
            r.Recognition("speech", "hello", SpeechRecognitionUpdateKind.Partial);
            r.Recognition("speech", "hello world", SpeechRecognitionUpdateKind.Confirmed, "turn");
            r.Start("turn", "hello world", "$decorated", 1);
            var facts = r.State.DrainEvents();
            var speech = facts.Where(item => item.Kind == ConversationEventKind.UserSpeechPartial || item.Kind == ConversationEventKind.UserSpeechConfirmed).ToArray();
            Assert.That(speech.Length, Is.EqualTo(2));
            Assert.That(speech[1].Utterance.Id, Is.EqualTo(speech[0].Utterance.Id));
            Assert.That(facts.Last().Kind, Is.EqualTo(ConversationEventKind.TurnStarted));
            Assert.That((string)facts.Last().Metadata["request_text"], Is.EqualTo("$decorated"));
        }

        [Test]
        public void CanceledRecognitionPublishesItsIdentityWithoutDeletingANewerPartial()
        {
            var r = new Rig(); r.Recognition("old", "old", SpeechRecognitionUpdateKind.Partial);
            var old = r.User.Id;
            r.Recognition("new", "new", SpeechRecognitionUpdateKind.Partial); r.State.DrainEvents();
            r.Recognition("old", null, SpeechRecognitionUpdateKind.Canceled);
            var canceled = r.State.DrainEvents().Single();
            Assert.That(canceled.Kind, Is.EqualTo(ConversationEventKind.UserSpeechCanceled));
            Assert.That(canceled.Utterance.Id, Is.EqualTo(old));
            Assert.That(canceled.RecognitionId, Is.EqualTo("old"));
            Assert.That(canceled.Utterance.IsCanceled, Is.True);
            Assert.That(r.User.Text, Is.EqualTo("new"));
        }

        [Test]
        public void FilteredAssistantSegmentsRemainInRawCumulativeContent()
        {
            var r = new Rig(); r.State.ShouldShowMessage = context => context.Text != "private";
            r.Present("turn", "public "); r.Present("turn", "private"); r.Present("turn", " answer");
            var facts = r.State.DrainEvents();
            Assert.That(facts[1].Utterance.IsDisplayAllowed, Is.False);
            Assert.That(facts[1].Text, Is.EqualTo("private"));
            Assert.That(r.State.Current.LatestAssistantUtterance.Text, Is.EqualTo("public private answer"));
            Assert.That(r.Assistant.DisplayText, Is.EqualTo("public  answer"));
        }

        [Test]
        public void FailingDisplayFilterStillPublishesTheOriginalConversationFact()
        {
            var r = new Rig(); r.State.ShouldShowMessage = context => throw new InvalidOperationException("bad filter");
            Assert.DoesNotThrow(() => r.Start("turn", "actual user text"));
            var facts = r.State.DrainEvents();
            Assert.That(facts.Any(item => item.Kind == ConversationEventKind.Error && item.ErrorMessage == "bad filter"), Is.True);
            Assert.That(facts.Single(item => item.Kind == ConversationEventKind.UserSpeechConfirmed).Text, Is.EqualTo("actual user text"));
            Assert.That(r.State.Current.LatestUserUtterance.Text, Is.EqualTo("actual user text"));
            Assert.That(r.User, Is.Null);
        }

        [Test]
        public void MetadataAndSnapshotsDoNotShareMutableContentWithEventsOrInputs()
        {
            var r = new Rig(); var response = Response(SpeechPipelineResponseType.Start, "turn");
            response.Metadata = new JObject { ["recognized_text"] = "original", ["nested"] = new JObject { ["value"] = 1 } };
            r.State.OnResponse(response, 1); response.Metadata["nested"]["value"] = 2;
            var facts = r.State.DrainEvents(); facts[0].Metadata["nested"]["value"] = 3;
            facts[0].Utterance.Metadata["nested"]["value"] = 4;
            var snapshot = r.State.Current; snapshot.LatestUserUtterance.Metadata["nested"]["value"] = 5;
            Assert.That((int)facts[1].Metadata["nested"]["value"], Is.EqualTo(1));
            Assert.That((int)r.State.Current.LatestUserUtterance.Metadata["nested"]["value"], Is.EqualTo(1));
            Assert.That((int)r.State.Current.DisplayUserUtterance.Metadata["nested"]["value"], Is.EqualTo(1));
            Assert.That(r.State.DrainEvents(), Is.Empty);
        }

        [Test]
        public void LifecycleFactsKeepMonotonicSequencesAndClearCatchUpState()
        {
            var r = new Rig();
            r.State.OnLifecycle(ConversationEventKind.Started, "run", "session", 0, "context");
            r.Start("turn", "question"); r.Present("turn", "answer"); r.State.SetCanListen(true);
            r.State.OnLifecycle(ConversationEventKind.Interrupted, "run", "session", 1, "context");
            Assert.That(r.State.Current.IsRunning, Is.True);
            Assert.That(r.State.Current.LatestUserUtterance, Is.Null);
            Assert.That(r.State.Current.LatestAssistantUtterance, Is.Null);
            Assert.That(r.User, Is.Null); Assert.That(r.Assistant, Is.Null);
            Assert.That(r.State.Current.CanListen, Is.False);
            r.State.OnLifecycle(ConversationEventKind.Stopped, "run", "session", 1);
            var facts = r.State.DrainEvents();
            Assert.That(facts.Select(item => item.Sequence), Is.EqualTo(Enumerable.Range(1, facts.Length).Select(value => (long)value)));
            Assert.That(facts.All(item => item.RunId == "run" && item.SessionId == "session" && item.OccurredAtUtc.Kind == DateTimeKind.Utc), Is.True);
            Assert.That(r.State.Current.IsRunning, Is.False);
        }

        [Test]
        public void QueuedOlderBoundariesDoNotRegressAlreadyInvalidatedGeneration()
        {
            var r = new Rig(); r.State.OnLifecycle(ConversationEventKind.Started, "run", "session", 0);
            r.Start("old", "old question"); r.State.Invalidate(2);
            r.Start("new", "new question");
            r.State.OnLifecycle(ConversationEventKind.Interrupted, "run", "session", 1);
            r.State.OnLifecycle(ConversationEventKind.Reset, "run", "session", 2, "new context");
            var facts = r.State.DrainEvents();
            Assert.That(facts.Single(item => item.Kind == ConversationEventKind.Interrupted).Generation, Is.EqualTo(1));
            Assert.That(facts.Single(item => item.Kind == ConversationEventKind.Reset).Generation, Is.EqualTo(2));
            Assert.That(r.State.Current.Generation, Is.EqualTo(2));
            Assert.That(r.State.Current.ContextId, Is.EqualTo("new context"));
            Assert.That(r.User.Text, Is.EqualTo("new question"));
        }

        [Test]
        public void TurnsWithoutVisibleSpeechStillProduceTerminalFacts()
        {
            var r = new Rig(); r.Start("internal", "$hidden"); r.State.DrainEvents();
            r.State.OnTurnEnded("internal", OrchestratorTurnEndReason.Canceled);
            var ended = r.State.DrainEvents().Single();
            Assert.That(ended.Kind, Is.EqualTo(ConversationEventKind.TurnEnded));
            Assert.That(ended.EndReason, Is.EqualTo(ConversationTurnEndReason.Canceled));
            Assert.That(r.State.Current.LatestUserUtterance.IsCanceled, Is.True);
        }

        [Test]
        public void StructuredPayloadIsCopiedAndPublishedForLogConsumers()
        {
            var r = new Rig(); var response = Response(SpeechPipelineResponseType.ToolCall, "turn");
            response.ToolCall = new ChatdollKit.SpeechPipeline.LLM.LlmToolCall { Id = "call", Name = "lookup", Arguments = "{}" };
            response.StructuredContent = new JObject { ["value"] = 1 };
            r.State.OnResponse(response, 1);
            response.StructuredContent["value"] = 2;
            var fact = r.State.DrainEvents().Single();
            Assert.That(fact.Kind, Is.EqualTo(ConversationEventKind.ToolCalled));
            Assert.That((string)fact.ToolCall["Name"], Is.EqualTo("lookup"));
            Assert.That((int)fact.StructuredContent["value"], Is.EqualTo(1));
        }

        private static SpeechPipelineResponse Response(SpeechPipelineResponseType type, string id, string text = null) =>
            new SpeechPipelineResponse { Type = type, TransactionId = id, SessionId = "session", ContextId = "context", VoiceText = text, Text = text };

        private sealed class Rig
        {
            internal readonly ConversationDisplayOptions Options = new ConversationDisplayOptions();
            internal readonly OrchestratorConversationState State;
            internal ConversationUtterance User => State.Current.DisplayUserUtterance;
            internal ConversationUtterance Assistant => State.Current.DisplayAssistantUtterance;
            internal Rig() { State = new OrchestratorConversationState(Options); }
            internal void Start(string id, string recognized, string request = null, long order = 0)
            {
                var response = Response(SpeechPipelineResponseType.Start, id); response.Metadata = new JObject();
                if (recognized != null) response.Metadata["recognized_text"] = recognized;
                if (request != null) response.Metadata["request_text"] = request;
                State.OnResponse(response, order);
            }
            internal void Present(string id, string text, long order = 0) => State.OnPresentationStarted(new AvatarRequest
            { TransactionId = id, SessionId = "session", ContextId = "context", VoiceText = text, Text = text }, order);
            internal void Recognition(string id, string text, SpeechRecognitionUpdateKind kind, string transaction = null,
                bool? active = null, double? duration = null, double observedAt = 0) =>
                State.OnRecognitionUpdate(new SpeechRecognitionUpdate
                {
                    RecognitionId = id, Text = text, Kind = kind, TransactionId = transaction, SessionId = "session",
                    IsSpeechActive = active, AudioDurationSeconds = duration, ObservedAtSeconds = observedAt
                });
        }
    }
}

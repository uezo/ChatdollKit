using System;
using System.Collections;
using ChatdollKit.UI.MessageWindow;
using ChatdollKit.Orchestration;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Window = ChatdollKit.UI.MessageWindow.MessageWindow;

namespace ChatdollKit.Tests.Orchestration.Unity
{
    public class MessageWindowTests
    {
        private GameObject root;
        private GameObject secondaryRoot;
        private Window window;
        private float originalTimeScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return new EnterPlayMode();
            originalTimeScale = Time.timeScale;
            root = new GameObject("Message window test");
            secondaryRoot = null;
            window = root.AddComponent<Window>();
            window.MessageText = CreateText("Message");
            window.SpeakerText = CreateText("Speaker");
            window.PreGap = 0f;
            window.CharacterInterval = 0.05f;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = originalTimeScale;
            if (root != null) UnityEngine.Object.Destroy(root);
            if (secondaryRoot != null) UnityEngine.Object.Destroy(secondaryRoot);
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator PrefabHasValidUiReferencesAndShowsWithoutDisablingItsHost()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ChatdollKit/Prefabs/Runtime/Orchestration/MessageWindow.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.activeSelf, Is.True);
            UnityEngine.Object.Destroy(root);
            root = UnityEngine.Object.Instantiate(prefab);
            window = root.GetComponent<Window>();
            Assert.That(window, Is.Not.Null);
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                Assert.That(component, Is.Not.Null, "The prefab must have no missing scripts.");
            Assert.That(root.GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(window.MessageText, Is.Not.Null);
            Assert.That(window.SpeakerText, Is.Not.Null);
            Assert.That(window.ContentRoot, Is.Not.Null);
            Assert.That(window.MessageText.font, Is.Not.Null);
            Assert.That(window.SpeakerText.font, Is.Not.Null);
            Assert.That(window.MessageText.transform.IsChildOf(window.ContentRoot.transform), Is.True);
            Assert.That(window.SpeakerText.transform.IsChildOf(window.ContentRoot.transform), Is.True);
            Assert.That(root.activeSelf, Is.True);
            Assert.That(window.ContentRoot.activeSelf, Is.False);

            window.SetMessage(Message("prefab", "Ready to display", false));
            // The responsive layout needs the instantiated Canvas dimensions before sizing its panel.
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(window.IsVisible, Is.True);
            Assert.That(window.MessageText.text, Is.EqualTo("Ready to display"));
            Assert.That(window.SpeakerText.text, Is.EqualTo("AI"));
            Assert.That(window.MessageText.rectTransform.rect.width, Is.GreaterThan(0f));
            Assert.That(window.MessageText.rectTransform.rect.height, Is.GreaterThan(0f));
            window.Hide();
            Assert.That(root.activeSelf, Is.True);
            Assert.That(window.ContentRoot.activeSelf, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SameIdAppendPreservesTypedPrefixAndFinishesCumulativeText()
        {
            window.SetMessage(Message("response", "Hello"));
            yield return Until(() => window.MessageText.text.Length > 0);
            var displayed = window.MessageText.text;
            window.SetMessage(Message("response", "Hello world"));
            Assert.That(window.MessageText.text, Is.EqualTo(displayed));
            yield return Until(() => !window.IsAnimating);
            Assert.That(window.MessageText.text, Is.EqualTo("Hello world"));
            Assert.That(window.IsVisible, Is.True);
        }

        [UnityTest]
        public IEnumerator NewIdReplacesCurrentTypingAndHideClearsAllPendingText()
        {
            window.SetMessage(Message("old", "Old response"));
            yield return Until(() => window.MessageText.text.Length > 0);
            window.SetMessage(Message("new", "New response"));
            Assert.That(window.MessageId, Is.EqualTo("new"));
            Assert.That(window.MessageText.text, Is.Empty);
            yield return Until(() => window.MessageText.text.Length > 0);
            Assert.That("New response".StartsWith(window.MessageText.text, StringComparison.Ordinal), Is.True);
            window.Hide();
            Assert.That(window.IsVisible, Is.False);
            Assert.That(window.IsAnimating, Is.False);
            Assert.That(window.MessageId, Is.Null);
            Assert.That(window.MessageText.text, Is.Empty);
            Assert.That(window.SpeakerText.text, Is.Empty);
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(window.MessageText.text, Is.Empty);
            window.SetMessage(Message("after-hide", "Visible again", false));
            Assert.That(window.IsVisible, Is.True);
            Assert.That(window.MessageText.text, Is.EqualTo("Visible again"));
        }

        [UnityTest]
        public IEnumerator InstantCorrectionReplacesTextWithoutWaitingForAnimationOrPreGap()
        {
            window.PreGap = 10f;
            window.SetMessage(Message("user", "incorrect recognition"));
            Assert.That(window.IsAnimating, Is.True);
            var correction = Message("user", "correct recognition", false);
            correction.SpeakerName = "User";
            window.SetMessage(correction);
            Assert.That(window.MessageText.text, Is.EqualTo("correct recognition"));
            Assert.That(window.SpeakerText.text, Is.EqualTo("User"));
            Assert.That(window.IsAnimating, Is.False);
            yield return null;
            Assert.That(window.MessageText.text, Is.EqualTo("correct recognition"));
        }

        [UnityTest]
        public IEnumerator SeparateVisualChildCanBeHiddenAndShownWhileOwnerStaysActive()
        {
            var content = new GameObject("Content");
            content.transform.SetParent(root.transform, false);
            window.ContentRoot = content;
            window.MessageText.transform.SetParent(content.transform, false);
            window.SpeakerText.transform.SetParent(content.transform, false);
            window.SetMessage(Message("first", "First", false));
            window.Hide();
            Assert.That(root.activeSelf, Is.True);
            Assert.That(content.activeSelf, Is.False);
            window.SetMessage(Message("second", "Second", false));
            Assert.That(window.IsVisible, Is.True);
            Assert.That(content.activeSelf, Is.True);
            Assert.That(window.MessageText.text, Is.EqualTo("Second"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisableCancelsTypingAndReenableDoesNotRestoreOldMessage()
        {
            window.SetMessage(Message("old", "A response that is still being typed"));
            yield return Until(() => window.MessageText.text.Length > 0);
            window.enabled = false;
            Assert.That(window.MessageText.text, Is.Empty);
            Assert.That(window.IsAnimating, Is.False);
            Assert.That(window.MessageId, Is.Null);
            window.enabled = true;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(window.MessageText.text, Is.Empty);
            Assert.That(window.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator PausedGameStillRevealsWholeUnicodeTextElements()
        {
            Time.timeScale = 0f;
            window.CharacterInterval = 1f;
            window.SetMessage(Message("unicode", "\U0001F600e\u0301"));
            yield return Until(() => window.MessageText.text.Length > 0);
            Assert.That(window.MessageText.text, Is.EqualTo("\U0001F600"));
            yield return Until(() => !window.IsAnimating);
            Assert.That(window.MessageText.text, Is.EqualTo("\U0001F600e\u0301"));
        }

        [UnityTest]
        public IEnumerator SourceSnapshotIsDisplayedImmediatelyAndPartialTextWaitsForConfirmation()
        {
            var source = new FakeSource();
            var partial = SourceMessage("user", "I would", MessageSpeaker.User, 1, false);
            partial.IsPartial = true;
            source.Send(partial);
            window.Options.UserHoldSeconds = 0.05f;
            window.Options.AnimateUserText = true;
            window.PreGap = 10f;
            window.ConfigureSource(source);
            Assert.That(window.MessageText.text, Is.EqualTo("I would"));
            Assert.That(window.SpeakerText.text, Is.EqualTo("User"));
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(window.IsVisible, Is.True, "Partial recognition must remain until it is resolved.");

            partial.Text = partial.DisplayText = "I would like tea";
            source.Send(partial);
            Assert.That(window.MessageText.text, Is.EqualTo("I would like tea"));
            partial.IsPartial = false;
            partial.IsComplete = true;
            source.Send(partial);
            yield return Until(() => !window.IsVisible);
            source.Send(partial, canListen: true);
            yield return null;
            Assert.That(window.IsVisible, Is.False, "A state-only event must not restore a locally dismissed message.");
        }

        [UnityTest]
        public IEnumerator SpeechPromptIsImmediateConfigurableAndHeldUntilRecognitionResolves()
        {
            var source = new FakeSource();
            window.Options.UserSpeechPromptDelay = 0f;
            window.Options.UserHoldSeconds = 0f;
            window.Options.AnimateUserText = true;
            window.PreGap = 10f;
            window.ConfigureSource(source);
            source.Send(canListen: true);
            Assert.That(window.IsVisible, Is.False, "Being ready to listen is not a speech start.");

            var pending = SourceMessage("speech", null, MessageSpeaker.User, 2, false);
            pending.IsPartial = pending.IsAwaitingRecognition = true;
            source.Send(pending);
            Assert.That(window.MessageText.text, Is.EqualTo("…"));
            Assert.That(window.SpeakerText.text, Is.EqualTo("User"));
            Assert.That(window.IsAnimating, Is.False);
            yield return new WaitForSecondsRealtime(0.05f);
            Assert.That(window.IsVisible, Is.True);

            window.Options.UserSpeechPrompt = "...";
            yield return null;
            Assert.That(window.MessageText.text, Is.EqualTo("..."));
            window.Options.ShowUserSpeechPrompt = false;
            yield return null;
            Assert.That(window.IsVisible, Is.False);
            window.Options.ShowUserSpeechPrompt = true;
            yield return null;
            Assert.That(window.MessageText.text, Is.EqualTo("..."));

            pending.IsAwaitingRecognition = false;
            pending.Text = pending.DisplayText = "Hello";
            source.Send(pending);
            Assert.That(window.MessageText.text, Is.EqualTo("Hello"));
            Assert.That(window.MessageId, Is.EqualTo("speech"));
            pending.IsPartial = false;
            pending.IsComplete = true;
            source.Send(pending);
            yield return Until(() => !window.IsVisible);

            pending.Id = "next-speech";
            pending.Order++;
            pending.Text = pending.DisplayText = null;
            pending.IsPartial = pending.IsAwaitingRecognition = true;
            pending.IsComplete = false;
            source.Send(pending);
            Assert.That(window.MessageText.text, Is.EqualTo("..."));
            source.Send();
            Assert.That(window.IsVisible, Is.False, "A cleared or canceled recognition clears its prompt.");
            window.Options.ShowUserMessages = false;
            source.Send(pending);
            Assert.That(window.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator CompletionDuringTypingPreservesProgressAndHoldBeginsAfterTyping()
        {
            var source = new FakeSource();
            var message = SourceMessage("assistant", "Hello world", MessageSpeaker.Assistant, 1, false);
            window.Options.AssistantHoldSeconds = 0.12f;
            window.IsTextAnimated = false;
            window.ConfigureSource(source);
            window.IsTextAnimated = true;
            source.Send(assistant: message);
            yield return Until(() => window.MessageText.text.Length > 0);
            var prefix = window.MessageText.text;
            message.IsComplete = true;
            source.Send(assistant: message);
            Assert.That(window.MessageText.text, Is.EqualTo(prefix));
            yield return Until(() => !window.IsAnimating);
            Assert.That(window.MessageText.text, Is.EqualTo("Hello world"));
            yield return new WaitForSecondsRealtime(0.03f);
            Assert.That(window.IsVisible, Is.True);
            source.Send(assistant: message, canListen: true);
            yield return Until(() => !window.IsVisible);
        }

        [UnityTest]
        public IEnumerator IncompleteAssistantDoesNotAutoHideAndNullSnapshotClearsIt()
        {
            var source = new FakeSource();
            window.Options.AssistantHoldSeconds = 0f;
            window.IsTextAnimated = false;
            window.ConfigureSource(source);
            source.Send(assistant: SourceMessage("assistant", "Still speaking", MessageSpeaker.Assistant, 1, false));
            yield return new WaitForSecondsRealtime(0.05f);
            Assert.That(window.IsVisible, Is.True);
            source.Send();
            Assert.That(window.IsVisible, Is.False);
            Assert.That(root.activeSelf, Is.True, "A source-bound root must keep receiving subsequent messages.");
            Assert.That(root.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            source.Send(assistant: SourceMessage("next", "Next message", MessageSpeaker.Assistant, 2, false));
            Assert.That(window.MessageText.text, Is.EqualTo("Next message"));
            Assert.That(root.GetComponent<CanvasGroup>().blocksRaycasts, Is.True);
        }

        [UnityTest]
        public IEnumerator SourceSwapRejectsDelayedCallbacksAndDisableReenableCatchesUp()
        {
            var first = new FakeSource();
            var second = new FakeSource();
            window.IsTextAnimated = false;
            window.ConfigureSource(first);
            var delayed = first.CaptureHandlers();
            first.Send(assistant: SourceMessage("old", "Old source", MessageSpeaker.Assistant, 1, false));
            second.Send(assistant: SourceMessage("new", "New source", MessageSpeaker.Assistant, 1, false));
            window.IsTextAnimated = false;
            window.ConfigureSource(second);
            Assert.That(first.SubscriberCount, Is.Zero);
            delayed(new ConversationEvent { Sequence = 100 });
            Assert.That(window.MessageText.text, Is.EqualTo("New source"));

            window.enabled = false;
            Assert.That(second.SubscriberCount, Is.Zero);
            second.Send(assistant: SourceMessage("offscreen", "Arrived while disabled", MessageSpeaker.Assistant, 2, false));
            window.enabled = true;
            Assert.That(second.SubscriberCount, Is.EqualTo(1));
            Assert.That(window.MessageText.text, Is.EqualTo("Arrived while disabled"));
            window.IsTextAnimated = false;
            window.ConfigureSource(null);
            Assert.That(second.SubscriberCount, Is.Zero);
            Assert.That(window.IsVisible, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SharedWindowChoosesNewestMessageAndDoesNotRestoreReplacedOlderMessage()
        {
            var source = new FakeSource();
            var user = SourceMessage("user", "Question", MessageSpeaker.User, 1, false);
            var assistant = SourceMessage("assistant", "Answer", MessageSpeaker.Assistant, 2, false);
            window.IsTextAnimated = false;
            window.ConfigureSource(source);
            source.Send(user);
            Assert.That(window.MessageText.text, Is.EqualTo("Question"));
            source.Send(user, assistant);
            Assert.That(window.MessageText.text, Is.EqualTo("Answer"));
            source.Send(user);
            Assert.That(window.IsVisible, Is.False, "Clearing the newer message must not bring back an older replaced message.");
            source.Send();
            source.Send(SourceMessage("fresh", "A new sequence", MessageSpeaker.User, 1, false));
            Assert.That(window.MessageText.text, Is.EqualTo("A new sequence"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SeparateWindowsApplyTheirOwnSpeakerAndLifetimePreferences()
        {
            secondaryRoot = new GameObject("Assistant window");
            var assistantWindow = secondaryRoot.AddComponent<Window>();
            assistantWindow.MessageText = CreateText("Assistant message");
            assistantWindow.MessageText.transform.SetParent(secondaryRoot.transform, false);
            assistantWindow.Options.ShowUserMessages = false;
            assistantWindow.Options.AutoHideAssistant = false;
            assistantWindow.IsTextAnimated = false;
            window.Options.ShowAssistantMessages = false;
            window.Options.AutoHideUser = false;
            window.Options.UserSpeakerName = "You";
            var source = new FakeSource();
            window.IsTextAnimated = false;
            window.ConfigureSource(source);
            assistantWindow.ConfigureSource(source);
            source.Send(SourceMessage("user", "Question", MessageSpeaker.User, 1),
                SourceMessage("assistant", "Answer", MessageSpeaker.Assistant, 2));
            Assert.That(window.MessageText.text, Is.EqualTo("Question"));
            Assert.That(window.SpeakerText.text, Is.EqualTo("You"));
            Assert.That(assistantWindow.MessageText.text, Is.EqualTo("Answer"));
            window.Hide();
            Assert.That(assistantWindow.MessageText.text, Is.EqualTo("Answer"));
            Assert.That(assistantWindow.IsVisible, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OlderEventIsIgnoredAndSnapshotMutationsDoNotLeakIntoWindow()
        {
            var source = new FakeSource();
            window.IsTextAnimated = false;
            window.ConfigureSource(source);
            source.Send(assistant: SourceMessage("new", "Current", MessageSpeaker.Assistant, 10, false));
            var current = source.CurrentConversation;
            current.DisplayAssistantUtterance.DisplayText = "Modified without notification";
            source.CaptureHandlers()(new ConversationEvent { Sequence = 0 });
            yield return null;
            Assert.That(window.MessageText.text, Is.EqualTo("Current"));
        }

        [UnityTest]
        public IEnumerator ListeningPromptUsesAvailabilityAndInterruptForwardsToSource()
        {
            var source = new FakeSource();
            window.Options.ShowListeningPrompt = true;
            window.Options.ListeningPrompt = "Speak now";
            window.IsTextAnimated = false;
            window.ConfigureSource(source);
            source.Send(canListen: true);
            Assert.That(window.MessageText.text, Is.EqualTo("Speak now"));
            window.Interrupt();
            Assert.That(source.InterruptCalls, Is.EqualTo(1));
            source.Send(canListen: false);
            Assert.That(window.IsVisible, Is.False);
            source.ThrowOnInterrupt = true;
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Source interruption failed");
            Assert.DoesNotThrow(window.Interrupt);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InvalidInspectorSourceWarnsOnceAndDoesNotDeactivateTheRoot()
        {
            window.Source = window;
            LogAssert.Expect(LogType.Warning, "MessageWindow does not implement IConversationEventSource.");
            yield return null;
            yield return null;
            Assert.That(root.activeSelf, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ReadOnlyConversationSourceDoesNotNeedToImplementInterrupt()
        {
            window.ConfigureSource(new ReadOnlySource());
            Assert.DoesNotThrow(window.Interrupt);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisplayTextIsSelectedBySourceWithoutFallingBackToRawText()
        {
            var source = new FakeSource();
            window.IsTextAnimated = false;
            window.ConfigureSource(source);
            var utterance = SourceMessage("assistant", "Raw text with control tags", MessageSpeaker.Assistant, 1, false);
            utterance.DisplayText = "Spoken text";
            source.Send(assistant: utterance);
            Assert.That(window.MessageText.text, Is.EqualTo("Spoken text"));
            utterance.DisplayText = string.Empty;
            source.Send(assistant: utterance);
            Assert.That(window.IsVisible, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LocalSpeechPromptCountsVoiceAcrossBriefSilenceAndKeepsPreviousText()
        {
            var source = new FakeSource();
            window.IsTextAnimated = false;
            window.Options.AutoHideAssistant = false;
            window.ConfigureSource(source);
            var assistant = SourceMessage("ai", "Previous answer", MessageSpeaker.Assistant, 1);
            source.Send(assistant: assistant);
            var pending = Pending("voice", 2);
            source.Send(pending, assistant, item: Activity(pending, true, 0.4, 0, true));
            Assert.That(window.MessageText.text, Is.EqualTo("Previous answer"));
            source.Send(pending, assistant, item: Activity(pending, false, 0.15, 0.4));
            source.Send(pending, assistant, item: Activity(pending, true, 0.4, 0.55));
            Assert.That(window.MessageText.text, Is.EqualTo("Previous answer"), "Silence must not count as voice.");
            source.Send(pending, assistant, item: Activity(pending, false, 0.1, 0.95));
            source.Send(pending, assistant, item: Activity(pending, true, 0.2, 1.05));
            Assert.That(window.MessageText.text, Is.EqualTo("…"));
            source.Send(pending, assistant, item: Activity(pending, false, 0.4, 1.25));
            Assert.That(window.MessageText.text, Is.EqualTo("…"), "Once visible, short silence must not flicker the prompt.");
            Assert.That(source.CurrentConversation.DisplayUserUtterance.Text, Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LongSilenceRestartsCandidateButShortRecognitionTextIsImmediate()
        {
            var source = new FakeSource();
            window.ConfigureSource(source);
            var pending = Pending("voice", 1);
            source.Send(pending, item: Activity(pending, true, 0.6, 0, true));
            source.Send(pending, item: Activity(pending, false, 0.25, 0.6));
            source.Send(pending, item: Activity(pending, true, 0.4, 0.85));
            Assert.That(window.IsVisible, Is.False);
            pending.IsAwaitingRecognition = false;
            pending.Text = pending.DisplayText = "はい";
            source.Send(pending);
            Assert.That(window.MessageText.text, Is.EqualTo("はい"));
            source.Send(pending, item: Activity(pending, true, 1.1, 1.25));
            Assert.That(window.MessageText.text, Is.EqualTo("はい"), "Activity after a partial must not replace its text.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator EachWindowOwnsItsDelayWithoutDelayingSourceFacts()
        {
            var source = new FakeSource();
            secondaryRoot = new GameObject("Immediate subscriber");
            var immediate = secondaryRoot.AddComponent<Window>();
            immediate.MessageText = CreateText("Immediate text");
            immediate.Options.UserSpeechPromptDelay = 0;
            immediate.ConfigureSource(source);
            window.ConfigureSource(source);
            var pending = Pending("voice", 1);
            source.Send(pending, item: Activity(pending, true, 0.2, 0, true));
            Assert.That(window.IsVisible, Is.False);
            Assert.That(immediate.MessageText.text, Is.EqualTo("…"));
            Assert.That(source.CurrentConversation.DisplayUserUtterance.IsAwaitingRecognition, Is.True);
            pending.IsAwaitingRecognition = pending.IsPartial = false;
            pending.IsComplete = true;
            pending.Text = pending.DisplayText = "はい";
            source.Send(pending);
            Assert.That(window.MessageText.text, Is.EqualTo("はい"));
            Assert.That(immediate.MessageText.text, Is.EqualTo("はい"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RemoteSpeechPromptUsesObservationTimesWhenEventsAreDeliveredInOneFrame()
        {
            var source = new FakeSource();
            window.ConfigureSource(source);
            var pending = Pending("remote", 1);
            source.Send(pending, item: Activity(pending, true, null, 100, true));
            source.Send(pending, item: Activity(pending, true, null, 100.1));
            // A longer gap resets the candidate. These timestamps belong to the source clock.
            source.Send(pending, item: Activity(pending, true, null, 101));
            for (var i = 1; i <= 9; i++)
                source.Send(pending, item: Activity(pending, true, null, 101 + i * 0.1));
            Assert.That(window.IsVisible, Is.False);
            source.Send(pending, item: Activity(pending, true, null, 102));
            Assert.That(window.MessageText.text, Is.EqualTo("…"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PromptTimeoutOnlyHidesDisplayAndSameRecognitionCanResumeOrResolve()
        {
            var source = new FakeSource();
            window.Options.UserSpeechPromptDelay = 0.1f;
            window.Options.UserSpeechPromptTimeout = 0.05f;
            window.ConfigureSource(source);
            var pending = Pending("remote", 1);
            source.Send(pending, item: Activity(pending, true, 0.1, 0, true));
            Assert.That(window.MessageText.text, Is.EqualTo("…"));
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(window.IsVisible, Is.False);
            Assert.That(source.CurrentConversation.DisplayUserUtterance.IsAwaitingRecognition, Is.True);
            Assert.That(source.CurrentConversation.DisplayUserUtterance.IsCanceled, Is.False);
            source.Send(pending, item: Activity(pending, true, 0.1, 10));
            Assert.That(window.MessageText.text, Is.EqualTo("…"));
            yield return new WaitForSecondsRealtime(0.1f);
            pending.IsAwaitingRecognition = pending.IsPartial = false;
            pending.IsComplete = true;
            pending.Text = pending.DisplayText = "A late recognition";
            source.Send(pending);
            Assert.That(window.MessageText.text, Is.EqualTo("A late recognition"));
        }

        [UnityTest]
        public IEnumerator RebindingAndClearedProjectionDiscardPendingSpeechTiming()
        {
            var source = new FakeSource();
            window.ConfigureSource(source);
            var pending = Pending("voice", 1);
            source.Send(pending, item: Activity(pending, true, 0.6, 0, true));
            window.enabled = false;
            window.enabled = true;
            source.Send(pending, item: Activity(pending, true, 0.4, 0.6));
            Assert.That(window.IsVisible, Is.False, "Rebinding cannot inherit a previous subscription's duration.");
            source.Send();
            source.Send(pending, item: Activity(pending, true, 0.6, 1));
            Assert.That(window.IsVisible, Is.False, "Removing the display candidate must clear accumulated duration.");
            source.Send(pending, item: Activity(pending, true, 0.4, 1.6));
            Assert.That(window.MessageText.text, Is.EqualTo("…"));
            source.Send();
            Assert.That(window.IsVisible, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StartedWithoutFurtherVoiceDoesNotMatureByWallClockTime()
        {
            var source = new FakeSource();
            window.Options.UserSpeechPromptDelay = 0.05f;
            window.ConfigureSource(source);
            var previous = SourceMessage("previous-user", "Previous reply", MessageSpeaker.User, 1);
            window.Options.UserHoldSeconds = 0.05f;
            source.Send(previous);
            var pending = Pending("noise", 2);
            source.Send(pending, item: Activity(pending, true, 0.01, 0, true));
            Assert.That(window.MessageText.text, Is.EqualTo("Previous reply"));
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(window.IsVisible, Is.False, "The previous hold still expires and noise never becomes a prompt.");
        }

        [UnityTest]
        public IEnumerator CanceledNoisePreservesPreviousHoldAndSpeakerOptionsStillApply()
        {
            var source = new FakeSource();
            window.ConfigureSource(source);
            window.Options.UserHoldSeconds = 0.1f;
            source.Send(SourceMessage("previous", "Previous reply", MessageSpeaker.User, 1));
            var pending = Pending("noise", 2);
            source.Send(pending, item: Activity(pending, true, 0.01, 0, true));
            source.Send(item: new ConversationEvent
            {
                Kind = ConversationEventKind.UserSpeechCanceled, RecognitionId = pending.RecognitionId
            });
            Assert.That(window.MessageText.text, Is.EqualTo("Previous reply"));
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(window.IsVisible, Is.False);

            window.IsTextAnimated = false;
            var assistant = SourceMessage("ai", "Answer", MessageSpeaker.Assistant, 3);
            source.Send(assistant: assistant);
            pending = Pending("another-noise", 4);
            source.Send(pending, assistant, item: Activity(pending, true, 0.01, 1, true));
            window.Options.ShowAssistantMessages = false;
            yield return null;
            Assert.That(window.IsVisible, Is.False, "A waiting candidate must not keep a disabled speaker visible.");
        }

        private static ConversationUtterance Pending(string id, long order)
        {
            var result = SourceMessage(id, null, MessageSpeaker.User, order, false);
            result.RecognitionId = id;
            result.IsAwaitingRecognition = result.IsPartial = true;
            return result;
        }

        private static ConversationEvent Activity(ConversationUtterance pending, bool active,
            double? duration, double observedAt, bool started = false) => new ConversationEvent
        {
            Kind = started ? ConversationEventKind.UserSpeechStarted : ConversationEventKind.UserSpeechActivity,
            RecognitionId = pending.RecognitionId, IsSpeechActive = active,
            AudioDurationSeconds = duration, ObservedAtSeconds = observedAt
        };

        private Text CreateText(string name)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(root.transform, false);
            return textObject.GetComponent<Text>();
        }

        private static MessageWindowMessage Message(string id, string text, bool animate = true)
        {
            return new MessageWindowMessage
            {
                MessageId = id,
                Speaker = MessageSpeaker.Assistant,
                SpeakerName = "AI",
                Text = text,
                Animate = animate
            };
        }

        private static ConversationUtterance SourceMessage(string id, string text, MessageSpeaker speaker,
            long order, bool complete = true) => new ConversationUtterance
        {
            Id = id, Text = text, DisplayText = text,
            Speaker = speaker == MessageSpeaker.User ? ConversationSpeaker.User : ConversationSpeaker.Assistant,
            Order = order, IsComplete = complete, IsDisplayAllowed = true
        };

        private sealed class FakeSource : IConversationEventSource, IConversationControl
        {
            public ConversationSnapshot CurrentConversation { get; private set; } = new ConversationSnapshot();
            public event Action<ConversationEvent> ConversationEventReceived;
            public int SubscriberCount => ConversationEventReceived?.GetInvocationList().Length ?? 0;
            public int InterruptCalls;
            public bool ThrowOnInterrupt;
            public Action<ConversationEvent> CaptureHandlers() => ConversationEventReceived;

            public void Send(ConversationUtterance user = null, ConversationUtterance assistant = null,
                bool canListen = false, ConversationEvent item = null)
            {
                CurrentConversation = new ConversationSnapshot
                {
                    Sequence = CurrentConversation.Sequence + 1, DisplayUserUtterance = user?.Copy(),
                    DisplayAssistantUtterance = assistant?.Copy(), CanListen = canListen
                };
                item = item?.Copy() ?? new ConversationEvent { Kind = ConversationEventKind.ListeningChanged };
                item.Sequence = CurrentConversation.Sequence;
                ConversationEventReceived?.Invoke(item);
            }

            public void Interrupt()
            {
                InterruptCalls++;
                if (ThrowOnInterrupt) throw new InvalidOperationException("Source interruption failed");
            }
        }

        private sealed class ReadOnlySource : IConversationEventSource
        {
            public ConversationSnapshot CurrentConversation { get; } = new ConversationSnapshot();
            public event Action<ConversationEvent> ConversationEventReceived { add { } remove { } }
        }

        private static IEnumerator Until(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 5f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "Timed out waiting for the message window.");
        }
    }
}

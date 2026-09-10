using System;
using System.Globalization;
using ChatdollKit.Orchestration;
using UnityEngine;
using UnityEngine.UI;

namespace ChatdollKit.UI.MessageWindow
{
    /// <summary>Displays source snapshots without interpreting conversation state.</summary>
    [AddComponentMenu("ChatdollKit/UI/Message Window")]
    public class MessageWindow : MonoBehaviour, IMessageWindow
    {
        [Tooltip("A component implementing IConversationEventSource, such as ChatdollOrchestrator.")]
        [SerializeField] public MonoBehaviour Source;
        [SerializeField] public MessageWindowOptions Options = new MessageWindowOptions();
        [SerializeField] public Text MessageText;
        [SerializeField] public Text SpeakerText;
        [Tooltip("Optional speaker-label background. Hidden when the speaker name is empty.")]
        [SerializeField] public GameObject SpeakerBadge;
        [Tooltip("Visual child to show and hide. If unset or set to this object, a CanvasGroup hides the visuals while subscriptions stay active.")]
        [SerializeField] public GameObject ContentRoot;
        [SerializeField] public bool IsTextAnimated = true;
        [Min(0f)] [SerializeField] public float CharacterInterval = 0.03f;
        [Min(0f)] [SerializeField] public float PreGap = 0.1f;

        public string MessageId { get; private set; }
        public bool IsVisible => hasMessage && isActiveAndEnabled &&
            (HasVisualChild ? ContentRoot.activeInHierarchy : rootVisibility != null && rootVisibility.alpha > 0f);
        public bool IsAnimating => IsVisible && visibleElements < textElements.Length;

        private bool HasVisualChild => ContentRoot != null && ContentRoot != gameObject;
        private CanvasGroup rootVisibility;
        private string targetText = string.Empty;
        private int[] textElements = Array.Empty<int>();
        private int visibleElements;
        private float timeUntilNextCharacter;
        private bool hasMessage;
        private bool hasConfiguredSource;
        private IConversationEventSource configuredSource;
        private IConversationEventSource boundSource;
        private Action<ConversationEvent> sourceHandler;
        private MonoBehaviour warnedInvalidSource;
        private long bindingVersion;
        private ConversationSnapshot snapshot;
        private long latestSequence = long.MinValue;
        private long latestPresentedOrder = long.MinValue;
        private MessageWindowMessage displayedSourceMessage;
        private MessageWindowMessage dismissedMessage;
        private double holdStartedAt = double.NaN;
        private readonly UserSpeechPromptState speechPrompt = new UserSpeechPromptState();
        private bool preservePreviousAfterSpeechCancel;
        private const string ListeningMessageId = "message-window-listening";

        private void Awake() => ClearDisplay();
        private void OnEnable() => Safe(EnsureSourceBinding);

        /// <summary>Uses a source supplied by code. Passing null disconnects it, regardless of the Inspector field.</summary>
        public void ConfigureSource(IConversationEventSource source)
        {
            hasConfiguredSource = true;
            configuredSource = source;
            if (isActiveAndEnabled) Safe(EnsureSourceBinding);
        }

        /// <summary>Interrupts the source when it also implements the optional conversation control contract.</summary>
        public void Interrupt()
        {
            Safe(() =>
            {
                EnsureSourceBinding();
                (boundSource as IConversationControl)?.Interrupt();
            });
        }

        /// <summary>
        /// Growing snapshots with the same ID preserve the existing reveal progress.
        /// A different ID or corrected text replaces the current message.
        /// </summary>
        public void SetMessage(MessageWindowMessage message)
        {
            if (message == null)
            {
                Hide();
                return;
            }
            if (!enabled) return;

            if (!gameObject.activeSelf) gameObject.SetActive(true);
            SetVisualVisibility(true);

            var nextText = message.Text ?? string.Empty;
            var append = hasMessage && MessageId == message.MessageId &&
                nextText.StartsWith(targetText, StringComparison.Ordinal);
            if (!append)
            {
                visibleElements = 0;
                timeUntilNextCharacter = Mathf.Max(0f, PreGap);
            }

            hasMessage = true;
            MessageId = message.MessageId;
            targetText = nextText;
            textElements = StringInfo.ParseCombiningCharacters(targetText);
            visibleElements = Math.Min(visibleElements, textElements.Length);
            if (!message.Animate || !IsTextAnimated || CharacterInterval <= 0f)
            {
                visibleElements = textElements.Length;
                timeUntilNextCharacter = Mathf.Max(0f, CharacterInterval);
            }

            if (SpeakerText != null) SpeakerText.text = message.SpeakerName ?? string.Empty;
            if (SpeakerBadge != null) SpeakerBadge.SetActive(SpeakerText != null && !string.IsNullOrWhiteSpace(SpeakerText.text));
            RenderText();
        }

        private void Update()
        {
            Safe(() =>
            {
                EnsureSourceBinding();
                RefreshSourceDisplay();
                UpdateTyping();
                UpdateHold();
            });
        }

        private void EnsureSourceBinding()
        {
            var nextSource = hasConfiguredSource ? configuredSource : Source as IConversationEventSource;
            // Unity objects may have been destroyed while their interface reference remains non-null.
            if (nextSource is UnityEngine.Object unitySource && unitySource == null) nextSource = null;
            if (!hasConfiguredSource && Source != null && nextSource == null && warnedInvalidSource != Source)
            {
                warnedInvalidSource = Source;
                Debug.LogWarning($"{Source.GetType().Name} does not implement IConversationEventSource.", this);
            }
            if (ReferenceEquals(nextSource, boundSource)) return;

            UnbindSource();
            if (nextSource == null) return;
            boundSource = nextSource;
            var version = bindingVersion;
            sourceHandler = conversationEvent => Safe(() =>
            {
                if (!isActiveAndEnabled || version != bindingVersion || !ReferenceEquals(nextSource, boundSource)) return;
                if (conversationEvent != null && conversationEvent.Sequence < latestSequence) return;
                ReceiveSnapshot(nextSource.CurrentConversation, conversationEvent);
            });
            boundSource.ConversationEventReceived += sourceHandler;
            // Subscribe before taking the snapshot so enabling a window also catches up to current output.
            ReceiveSnapshot(boundSource.CurrentConversation);
        }

        private void ReceiveSnapshot(ConversationSnapshot state, ConversationEvent item = null)
        {
            if (state != null && state.Sequence < latestSequence) return;
            preservePreviousAfterSpeechCancel = item?.Kind == ConversationEventKind.UserSpeechCanceled &&
                snapshot?.DisplayUserUtterance?.IsAwaitingRecognition == true &&
                item.RecognitionId == snapshot.DisplayUserUtterance.RecognitionId &&
                displayedSourceMessage != null && !displayedSourceMessage.IsAwaitingRecognition;
            snapshot = state?.Copy() ?? new ConversationSnapshot();
            latestSequence = state?.Sequence ?? latestSequence;
            speechPrompt.Receive(snapshot, item, Time.realtimeSinceStartupAsDouble,
                Options ?? (Options = new MessageWindowOptions()));
            if (snapshot.DisplayUserUtterance == null && snapshot.DisplayAssistantUtterance == null &&
                !preservePreviousAfterSpeechCancel)
            {
                // A cleared source also permits a new sequence with freshly assigned display orders.
                latestPresentedOrder = long.MinValue;
                dismissedMessage = null;
            }
            RefreshSourceDisplay();
        }

        private MessageWindowMessage ToMessage(ConversationUtterance utterance, MessageWindowOptions options) =>
            utterance == null || (utterance.IsAwaitingRecognition &&
                (!options.ShowUserSpeechPrompt || !speechPrompt.CanShow(Time.realtimeSinceStartupAsDouble, options))) ? null : new MessageWindowMessage
        {
            MessageId = utterance.Id,
            Speaker = utterance.Speaker == ConversationSpeaker.User ? MessageSpeaker.User : MessageSpeaker.Assistant,
            Text = utterance.IsAwaitingRecognition ? options.UserSpeechPrompt : utterance.DisplayText,
            Order = utterance.Order,
            IsComplete = utterance.IsComplete,
            IsPartial = utterance.IsPartial,
            IsAwaitingRecognition = utterance.IsAwaitingRecognition,
            Animate = !utterance.IsPartial && !utterance.IsAwaitingRecognition
        };

        private void RefreshSourceDisplay()
        {
            if (boundSource == null || snapshot == null) return;
            var options = Options ?? (Options = new MessageWindowOptions());
            var user = options.ShowUserMessages ? ToMessage(snapshot.DisplayUserUtterance, options) : null;
            var assistant = options.ShowAssistantMessages ? ToMessage(snapshot.DisplayAssistantUtterance, options) : null;
            var next = user == null ? assistant : assistant == null || user.Order > assistant.Order ? user : assistant;
            if (next != null && !string.IsNullOrEmpty(next.Text) && next.Order >= latestPresentedOrder)
            {
                if (SameContent(next, dismissedMessage))
                {
                    ShowListeningIfAvailable(options);
                    return;
                }

                var display = next.Copy();
                display.SpeakerName = display.Speaker == MessageSpeaker.User ? options.UserSpeakerName : options.AssistantSpeakerName;
                if (display.Speaker == MessageSpeaker.User) display.Animate = options.AnimateUserText && display.Animate;
                var changed = !SameContent(display, displayedSourceMessage) ||
                    display.SpeakerName != displayedSourceMessage?.SpeakerName || display.Animate != displayedSourceMessage?.Animate;
                if (changed || !hasMessage || MessageId != display.MessageId)
                {
                    var textChanged = !SameContent(display, displayedSourceMessage);
                    SetMessage(display);
                    if (textChanged) holdStartedAt = double.NaN;
                }
                if (!display.IsComplete || display.IsPartial) holdStartedAt = double.NaN;
                displayedSourceMessage = display;
                latestPresentedOrder = Math.Max(latestPresentedOrder, display.Order);
                return;
            }

            // A pending placeholder must not erase the previous text or restart its hold timer.
            // It participates in display ordering only after this window's delay has elapsed.
            if (((options.ShowUserMessages && snapshot.DisplayUserUtterance?.IsAwaitingRecognition == true && user == null) ||
                preservePreviousAfterSpeechCancel) && hasMessage && displayedSourceMessage != null &&
                !displayedSourceMessage.IsAwaitingRecognition &&
                (displayedSourceMessage.Speaker == MessageSpeaker.User ? options.ShowUserMessages : options.ShowAssistantMessages)) return;
            ShowListeningIfAvailable(options);
        }

        private void ShowListeningIfAvailable(MessageWindowOptions options)
        {
            displayedSourceMessage = null;
            holdStartedAt = double.NaN;
            if (!options.ShowListeningPrompt || !options.ShowUserMessages || !snapshot.CanListen ||
                string.IsNullOrEmpty(options.ListeningPrompt))
            {
                if (hasMessage) ClearDisplay();
                return;
            }
            if (MessageId == ListeningMessageId && targetText == options.ListeningPrompt) return;
            SetMessage(new MessageWindowMessage
            {
                MessageId = ListeningMessageId, Speaker = MessageSpeaker.Status,
                Text = options.ListeningPrompt, Animate = false
            });
        }

        private void UpdateHold()
        {
            var message = displayedSourceMessage;
            if (message == null || !hasMessage || MessageId != message.MessageId ||
                !message.IsComplete || message.IsPartial || IsAnimating) return;
            var options = Options;
            var autoHide = message.Speaker == MessageSpeaker.User ? options.AutoHideUser : options.AutoHideAssistant;
            if (!autoHide) { holdStartedAt = double.NaN; return; }
            var now = Time.realtimeSinceStartupAsDouble;
            if (double.IsNaN(holdStartedAt)) holdStartedAt = now;
            var hold = message.Speaker == MessageSpeaker.User ? options.UserHoldSeconds : options.AssistantHoldSeconds;
            if (float.IsNaN(hold) || float.IsInfinity(hold)) hold = 0f;
            if (now - holdStartedAt >= Math.Max(0f, hold)) Hide();
        }

        // Completion changes alone neither replay text nor restart a hold timer.
        private static bool SameContent(MessageWindowMessage left, MessageWindowMessage right) =>
            left != null && right != null && left.MessageId == right.MessageId && left.Order == right.Order &&
            left.Speaker == right.Speaker && left.Text == right.Text;

        private void UpdateTyping()
        {
            if (!IsAnimating) return;
            if (!IsTextAnimated || CharacterInterval <= 0f)
            {
                visibleElements = textElements.Length;
                RenderText();
                return;
            }

            timeUntilNextCharacter -= Time.unscaledDeltaTime;
            if (timeUntilNextCharacter > 0f) return;
            while (timeUntilNextCharacter <= 0f && visibleElements < textElements.Length)
            {
                visibleElements++;
                timeUntilNextCharacter += CharacterInterval;
            }
            if (visibleElements == textElements.Length) timeUntilNextCharacter = CharacterInterval;
            RenderText();
        }

        private void RenderText()
        {
            if (MessageText == null) return;
            var end = visibleElements < textElements.Length ? textElements[visibleElements] : targetText.Length;
            MessageText.text = targetText.Substring(0, end);
        }

        /// <summary>Dismisses the current snapshot locally until its text changes or a new message arrives.</summary>
        public void Hide()
        {
            if (displayedSourceMessage != null) dismissedMessage = displayedSourceMessage.Copy();
            ClearDisplay();
        }

        private void OnDisable()
        {
            Safe(DetachSource);
            // During GameObject destruction Unity may already have destroyed other components.
            // Hide an existing visual, but never create replacement components in this callback.
            ClearDisplay(false);
        }

        private void OnDestroy()
        {
            Safe(DetachSource);
            ResetDisplayState();
        }

        private void UnbindSource()
        {
            ClearDisplay();
            DetachSource();
        }

        private void DetachSource()
        {
            var previous = boundSource;
            var handler = sourceHandler;
            boundSource = null;
            sourceHandler = null;
            bindingVersion++;
            snapshot = null;
            latestSequence = long.MinValue;
            latestPresentedOrder = long.MinValue;
            dismissedMessage = null;
            speechPrompt.Reset();
            preservePreviousAfterSpeechCancel = false;
            if (previous != null && handler != null) previous.ConversationEventReceived -= handler;
        }

        private void ClearDisplay(bool createVisibility = true)
        {
            ResetDisplayState();
            if (MessageText != null) MessageText.text = string.Empty;
            if (SpeakerText != null) SpeakerText.text = string.Empty;
            if (SpeakerBadge != null) SpeakerBadge.SetActive(false);
            SetVisualVisibility(false, createVisibility);
        }

        private void ResetDisplayState()
        {
            hasMessage = false;
            MessageId = null;
            targetText = string.Empty;
            textElements = Array.Empty<int>();
            visibleElements = 0;
            timeUntilNextCharacter = 0f;
            displayedSourceMessage = null;
            holdStartedAt = double.NaN;
        }

        private void SetVisualVisibility(bool visible, bool createVisibility = true)
        {
            if (HasVisualChild)
            {
                // A fallback CanvasGroup may have been created before Inspector references were assigned.
                if (rootVisibility != null)
                {
                    rootVisibility.alpha = 1f;
                    rootVisibility.interactable = true;
                    rootVisibility.blocksRaycasts = true;
                }
                if (ContentRoot.activeSelf != visible) ContentRoot.SetActive(visible);
                return;
            }
            if (rootVisibility == null)
            {
                rootVisibility = GetComponent<CanvasGroup>();
                // Use Unity's null comparison, not ??, because destroyed Unity objects can
                // retain a managed reference after their native component has gone away.
                if (rootVisibility == null && createVisibility) rootVisibility = gameObject.AddComponent<CanvasGroup>();
            }
            if (rootVisibility == null) return;
            rootVisibility.alpha = visible ? 1f : 0f;
            rootVisibility.interactable = visible;
            rootVisibility.blocksRaycasts = visible;
        }

        private void Safe(Action action)
        {
            try { action(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }
}

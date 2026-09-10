using System;
using System.Collections.Generic;
using ChatdollKit.Avatar;
using ChatdollKit.SpeechPipeline;
using UnityEngine.Serialization;

namespace ChatdollKit.Orchestration
{
    public sealed partial class ChatdollOrchestrator : IConversationEventSource, IConversationControl
    {
        [FormerlySerializedAs("MessageOptions")]
        public ConversationDisplayOptions DisplayOptions = new ConversationDisplayOptions();
        public Func<ConversationMessageContext, bool> ShouldShowMessage { get; set; }
        public event Action<ConversationEvent> ConversationEventReceived;

        private readonly OrchestratorConversationState conversation = new OrchestratorConversationState();
        private readonly Queue<Action> conversationChanges = new Queue<Action>();
        private readonly Queue<ConversationEvent> conversationEvents = new Queue<ConversationEvent>();
        private Run conversationRun;
        private bool changingConversation, publishingConversation;

        /// <summary>Current facts and presentation decisions, not an event-history replay.
        /// Read and subscribe on Unity's main thread.</summary>
        public ConversationSnapshot CurrentConversation
        {
            get { RefreshConversationState(); return conversation.Current; }
        }

        public void Interrupt()
        {
            if (IsRunning) _ = ObserveAsync(() => InterruptAsync());
        }

        private void AttachConversationSource(Run run)
        {
            conversationRun = run;
            run.MessageGeneration = run.Core.Generation;
            // Only the actual playback target supplies prepared speech-start notifications.
            run.MessageAvatar = run.Core.Avatar as AvatarController;
            if (run.MessageAvatar != null)
            {
                run.MessageStartedHandler = request =>
                {
                    if (!IsCurrentConversationRun(run)) return;
                    RefreshConversationState();
                    if ((request.SessionId == null || request.SessionId == run.Core.SessionId) &&
                        run.Core.TryGetActivePresentationContext(request.TransactionId, out var generation, out var order) &&
                        generation == run.MessageGeneration)
                        ChangeConversation(() => conversation.OnPresentationStarted(request, order));
                };
                run.MessageAvatar.PresentationStarted += run.MessageStartedHandler;
            }
            run.RecognitionSource = run.Pipeline as ISpeechRecognitionSource;
            if (run.RecognitionSource != null)
            {
                run.RecognitionHandler = update =>
                {
                    var snapshot = update?.Copy();
                    // Provider callbacks may hold their own lock. These core reads are lock-free.
                    var generation = run.Core.Generation;
                    if ((snapshot?.Kind == SpeechRecognitionUpdateKind.Partial ||
                        snapshot?.Kind == SpeechRecognitionUpdateKind.Started ||
                        snapshot?.Kind == SpeechRecognitionUpdateKind.Activity) && !run.Core.AdmitsRecognition) return;
                    Post(run, () =>
                    {
                        if (!IsCurrentConversationRun(run) || snapshot == null || generation != run.Core.Generation ||
                            (snapshot.SessionId != null && snapshot.SessionId != run.Core.SessionId)) return;
                        RefreshConversationState();
                        ChangeConversation(() =>
                        {
                            // Recheck at application time: input can be suppressed while queued,
                            // including by a subscriber called during RefreshConversationState.
                            if (!IsCurrentConversationRun(run) || generation != run.Core.Generation) return;
                            if ((snapshot.Kind == SpeechRecognitionUpdateKind.Partial ||
                                snapshot.Kind == SpeechRecognitionUpdateKind.Started ||
                                snapshot.Kind == SpeechRecognitionUpdateKind.Activity) && !run.Core.AdmitsRecognition) return;
                            run.Core.TryGetTurnOrder(snapshot.TransactionId, out var order);
                            conversation.OnRecognitionUpdate(snapshot, order);
                        });
                    });
                };
                run.RecognitionSource.RecognitionUpdated += run.RecognitionHandler;
            }
        }

        private void DetachConversationSource(Run run)
        {
            if (run.MessageAvatar != null) run.MessageAvatar.PresentationStarted -= run.MessageStartedHandler;
            if (run.RecognitionSource != null) run.RecognitionSource.RecognitionUpdated -= run.RecognitionHandler;
            run.MessageAvatar = null;
            run.RecognitionSource = null;
            // NotifyRunStopped publishes the boundary synchronously, including during OnDisable.
        }

        private bool IsCurrentConversationRun(Run run) => ReferenceEquals(active, run) && run.Accepting;

        private void UpdateResponseConversation(Run run, OrchestratorResponseEvent observed)
        {
            if (!IsCurrentConversationRun(run) || !ReferenceEquals(observed.Source, run.Core) ||
                observed.Generation != run.Core.Generation) return;
            RefreshConversationState();
            ChangeConversation(() => conversation.OnResponse(observed.Response, observed.TurnOrder, run.MessageAvatar == null));
        }

        private void UpdateEndedConversation(Run run, OrchestratorTurnResult result)
        {
            if (!IsCurrentConversationRun(run) || !ReferenceEquals(result.Source, run.Core) ||
                result.Generation != run.Core.Generation) return;
            RefreshConversationState();
            ChangeConversation(() => conversation.OnTurnEnded(result.TransactionId, result.Reason));
        }

        private void UpdateConversationLifecycle(OrchestratorLifecycleEvent boundary)
        {
            var run = conversationRun;
            if (run == null || !ReferenceEquals(boundary.Source, run.Core)) return;
            ConversationEventKind kind;
            switch (boundary.Kind)
            {
                case OrchestratorLifecycleKind.Started: kind = ConversationEventKind.Started; break;
                case OrchestratorLifecycleKind.Stopped: kind = ConversationEventKind.Stopped; break;
                case OrchestratorLifecycleKind.Interrupted: kind = ConversationEventKind.Interrupted; break;
                case OrchestratorLifecycleKind.Reset: kind = ConversationEventKind.Reset; break;
                default: return;
            }
            ChangeConversation(() => conversation.OnLifecycle(kind, run.ConversationRunId,
                boundary.SessionId, boundary.Generation, boundary.ContextId));
        }

        private void RefreshConversationState() => ChangeConversation(SynchronizeConversationState);

        private void SynchronizeConversationState()
        {
            var run = active;
            if (run == null || !run.Accepting || !ReferenceEquals(run, conversationRun)) return;
            var generation = run.Core.Generation;
            if (run.MessageGeneration != generation)
            {
                run.MessageGeneration = generation;
                // Invalidate immediately; the exact lifecycle fact arrives through the core's FIFO.
                conversation.Invalidate(generation);
            }
            var microphone = run.Microphone;
            conversation.SetCanListen(!run.Core.HasActiveTurns && !run.Core.IsInputSuppressed &&
                microphone != null && microphone.IsRecording && !microphone.IsMuted);
            if (run.Core.IsInputSuppressed) conversation.SuppressAwaitingRecognition();
        }

        private void ChangeConversation(Action change)
        {
            conversationChanges.Enqueue(change);
            if (changingConversation) return;
            changingConversation = true;
            try
            {
                // A filter can interrupt synchronously. Defer nested state writes until its current
                // fact is recorded, then invalidate before observers read the presentation snapshot.
                while (conversationChanges.Count > 0)
                {
                    conversation.Options = DisplayOptions ?? (DisplayOptions = new ConversationDisplayOptions());
                    conversation.ShouldShowMessage = ShouldShowMessage;
                    try { conversationChanges.Dequeue()(); }
                    catch (Exception error) { conversation.OnError(error); Report(error); }
                    SynchronizeConversationState();
                    foreach (var item in conversation.DrainEvents()) conversationEvents.Enqueue(item);
                }
            }
            finally { changingConversation = false; }
            PublishConversationEvents();
        }

        private void PublishConversationEvents()
        {
            if (publishingConversation) return;
            publishingConversation = true;
            try
            {
                while (conversationEvents.Count > 0)
                    Publish(ConversationEventReceived, conversationEvents.Dequeue(), value => value.Copy());
            }
            finally { publishingConversation = false; }
        }
    }
}

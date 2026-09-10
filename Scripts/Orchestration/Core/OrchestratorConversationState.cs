using System;
using System.Collections.Generic;
using ChatdollKit.Avatar;
using ChatdollKit.SpeechPipeline;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.Orchestration
{
    /// <summary>Publishes conversation facts and keeps a presentation projection of those facts.
    /// The orchestrator validates event lifetime and supplies authoritative turn ordering/completion.
    /// No window, Unity object or presentation timing loop is owned here.</summary>
    public sealed class OrchestratorConversationState
    {
        private sealed class Turn
        {
            internal long Order;
            internal bool ExcludedFromProjection, UserConfirmed, SpeechStarted;
            internal string AssistantId;
            internal ConversationUtterance User, Assistant, GeneratedAssistant, SpokenAssistant, DisplayAssistant;
            internal string GeneratedText = "", GeneratedVoice = "", SpokenText = "", SpokenVoice = "";
        }

        private sealed class Recognition
        {
            internal long Order;
            internal string TransactionId;
            internal ConversationUtterance Utterance;
            internal bool Confirmed, Canceled;
        }

        private readonly ConversationSnapshot current = new ConversationSnapshot();
        private readonly Queue<ConversationEvent> events = new Queue<ConversationEvent>();
        private readonly Dictionary<string, Turn> turns = new Dictionary<string, Turn>();
        private readonly Dictionary<string, Recognition> recognitions = new Dictionary<string, Recognition>();
        private readonly Dictionary<string, Recognition> recognitionByTurn = new Dictionary<string, Recognition>();
        private readonly Queue<string> recognitionIds = new Queue<string>();
        private readonly string idPrefix = Guid.NewGuid().ToString("N") + "-";
        private long nextId, nextOrder, latestTurnOrder, userOrder, assistantOrder;

        public ConversationDisplayOptions Options { get; set; }
        public Func<ConversationMessageContext, bool> ShouldShowMessage { get; set; }
        public ConversationSnapshot Current => current.Copy();

        public OrchestratorConversationState(ConversationDisplayOptions options = null)
        {
            Options = options ?? new ConversationDisplayOptions();
        }

        /// <summary>Returns every queued fact in order. Neither a consumer nor its metadata edits
        /// can mutate the current snapshot or an event retained by another consumer.</summary>
        public ConversationEvent[] DrainEvents()
        {
            var result = new ConversationEvent[events.Count];
            for (var i = 0; i < result.Length; i++) result[i] = events.Dequeue().Copy();
            return result;
        }

        public bool OnResponse(SpeechPipelineResponse response, long turnOrder, bool useResponseText = false)
        {
            if (response == null) return false;
            switch (response.Type)
            {
                case SpeechPipelineResponseType.Start:
                    return OnStart(response, turnOrder);
                case SpeechPipelineResponseType.Chunk:
                    return OnGenerated(response, turnOrder, useResponseText);
                default:
                    var kind = response.Type == SpeechPipelineResponseType.Accepted ? ConversationEventKind.TurnAccepted :
                        response.Type == SpeechPipelineResponseType.Final ? ConversationEventKind.AssistantGenerationCompleted :
                        response.Type == SpeechPipelineResponseType.ToolCall ? ConversationEventKind.ToolCalled :
                        response.Type == SpeechPipelineResponseType.Error ? ConversationEventKind.Error : ConversationEventKind.ResponseReceived;
                    var item = FromResponse(kind, response);
                    if (kind == ConversationEventKind.Error) item.ErrorMessage = response.Text;
                    if (response.TransactionId != null && turns.TryGetValue(response.TransactionId, out var turn))
                        item.Utterance = (kind == ConversationEventKind.AssistantGenerationCompleted
                            ? turn.GeneratedAssistant : turn.Assistant)?.Copy();
                    Emit(item);
                    return true;
            }
        }

        private bool OnStart(SpeechPipelineResponse response, long turnOrder)
        {
            if (string.IsNullOrEmpty(response.TransactionId)) { Emit(FromResponse(ConversationEventKind.TurnStarted, response)); return true; }
            var turn = GetTurn(response.TransactionId, turnOrder);
            var recognized = response.Metadata?["recognized_text"];
            // Explicitly empty recognition must not reveal a decorated request_text.
            var text = recognized?.Type == JTokenType.String ? (string)recognized : String(response.Metadata?["request_text"]);
            var duplicateConfirmation = turn.UserConfirmed && turn.User?.Text == text;
            var user = CreateUser(turn.User?.Id ?? NewId(), text, turn.Order, response.TransactionId,
                turn.User?.RecognitionId, response.SessionId, response.ContextId, response.Metadata, false);
            turn.User = user;
            turn.UserConfirmed = true;
            if (recognitionByTurn.TryGetValue(response.TransactionId, out var recognition)) recognition.Confirmed = true;
            current.LatestUserUtterance = user.Copy();
            SetUserProjection(user, !turn.ExcludedFromProjection);
            var started = FromResponse(ConversationEventKind.TurnStarted, response);
            started.Utterance = user.Copy();
            Emit(started);
            if (!duplicateConfirmation)
                Emit(UserEvent(ConversationEventKind.UserSpeechConfirmed, user));
            return true;
        }

        private bool OnGenerated(SpeechPipelineResponse response, long turnOrder, bool useResponseText)
        {
            if (string.IsNullOrEmpty(response.TransactionId)) { Emit(FromResponse(ConversationEventKind.AssistantGenerated, response)); return true; }
            var turn = GetTurn(response.TransactionId, turnOrder);
            turn.GeneratedText += response.Text ?? "";
            turn.GeneratedVoice += response.VoiceText ?? "";
            var assistant = CreateAssistant(turn, response.TransactionId, response.SessionId, response.ContextId,
                turn.GeneratedText, turn.GeneratedVoice, response.Metadata, response.VoiceText);
            if (Options.AssistantTiming == AssistantMessageTiming.ResponseReceived || useResponseText)
                SetAssistantProjection(turn, assistant, response.VoiceText);
            turn.Assistant = assistant;
            turn.GeneratedAssistant = assistant.Copy();
            current.LatestAssistantUtterance = assistant.Copy();
            var item = FromResponse(ConversationEventKind.AssistantGenerated, response);
            item.Utterance = assistant.Copy();
            Emit(item);
            return true;
        }

        public bool OnPresentationStarted(AvatarRequest request, long turnOrder, bool useResponseText = false)
        {
            if (request == null || string.IsNullOrEmpty(request.TransactionId)) return false;
            var turn = GetTurn(request.TransactionId, turnOrder);
            turn.SpeechStarted = true;
            turn.SpokenText += request.Text ?? "";
            turn.SpokenVoice += request.VoiceText ?? "";
            var assistant = CreateAssistant(turn, request.TransactionId, request.SessionId, request.ContextId,
                turn.SpokenText, turn.SpokenVoice, turn.Assistant?.Metadata, request.VoiceText);
            if (!useResponseText && Options.AssistantTiming == AssistantMessageTiming.PresentationStarted)
                SetAssistantProjection(turn, assistant, request.VoiceText);
            turn.Assistant = assistant;
            turn.SpokenAssistant = assistant.Copy();
            current.LatestAssistantUtterance = assistant.Copy();
            Emit(new ConversationEvent
            {
                Kind = ConversationEventKind.AssistantSpeechStarted, SessionId = request.SessionId,
                ContextId = request.ContextId, TransactionId = request.TransactionId,
                Text = request.Text, VoiceText = request.VoiceText, Utterance = assistant.Copy()
            });
            return true;
        }

        public bool OnRecognitionUpdate(SpeechRecognitionUpdate update, long turnOrder = 0)
        {
            if (update == null || string.IsNullOrEmpty(update.RecognitionId)) return false;
            recognitions.TryGetValue(update.RecognitionId, out var recognition);
            if (update.Kind == SpeechRecognitionUpdateKind.Activity)
            {
                // Activity belongs to an existing open recognition. It is not a new utterance,
                // transcript replacement, or display-order update.
                if (recognition == null || recognition.Confirmed || recognition.Canceled) return false;
                var activity = UserEvent(ConversationEventKind.UserSpeechActivity, recognition.Utterance);
                activity.Text = activity.VoiceText = null;
                activity.TransactionId = update.TransactionId ?? activity.TransactionId;
                CopyActivity(update, activity);
                Emit(activity);
                return true;
            }
            var started = update.Kind == SpeechRecognitionUpdateKind.Started;
            if (!started && update.Kind != SpeechRecognitionUpdateKind.Partial &&
                update.Kind != SpeechRecognitionUpdateKind.Confirmed && update.Kind != SpeechRecognitionUpdateKind.Canceled) return false;
            // Recognition may already have arrived before this notification. Never replace its
            // content with the empty speech-start projection, or restart a canceled recognition.
            if (started && recognition != null) return false;
            if (recognition == null)
            {
                var linkedTurn = !string.IsNullOrEmpty(update.TransactionId) ? GetTurn(update.TransactionId, turnOrder) : null;
                if (started && linkedTurn?.UserConfirmed == true) return false;
                recognition = new Recognition
                {
                    Order = linkedTurn?.Order ?? ++nextOrder,
                    Utterance = new ConversationUtterance { Id = linkedTurn?.User?.Id ?? NewId(), RecognitionId = update.RecognitionId }
                };
                recognitions.Add(update.RecognitionId, recognition);
                recognitionIds.Enqueue(update.RecognitionId);
                if (recognitionIds.Count > 256) recognitions.Remove(recognitionIds.Dequeue());
            }
            if (update.Kind == SpeechRecognitionUpdateKind.Canceled)
            {
                recognition.Canceled = true;
                var canceled = recognition.Utterance.Copy();
                canceled.Speaker = ConversationSpeaker.User;
                canceled.SessionId = update.SessionId ?? canceled.SessionId;
                canceled.TransactionId = update.TransactionId ?? canceled.TransactionId;
                canceled.IsCanceled = true; canceled.IsPartial = false; canceled.IsComplete = true;
                canceled.IsAwaitingRecognition = false;
                canceled.IsDisplayAllowed = false; canceled.DisplayText = null;
                recognition.Utterance = canceled;
                if (current.LatestUserUtterance?.Id == canceled.Id) current.LatestUserUtterance = canceled.Copy();
                RemoveUserProjection(canceled.Id);
                Emit(UserEvent(ConversationEventKind.UserSpeechCanceled, canceled));
                return true;
            }
            if (recognition.Canceled) return false;
            if (!string.IsNullOrEmpty(update.TransactionId))
            {
                recognition.TransactionId = update.TransactionId;
                recognitionByTurn[update.TransactionId] = recognition;
                if (turns.TryGetValue(update.TransactionId, out var linkedTurn)) linkedTurn.Order = recognition.Order;
            }
            var partial = started || update.Kind == SpeechRecognitionUpdateKind.Partial;
            var user = CreateUser(recognition.Utterance.Id, started ? "" : update.Text, recognition.Order, recognition.TransactionId,
                update.RecognitionId, update.SessionId, null, null, partial, started);
            var duplicateConfirmation = !partial && recognition.Confirmed && recognition.Utterance.Text == update.Text;
            recognition.Utterance = user;
            recognition.Confirmed |= !partial;
            current.LatestUserUtterance = user.Copy();
            var project = true;
            if (!string.IsNullOrEmpty(recognition.TransactionId) && turns.TryGetValue(recognition.TransactionId, out var turn))
            {
                turn.User = user.Copy(); turn.UserConfirmed |= !partial;
                project = !turn.ExcludedFromProjection;
            }
            SetUserProjection(user, project);
            if (!duplicateConfirmation)
            {
                var item = UserEvent(started ? ConversationEventKind.UserSpeechStarted :
                    partial ? ConversationEventKind.UserSpeechPartial : ConversationEventKind.UserSpeechConfirmed, user);
                if (started) CopyActivity(update, item);
                Emit(item);
            }
            return !duplicateConfirmation;
        }

        public bool OnTurnEnded(string transactionId, OrchestratorTurnEndReason reason)
        {
            if (string.IsNullOrEmpty(transactionId)) return false;
            turns.TryGetValue(transactionId, out var turn);
            var canceled = reason != OrchestratorTurnEndReason.Completed;
            if (recognitionByTurn.TryGetValue(transactionId, out var recognition))
            {
                recognition.Canceled = canceled;
                recognition.Confirmed = !canceled;
            }
            EndUtterance(current.LatestUserUtterance, transactionId, canceled);
            EndUtterance(current.LatestAssistantUtterance, transactionId, canceled);
            EndUtterance(current.DisplayUserUtterance, transactionId, canceled);
            EndUtterance(current.DisplayAssistantUtterance, transactionId, canceled);
            if (canceled)
            {
                if (current.DisplayUserUtterance?.TransactionId == transactionId) current.DisplayUserUtterance = null;
                if (current.DisplayAssistantUtterance?.TransactionId == transactionId) current.DisplayAssistantUtterance = null;
            }
            if (turn?.SpokenAssistant != null && turn.SpeechStarted && !canceled)
            {
                EndUtterance(turn.SpokenAssistant, transactionId, false);
                Emit(new ConversationEvent
                {
                    Kind = ConversationEventKind.AssistantSpeechCompleted, SessionId = turn.SpokenAssistant.SessionId,
                    ContextId = turn.SpokenAssistant.ContextId, TransactionId = transactionId,
                    Utterance = turn.SpokenAssistant.Copy(), EndReason = ConversationTurnEndReason.Completed
                });
            }
            Emit(new ConversationEvent
            {
                Kind = ConversationEventKind.TurnEnded, TransactionId = transactionId,
                SessionId = turn?.Assistant?.SessionId ?? turn?.User?.SessionId,
                ContextId = turn?.Assistant?.ContextId ?? turn?.User?.ContextId,
                EndReason = (ConversationTurnEndReason)reason
            });
            turns.Remove(transactionId);
            recognitionByTurn.Remove(transactionId);
            return true;
        }

        public bool OnLifecycle(ConversationEventKind kind, string runId, string sessionId, long generation, string contextId = null)
        {
            if (kind != ConversationEventKind.Started && kind != ConversationEventKind.Stopped &&
                kind != ConversationEventKind.Interrupted && kind != ConversationEventKind.Reset)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (kind == ConversationEventKind.Started || kind == ConversationEventKind.Stopped || generation > current.Generation)
            {
                ClearState();
                current.RunId = runId; current.SessionId = sessionId; current.Generation = generation; current.ContextId = contextId;
                if (kind == ConversationEventKind.Started) current.IsRunning = true;
                else if (kind == ConversationEventKind.Stopped) current.IsRunning = false;
            }
            else if (generation == current.Generation) current.ContextId = contextId;
            // Each actual boundary is a fact even if a later generation was already invalidated.
            Emit(new ConversationEvent { Kind = kind, SessionId = sessionId, ContextId = contextId }, generation, runId);
            return true;
        }

        public bool Invalidate(long generation, bool force = false)
        {
            if (!force && generation <= current.Generation) return false;
            ClearState();
            current.Generation = Math.Max(current.Generation, generation);
            Emit(new ConversationEvent { Kind = ConversationEventKind.StateChanged });
            return true;
        }

        public bool SetCanListen(bool canListen)
        {
            if (current.CanListen == canListen) return false;
            current.CanListen = canListen;
            Emit(new ConversationEvent { Kind = ConversationEventKind.ListeningChanged, CanListen = canListen });
            return true;
        }

        /// <summary>Stops presenting an unresolved onset when input is suppressed. This is a
        /// presentation decision, not a recognition cancellation or a change to its raw facts.</summary>
        public bool SuppressAwaitingRecognition()
        {
            if (current.DisplayUserUtterance?.IsAwaitingRecognition != true) return false;
            current.DisplayUserUtterance = null;
            Emit(new ConversationEvent { Kind = ConversationEventKind.StateChanged });
            return true;
        }

        public bool OnError(Exception error)
        {
            if (error == null) return false;
            Emit(new ConversationEvent { Kind = ConversationEventKind.Error, ErrorMessage = error.Message, ErrorType = error.GetType().FullName });
            return true;
        }

        private Turn GetTurn(string id, long turnOrder)
        {
            if (turns.TryGetValue(id, out var turn))
            {
                latestTurnOrder = Math.Max(latestTurnOrder, turnOrder);
                return turn;
            }
            recognitionByTurn.TryGetValue(id, out var recognition);
            turn = new Turn
            {
                Order = recognition?.Order ?? ++nextOrder,
                ExcludedFromProjection = turnOrder > 0 && turnOrder < latestTurnOrder,
                User = recognition?.Utterance.Copy(), UserConfirmed = recognition?.Confirmed ?? false
            };
            latestTurnOrder = Math.Max(latestTurnOrder, turnOrder);
            turns.Add(id, turn);
            return turn;
        }

        private ConversationUtterance CreateUser(string id, string text, long order, string transactionId,
            string recognitionId, string sessionId, string contextId, JObject metadata, bool partial, bool awaitingRecognition = false)
        {
            var allowed = CanShow(ConversationSpeaker.User, text, sessionId, contextId, transactionId, metadata, awaitingRecognition);
            return new ConversationUtterance
            {
                Id = id, Speaker = ConversationSpeaker.User, Text = text, VoiceText = text, Order = order * 2,
                TransactionId = transactionId, RecognitionId = recognitionId, SessionId = sessionId, ContextId = contextId,
                Metadata = (JObject)metadata?.DeepClone(), IsPartial = partial, IsComplete = !partial,
                IsAwaitingRecognition = awaitingRecognition,
                IsDisplayAllowed = allowed, DisplayText = allowed ? text : null
            };
        }

        private ConversationUtterance CreateAssistant(Turn turn, string transactionId, string sessionId,
            string contextId, string text, string voiceText, JObject metadata, string segmentVoice)
        {
            turn.AssistantId = turn.AssistantId ?? NewId();
            return new ConversationUtterance
            {
                Id = turn.AssistantId, Speaker = ConversationSpeaker.Assistant, Text = text, VoiceText = voiceText,
                Order = turn.Order * 2 + 1, TransactionId = transactionId, SessionId = sessionId, ContextId = contextId,
                Metadata = (JObject)metadata?.DeepClone(), IsComplete = false,
                IsDisplayAllowed = CanShow(ConversationSpeaker.Assistant, segmentVoice, sessionId, contextId, transactionId, metadata),
                DisplayText = turn.DisplayAssistant?.DisplayText
            };
        }

        private void SetUserProjection(ConversationUtterance user, bool eligibleTurn)
        {
            if (!user.IsDisplayAllowed) { RemoveUserProjection(user.Id); return; }
            if (!eligibleTurn || user.Order < userOrder) return;
            current.DisplayUserUtterance = user.Copy();
            userOrder = user.Order;
        }

        private void SetAssistantProjection(Turn turn, ConversationUtterance assistant, string segmentVoice)
        {
            if (!assistant.IsDisplayAllowed || turn.ExcludedFromProjection || assistant.Order < assistantOrder) return;
            var display = assistant.Copy();
            var replace = Options.AssistantMode == AssistantMessageMode.Replace;
            display.Id = replace ? NewId() : turn.DisplayAssistant?.Id ?? assistant.Id;
            display.DisplayText = replace ? segmentVoice : (turn.DisplayAssistant?.DisplayText ?? "") + segmentVoice;
            turn.DisplayAssistant = display;
            assistant.DisplayText = display.DisplayText;
            current.DisplayAssistantUtterance = display.Copy();
            assistantOrder = assistant.Order;
        }

        private void RemoveUserProjection(string id)
        {
            if (current.DisplayUserUtterance?.Id == id) current.DisplayUserUtterance = null;
        }

        private static void EndUtterance(ConversationUtterance utterance, string transactionId, bool canceled)
        {
            if (utterance?.TransactionId != transactionId) return;
            utterance.IsComplete = true; utterance.IsPartial = false; utterance.IsCanceled = canceled;
            utterance.IsAwaitingRecognition = false;
            if (canceled) { utterance.IsDisplayAllowed = false; utterance.DisplayText = null; }
        }

        private bool CanShow(ConversationSpeaker speaker, string text, string sessionId, string contextId, string transactionId,
            JObject metadata, bool awaitingRecognition = false)
        {
            if (!awaitingRecognition && string.IsNullOrWhiteSpace(text)) return false;
            if (speaker == ConversationSpeaker.User)
            {
                if (!string.IsNullOrEmpty(Options.InternalRequestPrefix) && text?.StartsWith(Options.InternalRequestPrefix, StringComparison.Ordinal) == true) return false;
                if (!Options.ShowWakewordMessages && (IsTrue(metadata?["IsWakeword"]) || IsTrue(metadata?["is_wakeword"]))) return false;
            }
            try
            {
                return ShouldShowMessage == null || ShouldShowMessage(new ConversationMessageContext
                {
                    Speaker = speaker, Text = text, SessionId = sessionId, ContextId = contextId,
                    IsAwaitingRecognition = awaitingRecognition,
                    TransactionId = transactionId, Metadata = (JObject)metadata?.DeepClone()
                });
            }
            catch (Exception error) { OnError(error); return false; }
        }

        private void ClearState()
        {
            current.LatestUserUtterance = current.LatestAssistantUtterance = null;
            current.DisplayUserUtterance = current.DisplayAssistantUtterance = null;
            current.CanListen = false;
            turns.Clear(); recognitions.Clear(); recognitionByTurn.Clear(); recognitionIds.Clear();
            userOrder = assistantOrder = latestTurnOrder = 0;
        }

        private void Emit(ConversationEvent item, long? generation = null, string runId = null)
        {
            item.Sequence = ++current.Sequence; item.OccurredAtUtc = DateTime.UtcNow;
            item.RunId = runId ?? current.RunId; item.Generation = generation ?? current.Generation;
            item.SessionId = item.SessionId ?? current.SessionId;
            item.ContextId = item.ContextId ?? current.ContextId;
            events.Enqueue(item.Copy());
        }

        private static ConversationEvent FromResponse(ConversationEventKind kind, SpeechPipelineResponse response) => new ConversationEvent
        {
            Kind = kind, SessionId = response.SessionId, ContextId = response.ContextId, TransactionId = response.TransactionId,
            Text = response.Text, VoiceText = response.VoiceText, Metadata = (JObject)response.Metadata?.DeepClone(), ResponseType = response.Type.ToString(),
            ToolCall = response.ToolCall == null ? null : JObject.FromObject(response.ToolCall),
            StructuredContent = (JObject)response.StructuredContent?.DeepClone()
        };

        private static ConversationEvent UserEvent(ConversationEventKind kind, ConversationUtterance user) => new ConversationEvent
        {
            Kind = kind, SessionId = user.SessionId, ContextId = user.ContextId, TransactionId = user.TransactionId,
            RecognitionId = user.RecognitionId, Text = user.Text, VoiceText = user.VoiceText,
            Metadata = (JObject)user.Metadata?.DeepClone(), Utterance = user.Copy()
        };

        private static void CopyActivity(SpeechRecognitionUpdate source, ConversationEvent target)
        {
            target.IsSpeechActive = source.IsSpeechActive;
            target.AudioDurationSeconds = source.AudioDurationSeconds;
            target.ObservedAtSeconds = source.ObservedAtSeconds;
        }

        private string NewId() => idPrefix + (++nextId);
        private static string String(JToken value) => value?.Type == JTokenType.String ? (string)value : null;
        private static bool IsTrue(JToken value) => value?.Type == JTokenType.Boolean && (bool)value;
    }
}

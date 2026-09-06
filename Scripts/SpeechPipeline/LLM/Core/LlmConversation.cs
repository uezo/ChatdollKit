using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.LLM
{
    /// <summary>Serializes one conversation and commits only complete successful turns.
    /// Use one wrapper per conversation and send its requests through this wrapper.</summary>
    public sealed class LlmConversation : ILlmService
    {
        private readonly ILlmService service;
        private readonly LlmHistoryFormat historyFormat;
        private readonly bool ownsService;
        private readonly object sync = new object();
        private readonly SpeechAsyncSemaphore operations = new SpeechAsyncSemaphore(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly HashSet<UniTask> active = new HashSet<UniTask>();
        private readonly SpeechCallbackGuard callbacks = new SpeechCallbackGuard();
        private UniTask? disposeTask;
        private bool disposing;
        public InMemoryContextManager Context { get; } = new InMemoryContextManager();

        /// <summary>Infers the history format for the three built-in providers. The service remains caller-owned by default.</summary>
        public LlmConversation(ILlmService service, bool ownsService = false)
            : this(service, InferFormat(service), ownsService) { }

        /// <summary>Use an explicit format for a custom ILlmService. ownsService opts into disposing that service.</summary>
        public LlmConversation(ILlmService service, LlmHistoryFormat historyFormat, bool ownsService = false)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            if (!Enum.IsDefined(typeof(LlmHistoryFormat), historyFormat)) throw new ArgumentOutOfRangeException(nameof(historyFormat));
            this.historyFormat = historyFormat;
            this.ownsService = ownsService;
        }

        public UniTask<LlmResult> ChatAsync(LlmRequest request, Func<LlmResponse, UniTask> onResponse = null,
            CancellationToken cancellationToken = default)
        {
            RejectReentry();
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrEmpty(request.ContextId)) throw new ArgumentException("ContextId is required.", nameof(request));
            if (!string.IsNullOrEmpty(request.PreviousResponseId) || request.History?.Count > 0)
                throw new ArgumentException("LlmConversation supplies History and PreviousResponseId. Use ILlmService directly for manual history.", nameof(request));
            var owned = request.Copy();
            return StartOperation(async token =>
            {
                var previous = Context.GetSnapshot();
                if (previous.ContextId != null && previous.ContextId != owned.ContextId)
                    throw new InvalidOperationException("Reset the conversation before changing ContextId.");
                owned.History = previous.History;
                owned.PreviousResponseId = historyFormat == LlmHistoryFormat.Responses ? previous.ResponseId : null;
                string correction = null;
                var result = await callbacks.InvokeAsync(() => service.ChatAsync(owned, async response =>
                {
                    // Capture the correction before application callbacks can edit the notification.
                    if (response.GuardrailName != null && response.Text != null) correction = response.Text;
                    if (onResponse != null) await callbacks.InvokeAsync(() => onResponse(response));
                }, token));
                token.ThrowIfCancellationRequested();
                if (result == null) throw new InvalidOperationException("The LLM service returned no result.");
                if (result.Error != null || result.InputItems == null || result.InputItems.Count == 0) return result;
                var output = result.OutputItems == null ? new JArray() : (JArray)result.OutputItems.DeepClone();
                if (HasUnresolvedTools(output, result.ToolCalls)) return result;
                if (output.Count == 0)
                {
                    if (!string.IsNullOrEmpty(result.Text)) output.Add(Assistant(result.Text));
                }
                else if (correction != null) output.Add(Assistant(correction));
                var responseId = historyFormat == LlmHistoryFormat.Responses && result.CanUsePreviousResponse && correction == null
                    ? result.ResponseId : null;
                Context.TryCommit(previous, owned.ContextId, result.InputItems, output, responseId, token);
                // Commit is the success boundary. Cancellation after it does not turn a saved turn into a failed call.
                return result;
            }, cancellationToken);
        }

        /// <summary>Waits for earlier operations, then clears the single conversation.</summary>
        public UniTask ResetAsync(CancellationToken cancellationToken = default)
        {
            RejectReentry();
            return StartOperation(token => { token.ThrowIfCancellationRequested(); Context.Reset(); return UniTask.FromResult(true); }, cancellationToken);
        }

        public LlmServiceOptions GetOptions() => service.GetOptions();

        /// <summary>Update while idle. History is retained, but the next Responses request sends it in full.</summary>
        public void UpdateOptions(LlmServiceOptions options)
        {
            RejectReentry();
            lock (sync)
            {
                ThrowIfDisposing();
                if (active.Any(task => !task.Status.IsCompleted())) throw new InvalidOperationException("Wait for conversation operations to finish before updating options.");
                service.UpdateOptions(options);
                Context.ClearResponseId();
            }
        }

        /// <summary>Copies the settings and waits for earlier conversation operations before applying them.
        /// On success history is retained and the next Responses request sends it in full.</summary>
        public UniTask UpdateOptionsAsync(LlmServiceOptions options, CancellationToken cancellationToken = default)
        {
            RejectReentry();
            if (options == null) throw new ArgumentNullException(nameof(options));
            var owned = options.Copy();
            return StartOperation(token =>
            {
                token.ThrowIfCancellationRequested();
                service.UpdateOptions(owned);
                // Updating the service and invalidating its continuation form one success boundary.
                // A later cancellation must not leave an ID from the previous configuration in use.
                Context.ClearResponseId();
                return UniTask.FromResult(true);
            }, cancellationToken);
        }

        private UniTask<T> StartOperation<T>(Func<CancellationToken, UniTask<T>> action, CancellationToken cancellationToken)
        {
            var completion = new SpeechCompletionSource<T>();
            var activeTask = completion.Task.AsUniTask();
            lock (sync)
            {
                ThrowIfDisposing();
                cancellationToken.ThrowIfCancellationRequested();
                active.Add(activeTask);
            }
            _ = RunAsync(action, completion, activeTask, cancellationToken);
            return completion.Task;
        }

        private async UniTask RunAsync<T>(Func<CancellationToken, UniTask<T>> action, SpeechCompletionSource<T> completion, UniTask activeTask, CancellationToken cancellationToken)
        {

            try
            {
                T result;
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
                {
                    await operations.WaitAsync(linked.Token);
                    try
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        result = await action(linked.Token);
                    }
                    finally { operations.Release(); }
                }
                completion.TrySetResult(result);
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception error) { completion.TrySetException(error); }
            finally
            {

                lock (sync) active.Remove(activeTask);
            }
        }

        public UniTask DisposeAsync()
        {
            RejectReentry();
            SpeechCompletionSource<bool> completion;
            UniTask[] pending;
            lock (sync)
            {
                if (disposeTask != null) return disposeTask.Value;
                disposing = true;
                pending = active.ToArray();
                completion = new SpeechCompletionSource<bool>();
                disposeTask = completion.Task;
            }
            _ = DisposeCoreAsync(pending, completion);
            return disposeTask.Value;
        }

        private async UniTask DisposeCoreAsync(UniTask[] pending, SpeechCompletionSource<bool> completion)
        {
            Exception failure = null;
            try { lifetime.Cancel(); } catch (Exception error) { failure = error; }
            try { await SpeechAsync.WhenAll(pending); } catch { }
            try { if (ownsService) await service.DisposeAsync(); }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            finally { lifetime.Dispose(); operations.Dispose(); }
            if (failure == null) completion.TrySetResult(true); else completion.TrySetException(failure);
        }

        private bool HasUnresolvedTools(JArray output, IReadOnlyList<LlmToolCall> callsInResult)
        {
            var pending = new HashSet<string>(StringComparer.Ordinal);
            foreach (var call in callsInResult ?? Array.Empty<LlmToolCall>()) pending.Add(call.Id ?? "");
            foreach (var item in output.OfType<JObject>())
            {
                if (historyFormat == LlmHistoryFormat.Responses)
                {
                    var type = (string)item["type"];
                    if (type == "function_call") pending.Add((string)item["call_id"] ?? "");
                    else if (type == "function_call_output") pending.Remove((string)item["call_id"] ?? "");
                }
                else
                {
                    if (item["tool_calls"] is JArray calls)
                        foreach (var call in calls) pending.Add((string)call["id"] ?? "");
                    if ((string)item["role"] == "tool") pending.Remove((string)item["tool_call_id"] ?? "");
                }
            }
            return pending.Count != 0;
        }

        private static JObject Assistant(string text) => new JObject { ["role"] = "assistant", ["content"] = text };
        private static LlmHistoryFormat InferFormat(ILlmService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (service is ChatCompletionsClient) return LlmHistoryFormat.ChatCompletions;
            if (service is OpenAIResponsesClient || service is OpenAIResponsesWebSocketClient) return LlmHistoryFormat.Responses;
            throw new ArgumentException("Specify the history format for a custom ILlmService.", nameof(service));
        }
        private void RejectReentry()
        {
            callbacks.ThrowIfActive("Schedule conversation control after its callback, hook, or tool returns.");
        }
        private void ThrowIfDisposing() { if (disposing) throw new ObjectDisposedException(nameof(LlmConversation)); }
    }
}

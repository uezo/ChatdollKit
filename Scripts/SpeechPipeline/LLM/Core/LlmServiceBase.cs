// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.LLM
{
    public abstract class LlmServiceBase : ILlmService
    {
        private readonly object sync = new object();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly HashSet<UniTask> active = new HashSet<UniTask>();
        private readonly SpeechCallbackGuard callbacks = new SpeechCallbackGuard();
        private LlmServiceOptions options;
        private bool disposing;
        private UniTask? disposeTask;
        private Func<LlmRequest, CancellationToken, UniTask<LlmRequest>> requestFilter;
        private Func<LlmRequest, CancellationToken, UniTask<string>> systemPromptFactory;
        private Func<LlmRequest, CancellationToken, UniTask<JArray>> initialMessagesFactory;
        private Func<LlmToolCall, LlmRequest, CancellationToken, UniTask<LlmToolResult>> toolExecutor;

        public Func<LlmRequest, CancellationToken, UniTask<LlmRequest>> RequestFilterAsync
        { get { lock (sync) return requestFilter; } set { lock (sync) { ThrowIfDisposing(); requestFilter = value; } } }
        public Func<LlmRequest, CancellationToken, UniTask<string>> SystemPromptFactoryAsync
        { get { lock (sync) return systemPromptFactory; } set { lock (sync) { ThrowIfDisposing(); systemPromptFactory = value; } } }
        public Func<LlmRequest, CancellationToken, UniTask<JArray>> InitialMessagesFactoryAsync
        { get { lock (sync) return initialMessagesFactory; } set { lock (sync) { ThrowIfDisposing(); initialMessagesFactory = value; } } }
        public Func<LlmToolCall, LlmRequest, CancellationToken, UniTask<LlmToolResult>> ToolExecutorAsync
        { get { lock (sync) return toolExecutor; } set { lock (sync) { ThrowIfDisposing(); toolExecutor = value; } } }

        protected LlmServiceBase(LlmServiceOptions options)
        {
            this.options = CopyOptions(options);
        }
        protected virtual bool UsesResponsesApi => true;
        protected abstract UniTask<LlmProviderResult> GenerateCoreAsync(LlmRequest request, LlmServiceOptions options,
            Func<LlmResponse, UniTask> emit, CancellationToken cancellationToken);
        protected virtual UniTask DisposeResourcesAsync() => UniTask.CompletedTask;

        public LlmServiceOptions GetOptions() { lock (sync) return options.Copy(); }
        public void UpdateOptions(LlmServiceOptions replacement)
        {
            var snapshot = CopyOptions(replacement);
            lock (sync)
            {
                ThrowIfDisposing();
                if (snapshot.GetType() != options.GetType()) throw new ArgumentException("Options must match the service's configuration type.");
                options = snapshot;
            }
        }

        public UniTask<LlmResult> ChatAsync(LlmRequest request, Func<LlmResponse, UniTask> onResponse = null,
            CancellationToken cancellationToken = default)
        {
            RejectReentry();
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrEmpty(request.ContextId)) throw new ArgumentException("ContextId is required.", nameof(request));
            var completion = new SpeechCompletionSource<LlmResult>();
            var activeTask = completion.Task.AsUniTask();
            Call call;
            lock (sync)
            {
                ThrowIfDisposing();
                cancellationToken.ThrowIfCancellationRequested();
                call = new Call
                {
                    Request = request.Copy(), Options = options.Copy(), Emit = onResponse,
                    Filter = requestFilter, SystemPrompt = systemPromptFactory, InitialMessages = initialMessagesFactory,
                    ToolExecutor = toolExecutor
                };
                active.Add(activeTask);
            }
            _ = RunAsync(call, completion, activeTask, cancellationToken);
            return completion.Task;
        }

        private async UniTask RunAsync(Call call, SpeechCompletionSource<LlmResult> completion, UniTask activeTask, CancellationToken cancellationToken)
        {

            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken))
                {
                    using var timeout = SpeechAsync.Timeout(linked, TimeSpan.FromSeconds(call.Options.TimeoutSeconds));
                    linked.Token.ThrowIfCancellationRequested();
                    var result = await ChatCoreAsync(call, linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    completion.TrySetResult(result);
                }
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (CallbackException error) { completion.TrySetException(error.InnerException); }
            catch (Exception error) { completion.TrySetException(error); }
            finally
            {

                lock (sync) active.Remove(activeTask);
            }
        }

        private async UniTask<LlmResult> ChatCoreAsync(Call call, CancellationToken token)
        {
            var request = call.Request;
            var settings = call.Options;
            var result = new LlmResult { ContextId = request.ContextId, Text = string.Empty, OutputItems = new JArray() };
            if (call.Filter != null)
            {
                var filtered = await callbacks.InvokeAsync(() => call.Filter(request, token));
                token.ThrowIfCancellationRequested();
                if (filtered == null) return result;
                request = filtered.Copy();
                // Correlation belongs to the original call, even if the hook replaces the request.
                request.ContextId = result.ContextId;
            }
            if (request.Input == null && string.IsNullOrEmpty(request.Text) && request.ImageUrls.Length == 0) return result;
            if (call.SystemPrompt != null) settings.SystemPrompt = await callbacks.InvokeAsync(() => call.SystemPrompt(request, token));
            token.ThrowIfCancellationRequested();
            if (call.InitialMessages != null)
                settings.InitialMessages = (JArray)(await callbacks.InvokeAsync(() => call.InitialMessages(request, token)))?.DeepClone() ?? new JArray();
            token.ThrowIfCancellationRequested();

            result.InputItems = LlmRequestBuilder.GetCurrentInput(request, UsesResponsesApi);
            var requestGuardrail = await ApplyGuardrailsAsync(settings.Guardrails, LlmGuardrailScope.Request, request, request.Text, token);
            if (requestGuardrail != null)
            {
                if (requestGuardrail.Action == LlmGuardrailAction.Block)
                {
                    result.Text = requestGuardrail.Text ?? string.Empty;
                    await EmitAsync(call, new LlmResponse { Text = result.Text, VoiceText = LlmTextProcessor.RemoveControlTags(result.Text), GuardrailName = requestGuardrail.Name }, token);
                    await EmitFinalAsync(call, result, token);
                    return result;
                }
                request.Text = requestGuardrail.Text;
                if (request.Input != null) ReplaceLastInputText(request.Input, request.Text);
                result.InputItems = LlmRequestBuilder.GetCurrentInput(request, UsesResponsesApi);
            }

            AddToolDefinitions(settings);
            var editParameters = settings.EditRequestParameters;
            if (editParameters != null)
                settings.EditRequestParameters = (body, value) =>
                {
                    token.ThrowIfCancellationRequested();
                    try { callbacks.Invoke(() => editParameters(body, value)); }
                    catch (LlmServiceException error) { throw new CallbackException(error); }
                    token.ThrowIfCancellationRequested();
                };
            var processor = new LlmTextProcessor(settings, request.ContextId);
            var allCalls = new List<LlmToolCall>();
            try
            {
                for (var round = 0; ; round++)
                {
                    var provider = await callbacks.InvokeAsync(() => GenerateCoreAsync(request, settings, async response =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (response.Error != null) throw new LlmServiceException(response.Error);
                        if (!string.IsNullOrEmpty(response.Text))
                            foreach (var chunk in processor.Append(response.Text)) await EmitAsync(call, chunk, token);
                        if (response.ToolCall != null)
                            foreach (var chunk in processor.Flush()) await EmitAsync(call, chunk, token);
                        if (response.ToolCall != null || response.IsRecovery || response.StructuredContent != null)
                            await EmitAsync(call, response, token);
                    }, token));
                    token.ThrowIfCancellationRequested();
                    if (provider == null) throw new LlmServiceException(new LlmError { Code = "missing_result", Message = "The provider returned no terminal result." });
                    result.ResponseId = provider.ResponseId;
                    result.CanUsePreviousResponse = UsesResponsesApi && provider.CanUsePreviousResponse && !string.IsNullOrEmpty(provider.ResponseId);
                    result.RecoveredPreviousResponse |= provider.RecoveredPreviousResponse;
                    result.Usage = provider.Usage;
                    if (provider.OutputItems != null) foreach (var item in provider.OutputItems) result.OutputItems.Add(item.DeepClone());
                    var calls = provider.ToolCalls ?? new List<LlmToolCall>();
                    allCalls.AddRange(calls);
                    if (calls.Count != 0)
                    {
                        foreach (var chunk in processor.Flush()) await EmitAsync(call, chunk, token);
                        foreach (var tool in calls)
                            await EmitAsync(call, new LlmResponse { ToolCall = tool.Copy() }, token);
                    }
                    if (calls.Count == 0 || calls.Any(tool => ResolveTool(settings, call.ToolExecutor, tool) == null)) break;
                    if (round >= settings.MaxToolRounds)
                        throw new LlmServiceException(new LlmError { Code = "tool_round_limit", Message = "The configured tool-round limit was reached." });

                    var outputs = new JArray();
                    var continueChain = true;
                    foreach (var tool in calls)
                    {
                        var output = await InvokeApplicationAsync(() => ResolveTool(settings, call.ToolExecutor, tool)(tool.Copy(), request.Copy(), token));
                        token.ThrowIfCancellationRequested();
                        if (output == null) throw new InvalidOperationException("The tool returned no result.");
                        if (!string.IsNullOrEmpty(output.Text))
                        {
                            foreach (var chunk in processor.Append(output.Text)) await EmitAsync(call, chunk, token);
                            foreach (var chunk in processor.Flush()) await EmitAsync(call, chunk, token);
                        }
                        if (output.StructuredContent != null)
                            await EmitAsync(call, new LlmResponse { ToolCall = tool.Copy(), StructuredContent = (JObject)output.StructuredContent.DeepClone() }, token);
                        continueChain &= output.ContinueChain;
                        outputs.Add(UsesResponsesApi
                            ? new JObject { ["type"] = "function_call_output", ["call_id"] = tool.Id, ["output"] = output.Data?.ToString(Formatting.None) ?? "null" }
                            : new JObject { ["role"] = "tool", ["tool_call_id"] = tool.Id, ["content"] = output.Data?.ToString(Formatting.None) ?? "null" });
                    }
                    foreach (var item in outputs) result.OutputItems.Add(item.DeepClone());
                    // Tool outputs are local until a following generation accepts them.
                    result.CanUsePreviousResponse = false;
                    if (!continueChain) break;
                    if (UsesResponsesApi)
                    {
                        request.PreviousResponseId = provider.ResponseId;
                        // With store=false or missing response ID, explicitly carry the complete chain forward.
                        if (string.IsNullOrEmpty(provider.ResponseId) || !provider.CanUsePreviousResponse)
                        {
                            var current = ComposeCurrentInput(request, true);
                            if (provider.OutputItems != null) foreach (var item in provider.OutputItems) current.Add(item.DeepClone());
                            foreach (var item in outputs) current.Add(item.DeepClone());
                            request.Input = current;
                            request.PreviousResponseId = null;
                        }
                        else request.Input = outputs;
                    }
                    else
                    {
                        var current = ComposeCurrentInput(request, false);
                        if (provider.OutputItems != null) foreach (var item in provider.OutputItems) current.Add(item.DeepClone());
                        foreach (var item in outputs) current.Add(item.DeepClone());
                        request.Input = current;
                    }
                    // Upstream applies inline parameters only to the first generation.
                    // In particular, a forced tool_choice or old input/ID must not overwrite the tool continuation.
                    request.Parameters = null;
                }
                foreach (var chunk in processor.Complete()) await EmitAsync(call, chunk, token);
                result.Text = processor.Text;
                var responseGuardrail = await InvokeApplicationAsync(() => ApplyGuardrailsAsync(settings.Guardrails, LlmGuardrailScope.Response, request, result.Text, token));
                if (responseGuardrail != null)
                {
                    // Upstream emits a correction after streaming; it does not retract already emitted speech.
                    result.Text = responseGuardrail.Text ?? string.Empty;
                    result.CanUsePreviousResponse = false;
                    await EmitAsync(call, new LlmResponse { Text = result.Text, VoiceText = LlmTextProcessor.RemoveControlTags(result.Text), GuardrailName = responseGuardrail.Name }, token);
                }
            }
            catch (LlmServiceException error)
            {
                result.Text = processor.Text;
                result.Error = error.Error;
                result.CanUsePreviousResponse = false;
            }
            result.ToolCalls = allCalls.AsReadOnly();
            await EmitFinalAsync(call, result, token);
            return result;
        }

        private static Func<LlmToolCall, LlmRequest, CancellationToken, UniTask<LlmToolResult>> ResolveTool(LlmServiceOptions settings,
            Func<LlmToolCall, LlmRequest, CancellationToken, UniTask<LlmToolResult>> fallback, LlmToolCall call)
            => settings.Tools.FirstOrDefault(tool => tool.Name == call.Name)?.ExecuteAsync ?? fallback;

        private void AddToolDefinitions(LlmServiceOptions settings)
        {
            if (settings.Tools.Length == 0) return;
            var definitions = settings.ToolDefinitions ?? new JArray();
            foreach (var tool in settings.Tools)
            {
                var function = new JObject { ["name"] = tool.Name, ["description"] = tool.Description, ["parameters"] = tool.Parameters.DeepClone() };
                if (tool.Strict.HasValue) function["strict"] = tool.Strict.Value;
                if (UsesResponsesApi) { function["type"] = "function"; definitions.Add(function); }
                else definitions.Add(new JObject { ["type"] = "function", ["function"] = function });
            }
            settings.ToolDefinitions = definitions;
        }

        private static JArray ComposeCurrentInput(LlmRequest request, bool responses)
        {
            if (request.Input != null) return (JArray)request.Input.DeepClone();
            if (request.ImageUrls.Length == 0) return new JArray(new JObject { ["role"] = "user", ["content"] = request.Text });
            var content = new JArray();
            foreach (var url in request.ImageUrls)
                content.Add(responses ? new JObject { ["type"] = "input_image", ["image_url"] = url }
                    : new JObject { ["type"] = "image_url", ["image_url"] = new JObject { ["url"] = url } });
            if (!string.IsNullOrEmpty(request.Text)) content.Add(new JObject { ["type"] = responses ? "input_text" : "text", ["text"] = request.Text });
            return new JArray(new JObject { ["role"] = "user", ["content"] = content });
        }

        private static void ReplaceLastInputText(JArray input, string text)
        {
            if (!(input.Last is JObject message) || (string)message["role"] != "user") return;
            if (message["content"] is JArray content)
            {
                var type = content.OfType<JObject>().Any(part => (string)part["type"] == "input_text" || (string)part["type"] == "input_image")
                    ? "input_text" : "text";
                foreach (var part in content.OfType<JObject>().Where(part => (string)part["type"] == "text" || (string)part["type"] == "input_text").ToArray()) part.Remove();
                content.Add(new JObject { ["type"] = type, ["text"] = text ?? string.Empty });
            }
            else message["content"] = text;
        }

        private async UniTask<LlmGuardrailResult> ApplyGuardrailsAsync(ILlmGuardrail[] guardrails, LlmGuardrailScope scope,
            LlmRequest request, string text, CancellationToken token)
        {
            var applicable = guardrails.Where(guardrail => guardrail.Scope == scope || guardrail.Scope == LlmGuardrailScope.Both).ToArray();
            if (applicable.Length == 0) return null;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var tasks = applicable.Select(guardrail => SpeechAsync.Share(ApplyGuardrailAsync(guardrail, request, text, linked.Token))).ToList();
                var all = tasks.ToArray();
                try
                {
                    while (tasks.Count != 0)
                    {
                        var completed = await UniTask.WhenAny(tasks);
                        tasks.RemoveAt(completed.winArgumentIndex);
                        var decision = completed.result;
                        token.ThrowIfCancellationRequested();
                        if (decision?.IsTriggered == true) return decision;
                    }
                    return null;
                }
                finally
                {
                    try { linked.Cancel(); }
                    finally { try { await SpeechAsync.WhenAll(all.Select(task => (UniTask)task)); } catch { /* The selected failure is propagated above. */ } }
                }
            }
        }

        private async UniTask<LlmGuardrailResult> ApplyGuardrailAsync(ILlmGuardrail guardrail,
            LlmRequest request, string text, CancellationToken token)
        {
            await SpeechAsync.Yield();
            token.ThrowIfCancellationRequested();
            return await callbacks.InvokeAsync(() => guardrail.ApplyAsync(request.Copy(), text, token));
        }

        private async UniTask EmitAsync(Call call, LlmResponse response, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            response.ContextId = call.Request.ContextId;
            if (call.Emit != null)
            {
                try { await callbacks.InvokeAsync(() => call.Emit(response)); }
                catch (LlmServiceException error) { throw new CallbackException(error); }
            }
            token.ThrowIfCancellationRequested();
        }
        private async UniTask<T> InvokeApplicationAsync<T>(Func<UniTask<T>> action)
        {
            try { return await callbacks.InvokeAsync(action); }
            catch (LlmServiceException error) { throw new CallbackException(error); }
        }
        private UniTask EmitFinalAsync(Call call, LlmResult result, CancellationToken token) => EmitAsync(call, new LlmResponse
        { IsFinal = true, ResponseId = result.ResponseId, Error = result.Error, IsRecovery = result.RecoveredPreviousResponse }, token);

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
            try { await DisposeResourcesAsync(); }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            finally { lifetime.Dispose(); }
            if (failure == null) completion.TrySetResult(true); else completion.TrySetException(failure);
        }

        private void RejectReentry()
        {
            callbacks.ThrowIfActive("Schedule service control after its callback, hook, or tool returns.");
        }
        private void ThrowIfDisposing() { if (disposing) throw new ObjectDisposedException(GetType().Name); }
        private static LlmServiceOptions CopyOptions(LlmServiceOptions value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var copy = value.Copy();
            copy.Validate();
            return copy;
        }
        private sealed class Call
        {
            public LlmRequest Request;
            public LlmServiceOptions Options;
            public Func<LlmResponse, UniTask> Emit;
            public Func<LlmRequest, CancellationToken, UniTask<LlmRequest>> Filter;
            public Func<LlmRequest, CancellationToken, UniTask<string>> SystemPrompt;
            public Func<LlmRequest, CancellationToken, UniTask<JArray>> InitialMessages;
            public Func<LlmToolCall, LlmRequest, CancellationToken, UniTask<LlmToolResult>> ToolExecutor;
        }
        private sealed class CallbackException : Exception
        {
            public CallbackException(Exception inner) : base("The response callback failed.", inner) { }
        }
    }
}

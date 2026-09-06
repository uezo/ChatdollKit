// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.LLM
{
    public sealed class OpenAIResponsesWebSocketServiceOptions : LlmServiceOptions
    {
        /// <summary>Complete endpoint URL, including /v1/responses.</summary>
        public string WebSocketUrl { get; set; } = "wss://api.openai.com/v1/responses";
        public int MaxConnections { get; set; } = 1;
        public double MaxConnectionAgeSeconds { get; set; } = 3300;

        public override void Validate()
        {
            base.Validate();
            if (MaxConnections < 1) throw new ArgumentOutOfRangeException(nameof(MaxConnections));
            if (double.IsNaN(MaxConnectionAgeSeconds) || double.IsInfinity(MaxConnectionAgeSeconds) || MaxConnectionAgeSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxConnectionAgeSeconds));
            if (!Uri.TryCreate(WebSocketUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "wss" && !(uri.Scheme == "ws" && uri.IsLoopback)) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Use an absolute WSS endpoint without credentials, query or fragment (WS is allowed for loopback tests).", nameof(WebSocketUrl));
        }
    }

    /// <summary>Persistent Responses connections, one in-flight request per connection.
    /// Conversation history and continuation IDs remain caller-owned.</summary>
    public sealed class OpenAIResponsesWebSocketClient : LlmServiceBase
    {
        private readonly object poolSync = new object();
        private readonly List<LlmWebSocketPool> pools = new List<LlmWebSocketPool>();
        private readonly Func<ILlmWebSocketConnection> connectionFactory;
        private LlmWebSocketPool currentPool;

        public OpenAIResponsesWebSocketClient(OpenAIResponsesWebSocketServiceOptions options,
            Func<ILlmWebSocketConnection> connectionFactory = null)
            : base(options ?? throw new ArgumentNullException(nameof(options)))
        {
            this.connectionFactory = connectionFactory ?? (() => new NativeLlmWebSocketConnection());
        }

        protected override async UniTask<LlmProviderResult> GenerateCoreAsync(LlmRequest request, LlmServiceOptions options,
            Func<LlmResponse, UniTask> emit, CancellationToken token)
        {
            var settings = (OpenAIResponsesWebSocketServiceOptions)options;
            var message = LlmRequestBuilder.BuildResponsesRequest(request, settings);
            message["type"] = "response.create";
            message.Remove("stream");
            message.Remove("background");
            var pool = SelectPool(settings);
            using (var lease = await TransportAsync(() => pool.RentAsync(token), token))
            {
                var recovered = false;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var callbackFailed = false;
                    var state = new ResponsesStreamState(request.ContextId, async response =>
                    {
                        try { await emit(response); }
                        catch { callbackFailed = true; throw; }
                    });
                    await TransportAsync(async () =>
                    {
                        await lease.Connection.SendTextAsync(message.ToString(Formatting.None), token);
                        return true;
                    }, token);

                    var outputReceived = false;
                    var retry = false;
                    while (!state.IsCompleted)
                    {
                        var raw = await TransportAsync(() => lease.Connection.ReceiveTextAsync(token), token);
                        if (raw == null)
                            throw LlmErrorParser.Failure("websocket_closed", "The WebSocket closed before the response completed.");
                        JObject evt;
                        try { evt = JObject.Parse(raw); }
                        catch (JsonException) { throw LlmErrorParser.Failure("invalid_json", "The WebSocket returned invalid JSON."); }
                        if (IsOutputEvent(evt)) outputReceived = true;
                        try { await state.ProcessEventAsync(evt); }
                        catch (LlmServiceException exception) when (!callbackFailed && settings.EnablePreviousResponseFallback &&
                            !recovered && !outputReceived && LlmRequestBuilder.CanRecoverPreviousResponse(message, request, exception.Error))
                        {
                            // The rejected request produced no output. Retry once on the same connection.
                            message = LlmRequestBuilder.BuildResponsesRecoveryRequest(message, request, settings);
                            recovered = true;
                            retry = true;
                            break;
                        }
                    }
                    if (retry) continue;
                    token.ThrowIfCancellationRequested();
                    state.Result.RecoveredPreviousResponse = recovered;
                    // A later tool round can lease another connection, whose local cache is different.
                    state.Result.CanUsePreviousResponse = message["store"]?.Type != JTokenType.Boolean || (bool)message["store"];
                    lease.MarkCompleted();
                    return state.Result;
                }
            }
        }

        private static bool IsOutputEvent(JObject evt)
        {
            var type = LlmErrorParser.Value(evt["type"]);
            return type != null && (type.StartsWith("response.output_", StringComparison.Ordinal) ||
                type.StartsWith("response.function_call_arguments.", StringComparison.Ordinal) ||
                type.StartsWith("response.content_part.", StringComparison.Ordinal) ||
                type.StartsWith("response.refusal.", StringComparison.Ordinal) ||
                type.StartsWith("response.reasoning", StringComparison.Ordinal));
        }

        private LlmWebSocketPool SelectPool(OpenAIResponsesWebSocketServiceOptions options)
        {
            var uri = new Uri(options.WebSocketUrl);
            LlmWebSocketPool previous = null;
            LlmWebSocketPool selected;
            lock (poolSync)
            {
                if (currentPool == null || !currentPool.Matches(uri, options.ApiKey, options.MaxConnections, options.MaxConnectionAgeSeconds))
                {
                    previous = currentPool;
                    currentPool = new LlmWebSocketPool(uri, options.ApiKey, options.MaxConnections,
                        options.MaxConnectionAgeSeconds, connectionFactory);
                    pools.Add(currentPool);
                }
                selected = currentPool;
            }
            // Old requests retain their configuration; their connections are disposed on return.
            previous?.Retire();
            return selected;
        }

        private static async UniTask<T> TransportAsync<T>(Func<UniTask<T>> action, CancellationToken token)
        {
            try { return await action(); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                token.ThrowIfCancellationRequested();
                // Transport exceptions can contain URLs or handshake details; expose a stable error only.
                throw LlmErrorParser.Failure("websocket_transport_error", "The WebSocket transport failed.");
            }
        }

        protected override UniTask DisposeResourcesAsync()
        {
            LlmWebSocketPool[] ownedPools;
            lock (poolSync)
            {
                ownedPools = pools.ToArray();
                pools.Clear();
                currentPool = null;
            }
            foreach (var pool in ownedPools) pool.Dispose();
            return UniTask.CompletedTask;
        }
    }
}

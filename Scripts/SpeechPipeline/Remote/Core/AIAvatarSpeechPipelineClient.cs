using ChatdollKit.SpeechPipeline;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.Remote
{
    /// <summary>
    /// One AIAvatarKit WebSocket conversation. Public session identity remains stable while each
    /// connection uses a fresh remote session to isolate delayed responses and server cleanup.
    /// Interrupt/reset end local delivery and replace the connection; the current server protocol
    /// does not guarantee cancellation of work already running on the server.
    /// </summary>
    public sealed class AIAvatarSpeechPipelineClient : ISpeechPipeline, ISpeechRecognitionSource
    {
        private sealed class Turn
        {
            internal readonly string Id = Guid.NewGuid().ToString("N");
            internal readonly SpeechCompletionSource<SpeechPipelineResponse> Completion = new SpeechCompletionSource<SpeechPipelineResponse>();
            internal readonly CancellationTokenSource Deadline = new CancellationTokenSource();
            internal bool Terminal;
            internal string RecognitionId;
        }

        private sealed class Connection
        {
            internal readonly string SessionId = Guid.NewGuid().ToString("N");
            internal readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
            internal readonly SpeechCompletionSource<bool> Ready = new SpeechCompletionSource<bool>();
            internal IAIAvatarConnection Transport;
            internal UniTask Receiving;
            internal bool Connected, Failed;
            internal Turn Turn;
            internal SpeechRecognitionUpdate Recognition;
        }

        private readonly object sync = new object();
        private readonly AIAvatarSpeechPipelineOptions options;
        private readonly Func<IAIAvatarConnection> connectionFactory;
        private readonly ISpeechPipelineClock clock;
        private readonly SpeechAsyncSemaphore lifecycle = new SpeechAsyncSemaphore(1, 1);
        private readonly SpeechAsyncSemaphore delivery = new SpeechAsyncSemaphore(1, 1);
        private readonly SpeechCallbackGuard callbacks = new SpeechCallbackGuard();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Connection connection;
        private string contextId;
        private bool disposing;
        private UniTask? disposal;

        public string SessionId { get; }
        public string ContextId { get { lock (sync) return contextId; } }
        public string RemoteSessionId { get { lock (sync) return connection?.SessionId; } }
        public bool IsConnected { get { lock (sync) return !disposing && connection?.Connected == true && connection.Transport.IsOpen; } }
        public event Func<SpeechPipelineResponse, UniTask> ResponseReceived;
        public event Action<Exception> Error;
        /// <summary>Optional server info event containing metadata.partial_request_text.</summary>
        public event Action<string> SpeechDetecting;
        public event Action<SpeechRecognitionUpdate> RecognitionUpdated;
        public event Action Voiced;

        public AIAvatarSpeechPipelineClient(AIAvatarSpeechPipelineOptions options = null,
            Func<IAIAvatarConnection> connectionFactory = null, ISpeechPipelineClock clock = null)
        {
            this.options = (options ?? new AIAvatarSpeechPipelineOptions()).Copy();
            this.options.Validate();
            this.connectionFactory = connectionFactory ?? (() => new NativeAIAvatarConnection());
            this.clock = clock ?? new SpeechPipelineClock();
            SessionId = this.options.SessionId ?? Guid.NewGuid().ToString("N");
            contextId = this.options.ContextId;
        }

        public async UniTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            RejectReentry();
            using (var source = Linked(cancellationToken))
            {
                await lifecycle.WaitAsync(source.Token);
                try { CheckAlive(); await ConnectLockedAsync(source.Token); }
                finally { lifecycle.Release(); }
            }
        }

        private async UniTask<Connection> ConnectLockedAsync(CancellationToken token)
        {
            Connection current;
            lock (sync) current = connection;
            if (current?.Connected == true && current.Transport.IsOpen) return current;
            if (current != null) await RetireLockedAsync(current, "disconnected");

            current = new Connection();
            try
            {
                current.Transport = connectionFactory() ?? throw new InvalidOperationException("The connection factory returned null.");
                lock (sync) { CheckAlive(); connection = current; }
                using (var startup = CancellationTokenSource.CreateLinkedTokenSource(token, current.Lifetime.Token))
                using (SpeechAsync.Timeout(startup, options.ConnectTimeout))
                {
                    await current.Transport.ConnectAsync(new Uri(options.Url), options.ApiKey, startup.Token);
                    current.Receiving = SpeechAsync.Run(() => ReceiveAsync(current));
                    await current.Transport.SendAsync(AIAvatarProtocol.Start(options, current.SessionId, ContextId).ToString(Formatting.None), startup.Token);
                    await SpeechAsync.WaitAsync(current.Ready.Task, startup.Token);
                    startup.Token.ThrowIfCancellationRequested();
                    lock (sync)
                        if (!ReferenceEquals(connection, current) || !current.Connected)
                            throw new InvalidOperationException("The AIAvatarKit connection closed during initialization.");
                }
                return current;
            }
            catch
            {
                if (current.Transport == null) current.Lifetime.Dispose();
                else await RetireLockedAsync(current, "connection_failed");
                throw;
            }
        }

        public UniTask<SpeechPipelineResponse> InvokeAsync(SpeechPipelineRequest request, CancellationToken cancellationToken = default)
        {
            // Explicit-request correlation is implemented separately from microphone-created turns.
            throw new NotSupportedException("Explicit AIAvatarKit requests are not yet configured.");
        }

        /// <summary>Sends raw signed PCM16 little-endian. The server determines sample rate and channel count.</summary>
        public async UniTask ProcessAudioSamplesAsync(byte[] samples, CancellationToken cancellationToken = default)
        {
            RejectReentry();
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if ((samples.Length & 1) != 0) throw new ArgumentException("PCM16 input must contain complete samples.", nameof(samples));
            var encoded = Convert.ToBase64String(samples);
            using (var source = Linked(cancellationToken))
            {
                await lifecycle.WaitAsync(source.Token);
                try
                {
                    CheckAlive();
                    if (samples.Length == 0) return;
                    var current = await ConnectLockedAsync(source.Token);
                    var message = new JObject { ["type"] = "data", ["session_id"] = current.SessionId, ["audio_data"] = encoded };
                    await current.Transport.SendAsync(message.ToString(Formatting.None), source.Token);
                }
                finally { lifecycle.Release(); }
            }
        }

        public UniTask InterruptAsync(CancellationToken cancellationToken = default) => ReconnectAsync(false, null, cancellationToken);
        public UniTask ResetAsync(string contextId = null, CancellationToken cancellationToken = default)
        {
            if (contextId != null && string.IsNullOrWhiteSpace(contextId)) throw new ArgumentException("ContextId must be nonempty when supplied.", nameof(contextId));
            return ReconnectAsync(true, contextId, cancellationToken);
        }

        private async UniTask ReconnectAsync(bool reset, string nextContext, CancellationToken token)
        {
            RejectReentry();
            using (var source = Linked(token))
            {
                await lifecycle.WaitAsync(source.Token);
                try
                {
                    CheckAlive();
                    Connection previous;
                    lock (sync) previous = connection;
                    if (previous != null) await RetireLockedAsync(previous, reset ? "reset" : "interrupted");
                    if (reset) lock (sync) contextId = nextContext;
                    await PublishAsync(null, new SpeechPipelineResponse { Type = SpeechPipelineResponseType.Stop, SessionId = SessionId, ContextId = ContextId });
                    await ConnectLockedAsync(source.Token);
                }
                finally { lifecycle.Release(); }
            }
        }

        /// <summary>
        /// Waits for sends ahead of this call and the response turn already accepted by the server.
        /// The wire protocol has no flush acknowledgement for audio still buffered by remote VAD.
        /// </summary>
        public async UniTask DrainAsync()
        {
            RejectReentry();
            Turn pending;
            using (var source = Linked(CancellationToken.None))
            {
                await lifecycle.WaitAsync(source.Token);
                try { CheckAlive(); lock (sync) pending = connection?.Turn; }
                finally { lifecycle.Release(); }
                if (pending != null) await SpeechAsync.WaitAsync(pending.Completion.Task, source.Token);
                await delivery.WaitAsync(source.Token);
                delivery.Release();
            }
        }

        private async UniTask ReceiveAsync(Connection current)
        {
            try
            {
                while (!current.Lifetime.IsCancellationRequested)
                {
                    var json = await current.Transport.ReceiveAsync(current.Lifetime.Token);
                    if (json == null) throw new InvalidOperationException("The AIAvatarKit server closed the connection.");
                    var message = JObject.Parse(json);
                    if ((string)message["session_id"] != current.SessionId) continue;
                    lock (sync) if (!ReferenceEquals(connection, current) || disposing) return;
                    await HandleMessageAsync(current, message);
                }
            }
            catch (OperationCanceledException) when (current.Lifetime.IsCancellationRequested) { }
            catch (Exception error) { await FailAsync(current, error); }
            finally { lock (sync) current.Connected = false; }
        }

        private async UniTask HandleMessageAsync(Connection current, JObject message)
        {
            var type = (string)message["type"];
            if (type == "connected")
            {
                lock (sync)
                {
                    if (!ReferenceEquals(connection, current) || disposing) return;
                    current.Connected = true;
                    if (message["context_id"]?.Type == JTokenType.String) contextId = (string)message["context_id"];
                }
                current.Ready.TrySetResult(true);
                return;
            }
            if (type == "info")
            {
                var text = (message["metadata"] as JObject)?["partial_request_text"];
                if (text?.Type == JTokenType.String)
                {
                    lock (sync)
                    {
                        if (!ReferenceEquals(connection, current) || disposing || current.Failed) return;
                        current.Recognition = current.Recognition ?? new SpeechRecognitionUpdate
                        { RecognitionId = Guid.NewGuid().ToString("N"), SessionId = SessionId };
                        current.Recognition.Kind = SpeechRecognitionUpdateKind.Partial;
                        current.Recognition.Text = (string)text;
                        current.Recognition.IsSpeechActive = null;
                        current.Recognition.AudioDurationSeconds = null;
                        current.Recognition.ObservedAtSeconds = clock.ElapsedSeconds;
                        PublishRecognition(current.Recognition);
                        Notify(() => SpeechDetecting?.Invoke((string)text));
                    }
                }
                return;
            }
            if (type == "voiced")
            {
                lock (sync)
                {
                    if (!ReferenceEquals(connection, current) || disposing || current.Failed) return;
                    var now = clock.ElapsedSeconds;
                    if (current.Recognition == null)
                    {
                        current.Recognition = new SpeechRecognitionUpdate
                        {
                            RecognitionId = Guid.NewGuid().ToString("N"), SessionId = SessionId,
                            Kind = SpeechRecognitionUpdateKind.Started, IsSpeechActive = true,
                            AudioDurationSeconds = null, ObservedAtSeconds = now
                        };
                        PublishRecognition(current.Recognition);
                    }
                    else if (current.Recognition.TransactionId == null)
                    {
                        // Activity is a separate fact: retain any recognition hypothesis and its
                        // kind until the server confirms or cancels that recognition.
                        PublishRecognition(new SpeechRecognitionUpdate
                        {
                            RecognitionId = current.Recognition.RecognitionId, SessionId = SessionId,
                            Kind = SpeechRecognitionUpdateKind.Activity, IsSpeechActive = true,
                            AudioDurationSeconds = null, ObservedAtSeconds = now
                        });
                    }
                    Notify(() => Voiced?.Invoke());
                }
                return;
            }
            if (type == "stop")
            {
                lock (sync) CloseRecognition(current, SpeechRecognitionUpdateKind.Canceled);
                await PublishAsync(current, AIAvatarProtocol.Response(message, SessionId, null, options.MaxResponseAudioBytes));
                return;
            }
            Turn turn;
            lock (sync) turn = current.Turn;
            if (type == "accepted")
            {
                lock (sync)
                {
                    if (!ReferenceEquals(connection, current) || disposing || current.Failed) return;
                }
                if (turn != null) await CompleteTurnAsync(current, turn, Canceled(turn, "superseded"));
                turn = new Turn();
                lock (sync)
                {
                    if (!ReferenceEquals(connection, current) || disposing) return;
                    current.Turn = turn;
                    turn.RecognitionId = current.Recognition?.RecognitionId;
                    if (current.Recognition != null) current.Recognition.TransactionId = turn.Id;
                }
                WatchTurnAsync(current, turn, turn.Deadline.Token).Forget();
            }
            if (turn == null)
            {
                // The current adapter sends a synthetic interrupted final again at the next accepted event.
                if (type == "final" && (message["metadata"] as JObject)?.Value<bool?>("interrupted") == true) return;
                throw new FormatException("AIAvatarKit sent " + type + " without an accepted response turn.");
            }
            var response = AIAvatarProtocol.Response(message, SessionId, turn.Id, options.MaxResponseAudioBytes);
            if (type == "start")
            {
                lock (sync)
                {
                    if (!ReferenceEquals(connection, current) || disposing) return;
                    if (response.ContextId != null) contextId = response.ContextId;
                    if (current.Recognition?.RecognitionId == turn.RecognitionId)
                    {
                        var recognized = response.Metadata?["recognized_text"];
                        // request_text can contain timestamps, merged requests or internal instructions.
                        if (recognized?.Type == JTokenType.String)
                            CloseRecognition(current, SpeechRecognitionUpdateKind.Confirmed, (string)recognized);
                        else CloseRecognition(current, SpeechRecognitionUpdateKind.Canceled);
                    }
                }
            }
            if (response.IsTerminal)
            {
                if (response.Type == SpeechPipelineResponseType.Final && response.Metadata?.Value<bool?>("interrupted") == true)
                    response.Type = SpeechPipelineResponseType.Canceled;
                await CompleteTurnAsync(current, turn, response);
            }
            else await PublishAsync(current, response);
        }

        private async UniTask CompleteTurnAsync(Connection current, Turn turn, SpeechPipelineResponse response)
        {
            lock (sync)
            {
                turn.Terminal = true;
                if (ReferenceEquals(connection, current) && current.Recognition?.RecognitionId == turn.RecognitionId)
                    CloseRecognition(current, SpeechRecognitionUpdateKind.Canceled);
            }
            turn.Deadline.Cancel();
            await PublishAsync(current, response);
            lock (sync)
            {
                // A control boundary may have retired this connection while application delivery
                // was awaiting. Its retirement now owns cancellation and completion of this turn.
                if (!ReferenceEquals(connection, current)) return;
                if (ReferenceEquals(current.Turn, turn)) current.Turn = null;
            }
            turn.Deadline.Dispose();
            turn.Completion.TrySetResult(response.Copy());
        }

        private async UniTask WatchTurnAsync(Connection current, Turn turn, CancellationToken token)
        {
            try
            {
                await SpeechAsync.Delay(options.RequestTimeout, token);
                token.ThrowIfCancellationRequested();
                await FailAsync(current, new TimeoutException("The AIAvatarKit response did not complete within RequestTimeout."), turn);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { ReportError(error); }
        }

        private async UniTask FailAsync(Connection current, Exception error, Turn expectedTurn = null)
        {
            Turn turn;
            lock (sync)
            {
                if (!ReferenceEquals(connection, current) || disposing || current.Failed) return;
                if (expectedTurn != null && (!ReferenceEquals(current.Turn, expectedTurn) || expectedTurn.Terminal)) return;
                current.Connected = false; current.Failed = true; turn = current.Turn; current.Turn = null;
                CloseRecognition(current, SpeechRecognitionUpdateKind.Canceled);
            }
            current.Ready.TrySetException(error);
            current.Lifetime.Cancel();
            current.Transport.Abort();
            if (turn != null)
            {
                turn.Deadline.Cancel(); turn.Deadline.Dispose();
                var response = new SpeechPipelineResponse
                {
                    Type = SpeechPipelineResponseType.Error, SessionId = SessionId, ContextId = ContextId, TransactionId = turn.Id,
                    Metadata = new JObject { ["error"] = error.Message }
                };
                await PublishAsync(current, response);
                turn.Completion.TrySetException(error);
            }
            ReportError(error);
        }

        private async UniTask RetireLockedAsync(Connection current, string reason)
        {
            Turn turn;
            lock (sync)
            {
                if (ReferenceEquals(connection, current)) connection = null;
                current.Connected = false; turn = current.Turn; current.Turn = null;
                CloseRecognition(current, SpeechRecognitionUpdateKind.Canceled);
            }
            current.Ready.TrySetCanceled();
            var failures = new List<Exception>();
            try { current.Lifetime.Cancel(); } catch (Exception error) { failures.Add(error); }
            try { current.Transport.Abort(); } catch (Exception error) { failures.Add(error); }
            try { await current.Receiving; } catch (Exception error) { failures.Add(error); }
            try { current.Transport.Dispose(); } catch (Exception error) { failures.Add(error); }
            current.Lifetime.Dispose();
            if (turn != null)
            {
                turn.Deadline.Cancel(); turn.Deadline.Dispose();
                var response = Canceled(turn, reason);
                await PublishAsync(null, response);
                turn.Completion.TrySetResult(response);
            }
            if (failures.Count > 0) throw new AggregateException(failures);
        }

        private SpeechPipelineResponse Canceled(Turn turn, string reason) => new SpeechPipelineResponse
        {
            Type = SpeechPipelineResponseType.Canceled, SessionId = SessionId, ContextId = ContextId, TransactionId = turn.Id,
            Metadata = new JObject { ["reason"] = reason }
        };

        private async UniTask PublishAsync(Connection expected, SpeechPipelineResponse response)
        {
            await delivery.WaitAsync();
            try
            {
                var handlers = ResponseReceived;
                if (handlers == null) return;
                foreach (Func<SpeechPipelineResponse, UniTask> handler in handlers.GetInvocationList())
                {
                    lock (sync) if (disposing || (expected != null && !ReferenceEquals(connection, expected))) return;
                    try { await callbacks.InvokeAsync(() => handler(response.Copy())); }
                    catch (Exception error) { ReportError(error); }
                }
            }
            finally { delivery.Release(); }
        }

        // Called under sync so a connection/control boundary cannot overtake a partial notification.
        private void CloseRecognition(Connection current, SpeechRecognitionUpdateKind kind, string text = null)
        {
            var update = current.Recognition;
            if (update == null) return;
            current.Recognition = null;
            update.Kind = kind;
            if (text != null) update.Text = text;
            update.IsSpeechActive = null;
            update.AudioDurationSeconds = null;
            update.ObservedAtSeconds = clock.ElapsedSeconds;
            PublishRecognition(update);
        }

        private void PublishRecognition(SpeechRecognitionUpdate update)
        {
            var handlers = RecognitionUpdated;
            if (handlers == null) return;
            var snapshot = update.Copy();
            foreach (Action<SpeechRecognitionUpdate> handler in handlers.GetInvocationList())
                Notify(() => handler(snapshot.Copy()));
        }

        private void Notify(Action notification)
        {
            try { callbacks.Invoke(notification); }
            catch (Exception error) { ReportError(error); }
        }

        private void ReportError(Exception error)
        {
            var handlers = Error;
            if (handlers == null) return;
            foreach (Action<Exception> handler in handlers.GetInvocationList())
                try { callbacks.Invoke(() => handler(error)); } catch { }
        }

        public UniTask DisposeAsync()
        {
            RejectReentry();
            Connection current;
            var completion = new SpeechCompletionSource<bool>();
            lock (sync)
            {
                if (disposal.HasValue) return disposal.Value;
                disposing = true; disposal = completion.Task; current = connection;
            }
            // Always start cleanup even if an injected transport/cancellation callback throws.
            try { lifetime.Cancel(); } catch (Exception error) { ReportError(error); }
            try { current?.Lifetime.Cancel(); } catch (Exception error) { ReportError(error); }
            try { current?.Transport.Abort(); } catch (Exception error) { ReportError(error); }
            DisposeCoreAsync(completion).Forget();
            return disposal.Value;
        }

        private async UniTask DisposeCoreAsync(SpeechCompletionSource<bool> completion)
        {
            try
            {
                await lifecycle.WaitAsync();
                try
                {
                    Connection current;
                    lock (sync) current = connection;
                    if (current != null) await RetireLockedAsync(current, "disposed");
                }
                finally { lifecycle.Release(); }
                await delivery.WaitAsync(); delivery.Release();
                completion.TrySetResult(true);
            }
            catch (Exception error) { completion.TrySetException(error); }
            finally { lifetime.Dispose(); }
        }

        private CancellationTokenSource Linked(CancellationToken token)
        {
            lock (sync) { CheckAlive(); return CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token); }
        }
        private void CheckAlive() { lock (sync) if (disposing) throw new ObjectDisposedException(nameof(AIAvatarSpeechPipelineClient)); }
        private void RejectReentry() => callbacks.ThrowIfActive("Schedule AIAvatar pipeline control after its response or diagnostic callback returns.");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.LLM;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class OpenAIResponsesWebSocketClientTests
    {
        private sealed class FakeConnection : ILlmWebSocketConnection
        {
            private readonly Queue<string> incoming = new Queue<string>();
            private readonly SpeechAsyncSemaphore available = new SpeechAsyncSemaphore(0);
            private readonly object sync = new object();
            internal readonly List<JObject> Sent = new List<JObject>();
            internal readonly SpeechCompletionSource<bool> ReadEntered = Completion<bool>();
            internal readonly SpeechCompletionSource<bool> SecondSend = Completion<bool>();
            internal Action<FakeConnection, JObject> OnSend;
            internal Exception ConnectFailure;
            internal string ApiKey;
            internal Uri Uri;
            internal bool Disposed;
            internal int AbortCount;
            public bool IsOpen { get; private set; }

            public UniTask ConnectAsync(Uri uri, string apiKey, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Uri = uri;
                ApiKey = apiKey;
                if (ConnectFailure != null) throw ConnectFailure;
                IsOpen = true;
                return UniTask.CompletedTask;
            }

            public UniTask SendTextAsync(string message, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var body = JObject.Parse(message);
                Sent.Add(body);
                OnSend?.Invoke(this, body);
                if (Sent.Count == 2) SecondSend.TrySetResult(true);
                return UniTask.CompletedTask;
            }

            public async UniTask<string> ReceiveTextAsync(CancellationToken token)
            {
                ReadEntered.TrySetResult(true);
                await available.WaitAsync(token);
                lock (sync) return incoming.Dequeue();
            }

            internal void Enqueue(params string[] messages)
            {
                lock (sync) foreach (var message in messages) incoming.Enqueue(message);
                foreach (var _ in messages) available.Release();
            }
            public void Abort() { Interlocked.Increment(ref AbortCount); IsOpen = false; }
            public void Dispose() { Disposed = true; IsOpen = false; }
        }

        private static SpeechCompletionSource<T> Completion<T>() => new SpeechCompletionSource<T>();
        private static OpenAIResponsesWebSocketServiceOptions Options(int maxConnections = 1) => new OpenAIResponsesWebSocketServiceOptions
        {
            ApiKey = "test-key", MaxConnections = maxConnections, TimeoutSeconds = 5,
            SplitChars = new[] { "." }, OptionSplitChars = Array.Empty<string>()
        };
        private static LlmRequest Request(string text = "Hello") => new LlmRequest { ContextId = "test", Text = text };
        private static string Delta(string text) => new JObject { ["type"] = "response.output_text.delta", ["delta"] = text }.ToString();
        private static string Completed(string id = "resp_ok") => new JObject
        {
            ["type"] = "response.completed", ["response"] = new JObject { ["id"] = id, ["output"] = new JArray(), ["usage"] = new JObject { ["output_tokens"] = 2 } }
        }.ToString();
        private static string PreviousError(bool failed = false) => new JObject
        {
            ["type"] = failed ? "response.failed" : "error",
            [failed ? "response" : "error"] = failed
                ? new JObject { ["error"] = ErrorDetails() } : ErrorDetails()
        }.ToString();
        private static JObject ErrorDetails() => new JObject
        {
            ["code"] = "previous_response_not_found", ["param"] = "previous_response_id", ["message"] = "Previous response not found."
        };
        private static FakeConnection Successful() => new FakeConnection { OnSend = (socket, _) => socket.Enqueue(Delta("Hello."), Completed()) };

        private static async UniTask Entered(UniTask entered, UniTask operation)
        {
            var winner = await UniTask.WhenAny(entered, operation, SpeechAsync.Delay(TimeSpan.FromMilliseconds(5000)));
            if (winner == 1) await operation;
            Assert.That(winner, Is.EqualTo(0), "The request did not reach the expected transport operation.");
        }

        [Test]
        public async NUnitTask CompletedConnectionsAreReusedAndContinuationSendsOnlyNewInput()
        {
            var socket = Successful();
            var creations = 0;
            var settings = Options();
            settings.SystemPrompt = "Be concise";
            settings.ReasoningEffort = "low";
            settings.InitialMessages = new JArray(new JObject { ["role"] = "assistant", ["content"] = "Initial" });
            var service = new OpenAIResponsesWebSocketClient(settings, () => { creations++; return socket; });
            try
            {
                var first = await service.ChatAsync(Request());
                Assert.That(first.Text, Is.EqualTo("Hello."));
                Assert.That(first.ResponseId, Is.EqualTo("resp_ok"));
                Assert.That((int)first.Usage["output_tokens"], Is.EqualTo(2));
                var next = Request("Next");
                next.PreviousResponseId = first.ResponseId;
                next.History.Add(new JObject { ["role"] = "assistant", ["content"] = first.Text });
                await service.ChatAsync(next);
                Assert.That(creations, Is.EqualTo(1));
                Assert.That(socket.Sent[0]["type"].Value<string>(), Is.EqualTo("response.create"));
                Assert.That(socket.Sent[0]["stream"], Is.Null);
                Assert.That(socket.Sent[0]["background"], Is.Null);
                Assert.That(socket.Sent[0]["instructions"].Value<string>(), Is.EqualTo("Be concise"));
                Assert.That(socket.Sent[0]["reasoning"]["effort"].Value<string>(), Is.EqualTo("low"));
                Assert.That(((JArray)socket.Sent[0]["input"]).Count, Is.EqualTo(2));
                Assert.That(((JArray)socket.Sent[1]["input"]).Count, Is.EqualTo(1));
                Assert.That(socket.Sent[1]["previous_response_id"].Value<string>(), Is.EqualTo(first.ResponseId));
                Assert.That(socket.Disposed, Is.False);
            }
            finally { await service.DisposeAsync(); }
            Assert.That(socket.Disposed, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask InvalidPreviousIdRetriesOnceWithFullHistoryOnTheSameConnection(bool failed)
        {
            var socket = new FakeConnection
            {
                OnSend = (s, _) => { if (s.Sent.Count == 1) s.Enqueue(PreviousError(failed)); else s.Enqueue(Delta("Recovered."), Completed("resp_new")); }
            };
            var edits = 0;
            var settings = Options();
            settings.InitialMessages.Add(new JObject { ["role"] = "assistant", ["content"] = "Initial" });
            settings.EditRequestParameters = (body, _) => { edits++; body["metadata"] = new JObject { ["test"] = "preserved" }; };
            var request = Request();
            request.PreviousResponseId = "resp_missing";
            request.History.Add(new JObject { ["role"] = "user", ["content"] = "Earlier" });
            request.History.Add(new JObject { ["role"] = "assistant", ["content"] = "Answer" });
            var service = new OpenAIResponsesWebSocketClient(settings, () => socket);
            try
            {
                var result = await service.ChatAsync(request);
                Assert.That(result.Error, Is.Null);
                Assert.That(result.RecoveredPreviousResponse, Is.True);
                Assert.That(result.ResponseId, Is.EqualTo("resp_new"));
                Assert.That(socket.Sent.Count, Is.EqualTo(2));
                Assert.That(socket.Sent[1]["previous_response_id"], Is.Null);
                Assert.That(((JArray)socket.Sent[1]["input"]).Count, Is.EqualTo(4));
                Assert.That((string)socket.Sent[1]["input"][3]["content"], Is.EqualTo("Hello"));
                Assert.That((string)socket.Sent[1]["metadata"]["test"], Is.EqualTo("preserved"));
                Assert.That(edits, Is.EqualTo(1));
                Assert.That(request.PreviousResponseId, Is.EqualTo("resp_missing"));
                Assert.That(request.History.Count, Is.EqualTo(2));
            }
            finally { await service.DisposeAsync(); }
        }

        [TestCase("disabled", 1)]
        [TestCase("after_output", 1)]
        [TestCase("not_user", 1)]
        [TestCase("no_previous", 1)]
        [TestCase("repeated_failure", 2)]
        public async NUnitTask RecoveryIsRestrictedToOneUnstartedUserTurn(string scenario, int sends)
        {
            var socket = new FakeConnection { OnSend = (s, _) =>
            {
                if (scenario == "after_output") s.Enqueue(Delta("Partial."));
                s.Enqueue(PreviousError());
            } };
            var settings = Options();
            settings.EnablePreviousResponseFallback = scenario != "disabled";
            var request = Request();
            if (scenario != "no_previous") request.PreviousResponseId = "resp_missing";
            if (scenario == "not_user") request.Input = new JArray(new JObject { ["role"] = "assistant", ["content"] = "Input" });
            var service = new OpenAIResponsesWebSocketClient(settings, () => socket);
            try
            {
                var result = await service.ChatAsync(request);
                Assert.That(result.Error, Is.Not.Null);
                Assert.That(result.ResponseId, Is.Null);
                Assert.That(socket.Sent.Count, Is.EqualTo(sends));
                Assert.That(socket.Disposed, Is.True);
            }
            finally { await service.DisposeAsync(); }
        }

        [TestCase("error")]
        [TestCase("failed")]
        [TestCase("incomplete")]
        [TestCase("close")]
        [TestCase("invalid_json")]
        [TestCase("missing_id")]
        public async NUnitTask AnUnsuccessfulResponseDiscardsItsConnection(string failure)
        {
            var bad = new FakeConnection { OnSend = (s, _) =>
            {
                switch (failure)
                {
                    case "error": s.Enqueue(PreviousError()); break;
                    case "failed": s.Enqueue(PreviousError(true)); break;
                    case "incomplete": s.Enqueue("{\"type\":\"response.incomplete\",\"response\":{\"incomplete_details\":{\"reason\":\"max_output_tokens\"}}}"); break;
                    case "close": s.Enqueue((string)null); break;
                    case "invalid_json": s.Enqueue("not-json"); break;
                    case "missing_id": s.Enqueue("{\"type\":\"response.completed\",\"response\":{}}"); break;
                }
            } };
            var good = Successful();
            var pending = new Queue<FakeConnection>(new[] { bad, good });
            var service = new OpenAIResponsesWebSocketClient(Options(), () => pending.Dequeue());
            try
            {
                var result = await service.ChatAsync(Request());
                Assert.That(result.Error, Is.Not.Null);
                Assert.That(result.ResponseId, Is.Null);
                Assert.That(bad.Disposed, Is.True);
                Assert.That((await service.ChatAsync(Request())).ResponseId, Is.EqualTo("resp_ok"));
                Assert.That(good.Sent.Count, Is.EqualTo(1));
            }
            finally { await service.DisposeAsync(); }
        }

        [TestCase("response.output_item.added")]
        [TestCase("response.output_item.done")]
        [TestCase("response.function_call_arguments.delta")]
        [TestCase("response.function_call_arguments.done")]
        [TestCase("response.refusal.delta")]
        [TestCase("response.reasoning_summary_text.delta")]
        [TestCase("response.content_part.added")]
        public async NUnitTask NonTextOutputAlsoPreventsPreviousIdRecovery(string eventType)
        {
            var output = new JObject
            {
                ["type"] = eventType, ["output_index"] = 0, ["delta"] = "partial", ["arguments"] = "{}",
                ["item"] = new JObject { ["type"] = "function_call", ["name"] = "sample", ["call_id"] = "call_1", ["arguments"] = "{}" }
            };
            var socket = new FakeConnection { OnSend = (s, _) => s.Enqueue(output.ToString(), PreviousError()) };
            var service = new OpenAIResponsesWebSocketClient(Options(), () => socket);
            var request = Request();
            request.PreviousResponseId = "resp_missing";
            try
            {
                var result = await service.ChatAsync(request);
                Assert.That(result.Error, Is.Not.Null);
                Assert.That(result.ResponseId, Is.Null);
                Assert.That(result.RecoveredPreviousResponse, Is.False);
                Assert.That(socket.Sent.Count, Is.EqualTo(1));
                Assert.That(socket.Disposed, Is.True);
            }
            finally { await service.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask CallbackFailurePropagatesAndDoesNotReuseUnreadEvents()
        {
            var socket = Successful();
            var service = new OpenAIResponsesWebSocketClient(Options(), () => socket);
            try
            {
                await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(async () =>
                    await service.ChatAsync(Request(), _ => throw new InvalidOperationException("callback-failure")));
                Assert.That(socket.Disposed, Is.True);
            }
            finally { await service.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask CancelledReceiveDiscardsConnectionAndAllowsTheNextRequest()
        {
            var blocked = new FakeConnection();
            var good = Successful();
            var pending = new Queue<FakeConnection>(new[] { blocked, good });
            var service = new OpenAIResponsesWebSocketClient(Options(), () => pending.Dequeue());
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    var call = service.ChatAsync(Request(), cancellationToken: cancellation.Token);
                    await Entered(blocked.ReadEntered.Task, call);
                    cancellation.Cancel();
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await call);
                    Assert.That(blocked.Disposed, Is.True);
                    Assert.That((await service.ChatAsync(Request())).Error, Is.Null);
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask DisposeCancelsActiveAndQueuedRequestsAndClosesOwnedConnections()
        {
            var socket = new FakeConnection();
            var creations = 0;
            var service = new OpenAIResponsesWebSocketClient(Options(), () => { creations++; return socket; });
            var first = service.ChatAsync(Request());
            await Entered(socket.ReadEntered.Task, first);
            var second = service.ChatAsync(Request());
            await service.DisposeAsync();
            await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await first);
            await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await second);
            Assert.That(creations, Is.EqualTo(1));
            Assert.That(socket.Disposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => service.ChatAsync(Request()));
            await service.DisposeAsync();
        }

        [Test]
        public async NUnitTask PoolAllowsBoundedParallelRequestsAndReusesAReleasedSlot()
        {
            var sockets = new List<FakeConnection>();
            var service = new OpenAIResponsesWebSocketClient(Options(2), () =>
            {
                var socket = new FakeConnection();
                lock (sockets) sockets.Add(socket);
                return socket;
            });
            try
            {
                var first = service.ChatAsync(Request("First"));
                var second = service.ChatAsync(Request("Second"));
                FakeConnection a;
                FakeConnection b;
                lock (sockets) { a = sockets[0]; b = sockets[1]; }
                await Entered(a.ReadEntered.Task, first);
                await Entered(b.ReadEntered.Task, second);
                var third = service.ChatAsync(Request("Third"));
                Assert.That(sockets.Count, Is.EqualTo(2));
                Assert.That(third.Status.IsCompleted(), Is.False);
                a.Enqueue(Completed("resp_first"));
                await first;
                await Entered(a.SecondSend.Task, third);
                Assert.That(sockets.Count, Is.EqualTo(2));
                a.Enqueue(Completed("resp_third"));
                b.Enqueue(Completed("resp_second"));
                Assert.That((await second).ResponseId, Is.EqualTo("resp_second"));
                Assert.That((await third).ResponseId, Is.EqualTo("resp_third"));
            }
            finally { await service.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask ExpiredConnectionIsReplaced()
        {
            var sockets = new List<FakeConnection>();
            var options = Options();
            options.MaxConnectionAgeSeconds = 0.001;
            var service = new OpenAIResponsesWebSocketClient(options, () => { var socket = Successful(); sockets.Add(socket); return socket; });
            try
            {
                await service.ChatAsync(Request());
                await SpeechAsync.Delay(TimeSpan.FromMilliseconds(5));
                await service.ChatAsync(Request());
                Assert.That(sockets.Count, Is.EqualTo(2));
                Assert.That(sockets[0].Disposed, Is.True);
            }
            finally { await service.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask UpdatingConnectionSettingsRetiresOldIdleConnections()
        {
            var sockets = new List<FakeConnection>();
            var service = new OpenAIResponsesWebSocketClient(Options(), () => { var socket = Successful(); sockets.Add(socket); return socket; });
            try
            {
                await service.ChatAsync(Request());
                var updated = Options(2);
                updated.ApiKey = "updated-test-key";
                updated.WebSocketUrl = "wss://example.test/responses";
                service.UpdateOptions(updated);
                await service.ChatAsync(Request());
                Assert.That(sockets.Count, Is.EqualTo(2));
                Assert.That(sockets[0].Disposed, Is.True);
                Assert.That(sockets[1].ApiKey, Is.EqualTo("updated-test-key"));
                Assert.That(sockets[1].Uri.AbsoluteUri, Is.EqualTo("wss://example.test/responses"));
            }
            finally { await service.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask ConnectFailureDisposesTheConnectionAndReleasesCapacity()
        {
            var bad = new FakeConnection { ConnectFailure = new IOException("private handshake details") };
            var good = Successful();
            var sockets = new Queue<FakeConnection>(new[] { bad, good });
            var service = new OpenAIResponsesWebSocketClient(Options(), () => sockets.Dequeue());
            try
            {
                var error = (await service.ChatAsync(Request())).Error;
                Assert.That(error.Code, Is.EqualTo("websocket_transport_error"));
                Assert.That(error.Message, Does.Not.Contain("private handshake"));
                Assert.That(bad.Disposed, Is.True);
                Assert.That((await service.ChatAsync(Request())).Error, Is.Null);
            }
            finally { await service.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask RequestTimeoutDiscardsConnection()
        {
            var socket = new FakeConnection();
            var options = Options();
            options.TimeoutSeconds = 0.03;
            var service = new OpenAIResponsesWebSocketClient(options, () => socket);
            try
            {
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await service.ChatAsync(Request()));
                Assert.That(socket.Disposed, Is.True);
            }
            finally { await service.DisposeAsync(); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async NUnitTask ToolContinuationUsesFinalEditedStoreSetting(bool store)
        {
            var call = new JObject { ["type"] = "function_call", ["call_id"] = "call_store", ["name"] = "sample", ["arguments"] = "{}" };
            var socket = new FakeConnection { OnSend = (s, _) =>
            {
                if (s.Sent.Count == 1)
                    s.Enqueue(new JObject { ["type"] = "response.completed", ["response"] = new JObject { ["id"] = "resp_tool", ["output"] = new JArray(call.DeepClone()) } }.ToString());
                else s.Enqueue(Delta("Done."), Completed("resp_final"));
            } };
            var options = Options();
            // Exercise the final payload edit, not just the obvious ExtraBody/Parameters values.
            options.ExtraBody = new JObject { ["store"] = !store };
            options.EditRequestParameters = (body, _) => body["store"] = store;
            options.Tools = new[] { new LlmTool { Name = "sample", ExecuteAsync = (_, __, ___) => UniTask.FromResult(new LlmToolResult { Data = new JValue("tool output") }) } };
            var service = new OpenAIResponsesWebSocketClient(options, () => socket);
            try
            {
                var result = await service.ChatAsync(Request());
                Assert.That(result.Error, Is.Null);
                Assert.That(result.ResponseId, Is.EqualTo("resp_final"));
                Assert.That(socket.Sent.Count, Is.EqualTo(2));
                Assert.That((bool)socket.Sent[0]["store"], Is.EqualTo(store));
                var followup = socket.Sent[1];
                if (store)
                {
                    Assert.That((string)followup["previous_response_id"], Is.EqualTo("resp_tool"));
                    Assert.That(((JArray)followup["input"]).Count, Is.EqualTo(1));
                }
                else
                {
                    Assert.That(followup["previous_response_id"], Is.Null);
                    Assert.That(((JArray)followup["input"]).Count, Is.EqualTo(3));
                    Assert.That((string)followup["input"][0]["role"], Is.EqualTo("user"));
                    Assert.That((string)followup["input"][1]["type"], Is.EqualTo("function_call"));
                }
                Assert.That((string)((JArray)followup["input"]).Last["type"], Is.EqualTo("function_call_output"));
            }
            finally { await service.DisposeAsync(); }
        }

        [Test]
        public async NUnitTask NativeTransportPreservesUtf8AcrossWebSocketFragments()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            using (var connection = new NativeLlmWebSocketConnection())
            {
                try
                {
                    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    var peerTask = ServeFragments(listener, timeout.Token);
                    await connection.ConnectAsync(new Uri("ws://127.0.0.1:" + port), "loopback-test-key", timeout.Token);
                    Assert.That(await connection.ReceiveTextAsync(timeout.Token), Is.EqualTo("日本語"));
                    Assert.That(await connection.ReceiveTextAsync(timeout.Token), Is.Null);
                    await peerTask;
                }
                finally { listener.Stop(); }
            }
        }

        private static async UniTask ServeFragments(TcpListener listener, CancellationToken token)
        {
            using (var peer = await SpeechAsync.FromTask(listener.AcceptTcpClientAsync()))
            using (var stream = peer.GetStream())
            {
                var header = new StringBuilder();
                var single = new byte[1];
                while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (await SpeechAsync.FromTask(stream.ReadAsync(single, 0, 1, token)) == 0) throw new IOException("No handshake.");
                    header.Append((char)single[0]);
                }
                var keyLine = header.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None)
                    .Single(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase));
                var key = keyLine.Substring(keyLine.IndexOf(':') + 1).Trim();
                string accept;
                using (var sha1 = SHA1.Create()) accept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                var response = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
                await SpeechAsync.FromTask(stream.WriteAsync(response, 0, response.Length, token));
                var utf8 = Encoding.UTF8.GetBytes("日本語");
                // Split the first three-byte character between an initial and continuation frame.
                var frames = new byte[2 + 2 + 2 + utf8.Length - 2 + 4];
                frames[0] = 0x01; frames[1] = 2; frames[2] = utf8[0]; frames[3] = utf8[1];
                frames[4] = 0x80; frames[5] = (byte)(utf8.Length - 2);
                Buffer.BlockCopy(utf8, 2, frames, 6, utf8.Length - 2);
                var close = 6 + utf8.Length - 2;
                frames[close] = 0x88; frames[close + 1] = 2; frames[close + 2] = 0x03; frames[close + 3] = 0xe8;
                await SpeechAsync.FromTask(stream.WriteAsync(frames, 0, frames.Length, token));
            }
        }
    }
}

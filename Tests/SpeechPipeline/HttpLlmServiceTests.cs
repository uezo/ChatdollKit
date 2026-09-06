using ChatdollKit.SpeechPipeline.STT;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
    public class HttpLlmServiceTests
    {
        [Test]
        public async NUnitTask ChatBuildsNativeMessagesAndPreservesZeroAndParameterPrecedence()
        {
            var handler = new FakeHandler(_ => Sse(ChatText("答え。") + "data: [DONE]\n\n"));
            using (var client = new HttpClient(handler))
            {
                var options = Options();
                options.SystemPrompt = "system";
                options.InitialMessages = new JArray(Message("assistant", "initial"));
                options.Temperature = 0;
                options.ReasoningEffort = "none";
                options.MaxOutputTokens = 42;
                options.ExtraBody = new JObject { ["top_p"] = 0.2, ["custom"] = "extra" };
                options.EditRequestParameters = (body, _) => body["custom"] = "edited";
                var request = new LlmRequest
                {
                    ContextId = "chat", Text = "question", ImageUrls = new[] { "https://example.test/image.png" },
                    History = new JArray(Message("user", "history")),
                    Parameters = new JObject { ["top_p"] = 0.4, ["custom"] = "request" }
                };
                var service = new ChatCompletionsClient(options, client);
                try
                {
                    var result = await service.ChatAsync(request);
                    Assert.That(result.Error, Is.Null);
                    Assert.That(result.Text, Is.EqualTo("答え。"));
                    var sent = handler.Requests.Single();
                    Assert.That(sent.Path, Is.EqualTo("/v1/chat/completions"));
                    Assert.That(sent.Authorization, Is.EqualTo("Bearer test-key"));
                    Assert.That(client.DefaultRequestHeaders.Authorization, Is.Null);
                    Assert.That((double)sent.Body["temperature"], Is.Zero);
                    Assert.That((string)sent.Body["reasoning_effort"], Is.EqualTo("none"));
                    Assert.That((int)sent.Body["max_completion_tokens"], Is.EqualTo(42));
                    Assert.That((double)sent.Body["top_p"], Is.EqualTo(0.4));
                    Assert.That((string)sent.Body["custom"], Is.EqualTo("edited"));
                    Assert.That(sent.Body["extra_body"], Is.Null);
                    var messages = (JArray)sent.Body["messages"];
                    Assert.That(messages.Count, Is.EqualTo(4));
                    Assert.That((string)messages[0]["role"], Is.EqualTo("system"));
                    Assert.That((string)messages[2]["content"], Is.EqualTo("history"));
                    Assert.That((string)messages[3]["content"][0]["image_url"]["url"], Is.EqualTo(request.ImageUrls[0]));
                    Assert.That((string)messages[3]["content"][1]["type"], Is.EqualTo("text"));
                    Assert.That((string)result.OutputItems[0]["content"], Is.EqualTo("答え。"));
                    Assert.That(request.History.Count, Is.EqualTo(1));
                }
                finally { await service.DisposeAsync(); }
                Assert.That(handler.Disposed, Is.False, "Injected HTTP client remains caller-owned.");
            }
        }

        [Test]
        public async NUnitTask ChatTrimsHistoryBeforeFirstUserWhileKeepingInitialMessages()
        {
            var handler = new FakeHandler(_ => Sse("data: [DONE]\n\n"));
            using (var client = new HttpClient(handler))
            {
                var options = Options();
                options.InitialMessages.Add(Message("assistant", "initial"));
                var service = new ChatCompletionsClient(options, client);
                try
                {
                    await service.ChatAsync(new LlmRequest
                    {
                        Text = "current", History = new JArray(Message("assistant", "orphan"), Message("tool", "orphan_tool"),
                            Message("user", "first_user"), Message("assistant", "reply"))
                    });
                    Assert.That(((JArray)handler.Requests[0].Body["messages"]).Select(x => (string)x["content"]),
                        Is.EqualTo(new[] { "initial", "first_user", "reply", "current" }));
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask ResponsesBuildsImagesAndCarriesInstructionsWithPreviousId()
        {
            var handler = new FakeHandler(_ => Sse(ResponsesText("yes") + Completed("resp_new")));
            using (var client = new HttpClient(handler))
            {
                var options = Options();
                options.SystemPrompt = "instruction";
                options.InitialMessages = new JArray(Message("user", "initial"));
                options.ReasoningEffort = "low";
                options.ToolDefinitions = JArray.Parse("[{\"type\":\"function\",\"function\":{\"name\":\"lookup\",\"parameters\":{\"type\":\"object\"},\"strict\":true}}]");
                var service = new OpenAIResponsesClient(options, client);
                try
                {
                    var result = await service.ChatAsync(new LlmRequest
                    {
                        Text = "what", PreviousResponseId = "resp_old", ImageUrls = new[] { "https://example.test/a.png" },
                        History = new JArray(Message("user", "history"))
                    });
                    Assert.That(result.Error, Is.Null);
                    Assert.That(result.ResponseId, Is.EqualTo("resp_new"));
                    var body = handler.Requests[0].Body;
                    Assert.That(handler.Requests[0].Path, Is.EqualTo("/v1/responses"));
                    Assert.That((string)body["instructions"], Is.EqualTo("instruction"));
                    Assert.That((string)body["previous_response_id"], Is.EqualTo("resp_old"));
                    Assert.That((string)body["reasoning"]["effort"], Is.EqualTo("low"));
                    Assert.That(body["temperature"], Is.Null);
                    Assert.That(((JArray)body["input"]).Count, Is.EqualTo(1));
                    Assert.That((string)body["input"][0]["content"][0]["type"], Is.EqualTo("input_image"));
                    Assert.That((string)body["input"][0]["content"][1]["type"], Is.EqualTo("input_text"));
                    Assert.That((string)body["tools"][0]["name"], Is.EqualTo("lookup"));
                    Assert.That((bool)body["tools"][0]["strict"], Is.True);
                    Assert.That(body["tools"][0]["function"], Is.Null);
                    Assert.That(options.ToolDefinitions[0]["function"], Is.Not.Null);
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask ResponsesToolFollowupUsesEffectiveStoreAfterOverridesAndEdit(bool overrideWithTrue)
        {
            var toolResponse = Data(JObject.Parse("{\"type\":\"response.completed\",\"response\":{\"id\":\"tool_response\",\"output\":[{\"type\":\"function_call\",\"call_id\":\"call1\",\"name\":\"lookup\",\"arguments\":\"{}\"}]}}"));
            var handler = new FakeHandler(index => Sse(index == 0 ? toolResponse : ResponsesText("done") + Completed("final_response")));
            using (var client = new HttpClient(handler))
            {
                var options = Options();
                options.ExtraBody = new JObject { ["store"] = overrideWithTrue ? false : true };
                if (!overrideWithTrue) options.EditRequestParameters = (body, _) => body["store"] = false;
                var service = new OpenAIResponsesClient(options, client);
                service.ToolExecutorAsync = (_, __, ___) => UniTask.FromResult(new LlmToolResult { Data = new JObject { ["ok"] = true } });
                try
                {
                    var result = await service.ChatAsync(new LlmRequest
                    {
                        Text = "lookup", Parameters = overrideWithTrue ? new JObject { ["store"] = true } : null
                    });
                    Assert.That(result.Error, Is.Null);
                    Assert.That(handler.Requests.Count, Is.EqualTo(2));
                    Assert.That((bool)handler.Requests[0].Body["store"], Is.EqualTo(overrideWithTrue));
                    var followup = handler.Requests[1].Body;
                    var input = (JArray)followup["input"];
                    Assert.That(input.Count, Is.EqualTo(overrideWithTrue ? 1 : 3));
                    Assert.That((string)followup["previous_response_id"], Is.EqualTo(overrideWithTrue ? "tool_response" : null));
                    Assert.That((string)input.Last["type"], Is.EqualTo("function_call_output"));
                    if (!overrideWithTrue)
                    {
                        Assert.That((string)input[0]["content"], Is.EqualTo("lookup"));
                        Assert.That((string)input[1]["type"], Is.EqualTo("function_call"));
                    }
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask ResponsesWithoutIdIncludesCallerHistoryAndNativeCurrentInput()
        {
            var handler = new FakeHandler(_ => Sse(Completed("new")));
            using (var client = new HttpClient(handler))
            {
                var options = Options();
                options.InitialMessages.Add(Message("user", "initial"));
                var service = new OpenAIResponsesClient(options, client);
                try
                {
                    await service.ChatAsync(new LlmRequest
                    {
                        Text = "ignored", History = new JArray(Message("assistant", "history")),
                        Input = new JArray(Message("user", "native"))
                    });
                    var input = (JArray)handler.Requests[0].Body["input"];
                    Assert.That(input.Select(x => (string)x["content"]), Is.EqualTo(new[] { "initial", "history", "native" }));
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask MissingPreviousResponseRetriesOnceWithHistoryAndKeepsEditedParameters()
        {
            var handler = new FakeHandler(index => index == 0 ? MissingId() : Sse(ResponsesText("restored") + Completed("new_id")));
            using (var client = new HttpClient(handler))
            {
                var editCalls = 0;
                var options = Options();
                options.InitialMessages.Add(Message("user", "initial"));
                options.EditRequestParameters = (body, _) => { editCalls++; body["temperature"] = 0.3; };
                var service = new OpenAIResponsesClient(options, client);
                var chunks = new List<LlmResponse>();
                try
                {
                    var result = await service.ChatAsync(new LlmRequest
                    {
                        ContextId = "ctx", Text = "current", PreviousResponseId = "stale",
                        History = new JArray(Message("assistant", "history"))
                    }, chunk => { chunks.Add(chunk); return UniTask.CompletedTask; });
                    Assert.That(result.Error, Is.Null);
                    Assert.That(result.RecoveredPreviousResponse, Is.True);
                    Assert.That(result.ResponseId, Is.EqualTo("new_id"));
                    Assert.That(editCalls, Is.EqualTo(1));
                    Assert.That(handler.Requests.Count, Is.EqualTo(2));
                    var retry = handler.Requests[1].Body;
                    Assert.That(retry["previous_response_id"], Is.Null);
                    Assert.That((double)retry["temperature"], Is.EqualTo(0.3));
                    Assert.That(((JArray)retry["input"]).Select(x => (string)x["content"]), Is.EqualTo(new[] { "initial", "history", "current" }));
                    Assert.That(chunks.Count(x => x.IsRecovery && !x.IsFinal), Is.EqualTo(1));
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [TestCase("disabled")]
        [TestCase("no_id")]
        [TestCase("tool_output")]
        [TestCase("unrelated")]
        public async NUnitTask MissingIdFallbackRequiresEveryCondition(string mode)
        {
            var handler = new FakeHandler(_ => mode == "unrelated"
                ? JsonError("{\"error\":{\"code\":\"model_not_found\",\"message\":\"Model not found\",\"param\":\"model\"}}") : MissingId());
            using (var client = new HttpClient(handler))
            {
                var options = Options();
                options.EnablePreviousResponseFallback = mode != "disabled";
                var service = new OpenAIResponsesClient(options, client);
                try
                {
                    var request = new LlmRequest { Text = "current", PreviousResponseId = mode == "no_id" ? null : "old" };
                    if (mode == "tool_output") request.Input = JArray.Parse("[{\"type\":\"function_call_output\",\"call_id\":\"call1\",\"output\":\"ok\"}]");
                    var result = await service.ChatAsync(request);
                    Assert.That(result.Error, Is.Not.Null);
                    Assert.That(result.Error.StatusCode, Is.EqualTo(400));
                    Assert.That(handler.Requests.Count, Is.EqualTo(1));
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask RepeatedMissingIdDoesNotCauseThirdRequest()
        {
            var handler = new FakeHandler(_ => MissingId());
            using (var client = new HttpClient(handler))
            {
                var service = new OpenAIResponsesClient(Options(), client);
                try
                {
                    var result = await service.ChatAsync(new LlmRequest { Text = "current", PreviousResponseId = "old" });
                    Assert.That(result.Error, Is.Not.Null);
                    Assert.That(handler.Requests.Count, Is.EqualTo(2));
                    Assert.That(result.ResponseId, Is.Null);
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [TestCase("response.failed", "server_error")]
        [TestCase("response.incomplete", "max_output_tokens")]
        [TestCase("error", "previous_response_not_found")]
        [TestCase("eof", "unexpected_eof")]
        public async NUnitTask ResponsesStreamFailureNeverCommitsIdOrRetries(string type, string expectedCode)
        {
            var tail = type == "eof" ? "" : Data(new JObject
            {
                ["type"] = type,
                ["error"] = new JObject { ["code"] = "previous_response_not_found", ["message"] = "Previous response not found" },
                ["response"] = new JObject
                {
                    ["id"] = "unfinished", ["error"] = new JObject { ["code"] = "server_error", ["message"] = "Failed" },
                    ["incomplete_details"] = new JObject { ["reason"] = "max_output_tokens" }
                }
            });
            var handler = new FakeHandler(_ => Sse(Data(new JObject { ["type"] = "response.created", ["response"] = new JObject { ["id"] = "unfinished" } }) + ResponsesText("partial。") + tail));
            using (var client = new HttpClient(handler))
            {
                var service = new OpenAIResponsesClient(Options(), client);
                try
                {
                    var result = await service.ChatAsync(new LlmRequest { Text = "current", PreviousResponseId = "old" });
                    Assert.That(result.Error?.Code, Is.EqualTo(expectedCode));
                    Assert.That(result.ResponseId, Is.Null);
                    Assert.That(handler.Requests.Count, Is.EqualTo(1));
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask SseHandlesByteFragmentsMultilineDataCommentsAndMixedNewlines()
        {
            var content = ": heartbeat\r\nid: ignored\r\nevent: message\r\ndata: {\"choices\":[{\"delta\":\r\ndata: {\"content\":\"こんにちは。\"}}]}\r\n\r\n" +
                "data: {\"choices\":[],\"usage\":{\"total_tokens\":3}}\n\n" + "data: [DONE]\r\r";
            var stream = new FragmentedStream(Encoding.UTF8.GetBytes(content));
            var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
            using (var client = new HttpClient(handler))
            {
                var service = new ChatCompletionsClient(Options(), client);
                try
                {
                    var result = await service.ChatAsync(new LlmRequest { Text = "hi" });
                    Assert.That(result.Error, Is.Null);
                    Assert.That(result.Text, Is.EqualTo("こんにちは。"));
                    Assert.That((int)result.Usage["total_tokens"], Is.EqualTo(3));
                    Assert.That(stream.Disposed, Is.True);
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [TestCase("data: {broken}\n\n", "invalid_json")]
        [TestCase("data: {\"choices\":[]}\n\n", "unexpected_eof")]
        [TestCase("data: {\"choices\":[{\"finish_reason\":\"length\"}]}\n\n", "length")]
        public async NUnitTask ChatMalformedOrUnfinishedStreamIsAnError(string content, string expectedCode)
        {
            var handler = new FakeHandler(_ => Sse(content));
            using (var client = new HttpClient(handler))
            {
                var service = new ChatCompletionsClient(Options(), client);
                try
                {
                    var result = await service.ChatAsync(new LlmRequest { Text = "hi" });
                    Assert.That(result.Error?.Code, Is.EqualTo(expectedCode));
                    Assert.That(handler.Requests.Count, Is.EqualTo(1));
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask ProvidersAssembleInterleavedToolsAndPreserveNativeOutput(bool responses)
        {
            string content;
            if (responses)
            {
                content = Data(JObject.Parse("{\"type\":\"response.output_item.added\",\"output_index\":1,\"item\":{\"type\":\"function_call\",\"id\":\"item1\",\"call_id\":\"call1\",\"name\":\"first\",\"arguments\":\"\"}}")) +
                    Data(JObject.Parse("{\"type\":\"response.output_item.added\",\"output_index\":2,\"item\":{\"type\":\"function_call\",\"call_id\":\"call2\",\"name\":\"second\",\"arguments\":\"\"}}")) +
                    Data(JObject.Parse("{\"type\":\"response.function_call_arguments.delta\",\"output_index\":1,\"delta\":\"{\\\"x\\\":\"}")) +
                    Data(JObject.Parse("{\"type\":\"response.function_call_arguments.delta\",\"output_index\":2,\"delta\":\"{}\"}")) +
                    Data(JObject.Parse("{\"type\":\"response.function_call_arguments.delta\",\"output_index\":1,\"delta\":\"1}\"}")) +
                    Data(JObject.Parse("{\"type\":\"response.completed\",\"response\":{\"id\":\"resp_tools\",\"output\":[{\"type\":\"reasoning\",\"id\":\"reason1\"},{\"type\":\"function_call\",\"call_id\":\"call1\",\"name\":\"first\",\"arguments\":\"{\\\"x\\\":1}\"},{\"type\":\"function_call\",\"call_id\":\"call2\",\"name\":\"second\",\"arguments\":\"{}\"}]}}"));
            }
            else
            {
                content = Data(JObject.Parse("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call1\",\"function\":{\"name\":\"first\",\"arguments\":\"{\\\"x\\\":\"}},{\"index\":1,\"id\":\"call2\",\"function\":{\"name\":\"second\",\"arguments\":\"{}\"}}]}}]}")) +
                    Data(JObject.Parse("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"1}\"}}]},\"finish_reason\":\"tool_calls\"}]}")) + "data: [DONE]\n\n";
            }
            var handler = new FakeHandler(_ => Sse(content));
            using (var client = new HttpClient(handler))
            {
                LlmServiceBase service = responses ? (LlmServiceBase)new OpenAIResponsesClient(Options(), client) : new ChatCompletionsClient(Options(), client);
                try
                {
                    var result = await service.ChatAsync(new LlmRequest { Text = "call tools" });
                    Assert.That(result.Error, Is.Null);
                    Assert.That(result.ToolCalls.Select(x => x.Id), Is.EqualTo(new[] { "call1", "call2" }));
                    Assert.That(result.ToolCalls.Select(x => x.Name), Is.EqualTo(new[] { "first", "second" }));
                    Assert.That(result.ToolCalls.Select(x => x.Arguments), Is.EqualTo(new[] { "{\"x\":1}", "{}" }));
                    Assert.That(handler.Requests.Count, Is.EqualTo(1), "Without an executor, calls are returned to the caller.");
                    Assert.That(result.OutputItems.Count, Is.EqualTo(responses ? 3 : 1));
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask DisposeCancelsBlockedStreamAndKeepsInjectedClientAlive()
        {
            var stream = new BlockingStream();
            var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
            using (var client = new HttpClient(handler))
            {
                var service = new OpenAIResponsesClient(Options(), client);
                var operation = service.ChatAsync(new LlmRequest { Text = "hi" });
                try
                {
                    await Bounded(stream.ReadStarted.Task);
                    await Bounded(service.DisposeAsync());
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await operation);
                    Assert.That(stream.Disposed, Is.True);
                    Assert.That(handler.Disposed, Is.False);
                }
                finally { stream.Dispose(); await Bounded(service.DisposeAsync()); }
            }
        }

        [Test]
        public async NUnitTask ThrowingConsumerReleasesStreamWithoutRetry()
        {
            var stream = new FragmentedStream(Encoding.UTF8.GetBytes(ResponsesText("sentence。") + Completed("new")));
            var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
            using (var client = new HttpClient(handler))
            {
                var service = new OpenAIResponsesClient(Options(), client);
                var failure = new InvalidOperationException("consumer failed");
                try
                {
                    var thrown = await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(async () =>
                        await service.ChatAsync(new LlmRequest { Text = "hi" }, _ => throw failure));
                    Assert.That(thrown, Is.SameAs(failure));
                    Assert.That(stream.Disposed, Is.True);
                    Assert.That(handler.Requests.Count, Is.EqualTo(1));
                }
                finally { await service.DisposeAsync(); }
            }
        }

        private static LlmServiceOptions Options() => new LlmServiceOptions { ApiKey = "test-key", BaseUrl = "https://example.test/v1", Model = "test-model", TimeoutSeconds = 3 };
        private static JObject Message(string role, string content) => new JObject { ["role"] = role, ["content"] = content };
        private static string Data(JObject data) => "data: " + data.ToString(Newtonsoft.Json.Formatting.None) + "\n\n";
        private static string ResponsesText(string text) => Data(new JObject { ["type"] = "response.output_text.delta", ["delta"] = text });
        private static string ChatText(string text) => Data(new JObject { ["id"] = "chat_id", ["choices"] = new JArray(new JObject { ["delta"] = new JObject { ["content"] = text } }) });
        private static string Completed(string id) => Data(new JObject { ["type"] = "response.completed", ["response"] = new JObject { ["id"] = id, ["output"] = new JArray(), ["usage"] = new JObject { ["total_tokens"] = 2 } } });
        private static HttpResponseMessage Sse(string text) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/event-stream") };
        private static HttpResponseMessage JsonError(string text) => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        private static HttpResponseMessage MissingId() => JsonError("{\"error\":{\"code\":\"previous_response_not_found\",\"param\":\"previous_response_id\",\"message\":\"Previous response does not exist\"}}");
        private static async UniTask Bounded(UniTask task)
        {
            task = SpeechAsync.Share(task);
            Assert.That(await UniTask.WhenAny(task, SpeechAsync.Delay(TimeSpan.FromMilliseconds(3000))), Is.EqualTo(0), "Operation did not settle within three seconds.");
            await task;
        }

        private sealed class RequestSnapshot
        {
            internal string Path;
            internal string Authorization;
            internal JObject Body;
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<int, HttpResponseMessage> respond;
            internal readonly List<RequestSnapshot> Requests = new List<RequestSnapshot>();
            internal bool Disposed;
            internal FakeHandler(Func<int, HttpResponseMessage> respond) { this.respond = respond; }
            protected override async System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Requests.Add(new RequestSnapshot
                {
                    Path = request.RequestUri.AbsolutePath, Authorization = request.Headers.Authorization?.ToString(),
                    Body = JObject.Parse(await request.Content.ReadAsStringAsync())
                });
                return respond(Requests.Count - 1);
            }
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        }

        private sealed class FragmentedStream : MemoryStream
        {
            internal bool Disposed;
            internal FragmentedStream(byte[] data) : base(data, false) { }
            public override System.Threading.Tasks.Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => base.ReadAsync(buffer, offset, Math.Min(count, 1), token);
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        }

        private sealed class BlockingStream : Stream
        {
            internal readonly SpeechCompletionSource<bool> ReadStarted = new SpeechCompletionSource<bool>();
            private readonly SpeechCompletionSource<int> read = new SpeechCompletionSource<int>();
            internal bool Disposed;
            public override System.Threading.Tasks.Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            { ReadStarted.TrySetResult(true); return read.Task.AsTask(); }
            protected override void Dispose(bool disposing) { Disposed = true; read.TrySetException(new ObjectDisposedException(nameof(BlockingStream))); base.Dispose(disposing); }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}

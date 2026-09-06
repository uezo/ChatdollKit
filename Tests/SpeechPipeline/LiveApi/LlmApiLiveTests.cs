using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.LLM;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline.LiveApi
{
    public class LlmApiLiveTests
    {
        [Test]
        [Explicit("Sends a streaming Chat Completions request using the STT test API key.")]
        [Category("LiveApi")]
        public async NUnitTask ChatCompletionsStreamsText()
        {
            var service = CreateService("chat");
            try
            {
                await CheckAsync(service, new LlmRequest { Text = "Reply with exactly: こんにちは。", ContextId = "live-chat" }, "こんにちは");
            }
            finally { await service.DisposeAsync(); }
        }

        [TestCase("responses")]
        [TestCase("websocket")]
        [Explicit("Makes two generations to verify real Responses continuation and WebSocket reuse.")]
        [Category("LiveApi")]
        public async NUnitTask ResponsesContinueConversation(string transport)
        {
            var service = CreateService(transport);
            try
            {
                var first = await CheckAsync(service, new LlmRequest
                { Text = "Remember the code word HOSHIZORA. Reply with exactly: OK", ContextId = "live-continue" }, "OK");
                var next = await CheckAsync(service, new LlmRequest
                {
                    Text = "What is the code word? Reply with only that word.",
                    ContextId = "live-continue", PreviousResponseId = first.ResponseId
                }, "HOSHIZORA");
                Assert.That(next.ResponseId, Is.Not.EqualTo(first.ResponseId));
                Assert.That(next.RecoveredPreviousResponse, Is.False);
            }
            finally { await service.DisposeAsync(); }
        }

        [TestCase("responses")]
        [TestCase("websocket")]
        [Explicit("Sends an unknown previous_response_id, then one full-history recovery generation.")]
        [Category("LiveApi")]
        public async NUnitTask ResponsesRecoverUnknownResponseId(string transport)
        {
            var service = CreateService(transport);
            try
            {
                var result = await CheckAsync(service, new LlmRequest
                {
                    ContextId = "live-recovery", PreviousResponseId = "resp_000000000000000000000000000000000000000000000000",
                    History = new JArray(
                        new JObject { ["role"] = "user", ["content"] = "The code word is KOMOREBI." },
                        new JObject { ["role"] = "assistant", ["content"] = "I will remember it." }),
                    Text = "What is the code word? Reply with only that word."
                }, "KOMOREBI");
                Assert.That(result.RecoveredPreviousResponse, Is.True, "The API request must actually take the fallback path.");
            }
            finally { await service.DisposeAsync(); }
        }

        [TestCase("chat")]
        [TestCase("responses")]
        [TestCase("websocket")]
        [Explicit("Makes a real function call and a second generation using its locally computed result.")]
        [Category("LiveApi")]
        public async NUnitTask FunctionCallRoundTrip(string transport)
        {
            var service = CreateService(transport);
            var executions = 0;
            var settings = service.GetOptions();
            settings.Tools = new[] { new LlmTool
            {
                Name = "get_code_word", Description = "Returns the code word. Call this function to obtain it.", Strict = true,
                Parameters = new JObject { ["type"] = "object", ["properties"] = new JObject(), ["required"] = new JArray(), ["additionalProperties"] = false },
                ExecuteAsync = (call, request, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    executions++;
                    return UniTask.FromResult(new LlmToolResult { Data = new JObject { ["code_word"] = "AOZORA" } });
                }
            } };
            service.UpdateOptions(settings);
            try
            {
                var result = await CheckAsync(service, new LlmRequest
                { ContextId = "live-tool", Text = "Call get_code_word exactly once, then reply with only the returned code_word." }, "AOZORA");
                Assert.That(executions, Is.EqualTo(1));
                Assert.That(result.ToolCalls.Select(call => call.Name), Is.EqualTo(new[] { "get_code_word" }));
            }
            finally { await service.DisposeAsync(); }
        }

        [TestCase("chat")]
        [TestCase("responses")]
        [TestCase("websocket")]
        [Explicit("Makes two real generations through the automatic single-conversation context manager.")]
        [Category("LiveApi")]
        public async NUnitTask ConversationAutomaticallyRetainsHistory(string transport)
        {
            var conversation = new LlmConversation(CreateService(transport), ownsService: true);
            try
            {
                await CheckAsync(conversation, new LlmRequest
                { ContextId = "live-conversation", Text = "Remember the code word AOZORA. Reply with exactly AOZORA." }, "AOZORA");
                await CheckAsync(conversation, new LlmRequest
                { ContextId = "live-conversation", Text = "What code word did I ask you to remember? Reply with only that code word." }, "AOZORA");
                Assert.That(conversation.Context.GetSnapshot().History.Count, Is.GreaterThanOrEqualTo(4));
                await conversation.ResetAsync();
                Assert.That(conversation.Context.GetSnapshot().History, Is.Empty);
            }
            finally { await conversation.DisposeAsync(); }
        }

        private static ILlmService CreateService(string transport)
        {
            if (string.IsNullOrWhiteSpace(SpeechApiTestSettings.OpenAIApiKey))
                Assert.Ignore("Configure SpeechApiTestSettings.OpenAIApiKey to run the LLM live tests.");
            LlmServiceOptions options = transport == "websocket"
                ? new OpenAIResponsesWebSocketServiceOptions { MaxConnections = 1 }
                : new LlmServiceOptions();
            options.ApiKey = SpeechApiTestSettings.OpenAIApiKey;
            options.Model = Environment.GetEnvironmentVariable("CHATDOLLKIT_LLM_TEST_MODEL") ?? options.Model;
            // gpt-5.6-terra function tools on Chat Completions require reasoning_effort=none.
            options.ReasoningEffort = Environment.GetEnvironmentVariable("CHATDOLLKIT_LLM_TEST_REASONING_EFFORT") ?? (transport == "chat" ? "none" : "low");
            if (options.ReasoningEffort.Length == 0) options.ReasoningEffort = null;
            options.TimeoutSeconds = 60;
            options.MaxOutputTokens = 512;
            options.MaxToolRounds = 3;
            options.SystemPrompt = "Follow the user's short instructions exactly. Do not add commentary.";
            return transport == "chat" ? (ILlmService)new ChatCompletionsClient(options)
                : transport == "responses" ? (ILlmService)new OpenAIResponsesClient(options)
                : new OpenAIResponsesWebSocketClient((OpenAIResponsesWebSocketServiceOptions)options);
        }

        private static async UniTask<LlmResult> CheckAsync(ILlmService service, LlmRequest request, string expected)
        {
            var events = new List<LlmResponse>();
            LlmResult result;
            try { result = await service.ChatAsync(request, response => { events.Add(response); return UniTask.CompletedTask; }); }
            catch (Exception exception)
            {
                // Never include request headers, credentials, or raw server bodies in test output.
                Assert.Fail("LLM live request failed (" + exception.GetType().Name + ").");
                return null;
            }
            if (result.Error != null)
            {
                var message = (result.Error.Message ?? string.Empty).Replace(SpeechApiTestSettings.OpenAIApiKey, "[redacted]");
                Assert.Fail($"LLM live request failed: code={result.Error.Code}, status={result.Error.StatusCode}, parameter={result.Error.Parameter}. {message}");
            }
            Assert.That(result.Text, Does.Contain(expected));
            Assert.That(result.ResponseId, Is.Not.Null.And.Not.Empty);
            Assert.That(events.Count(response => response.IsFinal), Is.EqualTo(1));
            Assert.That(events.Last().IsFinal, Is.True);
            Assert.That(events.Last().ResponseId, Is.EqualTo(result.ResponseId));
            Assert.That(events.All(response => response.ContextId == request.ContextId), Is.True);
            Assert.That(string.Concat(events.Where(response => !response.IsFinal).Select(response => response.Text)), Is.EqualTo(result.Text));
            TestContext.WriteLine(service.GetType().Name + ": " + result.Text + (result.RecoveredPreviousResponse ? " (recovered)" : string.Empty));
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.LLM;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class LlmServiceBaseTests
    {
        private readonly List<FakeService> services = new List<FakeService>();
        private static LlmServiceOptions Options() => new LlmServiceOptions { ApiKey = "test-key" };
        private static LlmRequest Request(string context = "context") => new LlmRequest { ContextId = context, Text = "hello" };
        private static SpeechCompletionSource<bool> Signal() => new SpeechCompletionSource<bool>();
        private static LlmToolCall ToolCall(string name = "lookup") => new LlmToolCall { Id = "call-1", Name = name, Arguments = "{\"query\":\"value\"}" };
        private static JArray FunctionOutput() => new JArray(new JObject
        {
            ["type"] = "function_call", ["call_id"] = "call-1", ["name"] = "lookup", ["arguments"] = "{\"query\":\"value\"}"
        });
        private static Func<LlmResponse, UniTask> Collect(List<LlmResponse> responses) => response => { responses.Add(response); return UniTask.CompletedTask; };

        private FakeService Create(LlmServiceOptions options = null, bool responsesApi = true)
        {
            var service = new FakeService(options ?? Options(), responsesApi);
            services.Add(service);
            return service;
        }

        [TearDown]
        public async NUnitTask DisposeServices()
        {
            foreach (var service in services) await service.DisposeAsync();
            services.Clear();
        }

        private static async UniTask WaitFor(UniTask task, CancellationToken token)
        {
            task = SpeechAsync.Share(task);
            var canceled = Signal();
            using (token.Register(() => canceled.TrySetCanceled()))
            {
                await UniTask.WhenAny(task, canceled.Task);
                token.ThrowIfCancellationRequested();
                await task;
            }
        }

        private static async UniTask WaitForCancellation(CancellationToken token)
        {
            var canceled = Signal();
            using (token.Register(() => canceled.TrySetCanceled())) await canceled.Task;
        }

        [Test]
        public async NUnitTask CompletedRequestsReleaseTheirTrackingEntries()
        {
            var service = Create();
            for (var i = 0; i < 3; i++) await service.ChatAsync(Request());
            SpeechAsyncAssert.NoPendingOperations(service, "active");
        }

        [Test]
        public void OptionsPreserveDerivedTypeAndDeepCopyMutableSettings()
        {
            var options = new DerivedOptions
            {
                ApiKey = "test-key", Marker = "initial", InitialMessages = new JArray(new JObject { ["role"] = "user", ["content"] = "original" }),
                Tools = new[] { new LlmTool { Name = "lookup" } }, ExtraBody = new JObject { ["nested"] = new JObject { ["value"] = "original" } }
            };
            var service = Create(options);
            options.InitialMessages[0]["content"] = "mutated";
            options.Tools[0].Parameters["changed"] = true;
            options.ExtraBody["nested"]["value"] = "mutated";
            var first = (DerivedOptions)service.GetOptions();
            Assert.That(first.Marker, Is.EqualTo("initial"));
            Assert.That((string)first.InitialMessages[0]["content"], Is.EqualTo("original"));
            Assert.That(first.Tools[0].Parameters["changed"], Is.Null);
            Assert.That((string)first.ExtraBody["nested"]["value"], Is.EqualTo("original"));
            first.SplitChars[0] = "changed";
            first.Tools[0].Name = "changed";
            Assert.That(service.GetOptions().SplitChars[0], Is.EqualTo("。"));
            Assert.That(service.GetOptions().Tools[0].Name, Is.EqualTo("lookup"));
            Assert.Throws<ArgumentException>(() => service.UpdateOptions(Options()));
            Assert.That(service.GetOptions(), Is.TypeOf<DerivedOptions>());
            var replacement = new DerivedOptions { ApiKey = "replacement", Marker = "updated" };
            service.UpdateOptions(replacement);
            replacement.Marker = "changed after update";
            Assert.That(((DerivedOptions)service.GetOptions()).Marker, Is.EqualTo("updated"));
        }

        [Test]
        public async NUnitTask InFlightCallSnapshotsRequestOptionsAndHooksBeforeAwaiting()
        {
            var service = Create();
            var entered = Signal();
            var release = Signal();
            service.RequestFilterAsync = async (request, token) => { entered.TrySetResult(true); await WaitFor(release.Task, token); return request; };
            service.SystemPromptFactoryAsync = (request, token) => UniTask.FromResult("old prompt " + request.Text);
            service.InitialMessagesFactoryAsync = (request, token) => UniTask.FromResult(new JArray(new JObject { ["role"] = "assistant", ["content"] = "old initial" }));
            var request = Request();
            request.History.Add(new JObject { ["role"] = "user", ["content"] = "old history" });
            request.ImageUrls = new[] { "https://example.com/original.png" };
            request.Parameters = new JObject { ["nested"] = new JObject { ["value"] = "old" } };
            var active = service.ChatAsync(request);
            await entered.Task;
            request.Text = "mutated";
            request.History[0]["content"] = "mutated";
            request.ImageUrls[0] = "https://example.com/mutated.png";
            request.Parameters["nested"]["value"] = "mutated";
            var replacement = Options();
            replacement.Model = "new-model";
            service.UpdateOptions(replacement);
            service.RequestFilterAsync = (next, token) => { next.Text = "new filter"; return UniTask.FromResult(next); };
            service.SystemPromptFactoryAsync = (next, token) => UniTask.FromResult("new prompt");
            service.InitialMessagesFactoryAsync = (next, token) => UniTask.FromResult(new JArray());
            release.TrySetResult(true);
            await active;
            var original = service.Calls[0];
            Assert.That(original.Request.Text, Is.EqualTo("hello"));
            Assert.That((string)original.Request.History[0]["content"], Is.EqualTo("old history"));
            Assert.That(original.Request.ImageUrls[0], Is.EqualTo("https://example.com/original.png"));
            Assert.That((string)original.Request.Parameters["nested"]["value"], Is.EqualTo("old"));
            Assert.That(original.Options.Model, Is.Not.EqualTo("new-model"));
            Assert.That(original.Options.SystemPrompt, Is.EqualTo("old prompt hello"));
            Assert.That((string)original.Options.InitialMessages[0]["content"], Is.EqualTo("old initial"));
            await service.ChatAsync(Request("next"));
            Assert.That(service.Calls[1].Request.Text, Is.EqualTo("new filter"));
            Assert.That(service.Calls[1].Options.Model, Is.EqualTo("new-model"));
            Assert.That(service.Calls[1].Options.SystemPrompt, Is.EqualTo("new prompt"));
        }

        [Test]
        public async NUnitTask ReplacementRequestIsCopiedAndKeepsOriginalCorrelation()
        {
            var service = Create();
            var replacement = new LlmRequest { ContextId = "replacement-context", Text = "replacement", Parameters = new JObject { ["value"] = "old" } };
            var entered = Signal();
            var release = Signal();
            service.RequestFilterAsync = (request, token) => UniTask.FromResult(replacement);
            service.SystemPromptFactoryAsync = async (request, token) => { entered.TrySetResult(true); await WaitFor(release.Task, token); return "prompt"; };
            var responses = new List<LlmResponse>();
            var active = service.ChatAsync(Request("original-context"), Collect(responses));
            await entered.Task;
            replacement.Text = "mutated";
            replacement.Parameters["value"] = "mutated";
            release.TrySetResult(true);
            var result = await active;
            Assert.That(service.Calls[0].Request.ContextId, Is.EqualTo("original-context"));
            Assert.That(service.Calls[0].Request.Text, Is.EqualTo("replacement"));
            Assert.That((string)service.Calls[0].Request.Parameters["value"], Is.EqualTo("old"));
            Assert.That(result.ContextId, Is.EqualTo("original-context"));
            Assert.That(responses.All(response => response.ContextId == "original-context"), Is.True);
        }

        [Test]
        public async NUnitTask NullFilterAndEmptyInputSkipProviderAndCallbacks()
        {
            var service = Create();
            int callbacks = 0;
            service.RequestFilterAsync = (request, token) => UniTask.FromResult<LlmRequest>(null);
            var rejected = await service.ChatAsync(Request(), response => { callbacks++; return UniTask.CompletedTask; });
            Assert.That(rejected.Text, Is.Empty);
            service.RequestFilterAsync = null;
            service.SystemPromptFactoryAsync = (request, token) => throw new InvalidOperationException("An empty request must skip prompt hooks.");
            var empty = await service.ChatAsync(new LlmRequest { ContextId = "empty" });
            Assert.That(empty.Text, Is.Empty);
            Assert.That(service.Calls, Is.Empty);
            Assert.That(callbacks, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async NUnitTask ImagesOrNativeInputAllowRequestsWithoutText(bool nativeInput)
        {
            var service = Create();
            var request = new LlmRequest { ContextId = "context" };
            if (nativeInput) request.Input = new JArray(new JObject { ["role"] = "user", ["content"] = "native" });
            else request.ImageUrls = new[] { "https://example.com/image.png" };
            await service.ChatAsync(request);
            Assert.That(service.Calls.Count, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask ConcurrentCallsHaveIndependentBuffersAndCorrelation()
        {
            var service = Create();
            var enteredA = Signal();
            var enteredB = Signal();
            var finishA = Signal();
            var finishB = Signal();
            service.Generate = async (request, options, emit, token) =>
            {
                await emit(new LlmResponse { ContextId = "provider-context", Text = request.ContextId + " partial" });
                (request.ContextId == "a" ? enteredA : enteredB).TrySetResult(true);
                await WaitFor((request.ContextId == "a" ? finishA : finishB).Task, token);
                await emit(new LlmResponse { Text = " done。" });
                return new LlmProviderResult { ResponseId = "response-" + request.ContextId };
            };
            var chunksA = new List<LlmResponse>();
            var chunksB = new List<LlmResponse>();
            var a = service.ChatAsync(Request("a"), Collect(chunksA));
            var b = service.ChatAsync(Request("b"), Collect(chunksB));
            await UniTask.WhenAll(enteredA.Task, enteredB.Task);
            finishB.TrySetResult(true);
            Assert.That((await b).Text, Is.EqualTo("b partial done。"));
            Assert.That(a.Status.IsCompleted(), Is.False);
            finishA.TrySetResult(true);
            Assert.That((await a).Text, Is.EqualTo("a partial done。"));
            Assert.That(chunksA.All(chunk => chunk.ContextId == "a"), Is.True);
            Assert.That(chunksB.All(chunk => chunk.ContextId == "b"), Is.True);
            Assert.That(chunksA.Single(chunk => chunk.IsFinal).ResponseId, Is.EqualTo("response-a"));
            Assert.That(chunksB.Single(chunk => chunk.IsFinal).ResponseId, Is.EqualTo("response-b"));
        }

        [Test]
        public async NUnitTask CancellationReachesRequestFilterAndSkipsProvider()
        {
            var service = Create();
            var entered = Signal();
            service.RequestFilterAsync = async (request, token) => { entered.TrySetResult(true); await WaitForCancellation(token); return request; };
            using (var cancellation = new CancellationTokenSource())
            {
                var active = service.ChatAsync(Request(), cancellationToken: cancellation.Token);
                await entered.Task;
                cancellation.Cancel();
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await active);
                Assert.That(service.Calls, Is.Empty);
            }
        }

        [Test]
        public async NUnitTask CancellationAfterPromptHookSkipsSubsequentInitialMessageHook()
        {
            var service = Create();
            using (var cancellation = new CancellationTokenSource())
            {
                int initialCalls = 0;
                service.SystemPromptFactoryAsync = (request, token) => { cancellation.Cancel(); return UniTask.FromResult("prompt"); };
                service.InitialMessagesFactoryAsync = (request, token) => { initialCalls++; return UniTask.FromResult(new JArray()); };
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await service.ChatAsync(Request(), cancellationToken: cancellation.Token));
                Assert.That(initialCalls, Is.Zero);
                Assert.That(service.Calls, Is.Empty);
            }
            await UniTask.CompletedTask;
        }

        [Test]
        public async NUnitTask CancellationWaitsForInFlightCallbackAndDoesNotEmitFinal()
        {
            var service = Create();
            var entered = Signal();
            var release = Signal();
            int providerCleanup = 0;
            service.Generate = async (request, options, emit, token) =>
            {
                try { await emit(new LlmResponse { Text = "first。" }); return new LlmProviderResult(); }
                finally { providerCleanup++; }
            };
            var responses = new List<LlmResponse>();
            using (var cancellation = new CancellationTokenSource())
            {
                var active = service.ChatAsync(Request(), async response => { responses.Add(response); entered.TrySetResult(true); await release.Task; }, cancellation.Token);
                try
                {
                    await entered.Task;
                    cancellation.Cancel();
                    Assert.That(active.Status.IsCompleted(), Is.False, "The caller owns the callback, so it must return before the call is drained.");
                }
                finally { release.TrySetResult(true); }
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await active);
                Assert.That(providerCleanup, Is.EqualTo(1));
                Assert.That(responses.Any(response => response.IsFinal), Is.False);
            }
        }

        [Test]
        public async NUnitTask CancellationReachesToolAndPreventsFollowupRound()
        {
            var service = Create();
            var entered = Signal();
            service.Generate = (request, options, emit, token) => UniTask.FromResult(new LlmProviderResult { ToolCalls = new List<LlmToolCall> { ToolCall() } });
            service.ToolExecutorAsync = async (tool, request, token) => { entered.TrySetResult(true); await WaitForCancellation(token); return new LlmToolResult(); };
            var chunks = new List<LlmResponse>();
            using (var cancellation = new CancellationTokenSource())
            {
                var active = service.ChatAsync(Request(), Collect(chunks), cancellation.Token);
                await entered.Task;
                cancellation.Cancel();
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await active);
                Assert.That(service.Calls.Count, Is.EqualTo(1));
                Assert.That(chunks.Any(chunk => chunk.IsFinal), Is.False);
            }
        }

        [Test]
        public async NUnitTask DisposeCancelsAllCallsAndWaitsForProviderCleanupBeforeResources()
        {
            var service = Create();
            var bothEntered = Signal();
            var bothCanceled = Signal();
            var releaseCleanup = Signal();
            int entered = 0;
            int canceled = 0;
            int cleaned = 0;
            service.Generate = async (request, options, emit, token) =>
            {
                if (Interlocked.Increment(ref entered) == 2) bothEntered.TrySetResult(true);
                try { await WaitForCancellation(token); return new LlmProviderResult(); }
                finally
                {
                    if (Interlocked.Increment(ref canceled) == 2) bothCanceled.TrySetResult(true);
                    await releaseCleanup.Task;
                    Interlocked.Increment(ref cleaned);
                }
            };
            service.DisposeAction = () => { Assert.That(cleaned, Is.EqualTo(2)); return UniTask.CompletedTask; };
            var first = service.ChatAsync(Request("first"));
            var second = service.ChatAsync(Request("second"));
            await bothEntered.Task;
            UniTask disposal = default;
            try
            {
                disposal = service.DisposeAsync();
                Assert.That(service.DisposeAsync(), Is.EqualTo(disposal));
                await bothCanceled.Task;
                Assert.That(disposal.Status.IsCompleted(), Is.False);
                Assert.That(service.DisposedResources, Is.Zero);
                Assert.Throws<ObjectDisposedException>(() => service.ChatAsync(Request()));
                Assert.Throws<ObjectDisposedException>(() => service.UpdateOptions(Options()));
                Assert.Throws<ObjectDisposedException>(() => service.RequestFilterAsync = null);
            }
            finally { releaseCleanup.TrySetResult(true); }
            await disposal;
            await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await first);
            await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await second);
            Assert.That(service.DisposedResources, Is.EqualTo(1));
        }

        [TestCase("hook")]
        [TestCase("callback")]
        [TestCase("tool")]
        public async NUnitTask ReentrantChatAndDisposeAreRejectedWithoutDeadlock(string location)
        {
            var service = Create();
            int checkedCount = 0;
            Action check = () =>
            {
                Assert.Throws<InvalidOperationException>(() => service.ChatAsync(Request("nested")));
                Assert.Throws<InvalidOperationException>(() => service.DisposeAsync());
                checkedCount++;
            };
            if (location == "hook") service.RequestFilterAsync = (request, token) => { check(); return UniTask.FromResult(request); };
            if (location == "tool")
            {
                service.Generate = (request, options, emit, token) => UniTask.FromResult(new LlmProviderResult { ToolCalls = new List<LlmToolCall> { ToolCall() } });
                service.ToolExecutorAsync = (tool, request, token) => { check(); return UniTask.FromResult(new LlmToolResult { ContinueChain = false }); };
            }
            await service.ChatAsync(Request(), response => { if (location == "callback" && !response.IsFinal) check(); return UniTask.CompletedTask; });
            Assert.That(checkedCount, Is.EqualTo(1));
            await service.DisposeAsync();
        }

        [Test]
        public async NUnitTask RequestGuardrailBlockEmitsCleanSpeechAndFinalWithoutProvider()
        {
            var options = Options();
            options.Guardrails = new ILlmGuardrail[] { new FakeGuardrail(LlmGuardrailScope.Request,
                (request, text, token) => UniTask.FromResult(new LlmGuardrailResult { IsTriggered = true, Name = "block", Action = LlmGuardrailAction.Block, Text = "[face:sad]停止。" })) };
            var service = Create(options);
            var responses = new List<LlmResponse>();
            var result = await service.ChatAsync(Request(), Collect(responses));
            Assert.That(result.Text, Is.EqualTo("[face:sad]停止。"));
            Assert.That(service.Calls, Is.Empty);
            Assert.That(responses.Count, Is.EqualTo(2));
            Assert.That(responses[0].GuardrailName, Is.EqualTo("block"));
            Assert.That(responses[0].VoiceText, Is.EqualTo("停止。"));
            Assert.That(responses[1].IsFinal, Is.True);
        }

        [Test]
        public async NUnitTask RequestGuardrailReplacementChangesTextAndNativeInputWithoutMutatingCaller()
        {
            var options = Options();
            options.Guardrails = new ILlmGuardrail[] { new FakeGuardrail(LlmGuardrailScope.Request,
                (request, text, token) => UniTask.FromResult(new LlmGuardrailResult { IsTriggered = true, Name = "replace", Text = "safe input" })) };
            var service = Create(options);
            var request = Request();
            request.Input = new JArray(new JObject { ["role"] = "user", ["content"] = "original native" });
            await service.ChatAsync(request);
            Assert.That(service.Calls[0].Request.Text, Is.EqualTo("safe input"));
            Assert.That((string)service.Calls[0].Request.Input[0]["content"], Is.EqualTo("safe input"));
            Assert.That(request.Text, Is.EqualTo("hello"));
            Assert.That((string)request.Input[0]["content"], Is.EqualTo("original native"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async NUnitTask RequestGuardrailReplacementPreservesImagesInNativeMultimodalInput(bool responsesApi)
        {
            var options = Options();
            options.Guardrails = new ILlmGuardrail[] { new FakeGuardrail(LlmGuardrailScope.Request,
                (request, text, token) => UniTask.FromResult(new LlmGuardrailResult { IsTriggered = true, Text = "safe input" })) };
            var service = Create(options, responsesApi);
            var request = Request();
            var image = responsesApi
                ? new JObject { ["type"] = "input_image", ["image_url"] = "https://example.com/image.png" }
                : new JObject { ["type"] = "image_url", ["image_url"] = new JObject { ["url"] = "https://example.com/image.png" } };
            var content = new JArray(image, new JObject { ["type"] = responsesApi ? "input_text" : "text", ["text"] = "original" });
            request.Input = new JArray(new JObject { ["role"] = "user", ["content"] = content });
            await service.ChatAsync(request);
            var actual = (JArray)service.Calls[0].Request.Input[0]["content"];
            Assert.That(actual.Count, Is.EqualTo(2));
            Assert.That(JToken.DeepEquals(actual[0], image), Is.True);
            Assert.That((string)actual[1]["type"], Is.EqualTo(responsesApi ? "input_text" : "text"));
            Assert.That((string)actual[1]["text"], Is.EqualTo("safe input"));
            Assert.That((string)content[1]["text"], Is.EqualTo("original"));
        }

        [Test]
        public async NUnitTask ResponseGuardrailEmitsCorrectionAfterOriginalStream()
        {
            var options = Options();
            string inspected = null;
            options.Guardrails = new ILlmGuardrail[] { new FakeGuardrail(LlmGuardrailScope.Response, (request, text, token) =>
            {
                inspected = text;
                return UniTask.FromResult(new LlmGuardrailResult { IsTriggered = true, Name = "correction", Text = "[face:calm]訂正。" });
            }) };
            var service = Create(options);
            var responses = new List<LlmResponse>();
            var result = await service.ChatAsync(Request(), Collect(responses));
            Assert.That(inspected, Is.EqualTo("generated。"));
            Assert.That(result.Text, Is.EqualTo("[face:calm]訂正。"));
            CollectionAssert.AreEqual(new[] { "generated。", "[face:calm]訂正。", null }, responses.Select(response => response.Text));
            Assert.That(responses[1].VoiceText, Is.EqualTo("訂正。"));
            Assert.That(responses[1].GuardrailName, Is.EqualTo("correction"));
            Assert.That(responses[2].IsFinal, Is.True);
        }

        [Test]
        public async NUnitTask FirstTriggeredGuardrailCancelsAndDrainsRemainingGuardrails()
        {
            var winnerEntered = Signal();
            var loserEntered = Signal();
            var triggerWinner = Signal();
            var loserCanceled = Signal();
            var releaseLoser = Signal();
            var options = Options();
            options.Guardrails = new ILlmGuardrail[]
            {
                new FakeGuardrail(LlmGuardrailScope.Request, (request, text, token) => UniTask.FromResult(new LlmGuardrailResult())),
                new FakeGuardrail(LlmGuardrailScope.Request, async (request, text, token) =>
                {
                    winnerEntered.TrySetResult(true);
                    await WaitFor(triggerWinner.Task, token);
                    return new LlmGuardrailResult { IsTriggered = true, Name = "winner", Action = LlmGuardrailAction.Block, Text = "stop" };
                }),
                new FakeGuardrail(LlmGuardrailScope.Request, async (request, text, token) =>
                {
                    loserEntered.TrySetResult(true);
                    try { await WaitForCancellation(token); return new LlmGuardrailResult(); }
                    finally { loserCanceled.TrySetResult(true); await releaseLoser.Task; }
                })
            };
            var service = Create(options);
            var active = service.ChatAsync(Request());
            try
            {
                await UniTask.WhenAll(winnerEntered.Task, loserEntered.Task);
                triggerWinner.TrySetResult(true);
                await loserCanceled.Task;
                Assert.That(active.Status.IsCompleted(), Is.False, "A selected decision must still drain the other guards.");
                Assert.That(service.Calls, Is.Empty);
            }
            finally { releaseLoser.TrySetResult(true); }
            Assert.That((await active).Text, Is.EqualTo("stop"));
        }

        [Test]
        public async NUnitTask ProviderErrorEmitsErrorFinalWithoutFlushingPendingSpeech()
        {
            var service = Create();
            var error = new LlmError { Code = "provider_error", Message = "failed" };
            service.Generate = async (request, options, emit, token) =>
            {
                await emit(new LlmResponse { Text = "already spoken。pending" });
                await emit(new LlmResponse { Error = error });
                throw new InvalidOperationException("The provider error callback must interrupt generation.");
            };
            var responses = new List<LlmResponse>();
            var result = await service.ChatAsync(Request(), Collect(responses));
            Assert.That(result.Error, Is.SameAs(error));
            Assert.That(responses.Count, Is.EqualTo(2));
            Assert.That(responses[0].Text, Is.EqualTo("already spoken。"));
            Assert.That(responses[1].IsFinal, Is.True);
            Assert.That(responses[1].Error, Is.SameAs(error));
            Assert.That(responses.Any(response => response.Text == "pending"), Is.False);
        }

        [Test]
        public async NUnitTask CallbackServiceExceptionPropagatesUnchangedInsteadOfBecomingProviderError()
        {
            var service = Create();
            var error = new LlmServiceException(new LlmError { Code = "application_callback", Message = "application failure" });
            int callbacks = 0;
            var actual = await SpeechAsyncAssert.ThrowsAsync<LlmServiceException>(async () => await service.ChatAsync(Request(), response => { callbacks++; throw error; }));
            Assert.That(actual, Is.SameAs(error));
            Assert.That(callbacks, Is.EqualTo(1));
        }

        [TestCase(LlmGuardrailScope.Request)]
        [TestCase(LlmGuardrailScope.Response)]
        public async NUnitTask GuardrailServiceExceptionPropagatesUnchangedInsteadOfBecomingProviderError(LlmGuardrailScope scope)
        {
            var options = Options();
            var error = new LlmServiceException(new LlmError { Code = "application_guardrail", Message = "guardrail failure" });
            options.Guardrails = new ILlmGuardrail[] { new FakeGuardrail(scope, (request, text, token) => throw error) };
            var service = Create(options);
            var chunks = new List<LlmResponse>();
            var actual = await SpeechAsyncAssert.ThrowsAsync<LlmServiceException>(async () => await service.ChatAsync(Request(), Collect(chunks)));
            Assert.That(actual, Is.SameAs(error));
            Assert.That(chunks.Any(chunk => chunk.IsFinal), Is.False);
        }

        [Test]
        public async NUnitTask RequestParameterEditorServiceExceptionPropagatesUnchangedInsteadOfBecomingProviderError()
        {
            var options = Options();
            var error = new LlmServiceException(new LlmError { Code = "application_editor", Message = "editor failure" });
            options.EditRequestParameters = (body, request) => throw error;
            var service = Create(options);
            service.Generate = (request, settings, emit, token) =>
            {
                settings.EditRequestParameters(new JObject(), request);
                return UniTask.FromResult(new LlmProviderResult());
            };
            var chunks = new List<LlmResponse>();
            var actual = await SpeechAsyncAssert.ThrowsAsync<LlmServiceException>(async () => await service.ChatAsync(Request(), Collect(chunks)));
            Assert.That(actual, Is.SameAs(error));
            Assert.That(chunks, Is.Empty);
        }

        [Test]
        public async NUnitTask MissingProviderTerminalResultProducesExplicitFailureFinal()
        {
            var service = Create();
            service.Generate = (request, options, emit, token) => UniTask.FromResult<LlmProviderResult>(null);
            var responses = new List<LlmResponse>();
            var result = await service.ChatAsync(Request(), Collect(responses));
            Assert.That(result.Error.Code, Is.EqualTo("missing_result"));
            Assert.That(responses.Single().IsFinal, Is.True);
            Assert.That(responses[0].Error.Code, Is.EqualTo("missing_result"));
        }

        [Test]
        public async NUnitTask ResponsesToolRoundFlushesTextThenNotifiesExecutesAndCarriesOutput()
        {
            var options = Options();
            int executions = 0;
            options.Tools = new[] { new LlmTool
            {
                Name = "lookup", Description = "description", Strict = true,
                ExecuteAsync = (tool, request, token) =>
                {
                    executions++;
                    Assert.That(tool.Arguments, Is.EqualTo("{\"query\":\"value\"}"));
                    tool.Id = "tool mutation";
                    request.Text = "tool mutation";
                    return UniTask.FromResult(new LlmToolResult { Data = new JObject { ["answer"] = 42 } });
                }
            } };
            var service = Create(options);
            int round = 0;
            service.Generate = async (request, settings, emit, token) =>
            {
                if (round++ == 0)
                {
                    await emit(new LlmResponse { Text = "Checking" });
                    return new LlmProviderResult { ResponseId = "response-1", OutputItems = FunctionOutput(), ToolCalls = new List<LlmToolCall> { ToolCall() } };
                }
                await emit(new LlmResponse { Text = "done。" });
                return new LlmProviderResult { ResponseId = "response-2", Usage = new JObject { ["total_tokens"] = 9 } };
            };
            var chunks = new List<LlmResponse>();
            var caller = Request();
            caller.History.Add(new JObject { ["role"] = "user", ["content"] = "prior" });
            caller.Parameters = new JObject { ["tool_choice"] = "required" };
            var result = await service.ChatAsync(caller, chunk =>
            {
                chunks.Add(chunk);
                if (chunk.ToolCall != null) { chunk.ToolCall.Id = "callback mutation"; chunk.ToolCall.Arguments = "callback mutation"; }
                return UniTask.CompletedTask;
            });
            Assert.That(executions, Is.EqualTo(1));
            Assert.That(service.Calls.Count, Is.EqualTo(2));
            Assert.That(chunks[0].Text, Is.EqualTo("Checking"));
            Assert.That(chunks[1].ToolCall.Name, Is.EqualTo("lookup"));
            Assert.That(chunks.Count(chunk => chunk.ToolCall != null), Is.EqualTo(1));
            var followup = service.Calls[1];
            Assert.That(followup.Request.PreviousResponseId, Is.EqualTo("response-1"));
            Assert.That(followup.Request.Input.Count, Is.EqualTo(1));
            Assert.That((string)followup.Request.Input[0]["type"], Is.EqualTo("function_call_output"));
            Assert.That((string)followup.Request.Input[0]["call_id"], Is.EqualTo("call-1"));
            Assert.That((string)followup.Request.Input[0]["output"], Is.EqualTo("{\"answer\":42}"));
            Assert.That(followup.Request.Text, Is.EqualTo("hello"));
            Assert.That(followup.Request.Parameters, Is.Null, "Inline parameters apply only to the first generation.");
            Assert.That((string)service.Calls[0].Request.Parameters["tool_choice"], Is.EqualTo("required"));
            Assert.That((string)followup.Request.History[0]["content"], Is.EqualTo("prior"));
            Assert.That((string)followup.Options.ToolDefinitions[0]["type"], Is.EqualTo("function"));
            Assert.That((bool)followup.Options.ToolDefinitions[0]["strict"], Is.True);
            Assert.That(service.GetOptions().ToolDefinitions, Is.Null);
            Assert.That(caller.Input, Is.Null);
            Assert.That(result.Text, Is.EqualTo("Checkingdone。"));
            Assert.That(result.ResponseId, Is.EqualTo("response-2"));
            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(result.ToolCalls[0].Id, Is.EqualTo("call-1"));
            Assert.That(result.OutputItems.Count, Is.EqualTo(2));
            Assert.That((int)result.Usage["total_tokens"], Is.EqualTo(9));
        }

        [Test]
        public async NUnitTask ChatCompletionsToolFollowupContainsCurrentAssistantAndToolMessages()
        {
            var options = Options();
            options.Tools = new[] { new LlmTool { Name = "lookup", ExecuteAsync = (tool, request, token) => UniTask.FromResult(new LlmToolResult { Data = "tool value" }) } };
            var service = Create(options, false);
            int round = 0;
            service.Generate = (request, settings, emit, token) => UniTask.FromResult(round++ == 0
                ? new LlmProviderResult { OutputItems = new JArray(new JObject { ["role"] = "assistant", ["tool_calls"] = new JArray(new JObject { ["id"] = "call-1", ["type"] = "function" }) }), ToolCalls = new List<LlmToolCall> { ToolCall() } }
                : new LlmProviderResult());
            await service.ChatAsync(Request());
            var followup = service.Calls[1];
            CollectionAssert.AreEqual(new[] { "user", "assistant", "tool" }, followup.Request.Input.Select(item => (string)item["role"]));
            Assert.That((string)followup.Request.Input[2]["tool_call_id"], Is.EqualTo("call-1"));
            Assert.That((string)followup.Request.Input[2]["content"], Is.EqualTo("\"tool value\""));
            Assert.That((string)followup.Options.ToolDefinitions[0]["function"]["name"], Is.EqualTo("lookup"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async NUnitTask ResponsesWithoutStoredIdCarryCompleteCurrentToolChain(bool missingResponseId)
        {
            var options = Options();
            if (!missingResponseId) options.ExtraBody = new JObject { ["store"] = false };
            var service = Create(options);
            service.ToolExecutorAsync = (tool, request, token) => UniTask.FromResult(new LlmToolResult { Data = true });
            int round = 0;
            service.Generate = (request, settings, emit, token) => UniTask.FromResult(round++ == 0
                ? new LlmProviderResult { ResponseId = missingResponseId ? null : "unstored-id", CanUsePreviousResponse = missingResponseId, OutputItems = FunctionOutput(), ToolCalls = new List<LlmToolCall> { ToolCall() } }
                : new LlmProviderResult());
            await service.ChatAsync(Request());
            var followup = service.Calls[1].Request;
            Assert.That(followup.PreviousResponseId, Is.Null);
            Assert.That(followup.Input.Count, Is.EqualTo(3));
            Assert.That((string)followup.Input[0]["role"], Is.EqualTo("user"));
            Assert.That((string)followup.Input[0]["content"], Is.EqualTo("hello"));
            Assert.That((string)followup.Input[1]["type"], Is.EqualTo("function_call"));
            Assert.That((string)followup.Input[2]["type"], Is.EqualTo("function_call_output"));
        }

        [Test]
        public async NUnitTask ToolCanStopChainAndEmitImmediateTextAndStructuredContent()
        {
            var service = Create();
            service.Generate = (request, options, emit, token) => UniTask.FromResult(new LlmProviderResult { ToolCalls = new List<LlmToolCall> { ToolCall() } });
            service.ToolExecutorAsync = (tool, request, token) => UniTask.FromResult(new LlmToolResult
            {
                ContinueChain = false, Text = "local answer。", Data = new JObject { ["ok"] = true }, StructuredContent = new JObject { ["artifact"] = "result" }
            });
            var chunks = new List<LlmResponse>();
            var result = await service.ChatAsync(Request(), Collect(chunks));
            Assert.That(service.Calls.Count, Is.EqualTo(1));
            Assert.That(result.Text, Is.EqualTo("local answer。"));
            Assert.That((string)chunks.Single(chunk => chunk.StructuredContent != null).StructuredContent["artifact"], Is.EqualTo("result"));
            Assert.That((string)result.OutputItems.Single()["type"], Is.EqualTo("function_call_output"));
            Assert.That(chunks.Last().IsFinal, Is.True);
        }

        [Test]
        public async NUnitTask UnknownToolIsExposedToCallerWithoutExecutionOrAutomaticContinuation()
        {
            var service = Create();
            service.Generate = (request, options, emit, token) => UniTask.FromResult(new LlmProviderResult { ToolCalls = new List<LlmToolCall> { ToolCall("unknown") } });
            var chunks = new List<LlmResponse>();
            var result = await service.ChatAsync(Request(), Collect(chunks));
            Assert.That(service.Calls.Count, Is.EqualTo(1));
            Assert.That(result.Error, Is.Null);
            Assert.That(result.ToolCalls.Single().Name, Is.EqualTo("unknown"));
            Assert.That(chunks.Single(chunk => chunk.ToolCall != null).ToolCall.Name, Is.EqualTo("unknown"));
            Assert.That(chunks.Last().IsFinal, Is.True);
        }

        [Test]
        public async NUnitTask RoundLimitAllowsFinalGenerationButPreventsAnotherToolExecution()
        {
            var options = Options();
            options.MaxToolRounds = 1;
            var service = Create(options);
            int executions = 0;
            service.ToolExecutorAsync = (tool, request, token) => { executions++; return UniTask.FromResult(new LlmToolResult()); };
            service.Generate = (request, settings, emit, token) => UniTask.FromResult(new LlmProviderResult { ToolCalls = new List<LlmToolCall> { ToolCall() } });
            var chunks = new List<LlmResponse>();
            var result = await service.ChatAsync(Request(), Collect(chunks));
            Assert.That(result.Error.Code, Is.EqualTo("tool_round_limit"));
            Assert.That(executions, Is.EqualTo(1));
            Assert.That(service.Calls.Count, Is.EqualTo(2));
            Assert.That(result.ToolCalls.Count, Is.EqualTo(2));
            Assert.That(chunks.Last().Error.Code, Is.EqualTo("tool_round_limit"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask ApplicationToolFailurePropagatesWithoutFinalProviderError(bool providerExceptionType)
        {
            var service = Create();
            Exception error = providerExceptionType
                ? new LlmServiceException(new LlmError { Code = "application_tool", Message = "application tool failed" })
                : (Exception)new InvalidOperationException("application tool failed");
            service.Generate = (request, options, emit, token) => UniTask.FromResult(new LlmProviderResult { ToolCalls = new List<LlmToolCall> { ToolCall() } });
            service.ToolExecutorAsync = (tool, request, token) => throw error;
            var chunks = new List<LlmResponse>();
            Assert.That(await SpeechAsyncAssert.ThrowsInstanceOfAsync<Exception>(async () => await service.ChatAsync(Request(), Collect(chunks))), Is.SameAs(error));
            Assert.That(chunks.Any(chunk => chunk.IsFinal), Is.False);
        }

        [Test]
        public async NUnitTask TerminalVoiceTagSuppressesSuffixWhileProviderStillReachesTerminalResult()
        {
            var options = Options();
            options.VoiceTextTags = new[] { "answer" };
            options.TerminalVoiceTextTag = "answer";
            var service = Create(options);
            int emitted = 0;
            service.Generate = async (request, settings, emit, token) =>
            {
                await emit(new LlmResponse { Text = "<answer>yes</answer>discarded" });
                emitted++;
                await emit(new LlmResponse { Text = "more discarded" });
                emitted++;
                return new LlmProviderResult { ResponseId = "completed", RecoveredPreviousResponse = true };
            };
            var chunks = new List<LlmResponse>();
            var result = await service.ChatAsync(Request(), Collect(chunks));
            Assert.That(emitted, Is.EqualTo(2));
            Assert.That(result.Text, Is.EqualTo("<answer>yes</answer>"));
            Assert.That(result.ResponseId, Is.EqualTo("completed"));
            Assert.That(result.RecoveredPreviousResponse, Is.True);
            Assert.That(string.Concat(chunks.Select(chunk => chunk.VoiceText)), Is.EqualTo("yes"));
            Assert.That(chunks.Last().IsRecovery, Is.True);
            Assert.That(chunks.Last().IsFinal, Is.True);
        }

        private sealed class DerivedOptions : LlmServiceOptions { public string Marker { get; set; } }
        private sealed class CapturedCall
        {
            public LlmRequest Request;
            public LlmServiceOptions Options;
        }

        private sealed class FakeService : LlmServiceBase
        {
            private readonly bool responsesApi;
            public readonly List<CapturedCall> Calls = new List<CapturedCall>();
            public Func<LlmRequest, LlmServiceOptions, Func<LlmResponse, UniTask>, CancellationToken, UniTask<LlmProviderResult>> Generate;
            public Func<UniTask> DisposeAction;
            public int DisposedResources;
            protected override bool UsesResponsesApi => responsesApi;
            public FakeService(LlmServiceOptions options, bool responsesApi) : base(options)
            {
                this.responsesApi = responsesApi;
                Generate = async (request, settings, emit, token) => { await emit(new LlmResponse { Text = "generated。" }); return new LlmProviderResult(); };
            }
            protected override UniTask<LlmProviderResult> GenerateCoreAsync(LlmRequest request, LlmServiceOptions options, Func<LlmResponse, UniTask> emit, CancellationToken token)
            {
                lock (Calls) Calls.Add(new CapturedCall { Request = request.Copy(), Options = options.Copy() });
                return Generate(request, options, emit, token);
            }
            protected override async UniTask DisposeResourcesAsync()
            {
                if (DisposeAction != null) await DisposeAction();
                DisposedResources++;
            }
        }

        private sealed class FakeGuardrail : ILlmGuardrail
        {
            private readonly Func<LlmRequest, string, CancellationToken, UniTask<LlmGuardrailResult>> apply;
            public LlmGuardrailScope Scope { get; }
            public FakeGuardrail(LlmGuardrailScope scope, Func<LlmRequest, string, CancellationToken, UniTask<LlmGuardrailResult>> apply)
            { Scope = scope; this.apply = apply; }
            public UniTask<LlmGuardrailResult> ApplyAsync(LlmRequest request, string text, CancellationToken token) => apply(request, text, token);
        }
    }
}

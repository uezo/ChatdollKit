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
    public class LlmConversationTests
    {
        private readonly List<Rig> rigs = new List<Rig>();
        private static SpeechCompletionSource<bool> Signal() => new SpeechCompletionSource<bool>();
        private static LlmRequest Request(string text = "hello", string contextId = "context") => new LlmRequest
        { ContextId = contextId, SessionId = "input-session", UserId = "user", Channel = "voice", Text = text };

        private Rig Create(LlmHistoryFormat format = LlmHistoryFormat.Responses, bool ownsService = false, LlmServiceOptions options = null)
        {
            var service = new FakeService(format, options ?? new LlmServiceOptions { ApiKey = "test-key" });
            var rig = new Rig { Service = service, Conversation = new LlmConversation(service, format, ownsService) };
            rigs.Add(rig);
            return rig;
        }

        [TearDown]
        public async NUnitTask Cleanup()
        {
            foreach (var rig in rigs) { await rig.Conversation.DisposeAsync(); await rig.Service.DisposeAsync(); }
            rigs.Clear();
        }

        private static async UniTask WaitForCancellation(CancellationToken token)
        {
            var canceled = Signal();
            using (token.Register(() => canceled.TrySetCanceled())) await canceled.Task;
        }

        private static async UniTask CompleteWithin(UniTask task)
        {
            task = SpeechAsync.Share(task);
            Assert.That(await UniTask.WhenAny(task, SpeechAsync.Delay(TimeSpan.FromMilliseconds(3000))), Is.EqualTo(0), "The operation did not settle.");
            await task;
        }

        [Test]
        public async NUnitTask CompletedConversationOperationsReleaseTheirTrackingEntries()
        {
            var rig = Create();
            for (var i = 0; i < 3; i++) await rig.Conversation.ChatAsync(Request());
            await rig.Conversation.ResetAsync();
            SpeechAsyncAssert.NoPendingOperations(rig.Conversation, "active");
        }

        [TestCase(LlmHistoryFormat.Responses)]
        [TestCase(LlmHistoryFormat.ChatCompletions)]
        public async NUnitTask ConversationAutomaticallyCarriesHistoryAndKeepsRequestIdentifiers(LlmHistoryFormat format)
        {
            var rig = Create(format);
            var first = await rig.Conversation.ChatAsync(Request("first"));
            await rig.Conversation.ChatAsync(Request("second"));
            var next = rig.Service.Calls[1];
            Assert.That(next.History.Count, Is.EqualTo(2));
            Assert.That((string)next.History[0]["content"], Is.EqualTo("first"));
            Assert.That((string)next.History[1]["role"], Is.EqualTo("assistant"));
            Assert.That(next.PreviousResponseId, Is.EqualTo(format == LlmHistoryFormat.Responses ? first.ResponseId : null));
            Assert.That(next.ContextId, Is.EqualTo("context"));
            Assert.That(next.SessionId, Is.EqualTo("input-session"));
            Assert.That(next.UserId, Is.EqualTo("user"));
            Assert.That(next.Channel, Is.EqualTo("voice"));
            var snapshot = rig.Conversation.Context.GetSnapshot();
            Assert.That(snapshot.ContextId, Is.EqualTo("context"));
            Assert.That(snapshot.History.Count, Is.EqualTo(4));
            Assert.That(snapshot.ResponseId, Is.EqualTo(format == LlmHistoryFormat.Responses ? "response-2" : null));
        }

        [TestCase(LlmHistoryFormat.Responses)]
        [TestCase(LlmHistoryFormat.ChatCompletions)]
        public async NUnitTask MultimodalHistoryUsesTheProviderFormat(LlmHistoryFormat format)
        {
            var rig = Create(format);
            var request = Request("describe");
            request.ImageUrls = new[] { "https://example.com/image.png" };
            await rig.Conversation.ChatAsync(request);
            var content = (JArray)rig.Conversation.Context.GetSnapshot().History[0]["content"];
            Assert.That((string)content[0]["type"], Is.EqualTo(format == LlmHistoryFormat.Responses ? "input_image" : "image_url"));
            Assert.That((string)content[1]["type"], Is.EqualTo(format == LlmHistoryFormat.Responses ? "input_text" : "text"));
            Assert.That((string)content[1]["text"], Is.EqualTo("describe"));
        }

        [TestCase(LlmHistoryFormat.Responses)]
        [TestCase(LlmHistoryFormat.ChatCompletions)]
        public async NUnitTask CompleteToolChainIsPreservedInNativeHistory(LlmHistoryFormat format)
        {
            var options = new LlmServiceOptions
            {
                ApiKey = "test-key", Tools = new[] { new LlmTool { Name = "lookup", ExecuteAsync = (call, request, token) => UniTask.FromResult(new LlmToolResult { Data = new JObject { ["value"] = 42 } }) } }
            };
            var rig = Create(format, options: options);
            var round = 0;
            rig.Service.Generate = async (request, emit, token) =>
            {
                if (round++ == 0) return ToolProviderResult(format);
                await emit(new LlmResponse { Text = "answer。" });
                return rig.Service.Completed("answer。", "final-id");
            };
            await rig.Conversation.ChatAsync(Request());
            var history = rig.Conversation.Context.GetSnapshot().History;
            Assert.That(history.Count, Is.EqualTo(4));
            if (format == LlmHistoryFormat.Responses)
            {
                Assert.That((string)history[1]["type"], Is.EqualTo("function_call"));
                Assert.That((string)history[2]["type"], Is.EqualTo("function_call_output"));
                Assert.That((string)history[2]["output"], Is.EqualTo("{\"value\":42}"));
            }
            else
            {
                Assert.That((string)history[1]["tool_calls"][0]["id"], Is.EqualTo("call-1"));
                Assert.That((string)history[2]["role"], Is.EqualTo("tool"));
                Assert.That((string)history[2]["content"], Is.EqualTo("{\"value\":42}"));
            }
            Assert.That((string)history[3]["role"], Is.EqualTo("assistant"));
            await rig.Conversation.ChatAsync(Request("next"));
            Assert.That(rig.Service.Calls.Last().History.Count, Is.EqualTo(4));
            Assert.That(rig.Service.Calls.Last().PreviousResponseId, Is.EqualTo(format == LlmHistoryFormat.Responses ? "final-id" : null));
        }

        [TestCase(LlmHistoryFormat.Responses)]
        [TestCase(LlmHistoryFormat.ChatCompletions)]
        public async NUnitTask UnresolvedToolCallDoesNotCommitAnIncompleteConversation(LlmHistoryFormat format)
        {
            var rig = Create(format);
            await rig.Conversation.ChatAsync(Request("saved"));
            var saved = rig.Conversation.Context.GetSnapshot();
            rig.Service.Generate = (request, emit, token) => UniTask.FromResult(ToolProviderResult(format));
            var result = await rig.Conversation.ChatAsync(Request("needs tool"));
            Assert.That(result.Error, Is.Null);
            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            var current = rig.Conversation.Context.GetSnapshot();
            Assert.That(JToken.DeepEquals(current.History, saved.History), Is.True);
            Assert.That(current.ResponseId, Is.EqualTo(saved.ResponseId));
        }

        [Test]
        public async NUnitTask ProviderFailureDoesNotCommitPartialTextOrReplaceTheLastSuccessfulId()
        {
            var rig = Create();
            await rig.Conversation.ChatAsync(Request("saved"));
            var saved = rig.Conversation.Context.GetSnapshot();
            rig.Service.Generate = async (request, emit, token) =>
            {
                await emit(new LlmResponse { Text = "partial。unfinished" });
                throw new LlmServiceException(new LlmError { Code = "incomplete", Message = "failed" });
            };
            var result = await rig.Conversation.ChatAsync(Request("failed input"));
            Assert.That(result.Error.Code, Is.EqualTo("incomplete"));
            Assert.That(JToken.DeepEquals(rig.Conversation.Context.GetSnapshot().History, saved.History), Is.True);
            Assert.That(rig.Conversation.Context.GetSnapshot().ResponseId, Is.EqualTo(saved.ResponseId));
            rig.Service.Generate = rig.Service.DefaultGenerate;
            await rig.Conversation.ChatAsync(Request("retry"));
            Assert.That(rig.Service.Calls.Last().PreviousResponseId, Is.EqualTo(saved.ResponseId));
            Assert.That(rig.Service.Calls.Last().History.Count, Is.EqualTo(2));
        }

        [Test]
        public async NUnitTask CancellationAfterPartialOutputDoesNotCommitHistory()
        {
            var rig = Create();
            await rig.Conversation.ChatAsync(Request("saved"));
            var entered = Signal();
            rig.Service.Generate = async (request, emit, token) =>
            {
                await emit(new LlmResponse { Text = "partial。" });
                entered.TrySetResult(true);
                await WaitForCancellation(token);
                return rig.Service.Completed("unreachable", "unsaved-id");
            };
            using (var cancellation = new CancellationTokenSource())
            {
                var active = rig.Conversation.ChatAsync(Request("canceled"), cancellationToken: cancellation.Token);
                await entered.Task;
                cancellation.Cancel();
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await active);
            }
            var snapshot = rig.Conversation.Context.GetSnapshot();
            Assert.That(snapshot.History.Count, Is.EqualTo(2));
            Assert.That(snapshot.ResponseId, Is.EqualTo("response-1"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask CallbackFailureIncludingFinalNotificationDoesNotCommit(bool atFinal)
        {
            var rig = Create();
            var error = new LlmServiceException(new LlmError { Code = "application", Message = "callback" });
            var actual = await SpeechAsyncAssert.ThrowsAsync<LlmServiceException>(async () => await rig.Conversation.ChatAsync(Request(), response =>
            {
                if (response.IsFinal == atFinal) throw error;
                return UniTask.CompletedTask;
            }));
            Assert.That(actual, Is.SameAs(error));
            Assert.That(rig.Conversation.Context.GetSnapshot().History, Is.Empty);
            Assert.That(rig.Conversation.Context.GetSnapshot().ResponseId, Is.Null);
        }

        [Test]
        public async NUnitTask ConcurrentCallsWaitForGenerationAndCommitAndCopyTheirInputAtEntry()
        {
            var rig = Create();
            var firstEntered = Signal();
            var releaseFirst = Signal();
            rig.Service.Generate = async (request, emit, token) =>
            {
                if (request.Text == "first")
                {
                    firstEntered.TrySetResult(true);
                    using (token.Register(() => releaseFirst.TrySetCanceled())) await releaseFirst.Task;
                }
                return await rig.Service.DefaultGenerate(request, emit, token);
            };
            var first = rig.Conversation.ChatAsync(Request("first"));
            await firstEntered.Task;
            var secondInput = Request("second");
            secondInput.Parameters = new JObject { ["temperature"] = 0.1 };
            var second = rig.Conversation.ChatAsync(secondInput);
            secondInput.Text = "mutated";
            secondInput.Parameters["temperature"] = 0.9;
            Assert.That(rig.Service.Calls.Count, Is.EqualTo(1));
            Assert.That(rig.Conversation.Context.GetSnapshot().History, Is.Empty);
            releaseFirst.TrySetResult(true);
            await UniTask.WhenAll(first, second);
            Assert.That(rig.Service.Calls[1].Text, Is.EqualTo("second"));
            Assert.That((double)rig.Service.Calls[1].Parameters["temperature"], Is.EqualTo(0.1));
            Assert.That(rig.Service.Calls[1].History.Count, Is.EqualTo(2));
            Assert.That(rig.Service.Calls[1].PreviousResponseId, Is.EqualTo("response-1"));
        }

        [Test]
        public async NUnitTask CancelingQueuedCallDoesNotInvokeTheProviderOrChangeTheConversation()
        {
            var rig = Create();
            var entered = Signal();
            var release = Signal();
            rig.Service.Generate = async (request, emit, token) =>
            {
                entered.TrySetResult(true);
                using (token.Register(() => release.TrySetCanceled())) await release.Task;
                return await rig.Service.DefaultGenerate(request, emit, token);
            };
            var first = rig.Conversation.ChatAsync(Request("first"));
            await entered.Task;
            using (var cancellation = new CancellationTokenSource())
            {
                var second = rig.Conversation.ChatAsync(Request("second"), cancellationToken: cancellation.Token);
                cancellation.Cancel();
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await second);
            }
            Assert.That(rig.Service.Calls.Count, Is.EqualTo(1));
            release.TrySetResult(true);
            await first;
            Assert.That(rig.Conversation.Context.GetSnapshot().History.Count, Is.EqualTo(2));
        }

        [Test]
        public async NUnitTask ResetInvalidatesAnInFlightCommitAndAllowsANewContext()
        {
            var rig = Create();
            var entered = Signal();
            var release = Signal();
            rig.Service.Generate = async (request, emit, token) =>
            {
                entered.TrySetResult(true);
                using (token.Register(() => release.TrySetCanceled())) await release.Task;
                return await rig.Service.DefaultGenerate(request, emit, token);
            };
            var old = rig.Conversation.ChatAsync(Request());
            await entered.Task;
            rig.Conversation.Context.Reset();
            release.TrySetResult(true);
            await old;
            var cleared = rig.Conversation.Context.GetSnapshot();
            Assert.That(cleared.History, Is.Empty);
            Assert.That(cleared.ContextId, Is.Null);
            Assert.That(cleared.ResponseId, Is.Null);
            await rig.Conversation.ChatAsync(Request("new", "new-context"));
            Assert.That(rig.Service.Calls.Last().History, Is.Empty);
            Assert.That(rig.Service.Calls.Last().PreviousResponseId, Is.Null);
            Assert.That(rig.Conversation.Context.GetSnapshot().ContextId, Is.EqualTo("new-context"));
        }

        [Test]
        public async NUnitTask ResetAsyncWaitsForGenerationThenClearsTheCommittedTurn()
        {
            var rig = Create();
            var entered = Signal();
            var release = Signal();
            rig.Service.Generate = async (request, emit, token) =>
            {
                entered.TrySetResult(true);
                using (token.Register(() => release.TrySetCanceled())) await release.Task;
                return await rig.Service.DefaultGenerate(request, emit, token);
            };
            var active = rig.Conversation.ChatAsync(Request());
            await entered.Task;
            var reset = rig.Conversation.ResetAsync();
            Assert.That(reset.Status.IsCompleted(), Is.False);
            release.TrySetResult(true);
            await UniTask.WhenAll(active, reset);
            Assert.That(rig.Conversation.Context.GetSnapshot().History, Is.Empty);
        }

        [Test]
        public async NUnitTask ContextIdCannotChangeUntilResetAndManualHistoryIsRejected()
        {
            var rig = Create();
            await rig.Conversation.ChatAsync(Request());
            await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(async () => await rig.Conversation.ChatAsync(Request("other", "other-context")));
            var request = Request();
            request.PreviousResponseId = "manual";
            Assert.Throws<ArgumentException>(() => rig.Conversation.ChatAsync(request));
            request.PreviousResponseId = null;
            request.History.Add(new JObject { ["role"] = "user", ["content"] = "manual" });
            Assert.Throws<ArgumentException>(() => rig.Conversation.ChatAsync(request));
            Assert.That(rig.Service.Calls.Count, Is.EqualTo(1));
            await rig.Conversation.ResetAsync();
            await rig.Conversation.ChatAsync(Request("other", "other-context"));
            Assert.That(rig.Conversation.Context.GetSnapshot().ContextId, Is.EqualTo("other-context"));
        }

        [Test]
        public async NUnitTask StoredHistoryIsIndependentOfResultAndSnapshotMutations()
        {
            var rig = Create();
            var result = await rig.Conversation.ChatAsync(Request());
            result.InputItems[0]["content"] = "changed input";
            result.OutputItems.Clear();
            var first = rig.Conversation.Context.GetSnapshot();
            first.History[0]["content"] = "changed snapshot";
            first.History.Clear();
            var second = rig.Conversation.Context.GetSnapshot();
            Assert.That(second.History.Count, Is.EqualTo(2));
            Assert.That((string)second.History[0]["content"], Is.EqualTo("hello"));
        }

        [Test]
        public async NUnitTask UpdatingOptionsPreservesHistoryAndClearsContinuationId()
        {
            var rig = Create();
            await rig.Conversation.ChatAsync(Request());
            var options = rig.Conversation.GetOptions();
            options.Model = "updated-model";
            rig.Conversation.UpdateOptions(options);
            var snapshot = rig.Conversation.Context.GetSnapshot();
            Assert.That(snapshot.History.Count, Is.EqualTo(2));
            Assert.That(snapshot.ResponseId, Is.Null);
            await rig.Conversation.ChatAsync(Request("next"));
            Assert.That(rig.Service.Calls[1].History.Count, Is.EqualTo(2));
            Assert.That(rig.Service.Calls[1].PreviousResponseId, Is.Null);
        }

        [TestCase(LlmHistoryFormat.Responses)]
        [TestCase(LlmHistoryFormat.ChatCompletions)]
        public async NUnitTask AsyncOptionsUpdateRunsBetweenQueuedTurnsAndCopiesSettings(LlmHistoryFormat format)
        {
            var rig = Create(format, options: new LlmServiceOptions { ApiKey = "test-key", Model = "original-model" });
            var entered = Signal();
            var release = Signal();
            rig.Service.Generate = async (request, emit, token) =>
            {
                if (request.Text == "first") { entered.TrySetResult(true); await release.Task; }
                return await rig.Service.DefaultGenerate(request, emit, token);
            };
            var first = rig.Conversation.ChatAsync(Request("first"));
            UniTask update = default;
            UniTask<LlmResult> second = default;
            try
            {
                await CompleteWithin(entered.Task);
                var options = rig.Conversation.GetOptions();
                options.Model = "updated-model";
                options.ExtraBody = new JObject { ["nested"] = new JObject { ["value"] = "snapshot" } };
                options.VoiceTextTags = new[] { "speech" };
                update = rig.Conversation.UpdateOptionsAsync(options);
                second = rig.Conversation.ChatAsync(Request("second"));
                options.Model = "caller-mutated";
                options.ExtraBody["nested"]["value"] = "caller-mutated";
                options.VoiceTextTags[0] = "caller-mutated";
                Assert.That(update.Status.IsCompleted(), Is.False);
                Assert.That(second.Status.IsCompleted(), Is.False);
                Assert.That(rig.Conversation.GetOptions().Model, Is.EqualTo("original-model"));
            }
            finally { release.TrySetResult(true); }
            await CompleteWithin(UniTask.WhenAll(first, update, second));
            Assert.That(rig.Service.CallOptions[0].Model, Is.EqualTo("original-model"));
            Assert.That(rig.Service.CallOptions[1].Model, Is.EqualTo("updated-model"));
            Assert.That((string)rig.Service.CallOptions[1].ExtraBody["nested"]["value"], Is.EqualTo("snapshot"));
            Assert.That(rig.Service.CallOptions[1].VoiceTextTags, Is.EqualTo(new[] { "speech" }));
            Assert.That(rig.Service.Calls[1].History.Count, Is.EqualTo(2));
            Assert.That((string)rig.Service.Calls[1].History[0]["content"], Is.EqualTo("first"));
            Assert.That(rig.Service.Calls[1].PreviousResponseId, Is.Null);
            var snapshot = rig.Conversation.Context.GetSnapshot();
            Assert.That(snapshot.History.Count, Is.EqualTo(4));
            Assert.That(snapshot.ResponseId, Is.EqualTo(format == LlmHistoryFormat.Responses ? "response-2" : null));
        }

        [Test]
        public async NUnitTask CancelingQueuedOptionsUpdateKeepsSettingsHistoryAndContinuation()
        {
            var rig = Create(options: new LlmServiceOptions { ApiKey = "test-key", Model = "original-model" });
            await rig.Conversation.ChatAsync(Request("saved"));
            var entered = Signal();
            var release = Signal();
            rig.Service.Generate = async (request, emit, token) =>
            {
                entered.TrySetResult(true);
                await release.Task;
                return await rig.Service.DefaultGenerate(request, emit, token);
            };
            var active = rig.Conversation.ChatAsync(Request("active"));
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    await CompleteWithin(entered.Task);
                    var options = rig.Conversation.GetOptions();
                    options.Model = "canceled-model";
                    var update = rig.Conversation.UpdateOptionsAsync(options, cancellation.Token);
                    cancellation.Cancel();
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await CompleteWithin(update));
                    Assert.That(rig.Conversation.GetOptions().Model, Is.EqualTo("original-model"));
                    var saved = rig.Conversation.Context.GetSnapshot();
                    Assert.That(saved.History.Count, Is.EqualTo(2));
                    Assert.That(saved.ResponseId, Is.EqualTo("response-1"));
                    Assert.Throws<OperationCanceledException>(() => rig.Conversation.UpdateOptionsAsync(options, cancellation.Token));
                }
                finally { release.TrySetResult(true); }
            }
            await CompleteWithin(active);
            rig.Service.Generate = rig.Service.DefaultGenerate;
            await CompleteWithin(rig.Conversation.ChatAsync(Request("next")));
            Assert.That(rig.Service.CallOptions[2].Model, Is.EqualTo("original-model"));
            Assert.That(rig.Service.Calls[2].PreviousResponseId, Is.EqualTo("response-2"));
            Assert.That(rig.Service.Calls[2].History.Count, Is.EqualTo(4));
        }

        [Test]
        public async NUnitTask InvalidQueuedOptionsLeaveTheLatestTurnAndSettingsUnchanged()
        {
            var rig = Create(options: new LlmServiceOptions { ApiKey = "test-key", Model = "original-model" });
            var entered = Signal();
            var release = Signal();
            rig.Service.Generate = async (request, emit, token) =>
            {
                entered.TrySetResult(true);
                await release.Task;
                return await rig.Service.DefaultGenerate(request, emit, token);
            };
            var active = rig.Conversation.ChatAsync(Request("saved"));
            UniTask update = default;
            try
            {
                await CompleteWithin(entered.Task);
                var options = rig.Conversation.GetOptions();
                options.Model = "";
                update = rig.Conversation.UpdateOptionsAsync(options);
                Assert.That(update.Status.IsCompleted(), Is.False);
            }
            finally { release.TrySetResult(true); }
            await CompleteWithin(active);
            await SpeechAsyncAssert.ThrowsAsync<ArgumentException>(async () => await CompleteWithin(update));
            Assert.That(rig.Conversation.GetOptions().Model, Is.EqualTo("original-model"));
            var snapshot = rig.Conversation.Context.GetSnapshot();
            Assert.That(snapshot.History.Count, Is.EqualTo(2));
            Assert.That(snapshot.ResponseId, Is.EqualTo("response-1"));
            rig.Service.Generate = rig.Service.DefaultGenerate;
            await CompleteWithin(rig.Conversation.ChatAsync(Request("next")));
            Assert.That(rig.Service.Calls[1].PreviousResponseId, Is.EqualTo("response-1"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask DisposeHonorsServiceOwnershipAndIsIdempotent(bool ownsService)
        {
            var rig = Create(ownsService: ownsService);
            await rig.Conversation.ChatAsync(Request());
            var disposal = rig.Conversation.DisposeAsync();
            Assert.That(rig.Conversation.DisposeAsync(), Is.EqualTo(disposal));
            await disposal;
            Assert.That(rig.Service.DisposeCount, Is.EqualTo(ownsService ? 1 : 0));
            Assert.Throws<ObjectDisposedException>(() => rig.Conversation.ChatAsync(Request()));
            Assert.Throws<ObjectDisposedException>(() => rig.Conversation.ResetAsync());
            Assert.Throws<ObjectDisposedException>(() => rig.Conversation.UpdateOptions(rig.Service.GetOptions()));
            Assert.Throws<ObjectDisposedException>(() => rig.Conversation.UpdateOptionsAsync(rig.Service.GetOptions()));
            if (!ownsService) Assert.That((await rig.Service.ChatAsync(Request())).Error, Is.Null);
        }

        [Test]
        public async NUnitTask DisposeCancelsQueuedWorkAndDrainsActiveCallbackBeforeOwnedService()
        {
            var rig = Create(ownsService: true);
            var callbackEntered = Signal();
            var releaseCallback = Signal();
            var active = rig.Conversation.ChatAsync(Request(), async response => { callbackEntered.TrySetResult(true); await releaseCallback.Task; });
            await callbackEntered.Task;
            var queued = rig.Conversation.ChatAsync(Request("queued"));
            var originalModel = rig.Service.GetOptions().Model;
            var options = rig.Service.GetOptions();
            options.Model = "disposed-model";
            var update = rig.Conversation.UpdateOptionsAsync(options);
            UniTask disposal = default;
            try
            {
                Assert.Throws<InvalidOperationException>(() => rig.Conversation.UpdateOptions(rig.Service.GetOptions()));
                disposal = rig.Conversation.DisposeAsync();
                Assert.That(disposal.Status.IsCompleted(), Is.False);
                Assert.That(rig.Service.DisposeCount, Is.Zero);
            }
            finally { releaseCallback.TrySetResult(true); }
            await disposal;
            await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await active);
            await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await queued);
            await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await CompleteWithin(update));
            Assert.That(rig.Service.GetOptions().Model, Is.EqualTo(originalModel));
            Assert.That(rig.Service.Calls.Count, Is.EqualTo(1));
            Assert.That(rig.Service.DisposeCount, Is.EqualTo(1));
            Assert.That(rig.Conversation.Context.GetSnapshot().History, Is.Empty);
        }

        [TestCase("callback")]
        [TestCase("hook")]
        [TestCase("tool")]
        public async NUnitTask ReentrantControlIsRejectedFromCallbacksHooksAndTools(string location)
        {
            var rig = Create();
            int checkedCount = 0;
            Action check = () =>
            {
                Assert.Throws<InvalidOperationException>(() => rig.Conversation.ChatAsync(Request()));
                Assert.Throws<InvalidOperationException>(() => rig.Conversation.ResetAsync());
                Assert.Throws<InvalidOperationException>(() => rig.Conversation.UpdateOptionsAsync(rig.Service.GetOptions()));
                Assert.Throws<InvalidOperationException>(() => rig.Conversation.DisposeAsync());
                checkedCount++;
            };
            if (location == "hook") rig.Service.RequestFilterAsync = (request, token) => { check(); return UniTask.FromResult(request); };
            if (location == "tool")
            {
                rig.Service.Generate = (request, emit, token) => UniTask.FromResult(ToolProviderResult(LlmHistoryFormat.Responses));
                rig.Service.ToolExecutorAsync = (call, request, token) => { check(); return UniTask.FromResult(new LlmToolResult { Data = true, ContinueChain = false }); };
            }
            await rig.Conversation.ChatAsync(Request(), response => { if (location == "callback" && !response.IsFinal) check(); return UniTask.CompletedTask; });
            Assert.That(checkedCount, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask HistoryRecordsFilteredAndGuardrailReplacedInput()
        {
            var options = new LlmServiceOptions { ApiKey = "test-key", Guardrails = new ILlmGuardrail[] { new Guard(LlmGuardrailScope.Request, LlmGuardrailAction.Replace, "guarded") } };
            var rig = Create(options: options);
            rig.Service.RequestFilterAsync = (request, token) => { request.Text = "filtered"; return UniTask.FromResult(request); };
            await rig.Conversation.ChatAsync(Request("original"));
            Assert.That(rig.Service.Calls[0].Text, Is.EqualTo("guarded"));
            Assert.That((string)rig.Conversation.Context.GetSnapshot().History[0]["content"], Is.EqualTo("guarded"));
        }

        [Test]
        public async NUnitTask SkippedInputsNeverCommitOrBindContext()
        {
            var rig = Create();
            rig.Service.RequestFilterAsync = (request, token) => UniTask.FromResult<LlmRequest>(null);
            await rig.Conversation.ChatAsync(Request());
            rig.Service.RequestFilterAsync = null;
            await rig.Conversation.ChatAsync(new LlmRequest { ContextId = "different" });
            Assert.That(rig.Service.Calls, Is.Empty);
            Assert.That(rig.Conversation.Context.GetSnapshot().History, Is.Empty);
            Assert.That(rig.Conversation.Context.GetSnapshot().ContextId, Is.Null);
        }

        [TestCase(LlmGuardrailScope.Request)]
        [TestCase(LlmGuardrailScope.Response)]
        public async NUnitTask LocalGuardrailResponseIsSavedAndRequiresFullHistoryContinuation(LlmGuardrailScope scope)
        {
            var options = new LlmServiceOptions { ApiKey = "test-key", Guardrails = new ILlmGuardrail[] { new Guard(scope, LlmGuardrailAction.Block, "local answer") } };
            var rig = Create(options: options);
            await rig.Conversation.ChatAsync(Request());
            var snapshot = rig.Conversation.Context.GetSnapshot();
            Assert.That(snapshot.History.Count, Is.EqualTo(scope == LlmGuardrailScope.Request ? 2 : 3));
            Assert.That((string)snapshot.History.Last["content"], Is.EqualTo("local answer"));
            Assert.That(snapshot.ResponseId, Is.Null);
        }

        [Test]
        public async NUnitTask StoreFalseClearsTheOldIdWhileKeepingBothTurns()
        {
            var rig = Create();
            await rig.Conversation.ChatAsync(Request("first"));
            rig.Service.Generate = async (request, emit, token) =>
            {
                var result = await rig.Service.DefaultGenerate(request, emit, token);
                result.CanUsePreviousResponse = false;
                return result;
            };
            await rig.Conversation.ChatAsync(Request("second"));
            Assert.That(rig.Conversation.Context.GetSnapshot().ResponseId, Is.Null);
            Assert.That(rig.Conversation.Context.GetSnapshot().History.Count, Is.EqualTo(4));
            await rig.Conversation.ChatAsync(Request("third"));
            Assert.That(rig.Service.Calls.Last().PreviousResponseId, Is.Null);
            Assert.That(rig.Service.Calls.Last().History.Count, Is.EqualTo(4));
        }

        [Test]
        public async NUnitTask LocallyCompletedToolOutputIsSavedWithoutReusingTheEarlierServerId()
        {
            var rig = Create();
            rig.Service.Generate = (request, emit, token) => UniTask.FromResult(ToolProviderResult(LlmHistoryFormat.Responses));
            rig.Service.ToolExecutorAsync = (call, request, token) => UniTask.FromResult(new LlmToolResult
            { Data = new JObject { ["answer"] = 42 }, Text = "local answer。", ContinueChain = false });
            var result = await rig.Conversation.ChatAsync(Request());
            Assert.That(result.ResponseId, Is.EqualTo("tool-response"));
            Assert.That(result.CanUsePreviousResponse, Is.False);
            var snapshot = rig.Conversation.Context.GetSnapshot();
            Assert.That(snapshot.ResponseId, Is.Null);
            Assert.That(snapshot.History.Count, Is.EqualTo(3));
            Assert.That((string)snapshot.History[2]["type"], Is.EqualTo("function_call_output"));
            rig.Service.Generate = rig.Service.DefaultGenerate;
            await rig.Conversation.ChatAsync(Request("next"));
            Assert.That(rig.Service.Calls.Last().History.Count, Is.EqualTo(3));
            Assert.That(rig.Service.Calls.Last().PreviousResponseId, Is.Null);
        }

        [Test]
        public async NUnitTask BuiltInProvidersInferTheirFormatAndWebSocketDefaultsToOneConnection()
        {
            Assert.That(new OpenAIResponsesWebSocketServiceOptions().MaxConnections, Is.EqualTo(1));
            ILlmService[] providers =
            {
                new ChatCompletionsClient(new LlmServiceOptions { ApiKey = "test-key" }),
                new OpenAIResponsesClient(new LlmServiceOptions { ApiKey = "test-key" }),
                new OpenAIResponsesWebSocketClient(new OpenAIResponsesWebSocketServiceOptions { ApiKey = "test-key" })
            };
            foreach (var provider in providers)
            {
                var conversation = new LlmConversation(provider, ownsService: true);
                await conversation.DisposeAsync();
            }
        }

        private static LlmProviderResult ToolProviderResult(LlmHistoryFormat format)
        {
            var output = format == LlmHistoryFormat.Responses
                ? new JObject { ["type"] = "function_call", ["call_id"] = "call-1", ["name"] = "lookup", ["arguments"] = "{}" }
                : new JObject { ["role"] = "assistant", ["content"] = JValue.CreateNull(), ["tool_calls"] = new JArray(new JObject
                    { ["id"] = "call-1", ["type"] = "function", ["function"] = new JObject { ["name"] = "lookup", ["arguments"] = "{}" } }) };
            return new LlmProviderResult
            {
                ResponseId = "tool-response", OutputItems = new JArray(output),
                ToolCalls = new List<LlmToolCall> { new LlmToolCall { Id = "call-1", Name = "lookup", Arguments = "{}" } }
            };
        }

        private sealed class Rig { public FakeService Service; public LlmConversation Conversation; }
        private sealed class FakeService : LlmServiceBase
        {
            private readonly LlmHistoryFormat format;
            public readonly List<LlmRequest> Calls = new List<LlmRequest>();
            public readonly List<LlmServiceOptions> CallOptions = new List<LlmServiceOptions>();
            public Func<LlmRequest, Func<LlmResponse, UniTask>, CancellationToken, UniTask<LlmProviderResult>> Generate;
            public int DisposeCount;
            protected override bool UsesResponsesApi => format == LlmHistoryFormat.Responses;
            public FakeService(LlmHistoryFormat format, LlmServiceOptions options) : base(options)
            { this.format = format; Generate = DefaultGenerate; }
            public async UniTask<LlmProviderResult> DefaultGenerate(LlmRequest request, Func<LlmResponse, UniTask> emit, CancellationToken token)
            {
                await emit(new LlmResponse { Text = "reply。" });
                return Completed("reply。", "response-" + Calls.Count);
            }
            public LlmProviderResult Completed(string text, string id)
            {
                var message = new JObject { ["role"] = "assistant", ["content"] = text };
                if (UsesResponsesApi)
                {
                    message["type"] = "message";
                    message["id"] = "message-" + id;
                    message["content"] = new JArray(new JObject { ["type"] = "output_text", ["text"] = text, ["annotations"] = new JArray() });
                }
                return new LlmProviderResult { ResponseId = id, OutputItems = new JArray(message) };
            }
            protected override UniTask<LlmProviderResult> GenerateCoreAsync(LlmRequest request, LlmServiceOptions options, Func<LlmResponse, UniTask> emit, CancellationToken token)
            {
                lock (Calls) { Calls.Add(request.Copy()); CallOptions.Add(options.Copy()); }
                return Generate(request, emit, token);
            }
            protected override UniTask DisposeResourcesAsync() { DisposeCount++; return UniTask.CompletedTask; }
        }
        private sealed class Guard : ILlmGuardrail
        {
            public LlmGuardrailScope Scope { get; }
            private readonly LlmGuardrailAction action;
            private readonly string replacement;
            public Guard(LlmGuardrailScope scope, LlmGuardrailAction action, string replacement)
            { Scope = scope; this.action = action; this.replacement = replacement; }
            public UniTask<LlmGuardrailResult> ApplyAsync(LlmRequest request, string text, CancellationToken token)
                => UniTask.FromResult(new LlmGuardrailResult { IsTriggered = true, Name = "guard", Action = action, Text = replacement });
        }
    }
}

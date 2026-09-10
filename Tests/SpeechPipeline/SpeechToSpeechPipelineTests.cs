using ChatdollKit.SpeechPipeline.Performance;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.LLM;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.TTS;
using ChatdollKit.SpeechPipeline.VAD;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class SpeechToSpeechPipelineTests
    {
        private readonly List<Rig> rigs = new List<Rig>();
        private readonly List<SpeechCompletionSource<bool>> releases = new List<SpeechCompletionSource<bool>>();
        private static SpeechCompletionSource<bool> Signal() => new SpeechCompletionSource<bool>();
        private SpeechCompletionSource<bool> ReleaseGate() { var gate = Signal(); releases.Add(gate); return gate; }
        private static SpeechPipelineRequest Request(string text = "hello", string id = null) => new SpeechPipelineRequest
        { Text = text, TransactionId = id ?? Guid.NewGuid().ToString("N"), UserId = "user", Channel = "voice" };

        private Rig Create(SpeechPipelineOptions options = null, bool ownsComponents = false, ISpeechDetector vad = null)
        {
            options = options ?? new SpeechPipelineOptions();
            options.SessionId = "session";
            options.ContextId = options.ContextId ?? "context";
            var rig = new Rig();
            rig.Pipeline = new SpeechToSpeechPipeline(rig.Stt, rig.Llm, rig.Tts, options, vad,
                performanceRecorder: rig.Performance, clock: rig.Clock, ownsComponents: ownsComponents,
                historyFormat: LlmHistoryFormat.ChatCompletions);
            rig.Pipeline.ResponseReceived += response => { rig.Responses.Enqueue(response.Copy()); return UniTask.CompletedTask; };
            rig.Pipeline.Error += error => rig.Errors.Enqueue(error);
            rigs.Add(rig);
            return rig;
        }

        [TearDown]
        public async NUnitTask Cleanup()
        {
            foreach (var gate in releases) gate.TrySetResult(true);
            foreach (var rig in rigs) await Within(rig.Pipeline.DisposeAsync());
            releases.Clear(); rigs.Clear();
        }

        // The timer is only a deadlock guard. Test scheduling and expected outcomes use signals, not elapsed time.
        private static async UniTask Within(UniTask task)
        {
            task = SpeechAsync.Share(task);
            Assert.That(await UniTask.WhenAny(task, SpeechAsync.Delay(TimeSpan.FromMilliseconds(5000))), Is.EqualTo(0), "Operation did not finish after its gate was released.");
            await task;
        }
        private static async UniTask<T> Within<T>(UniTask<T> task) { task = SpeechAsync.Share(task); await Within((UniTask)task); return await task; }
        private static async UniTask WaitForCancellation(CancellationToken token)
        {
            var canceled = Signal();
            using (token.Register(() => canceled.TrySetCanceled())) await canceled.Task;
        }
        private static SpeechPipelineResponse[] For(Rig rig, string id) => rig.Responses.Where(response => response.TransactionId == id).ToArray();
        private static SpeechPipelineResponseType[] Types(Rig rig, string id) => For(rig, id).Select(response => response.Type).ToArray();

        [Test]
        public async NUnitTask AcceptedResetsUnfinishedRecordingAndKeepsTheResponseAndNextShortReply()
        {
            var vad = new StandardSpeechDetectorEngine(new StandardSpeechDetectorOptions
                { MinDuration = 0.01, SilenceDurationThreshold = 0.05 });
            var rig = Create(new SpeechPipelineOptions { MergeRequestThresholdSeconds = 0 }, vad: vad);
            rig.Stt.Handler = (session, audio, token) => UniTask.FromResult(new SpeechRecognitionResult { Text = "はい" });
            rig.Pipeline.ResponseReceived += response => response.Type == SpeechPipelineResponseType.Accepted
                ? rig.Pipeline.ResetSpeechInputAsync() : UniTask.CompletedTask;
            var voiced = new byte[3200]; // 100 ms at 16 kHz, mono PCM16, amplitude 1000.
            for (var i = 0; i < voiced.Length; i += 2) { voiced[i] = 0xe8; voiced[i + 1] = 0x03; }
            var silence = new byte[voiced.Length];

            // A request is accepted while the detector is still recording trailing audio.
            await rig.Pipeline.ProcessAudioSamplesAsync(voiced);
            Assert.That(await vad.IsRecordingAsync("session"), Is.True);
            var response = await Within(rig.Pipeline.InvokeAsync(Request("こんにちは")));
            Assert.That(response.Type, Is.EqualTo(SpeechPipelineResponseType.Final));
            Assert.That(await vad.IsRecordingAsync("session"), Is.False);
            await rig.Pipeline.ProcessAudioSamplesAsync(silence);
            await Within(rig.Pipeline.DrainAsync());
            Assert.That(rig.Stt.Calls, Is.Empty, "Discarded recording must not become another request.");

            await rig.Pipeline.ProcessAudioSamplesAsync(voiced);
            await rig.Pipeline.ProcessAudioSamplesAsync(silence);
            await Within(rig.Pipeline.DrainAsync());
            Assert.That(rig.Llm.Calls.Select(call => call.Text), Is.EqualTo(new[] { "こんにちは", "はい" }));
            Assert.That(rig.Llm.Calls.Last().History.ToString(), Does.Contain("こんにちは"));
            Assert.That(rig.Errors, Is.Empty);
            await rig.Pipeline.DisposeAsync(); await vad.DisposeAsync();
        }

        [Test]
        public async NUnitTask CommonRecognitionWaitsForValidatedStartAndKeepsRecognizedText()
        {
            var vad = new RecognizingDetector();
            var accepted = Signal();
            var release = ReleaseGate();
            var rig = Create(new SpeechPipelineOptions
            {
                TimestampIntervalSeconds = 1,
                OnAcceptedAsync = async (request, token) => { accepted.TrySetResult(true); await release.Task; }
            }, vad: vad);
            var updates = new ConcurrentQueue<SpeechRecognitionUpdate>();
            rig.Pipeline.RecognitionUpdated += updates.Enqueue;
            vad.Emit("recognition", null, SpeechRecognitionUpdateKind.Started);
            vad.Emit("recognition", "hel");
            vad.Emit("recognition", "hello");
            vad.Emit("recognition", "hello!", SpeechRecognitionUpdateKind.Confirmed);
            vad.Final("recognition", "hello!");
            await Within(accepted.Task);
            Assert.That(updates.Select(item => item.Kind), Is.EqualTo(new[]
            { SpeechRecognitionUpdateKind.Started, SpeechRecognitionUpdateKind.Partial, SpeechRecognitionUpdateKind.Partial }));
            release.TrySetResult(true);
            await Within(rig.Pipeline.DrainAsync());
            var confirmed = updates.Last();
            Assert.That(confirmed.Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Confirmed));
            Assert.That(confirmed.RecognitionId, Is.EqualTo("recognition"));
            Assert.That(confirmed.Text, Is.EqualTo("hello!"));
            Assert.That(confirmed.TransactionId, Is.EqualTo(rig.Responses.Single(item => item.Type == SpeechPipelineResponseType.Start).TransactionId));
            Assert.That(rig.Llm.Calls.Single().Text, Is.Not.EqualTo("hello!"));
            await vad.DisposeAsync();
        }

        [Test]
        public async NUnitTask CommonActivityPreservesTheLastTranscriptAndRequiresAnOpenRecognition()
        {
            var vad = new RecognizingDetector();
            var rig = Create(vad: vad);
            var updates = new ConcurrentQueue<SpeechRecognitionUpdate>();
            rig.Pipeline.RecognitionUpdated += updates.Enqueue;
            vad.Emit("unknown", null, SpeechRecognitionUpdateKind.Activity, active: true, duration: 10);
            Assert.That(updates, Is.Empty);
            vad.Emit("speech", null, SpeechRecognitionUpdateKind.Started, active: true, duration: 0.032, observedAt: 10);
            Assert.That(updates.Single().Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Started),
                "The pipeline forwards speech onset without a presentation delay.");
            Assert.That(updates.Single().AudioDurationSeconds, Is.EqualTo(0.032));
            vad.Emit("speech", null, SpeechRecognitionUpdateKind.Activity, active: false, duration: 0.032, observedAt: 10.032);
            vad.Emit("speech", "recognized text");
            vad.Emit("speech", null, SpeechRecognitionUpdateKind.Activity, active: true, observedAt: 10.064);
            vad.Emit("speech", null, SpeechRecognitionUpdateKind.Canceled);
            vad.Emit("speech", null, SpeechRecognitionUpdateKind.Activity, active: true, duration: 10);
            var actual = updates.ToArray();
            Assert.That(actual.Select(item => item.Kind), Is.EqualTo(new[]
            {
                SpeechRecognitionUpdateKind.Started, SpeechRecognitionUpdateKind.Activity,
                SpeechRecognitionUpdateKind.Partial, SpeechRecognitionUpdateKind.Activity, SpeechRecognitionUpdateKind.Canceled
            }));
            Assert.That(actual[1].IsSpeechActive, Is.False);
            Assert.That(actual[1].AudioDurationSeconds, Is.EqualTo(0.032));
            Assert.That(actual[1].ObservedAtSeconds, Is.EqualTo(10.032));
            Assert.That(actual[3].IsSpeechActive, Is.True);
            Assert.That(actual[3].AudioDurationSeconds, Is.Null);
            Assert.That(actual[3].ObservedAtSeconds, Is.EqualTo(10.064));
            Assert.That(actual[4].Text, Is.EqualTo("recognized text"), "Activity must not replace the stored partial transcript.");
            Assert.That(actual[4].IsSpeechActive, Is.Null);
            Assert.That(actual[4].AudioDurationSeconds, Is.Null);
            await vad.DisposeAsync();
        }

        [Test]
        public async NUnitTask DetectorConfirmationEndsActivityWhileAwaitingValidatedRequestStart()
        {
            var vad = new RecognizingDetector();
            var accepted = Signal();
            var release = ReleaseGate();
            var rig = Create(new SpeechPipelineOptions
            {
                OnAcceptedAsync = async (request, token) => { accepted.TrySetResult(true); await release.Task; }
            }, vad: vad);
            var updates = new ConcurrentQueue<SpeechRecognitionUpdate>();
            rig.Pipeline.RecognitionUpdated += updates.Enqueue;
            vad.Emit("speech", null, SpeechRecognitionUpdateKind.Started);
            vad.Emit("speech", "yes", SpeechRecognitionUpdateKind.Confirmed);
            vad.Final("speech", "yes");
            await Within(accepted.Task);
            vad.Emit("speech", null, SpeechRecognitionUpdateKind.Activity, active: true, duration: 10);
            vad.Emit("speech", "late partial");
            vad.Emit("speech", null, SpeechRecognitionUpdateKind.Activity, active: true, duration: 10);
            Assert.That(updates.Select(item => item.Kind), Is.EqualTo(new[] { SpeechRecognitionUpdateKind.Started }));
            release.TrySetResult(true);
            await Within(rig.Pipeline.DrainAsync());
            vad.Emit("speech", null, SpeechRecognitionUpdateKind.Activity, active: true, duration: 10);
            Assert.That(updates.Select(item => item.Kind), Is.EqualTo(new[]
                { SpeechRecognitionUpdateKind.Started, SpeechRecognitionUpdateKind.Confirmed }));
            Assert.That(updates.Last().Text, Is.EqualTo("yes"));
            await vad.DisposeAsync();
        }

        [Test]
        public async NUnitTask CommonRecognitionGatesSleepingInputAndCancelsRejectedFinal()
        {
            var vad = new RecognizingDetector();
            var rig = Create(new SpeechPipelineOptions
            {
                Wakewords = new[] { "robot" },
                ValidateRequestAsync = (request, token) => UniTask.FromResult("rejected")
            }, vad: vad);
            var updates = new ConcurrentQueue<SpeechRecognitionUpdate>();
            rig.Pipeline.RecognitionUpdated += updates.Enqueue;
            vad.Emit("utterance", null, SpeechRecognitionUpdateKind.Started);
            vad.Emit("utterance", null, SpeechRecognitionUpdateKind.Activity, active: true, duration: 2);
            vad.Emit("utterance", "hello");
            Assert.That(updates, Is.Empty);
            vad.Emit("utterance", "hello robot");
            vad.Final("utterance", "hello robot");
            await Within(rig.Pipeline.DrainAsync());
            Assert.That(updates.Select(item => item.Kind), Is.EqualTo(new[] { SpeechRecognitionUpdateKind.Partial, SpeechRecognitionUpdateKind.Canceled }));
            await vad.DisposeAsync();
        }

        [Test]
        public async NUnitTask CommonRecognitionClosesSpeechOnsetWithoutPartialAndDoesNotReviveCanceledId()
        {
            var vad = new RecognizingDetector();
            var rig = Create(vad: vad);
            var updates = new ConcurrentQueue<SpeechRecognitionUpdate>();
            rig.Pipeline.RecognitionUpdated += updates.Enqueue;
            vad.Emit("discarded", null, SpeechRecognitionUpdateKind.Started);
            vad.Emit("discarded", null, SpeechRecognitionUpdateKind.Canceled);
            vad.Emit("discarded", "late result");
            Assert.That(updates.Select(item => item.Kind), Is.EqualTo(new[]
            { SpeechRecognitionUpdateKind.Started, SpeechRecognitionUpdateKind.Canceled }));
            vad.Emit("reset", null, SpeechRecognitionUpdateKind.Started);
            await rig.Pipeline.ResetAsync();
            Assert.That(updates.Last().Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Canceled));
            Assert.That(updates.Last().RecognitionId, Is.EqualTo("reset"));
            vad.Emit("reset", "late result");
            Assert.That(updates.Count, Is.EqualTo(4));
            await vad.DisposeAsync();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask CommonRecognitionControlCancelsAndDoesNotReviveOldId(bool reset)
        {
            var vad = new RecognizingDetector();
            var rig = Create(vad: vad);
            var updates = new ConcurrentQueue<SpeechRecognitionUpdate>();
            rig.Pipeline.RecognitionUpdated += updates.Enqueue;
            vad.Emit("old", "pending");
            if (reset) await rig.Pipeline.ResetAsync(); else await rig.Pipeline.InterruptAsync();
            vad.Emit("old", "stale");
            Assert.That(updates.Select(item => item.Kind), Is.EqualTo(new[] { SpeechRecognitionUpdateKind.Partial, SpeechRecognitionUpdateKind.Canceled }));
            vad.Emit("new", "current");
            Assert.That(updates.Last().Text, Is.EqualTo("current"));
            await rig.Pipeline.DisposeAsync();
            vad.Emit("newer", "after disposal");
            Assert.That(updates.Last().Kind, Is.EqualTo(SpeechRecognitionUpdateKind.Canceled));
            await vad.DisposeAsync();
        }

        [Test]
        public async NUnitTask TextTakesPriorityOverAudioAndStreamsOneTerminalWithIdentifiers()
        {
            var rig = Create();
            var request = Request("written", "turn"); request.AudioData = new byte[] { 1, 2 };
            request.BlockBargeIn = true;
            var result = await rig.Pipeline.InvokeAsync(request);
            Assert.That(rig.Stt.Calls, Is.Empty);
            Assert.That(rig.Llm.Calls.Single().Text, Is.EqualTo("written"));
            Assert.That(Types(rig, "turn"), Is.EqualTo(new[] { SpeechPipelineResponseType.Accepted,
                SpeechPipelineResponseType.Start, SpeechPipelineResponseType.Chunk, SpeechPipelineResponseType.Final }));
            Assert.That(result.Type, Is.EqualTo(SpeechPipelineResponseType.Final));
            Assert.That(result.Text, Is.EqualTo("answer。"));
            Assert.That(result.VoiceText, Is.EqualTo("answer。"));
            Assert.That(For(rig, "turn").All(response => response.SessionId == "session"), Is.True);
            Assert.That(For(rig, "turn").Single(response => response.Type == SpeechPipelineResponseType.Start).ContextId, Is.EqualTo("context"));
            Assert.That((bool)For(rig, "turn")[0].Metadata["block_barge_in"], Is.True);
            Assert.That(rig.Tts.Calls.Count, Is.EqualTo(1), "The LLM's IsFinal control event must not trigger TTS.");
            Assert.That(request.SessionId, Is.Null, "The caller's request is not pipeline state.");
        }

        [Test]
        public async NUnitTask AudioIsRecognizedBeforeLlmAndStartContainsRecognizedText()
        {
            var rig = Create();
            rig.Stt.Handler = (session, audio, token) => UniTask.FromResult(new SpeechRecognitionResult { Text = "heard" });
            var request = Request(null, "audio"); request.AudioData = new byte[] { 3, 4 }; request.AudioDuration = 1.25;
            await rig.Pipeline.InvokeAsync(request);
            Assert.That(rig.Stt.Calls.Single().SessionId, Is.EqualTo("session"));
            Assert.That(rig.Stt.Calls.Single().Audio, Is.EqualTo(new byte[] { 3, 4 }));
            Assert.That(rig.Llm.Calls.Single().Text, Is.EqualTo("heard"));
            Assert.That((string)For(rig, "audio").Single(response => response.Type == SpeechPipelineResponseType.Start).Metadata["recognized_text"], Is.EqualTo("heard"));
            Assert.That(rig.Performance.Records.Single().VoiceLength, Is.EqualTo(1.25));
        }

        [Test]
        public async NUnitTask UnrecognizedAudioCancelsWithoutLlmOrTts()
        {
            var rig = Create();
            rig.Stt.Handler = (session, audio, token) => UniTask.FromResult(new SpeechRecognitionResult());
            var request = Request(null, "empty"); request.AudioData = new byte[] { 1, 0 };
            var result = await rig.Pipeline.InvokeAsync(request);
            Assert.That(result.Type, Is.EqualTo(SpeechPipelineResponseType.Canceled));
            Assert.That(Types(rig, "empty"), Is.EqualTo(new[] { SpeechPipelineResponseType.Accepted, SpeechPipelineResponseType.Canceled }));
            Assert.That(rig.Llm.Calls, Is.Empty); Assert.That(rig.Tts.Calls, Is.Empty);
        }

        [Test]
        public async NUnitTask ValidationSeesRecognizedTextAndCanCancelWithReason()
        {
            string seen = null;
            var rig = Create(new SpeechPipelineOptions { ValidateRequestAsync = (request, token) => { seen = request.Text; return UniTask.FromResult("too short"); } });
            var request = Request(null, "invalid"); request.AudioData = new byte[] { 1, 0 };
            var result = await rig.Pipeline.InvokeAsync(request);
            Assert.That(seen, Is.EqualTo("recognized"));
            Assert.That(result.Type, Is.EqualTo(SpeechPipelineResponseType.Canceled));
            Assert.That((string)result.Metadata["reason"], Is.EqualTo("too short"));
            Assert.That(rig.Llm.Calls, Is.Empty);
        }

        [Test]
        public async NUnitTask ImageOnlyInputAndSystemPromptParametersReachLlm()
        {
            var rig = Create();
            var request = Request(null); request.ImageUrls = new[] { "https://example.com/picture.png" };
            request.SystemPromptParameters = new JObject { ["name"] = "Ada" };
            request.LlmParameters = new JObject { ["temperature"] = 0.2 };
            var result = await rig.Pipeline.InvokeAsync(request);
            Assert.That(result.Type, Is.EqualTo(SpeechPipelineResponseType.Final));
            var call = rig.Llm.Calls.Single();
            Assert.That(call.ImageUrls, Is.EqualTo(request.ImageUrls));
            Assert.That((string)call.SystemPromptParameters["name"], Is.EqualTo("Ada"));
            Assert.That((double)call.Parameters["temperature"], Is.EqualTo(0.2));
            Assert.That(call.SessionId, Is.EqualTo("session")); Assert.That(call.UserId, Is.EqualTo("user"));
        }

        [Test]
        public async NUnitTask WakewordOpensConversationUntilTimeoutAndDoesNotStripTheWord()
        {
            var rig = Create(new SpeechPipelineOptions { Wakewords = new[] { "robot" }, WakewordTimeoutSeconds = 60 });
            Assert.That((await rig.Pipeline.InvokeAsync(Request("unaddressed"))).Type, Is.EqualTo(SpeechPipelineResponseType.Canceled));
            Assert.That(rig.Llm.Calls, Is.Empty);
            await rig.Pipeline.InvokeAsync(Request("hello robot"));
            rig.Clock.Advance(59);
            await rig.Pipeline.InvokeAsync(Request("follow up"));
            rig.Clock.Advance(61);
            Assert.That((await rig.Pipeline.InvokeAsync(Request("asleep again"))).Type, Is.EqualTo(SpeechPipelineResponseType.Canceled));
            Assert.That(rig.Llm.Calls.Select(call => call.Text), Is.EqualTo(new[] { "hello robot", "follow up" }));
        }

        [Test]
        public async NUnitTask LanguagePersistsAcrossChunksAndStyleParserFeedsTts()
        {
            var parsed = new List<string>();
            var rig = Create(new SpeechPipelineOptions { ProcessLlmChunkAsync = (request, chunk, token) =>
            { parsed.Add(chunk.Text); return UniTask.FromResult(new JObject { ["mood"] = "happy" }); } });
            rig.Llm.Handler = async (request, emit, token) =>
            {
                await emit(new LlmResponse { Text = "[lang:ja]こんにちは。", VoiceText = "こんにちは。" });
                await emit(new LlmResponse { Text = "続き。", VoiceText = "続き。" });
                await emit(new LlmResponse { Text = "<language code='en'>Hello.", VoiceText = "Hello." });
                return Completed(request, "all");
            };
            await rig.Pipeline.InvokeAsync(Request());
            Assert.That(rig.Tts.Calls.Select(call => call.Language), Is.EqualTo(new[] { "ja", "ja", "en" }));
            Assert.That(rig.Tts.Calls.Select(call => (string)call.StyleInfo["info"]["mood"]), Is.All.EqualTo("happy"));
            Assert.That((string)rig.Tts.Calls.First().StyleInfo["styled_text"], Is.EqualTo(parsed[0]));
            Assert.That(rig.Responses.Where(response => response.Type == SpeechPipelineResponseType.Chunk).Select(response => response.Language), Is.EqualTo(new[] { "ja", "ja", "en" }));
        }

        [Test]
        public async NUnitTask HooksAreOrderedAndBeforeTtsRunsOnceAtFirstVoiceChunk()
        {
            var order = new List<string>();
            var options = new SpeechPipelineOptions
            {
                OnAcceptedAsync = (request, token) => { order.Add("accepted-hook"); request.Text = "accepted input"; return UniTask.CompletedTask; },
                ValidateRequestAsync = (request, token) => { order.Add("validate:" + request.Text); return UniTask.FromResult<string>(null); },
                BeforeLlmAsync = (request, token) => { order.Add("before-llm"); request.Text = "llm input"; return UniTask.CompletedTask; },
                BeforeTtsAsync = (request, token) => { order.Add("before-tts"); return UniTask.CompletedTask; },
                OnFinishAsync = (request, response, token) => { order.Add("finish"); response.Metadata = new JObject { ["finished"] = true }; return UniTask.CompletedTask; }
            };
            var rig = Create(options);
            rig.Pipeline.ResponseReceived += response => { order.Add(response.Type.ToString()); return UniTask.CompletedTask; };
            rig.Llm.Handler = async (request, emit, token) =>
            {
                order.Add("llm:" + request.Text);
                await emit(new LlmResponse { Text = "[face:joy]" });
                await emit(new LlmResponse { Text = "one。", VoiceText = "one。" });
                await emit(new LlmResponse { Text = "two。", VoiceText = "two。" });
                return Completed(request, "one。two。");
            };
            rig.Tts.Handler = (request, token) => { order.Add("tts:" + request.Text); return UniTask.FromResult(new byte[] { 1 }); };
            var result = await rig.Pipeline.InvokeAsync(Request());
            Assert.That(order.IndexOf("Accepted"), Is.LessThan(order.IndexOf("accepted-hook")));
            Assert.That(order.IndexOf("validate:accepted input"), Is.LessThan(order.IndexOf("Start")));
            Assert.That(order.IndexOf("Start"), Is.LessThan(order.IndexOf("before-llm")));
            Assert.That(order.IndexOf("before-llm"), Is.LessThan(order.IndexOf("llm:llm input")));
            Assert.That(order.Count(item => item == "before-tts"), Is.EqualTo(1));
            Assert.That(order.IndexOf("before-tts"), Is.LessThan(order.IndexOf("tts:one。")));
            Assert.That(order.IndexOf("finish"), Is.LessThan(order.IndexOf("Final")));
            Assert.That((bool)result.Metadata["finished"], Is.True);
        }

        [Test]
        public async NUnitTask SkipTtsKeepsTextAndVoiceTextWithoutSynthesizing()
        {
            var rig = Create(); var request = Request(); request.SkipTts = true;
            await rig.Pipeline.InvokeAsync(request);
            Assert.That(rig.Tts.Calls, Is.Empty);
            var chunk = rig.Responses.Single(response => response.Type == SpeechPipelineResponseType.Chunk);
            Assert.That(chunk.Text, Is.EqualTo("answer。")); Assert.That(chunk.VoiceText, Is.EqualTo("answer。"));
            Assert.That(chunk.AudioData, Is.Null);
        }

        [Test]
        public async NUnitTask ToolNotificationsKeepStructuredContentAndDoNotCountAsFirstTextChunk()
        {
            var rig = Create();
            rig.Llm.Handler = async (request, emit, token) =>
            {
                await emit(new LlmResponse { ToolCall = new LlmToolCall { Id = "call", Name = "lookup", Arguments = "{}" }, StructuredContent = new JObject { ["card"] = "result" } });
                await emit(new LlmResponse { Text = "answer。", VoiceText = "answer。", GuardrailName = "guard", StructuredContent = new JObject { ["value"] = 42 } });
                return Completed(request, "answer。");
            };
            await rig.Pipeline.InvokeAsync(Request("question", "tool"));
            var tool = For(rig, "tool").Single(response => response.Type == SpeechPipelineResponseType.ToolCall);
            Assert.That(tool.ToolCall.Name, Is.EqualTo("lookup")); Assert.That((string)tool.StructuredContent["card"], Is.EqualTo("result"));
            var chunk = For(rig, "tool").Single(response => response.Type == SpeechPipelineResponseType.Chunk);
            Assert.That((bool)chunk.Metadata["is_first_chunk"], Is.True);
            Assert.That((bool)chunk.Metadata["is_guardrail_triggered"], Is.True);
            Assert.That((int)chunk.StructuredContent["value"], Is.EqualTo(42)); Assert.That(rig.Tts.Calls.Count, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask LlmRecoveryControlDoesNotProduceAnEmptySpeechChunk()
        {
            var rig = Create();
            rig.Llm.Handler = async (request, emit, token) =>
            {
                await emit(new LlmResponse { IsRecovery = true });
                return await rig.Llm.Default(request, emit, token);
            };
            await rig.Pipeline.InvokeAsync(Request());
            var chunk = rig.Responses.Single(response => response.Type == SpeechPipelineResponseType.Chunk);
            Assert.That(chunk.Text, Is.EqualTo("answer。"));
            Assert.That((bool)chunk.Metadata["is_first_chunk"], Is.True);
            Assert.That(rig.Tts.Calls.Count, Is.EqualTo(1));
        }

        [TestCase("stt")]
        [TestCase("llm")]
        [TestCase("tts")]
        [TestCase("hook")]
        public async NUnitTask ComponentFailuresProduceOneErrorWithOriginalTransaction(string component)
        {
            var failure = new InvalidOperationException("test failure");
            var rig = Create(component == "hook" ? new SpeechPipelineOptions { BeforeLlmAsync = (request, token) => throw failure } : null);
            if (component == "stt") rig.Stt.Handler = (session, audio, token) => throw failure;
            if (component == "llm") rig.Llm.Handler = (request, emit, token) => throw failure;
            if (component == "tts") rig.Tts.Handler = (request, token) => throw failure;
            var input = Request(component == "stt" ? null : "hello", "broken"); input.AudioData = new byte[] { 1, 0 };
            var result = await rig.Pipeline.InvokeAsync(input);
            Assert.That(result.Type, Is.EqualTo(SpeechPipelineResponseType.Error)); Assert.That(result.TransactionId, Is.EqualTo("broken"));
            Assert.That(For(rig, "broken").Count(response => response.IsTerminal), Is.EqualTo(1));
            Assert.That(rig.Errors, Does.Contain(failure));
        }

        [Test]
        public async NUnitTask LlmErrorControlIsAnErrorAndDoesNotSynthesizeOrCommitHistory()
        {
            var rig = Create();
            rig.Llm.Handler = async (request, emit, token) =>
            {
                var error = new LlmError { Code = "provider_error", Message = "failed" };
                await emit(new LlmResponse { IsFinal = true, Error = error });
                return new LlmResult { ContextId = request.ContextId, Error = error };
            };
            Assert.That((await rig.Pipeline.InvokeAsync(Request("failed"))).Type, Is.EqualTo(SpeechPipelineResponseType.Error));
            Assert.That(rig.Tts.Calls, Is.Empty);
            rig.Llm.Handler = null;
            await rig.Pipeline.InvokeAsync(Request("retry"));
            Assert.That(rig.Llm.Calls.Last().History, Is.Empty);
        }

        [Test]
        public async NUnitTask ConversationHistoryAutomaticallyContinuesAndResetStartsFresh()
        {
            var rig = Create();
            await rig.Pipeline.InvokeAsync(Request("first"));
            await rig.Pipeline.InvokeAsync(Request("second"));
            Assert.That(rig.Llm.Calls.Last().History.Count, Is.EqualTo(2));
            Assert.That((string)rig.Llm.Calls.Last().History[0]["content"], Is.EqualTo("first"));
            await rig.Pipeline.ResetAsync("next-context");
            await rig.Pipeline.InvokeAsync(Request("third"));
            Assert.That(rig.Llm.Calls.Last().History, Is.Empty);
            Assert.That(rig.Llm.Calls.Last().ContextId, Is.EqualTo("next-context"));
            Assert.That(rig.Pipeline.GetState().ContextId, Is.EqualTo("next-context"));
        }

        [Test]
        public async NUnitTask WaitInQueueDefersSttUntilEarlierTurnCompletes()
        {
            var entered = Signal(); var release = ReleaseGate(); var rig = Create();
            rig.Llm.Handler = async (request, emit, token) =>
            {
                if (request.Text == "first") { entered.TrySetResult(true); await release.Task; }
                return await rig.Llm.Default(request, emit, token);
            };
            var first = rig.Pipeline.InvokeAsync(Request("first", "first")); await Within(entered.Task);
            var input = Request(null, "queued"); input.AudioData = new byte[] { 1, 0 }; input.WaitInQueue = true;
            var queued = rig.Pipeline.InvokeAsync(input);
            Assert.That(rig.Stt.Calls, Is.Empty); Assert.That(queued.Status.IsCompleted(), Is.False);
            release.TrySetResult(true);
            await Within(UniTask.WhenAll(first, queued));
            Assert.That(rig.Llm.Calls.Select(call => call.Text), Is.EqualTo(new[] { "first", "recognized" }));
            Assert.That(rig.Llm.Calls.Last().History.Count, Is.EqualTo(2));
        }

        [Test]
        public async NUnitTask NewValidTurnCancelsActiveAndQueuedTurnsBeforeItsLlmStarts()
        {
            var entered = Signal(); var stopped = Signal(); var rig = Create();
            rig.Llm.Handler = async (request, emit, token) =>
            {
                if (request.Text == "first")
                {
                    entered.TrySetResult(true);
                    try { await WaitForCancellation(token); } finally { stopped.TrySetResult(true); }
                }
                Assert.That(stopped.Task.Status.IsCompleted(), Is.True);
                return await rig.Llm.Default(request, emit, token);
            };
            var first = rig.Pipeline.InvokeAsync(Request("first", "first")); await Within(entered.Task);
            var input = Request("queued", "queued"); input.WaitInQueue = true;
            var queued = rig.Pipeline.InvokeAsync(input);
            var newest = rig.Pipeline.InvokeAsync(Request("newest", "newest"));
            Assert.That((await Within(first)).Type, Is.EqualTo(SpeechPipelineResponseType.Canceled));
            Assert.That((await Within(queued)).Type, Is.EqualTo(SpeechPipelineResponseType.Canceled));
            Assert.That((await Within(newest)).Type, Is.EqualTo(SpeechPipelineResponseType.Final));
            Assert.That(rig.Llm.Calls.Select(call => call.Text), Is.EqualTo(new[] { "first", "newest" }));
            Assert.That(rig.Llm.Calls.Last().History, Is.Empty);
            Assert.That(For(rig, "first").Count(response => response.IsTerminal), Is.EqualTo(1));
            var events = rig.Responses.ToArray();
            Assert.That(Array.FindIndex(events, response => response.Type == SpeechPipelineResponseType.Stop && response.TransactionId == "first"),
                Is.LessThan(Array.FindIndex(events, response => response.Type == SpeechPipelineResponseType.Start && response.TransactionId == "newest")));
        }

        [TestCase("silence")]
        [TestCase("invalid")]
        [TestCase("asleep")]
        public async NUnitTask RejectedNewRequestDoesNotInterruptActiveTurn(string reason)
        {
            var entered = Signal(); var release = ReleaseGate();
            var options = new SpeechPipelineOptions();
            if (reason == "invalid") options.ValidateRequestAsync = (request, token) => UniTask.FromResult(request.Text == "bad" ? "invalid" : null);
            if (reason == "asleep") options.Wakewords = new[] { "robot" };
            var rig = Create(options); var firstToken = CancellationToken.None;
            rig.Llm.Handler = async (request, emit, token) =>
            { firstToken = token; entered.TrySetResult(true); await release.Task; return await rig.Llm.Default(request, emit, token); };
            var first = rig.Pipeline.InvokeAsync(Request("robot first", "first")); await Within(entered.Task);
            var stopCount = rig.Responses.Count(response => response.Type == SpeechPipelineResponseType.Stop);
            var input = Request("bad", "rejected");
            if (reason == "silence") { input.Text = null; input.AudioData = new byte[] { 1, 0 }; rig.Stt.Handler = (session, audio, token) => UniTask.FromResult(new SpeechRecognitionResult()); }
            var rejected = await Within(rig.Pipeline.InvokeAsync(input));
            Assert.That(rejected.Type, Is.EqualTo(SpeechPipelineResponseType.Canceled)); Assert.That(firstToken.IsCancellationRequested, Is.False);
            Assert.That(rig.Responses.Count(response => response.Type == SpeechPipelineResponseType.Stop), Is.EqualTo(stopCount));
            release.TrySetResult(true);
            Assert.That((await Within(first)).Type, Is.EqualTo(SpeechPipelineResponseType.Final));
        }

        [Test]
        public async NUnitTask SlowOlderSttCannotStealTheActiveTransactionFromNewerInput()
        {
            var entered = Signal(); var release = ReleaseGate(); var rig = Create();
            rig.Stt.Handler = async (session, audio, token) =>
            { entered.TrySetResult(true); await release.Task; return new SpeechRecognitionResult { Text = "obsolete" }; };
            var input = Request(null, "older"); input.AudioData = new byte[] { 1, 0 };
            var older = rig.Pipeline.InvokeAsync(input); await Within(entered.Task);
            var newer = rig.Pipeline.InvokeAsync(Request("newer", "newer"));
            release.TrySetResult(true);
            Assert.That((await Within(older)).Type, Is.EqualTo(SpeechPipelineResponseType.Canceled));
            Assert.That((await Within(newer)).Type, Is.EqualTo(SpeechPipelineResponseType.Final));
            Assert.That(rig.Llm.Calls.Select(call => call.Text), Is.EqualTo(new[] { "newer" }));
            Assert.That(Types(rig, "older").Contains(SpeechPipelineResponseType.Start), Is.False);
        }

        [Test]
        public async NUnitTask SupersededTtsBytesAreDiscardedEvenWhenProviderIgnoresCancellation()
        {
            var entered = Signal(); var release = ReleaseGate(); var rig = Create(); var calls = 0;
            rig.Tts.Handler = async (request, token) =>
            { if (Interlocked.Increment(ref calls) == 1) { entered.TrySetResult(true); await release.Task; } return new byte[] { 9, 8 }; };
            var older = rig.Pipeline.InvokeAsync(Request("older", "older")); await Within(entered.Task);
            var newer = rig.Pipeline.InvokeAsync(Request("newer", "newer"));
            release.TrySetResult(true);
            Assert.That((await Within(older)).Type, Is.EqualTo(SpeechPipelineResponseType.Canceled));
            Assert.That((await Within(newer)).Type, Is.EqualTo(SpeechPipelineResponseType.Final));
            Assert.That(Types(rig, "older").Contains(SpeechPipelineResponseType.Chunk), Is.False);
            Assert.That(rig.Llm.Calls.Last().History, Is.Empty);
        }

        [Test]
        public async NUnitTask CallerCancellationEmitsCanceledAndThrowsWithoutCommittingPartialHistory()
        {
            var entered = Signal(); var rig = Create();
            rig.Llm.Handler = async (request, emit, token) =>
            { await emit(new LlmResponse { Text = "partial。", VoiceText = "partial。" }); entered.TrySetResult(true); await WaitForCancellation(token); return Completed(request, "partial。"); };
            using (var cancel = new CancellationTokenSource())
            {
                var running = rig.Pipeline.InvokeAsync(Request("cancel", "cancel"), cancel.Token); await Within(entered.Task);
                cancel.Cancel();
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await Within(running));
            }
            Assert.That(For(rig, "cancel").Count(response => response.IsTerminal), Is.EqualTo(1));
            Assert.That(Types(rig, "cancel").Last(), Is.EqualTo(SpeechPipelineResponseType.Canceled));
            rig.Llm.Handler = null; await rig.Pipeline.InvokeAsync(Request("next"));
            Assert.That(rig.Llm.Calls.Last().History, Is.Empty);
        }

        [Test]
        public async NUnitTask CallerCancellationWhileFinalWaitsForDeliveryStillCancelsTheReturnedTask()
        {
            // Synchronous continuations deliberately park the completed first turn at the delivery lock
            // before cancellation; no scheduler delay is used to guess whether finalization has started.
            var finish = new SpeechCompletionSource<bool>(); releases.Add(finish);
            var finishing = Signal(); var accepted = Signal(); var releaseAccepted = ReleaseGate();
            var rig = Create(new SpeechPipelineOptions { OnFinishAsync = (request, response, token) =>
            {
                if (request.TransactionId != "first") return UniTask.CompletedTask;
                finishing.TrySetResult(true); return finish.Task;
            } });
            rig.Pipeline.ResponseReceived += response =>
            {
                if (response.Type != SpeechPipelineResponseType.Accepted || response.TransactionId != "second") return UniTask.CompletedTask;
                accepted.TrySetResult(true); return releaseAccepted.Task;
            };
            using (var cancel = new CancellationTokenSource())
            {
                var first = rig.Pipeline.InvokeAsync(Request("first", "first"), cancel.Token); await Within(finishing.Task);
                var second = rig.Pipeline.InvokeAsync(Request("second", "second")); await Within(accepted.Task);
                finish.TrySetResult(true);
                cancel.Cancel();
                releaseAccepted.TrySetResult(true);
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await Within(first));
                await Within(second);
            }
            Assert.That(For(rig, "first").Single(response => response.IsTerminal).Type, Is.EqualTo(SpeechPipelineResponseType.Canceled));
        }

        [Test]
        public async NUnitTask CancelingQueuedCallerNeverInvokesItsStt()
        {
            var entered = Signal(); var release = ReleaseGate(); var rig = Create();
            rig.Llm.Handler = async (request, emit, token) => { entered.TrySetResult(true); await release.Task; return await rig.Llm.Default(request, emit, token); };
            var first = rig.Pipeline.InvokeAsync(Request("first")); await Within(entered.Task);
            using (var cancel = new CancellationTokenSource())
            {
                var input = Request(null, "queued"); input.AudioData = new byte[] { 1, 0 }; input.WaitInQueue = true;
                var queued = rig.Pipeline.InvokeAsync(input, cancel.Token); cancel.Cancel();
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await Within(queued));
            }
            Assert.That(rig.Stt.Calls, Is.Empty);
            release.TrySetResult(true); await Within(first);
        }

        [Test]
        public async NUnitTask InterruptCancelsAndDrainsActiveWorkAndAllowsAnotherTurn()
        {
            var entered = Signal(); var rig = Create();
            rig.Tts.Handler = async (request, token) => { entered.TrySetResult(true); await WaitForCancellation(token); return null; };
            var running = rig.Pipeline.InvokeAsync(Request("interrupt", "active")); await Within(entered.Task);
            await Within(rig.Pipeline.InterruptAsync());
            Assert.That((await Within(running)).Type, Is.EqualTo(SpeechPipelineResponseType.Canceled));
            Assert.That(rig.Responses.Any(response => response.Type == SpeechPipelineResponseType.Stop && response.TransactionId == "active"), Is.True);
            rig.Tts.Handler = null;
            Assert.That((await rig.Pipeline.InvokeAsync(Request("next"))).Type, Is.EqualTo(SpeechPipelineResponseType.Final));
        }

        [Test]
        public async NUnitTask ResetCancelsActiveAndPendingWorkAndClearsConversation()
        {
            var entered = Signal(); var rig = Create();
            await rig.Pipeline.InvokeAsync(Request("saved"));
            rig.Llm.Handler = async (request, emit, token) => { entered.TrySetResult(true); await WaitForCancellation(token); return Completed(request, "unused"); };
            var running = rig.Pipeline.InvokeAsync(Request("running")); await Within(entered.Task);
            var input = Request("queued"); input.WaitInQueue = true; var queued = rig.Pipeline.InvokeAsync(input);
            await Within(rig.Pipeline.ResetAsync("reset-context"));
            Assert.That((await Within(running)).Type, Is.EqualTo(SpeechPipelineResponseType.Canceled));
            Assert.That((await Within(queued)).Type, Is.EqualTo(SpeechPipelineResponseType.Canceled));
            rig.Llm.Handler = null; await rig.Pipeline.InvokeAsync(Request("fresh"));
            Assert.That(rig.Llm.Calls.Last().History, Is.Empty); Assert.That(rig.Llm.Calls.Last().ContextId, Is.EqualTo("reset-context"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask DisposeCancelsQueuedAndRunningWorkAndHonorsComponentOwnership(bool owns)
        {
            var entered = Signal(); var rig = Create(ownsComponents: owns);
            rig.Llm.Handler = async (request, emit, token) => { entered.TrySetResult(true); await WaitForCancellation(token); return Completed(request, "unused"); };
            var running = rig.Pipeline.InvokeAsync(Request("running")); await Within(entered.Task);
            var input = Request("queued"); input.WaitInQueue = true; var queued = rig.Pipeline.InvokeAsync(input);
            await Within(rig.Pipeline.DisposeAsync()); await rig.Pipeline.DisposeAsync();
            Assert.That((await Within(running)).Type, Is.EqualTo(SpeechPipelineResponseType.Canceled));
            Assert.That((await Within(queued)).Type, Is.EqualTo(SpeechPipelineResponseType.Canceled));
            Assert.That(rig.Stt.DisposeCount, Is.EqualTo(owns ? 1 : 0));
            Assert.That(rig.Llm.DisposeCount, Is.EqualTo(owns ? 1 : 0)); Assert.That(rig.Tts.DisposeCount, Is.EqualTo(owns ? 1 : 0));
            await SpeechAsyncAssert.ThrowsAsync<ObjectDisposedException>(async () => await rig.Pipeline.InvokeAsync(Request()));
        }

        [Test]
        public async NUnitTask QueuedRequestsCaptureMutableInputAndOptionsBeforeWaiting()
        {
            var entered = Signal(); var release = ReleaseGate(); var hookText = new List<string>();
            var options = new SpeechPipelineOptions { BeforeLlmAsync = (request, token) => { hookText.Add(request.Text); return UniTask.CompletedTask; } };
            var rig = Create(options);
            rig.Llm.Handler = async (request, emit, token) =>
            { if (request.Text == "first") { entered.TrySetResult(true); await release.Task; } return await rig.Llm.Default(request, emit, token); };
            var first = rig.Pipeline.InvokeAsync(Request("first")); await Within(entered.Task);
            var input = Request("snapshot"); input.WaitInQueue = true; input.ImageUrls = new[] { "https://example.com/original" };
            input.SystemPromptParameters = new JObject { ["value"] = "before" };
            var queued = rig.Pipeline.InvokeAsync(input);
            input.Text = "mutated"; input.ImageUrls[0] = "mutated"; input.SystemPromptParameters["value"] = "after";
            options.BeforeLlmAsync = (request, token) => throw new InvalidOperationException("external options were retained");
            var replacement = rig.Pipeline.GetOptions();
            replacement.BeforeLlmAsync = (request, token) => { hookText.Add("new:" + request.Text); return UniTask.CompletedTask; };
            rig.Pipeline.UpdateOptions(replacement);
            release.TrySetResult(true); await Within(UniTask.WhenAll(first, queued));
            Assert.That(rig.Llm.Calls.Last().Text, Is.EqualTo("snapshot"));
            Assert.That(rig.Llm.Calls.Last().ImageUrls, Is.EqualTo(new[] { "https://example.com/original" }));
            Assert.That((string)rig.Llm.Calls.Last().SystemPromptParameters["value"], Is.EqualTo("before"));
            Assert.That(hookText, Is.EqualTo(new[] { "first", "snapshot" }));
            await rig.Pipeline.InvokeAsync(Request("third"));
            Assert.That(hookText.Last(), Is.EqualTo("new:third"));
        }

        [Test]
        public async NUnitTask ResponseCallbackFailureAbortsTheTurnBeforeHistoryCommit()
        {
            var rig = Create(); var failure = new InvalidOperationException("presentation failed");
            Func<SpeechPipelineResponse, UniTask> failingHandler = response =>
                response.Type == SpeechPipelineResponseType.Chunk ? throw failure : UniTask.CompletedTask;
            rig.Pipeline.ResponseReceived += failingHandler;
            var result = await rig.Pipeline.InvokeAsync(Request("failed", "failed"));
            Assert.That(result.Type, Is.EqualTo(SpeechPipelineResponseType.Error));
            Assert.That(For(rig, "failed").Count(response => response.IsTerminal), Is.EqualTo(1));
            Assert.That(rig.Errors, Does.Contain(failure));
            rig.Pipeline.ResponseReceived -= failingHandler;
            await rig.Pipeline.InvokeAsync(Request("retry"));
            Assert.That(rig.Llm.Calls.Last().History, Is.Empty);
        }

        [Test]
        public async NUnitTask ResponseCallbacksNeverOverlapAcrossConcurrentInvocations()
        {
            var rig = Create(); var entered = Signal(); var release = ReleaseGate(); var concurrent = 0; var maximum = 0;
            rig.Pipeline.ResponseReceived += async response =>
            {
                var count = Interlocked.Increment(ref concurrent);
                maximum = Math.Max(maximum, count);
                try
                {
                    if (response.Type == SpeechPipelineResponseType.Accepted && response.TransactionId == "first")
                    { entered.TrySetResult(true); await release.Task; }
                }
                finally { Interlocked.Decrement(ref concurrent); }
            };
            var first = rig.Pipeline.InvokeAsync(Request("first", "first")); await Within(entered.Task);
            var second = rig.Pipeline.InvokeAsync(Request("second", "second"));
            release.TrySetResult(true);
            await Within(UniTask.WhenAll(first, second));
            Assert.That(maximum, Is.EqualTo(1));
            Assert.That(Types(rig, "second").Last(), Is.EqualTo(SpeechPipelineResponseType.Final));
        }

        [TestCase("capacity")]
        [TestCase("transaction")]
        [TestCase("session")]
        public async NUnitTask InvalidConcurrentSubmissionDoesNotDisturbTheActiveRequest(string reason)
        {
            var rig = Create(new SpeechPipelineOptions { MaxPendingRequests = reason == "capacity" ? 1 : 16 });
            var entered = Signal(); var release = ReleaseGate(); var currentToken = CancellationToken.None;
            rig.Llm.Handler = async (request, emit, token) =>
            { currentToken = token; entered.TrySetResult(true); await release.Task; return await rig.Llm.Default(request, emit, token); };
            var first = rig.Pipeline.InvokeAsync(Request("first", "first")); await Within(entered.Task);
            var input = Request("invalid", reason == "transaction" ? "first" : "second");
            if (reason == "session") input.SessionId = "other";
            if (reason == "capacity") await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(async () => await rig.Pipeline.InvokeAsync(input));
            else await SpeechAsyncAssert.ThrowsAsync<ArgumentException>(async () => await rig.Pipeline.InvokeAsync(input));
            Assert.That(currentToken.IsCancellationRequested, Is.False);
            Assert.That(rig.Llm.Calls.Count, Is.EqualTo(1));
            release.TrySetResult(true); Assert.That((await Within(first)).Type, Is.EqualTo(SpeechPipelineResponseType.Final));
        }

        [Test]
        public async NUnitTask ResponseSubscribersReceiveIndependentCopiesAndReturnedFinalIsIndependent()
        {
            var rig = Create(); var later = new List<SpeechPipelineResponse>();
            rig.Pipeline.ResponseReceived += response =>
            { response.Text = "mutated"; if (response.AudioData != null) response.AudioData[0] = 99; if (response.Metadata != null) response.Metadata["changed"] = true; return UniTask.CompletedTask; };
            rig.Pipeline.ResponseReceived += response => { later.Add(response); return UniTask.CompletedTask; };
            var result = await rig.Pipeline.InvokeAsync(Request());
            Assert.That(result.Text, Is.EqualTo("answer。"));
            Assert.That(later.Single(response => response.Type == SpeechPipelineResponseType.Final).Text, Is.EqualTo("answer。"));
            Assert.That(later.Single(response => response.Type == SpeechPipelineResponseType.Chunk).AudioData[0], Is.EqualTo(1));
            Assert.That(later.Where(response => response.Metadata != null).All(response => response.Metadata["changed"] == null), Is.True);
        }

        [TestCase("callback")]
        [TestCase("hook")]
        public async NUnitTask ReentrantControlsAreRejectedWithoutDeadlocking(string location)
        {
            Rig rig = null; var checkedCount = 0;
            Func<UniTask> check = async () =>
            {
                await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(async () => await rig.Pipeline.InvokeAsync(Request()));
                await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(async () => await rig.Pipeline.InterruptAsync());
                await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(async () => await rig.Pipeline.ResetAsync());
                await SpeechAsyncAssert.ThrowsAsync<InvalidOperationException>(async () => await rig.Pipeline.DisposeAsync());
                checkedCount++;
            };
            rig = Create(location == "hook" ? new SpeechPipelineOptions { BeforeLlmAsync = (request, token) => check() } : null);
            if (location == "callback") rig.Pipeline.ResponseReceived += response => response.Type == SpeechPipelineResponseType.Start ? check() : UniTask.CompletedTask;
            Assert.That((await Within(rig.Pipeline.InvokeAsync(Request()))).Type, Is.EqualTo(SpeechPipelineResponseType.Final));
            Assert.That(checkedCount, Is.EqualTo(1));
        }

        [Test]
        public async NUnitTask MergeAndTimestampUseTheInjectedClockAndCanBeDisabledPerRequest()
        {
            var rig = Create(new SpeechPipelineOptions { MergeRequestThresholdSeconds = 5, MergeRequestPrefix = "merged:", TimestampIntervalSeconds = 10, TimestampPrefix = "time:" });
            await rig.Pipeline.InvokeAsync(Request("one"));
            rig.Clock.Advance(2); await rig.Pipeline.InvokeAsync(Request("two"));
            var third = Request("three"); third.AllowMerge = false; rig.Clock.Advance(11); await rig.Pipeline.InvokeAsync(third);
            var calls = rig.Llm.Calls.ToArray();
            Assert.That(calls[0].Text, Does.StartWith("time:2026/09/05 00:00:00"));
            Assert.That(calls[1].Text, Is.EqualTo("merged:one\ntwo"));
            Assert.That(calls[2].Text, Does.StartWith("time:2026/09/05 00:00:13"));
            Assert.That(calls[2].Text, Does.EndWith("three")); Assert.That(calls[2].Text, Does.Not.Contain("merged:"));
        }

        [Test]
        public async NUnitTask OptionsAreCopiedAndRejectSessionChanges()
        {
            var rig = Create(new SpeechPipelineOptions { Wakewords = new[] { "robot" } });
            var returned = rig.Pipeline.GetOptions(); returned.Wakewords[0] = "changed";
            Assert.That(rig.Pipeline.GetOptions().Wakewords, Is.EqualTo(new[] { "robot" }));
            var replacement = rig.Pipeline.GetOptions(); replacement.Wakewords = Array.Empty<string>();
            rig.Pipeline.UpdateOptions(replacement); replacement.Wakewords = new[] { "external" };
            Assert.That((await rig.Pipeline.InvokeAsync(Request("no wakeword"))).Type, Is.EqualTo(SpeechPipelineResponseType.Final));
            replacement = rig.Pipeline.GetOptions(); replacement.SessionId = "other";
            Assert.Throws<ArgumentException>(() => rig.Pipeline.UpdateOptions(replacement));
        }

        private static LlmResult Completed(LlmRequest request, string text) => new LlmResult
        {
            ContextId = request.ContextId, Text = text,
            InputItems = new JArray(new JObject { ["role"] = "user", ["content"] = request.Text ?? "" }),
            OutputItems = new JArray(new JObject { ["role"] = "assistant", ["content"] = text })
        };

        private sealed class RecognizingDetector : SpeechDetectorBase, ISpeechRecognitionSource
        {
            public event Action<SpeechRecognitionUpdate> RecognitionUpdated;
            public RecognizingDetector() : base(new SpeechDetectorOptions()) { }
            public void Emit(string id, string text, SpeechRecognitionUpdateKind kind = SpeechRecognitionUpdateKind.Partial,
                bool? active = null, double? duration = null, double observedAt = 0)
                => RecognitionUpdated?.Invoke(new SpeechRecognitionUpdate
                {
                    RecognitionId = id, SessionId = "session", Text = text, Kind = kind,
                    IsSpeechActive = active, AudioDurationSeconds = duration, ObservedAtSeconds = observedAt
                });
            public void Final(string id, string text)
                => PublishSpeechDetected(new SpeechDetectionResult(null, text, null, 0.5, "session", id));
            protected override RecordingSession CreateSession(string sessionId) => new RecordingSession(sessionId);
            protected override UniTask<bool> ProcessSamplesCoreAsync(byte[] samples, RecordingSession session, CancellationToken token)
                => UniTask.FromResult(false);
        }

        private sealed class Rig
        {
            public SpeechToSpeechPipeline Pipeline;
            public readonly FakeStt Stt = new FakeStt();
            public readonly FakeLlm Llm = new FakeLlm();
            public readonly FakeTts Tts = new FakeTts();
            public readonly FakeClock Clock = new FakeClock();
            public readonly FakePerformance Performance = new FakePerformance();
            public readonly ConcurrentQueue<SpeechPipelineResponse> Responses = new ConcurrentQueue<SpeechPipelineResponse>();
            public readonly ConcurrentQueue<Exception> Errors = new ConcurrentQueue<Exception>();
        }
        private sealed class SttCall { public string SessionId; public byte[] Audio; }
        private sealed class FakeStt : ISpeechRecognizer, IDisposable
        {
            public readonly ConcurrentQueue<SttCall> Calls = new ConcurrentQueue<SttCall>();
            public Func<string, byte[], CancellationToken, UniTask<SpeechRecognitionResult>> Handler;
            public int DisposeCount;
            public UniTask<SpeechRecognitionResult> RecognizeAsync(string sessionId, byte[] audio, CancellationToken cancellationToken = default)
            {
                Calls.Enqueue(new SttCall { SessionId = sessionId, Audio = (byte[])audio.Clone() });
                return Handler == null ? UniTask.FromResult(new SpeechRecognitionResult { Text = "recognized" }) : Handler(sessionId, audio, cancellationToken);
            }
            public void Dispose() { DisposeCount++; }
        }
        private sealed class FakeLlm : ILlmService
        {
            public readonly ConcurrentQueue<LlmRequest> Calls = new ConcurrentQueue<LlmRequest>();
            public Func<LlmRequest, Func<LlmResponse, UniTask>, CancellationToken, UniTask<LlmResult>> Handler;
            public int DisposeCount;
            public UniTask<LlmResult> ChatAsync(LlmRequest request, Func<LlmResponse, UniTask> onResponse = null, CancellationToken cancellationToken = default)
            { Calls.Enqueue(request.Copy()); return Handler == null ? Default(request, onResponse, cancellationToken) : Handler(request, onResponse, cancellationToken); }
            public async UniTask<LlmResult> Default(LlmRequest request, Func<LlmResponse, UniTask> emit, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (emit != null)
                {
                    await emit(new LlmResponse { ContextId = request.ContextId, Text = "answer。", VoiceText = "answer。" });
                    await emit(new LlmResponse { ContextId = request.ContextId, IsFinal = true });
                }
                token.ThrowIfCancellationRequested(); return Completed(request, "answer。");
            }
            public LlmServiceOptions GetOptions() => new LlmServiceOptions { ApiKey = "unused" };
            public void UpdateOptions(LlmServiceOptions options) { }
            public UniTask DisposeAsync() { DisposeCount++; return UniTask.CompletedTask; }
        }
        private sealed class FakeTts : ISpeechSynthesizer
        {
            public readonly ConcurrentQueue<SpeechSynthesisRequest> Calls = new ConcurrentQueue<SpeechSynthesisRequest>();
            public Func<SpeechSynthesisRequest, CancellationToken, UniTask<byte[]>> Handler;
            public int DisposeCount;
            public UniTask<byte[]> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken = default)
            { Calls.Enqueue(request.Copy()); return Handler == null ? UniTask.FromResult(new byte[] { 1, 2, 3 }) : Handler(request, cancellationToken); }
            public UniTask<byte[]> GenerateAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken = default) => throw new AssertionException("Pipeline must use the synthesis hooks and cache path.");
            public SpeechSynthesizerOptions GetOptions() => new SpeechSynthesizerOptions();
            public void UpdateOptions(SpeechSynthesizerOptions options) { }
            public UniTask DisposeAsync() { DisposeCount++; return UniTask.CompletedTask; }
        }
        private sealed class FakeClock : ISpeechPipelineClock
        {
            public DateTimeOffset UtcNow { get; private set; } = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
            public double ElapsedSeconds { get; private set; }
            public void Advance(double seconds) { UtcNow = UtcNow.AddSeconds(seconds); ElapsedSeconds += seconds; }
        }
        private sealed class FakePerformance : IPipelinePerformanceRecorder
        {
            public readonly ConcurrentQueue<PipelinePerformanceRecord> Records = new ConcurrentQueue<PipelinePerformanceRecord>();
            public UniTask RecordAsync(PipelinePerformanceRecord record, CancellationToken cancellationToken = default)
            { Records.Enqueue(record.Copy()); return UniTask.CompletedTask; }
        }
    }
}

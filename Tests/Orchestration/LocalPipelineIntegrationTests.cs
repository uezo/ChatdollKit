using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.Avatar;
using ChatdollKit.Orchestration;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.LLM;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.TTS;
using ChatdollKit.SpeechPipeline.VAD;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChatdollKit.Tests.Orchestration
{
    public sealed class LocalPipelineIntegrationTests
    {
        [Test]
        public async NUnitTask ConvertedMicrophonePassesThroughRealVadPipelineAndPresentation()
        {
            var llm = new LocalLlm();
            var avatar = new RecordingAvatar();
            var vad = new StandardSpeechDetectorEngine(new StandardSpeechDetectorOptions
            {
                SampleRate = 16000, MinDuration = 0.03, SilenceDurationThreshold = 0.04,
                VolumeDbThreshold = -40, PrerollBufferCount = 1
            });
            var wave = Pcm16Audio.WriteWave(new byte[320], 16000);
            var tts = SpeechSynthesizer.Create((request, token) => UniTask.FromResult(wave));
            var pipeline = new SpeechToSpeechPipeline(new DummySpeechRecognizer("聞こえました"), llm, tts,
                new SpeechPipelineOptions { SessionId = "microphone" }, vad,
                ownsComponents: true, historyFormat: LlmHistoryFormat.ChatCompletions);
            var core = new ChatdollOrchestratorEngine(pipeline, avatar, ownsPipeline: true);
            var input = new OrchestratorMicrophoneInput(48000, 2, 16000, 512);
            try
            {
                foreach (var pcm in input.Convert(Enumerable.Repeat(0.5f, 4800 * 2).ToArray(), 48000, false))
                    Assert.That(core.SubmitAudio(pcm), Is.True);
                foreach (var pcm in input.Convert(new float[4800 * 2], 48000, false))
                    Assert.That(core.SubmitAudio(pcm), Is.True);
                await Within(core.DrainAsync());
                Assert.That(llm.Requests.Single().Text, Is.EqualTo("聞こえました"));
                var shown = avatar.Items.Single();
                Assert.That(shown.VoiceText, Is.EqualTo("こんにちは。"));
                Assert.That(shown.AudioData, Is.EqualTo(wave));
                Assert.That(shown.Controls.Single().Name, Is.EqualTo("happy"));
                Assert.That(Pcm16Audio.ReadWave(shown.AudioData).Audio.Length / 2, Is.EqualTo(160));
            }
            finally { await Within(core.DisposeAsync()); }
        }

        [Test]
        public async NUnitTask MainStyleHookCanSubmitNextTurnWithoutPipelineCallbackReentry()
        {
            var llm = new LocalLlm();
            var tts = SpeechSynthesizer.Create((request, token) => UniTask.FromResult(Pcm16Audio.WriteWave(new byte[20], 16000)));
            var pipeline = new SpeechToSpeechPipeline(new DummySpeechRecognizer(), llm, tts,
                ownsComponents: true, historyFormat: LlmHistoryFormat.ChatCompletions);
            var avatar = new RecordingAvatar();
            var core = new ChatdollOrchestratorEngine(pipeline, avatar, ownsPipeline: true);
            var second = Signal<UniTask<SpeechPipelineResponse>>();
            core.BeforeRequestAsync = (request, token) => { request.AllowMerge = false; request.WaitInQueue = true; return UniTask.CompletedTask; };
            core.ResponseReceived += response =>
            {
                if (response.Type == SpeechPipelineResponseType.Final && response.TransactionId == "first")
                    second.TrySetResult(core.SendTextAsync("二回目"));
            };
            try
            {
                await Within(core.InvokeAsync(new SpeechPipelineRequest { Text = "一回目", TransactionId = "first" }));
                var next = await Within(second.Task);
                Assert.That((await Within(next)).Type, Is.EqualTo(SpeechPipelineResponseType.Final));
                await Within(core.DrainAsync());
                Assert.That(avatar.Items.Count, Is.EqualTo(2));
                Assert.That(llm.Requests.Select(item => item.Text), Is.EqualTo(new[] { "一回目", "二回目" }));
            }
            finally { await Within(core.DisposeAsync()); }
        }

        [Test]
        public async NUnitTask InterruptReleasesRealPipelineAndStopsPlaybackBeforeNextTurn()
        {
            var avatar = new RecordingAvatar();
            var playing = Signal<bool>();
            avatar.Handler = async (item, token) =>
            {
                if (item.TransactionId != "old") return;
                playing.TrySetResult(true);
                var canceled = Signal<bool>();
                using (token.Register(() => canceled.TrySetCanceled())) await canceled.Task;
            };
            var tts = SpeechSynthesizer.Create((request, token) => UniTask.FromResult(Pcm16Audio.WriteWave(new byte[20], 16000)));
            var pipeline = new SpeechToSpeechPipeline(new DummySpeechRecognizer(), new LocalLlm(), tts,
                ownsComponents: true, historyFormat: LlmHistoryFormat.ChatCompletions);
            var core = new ChatdollOrchestratorEngine(pipeline, avatar, ownsPipeline: true);
            try
            {
                await Within(core.InvokeAsync(new SpeechPipelineRequest { Text = "old", TransactionId = "old", BlockBargeIn = true }));
                await Within(playing.Task);
                Assert.That(core.IsInputSuppressed, Is.True, "Generation finished but audio is still active.");
                await Within(core.InterruptAsync());
                Assert.That(core.IsInputSuppressed, Is.False);
                await Within(core.SendTextAsync("new"));
                await Within(core.DrainAsync());
                Assert.That(avatar.Items.Count, Is.EqualTo(2));
            }
            finally { await Within(core.DisposeAsync()); }
        }

        private sealed class LocalLlm : ILlmService
        {
            internal readonly ConcurrentQueue<LlmRequest> Requests = new ConcurrentQueue<LlmRequest>();
            public async UniTask<LlmResult> ChatAsync(LlmRequest request, Func<LlmResponse, UniTask> onResponse = null, CancellationToken cancellationToken = default)
            {
                Requests.Enqueue(request.Copy()); cancellationToken.ThrowIfCancellationRequested();
                const string text = "[face:happy]こんにちは。";
                if (onResponse != null) await onResponse(new LlmResponse { Text = text, VoiceText = "こんにちは。" });
                return new LlmResult
                {
                    Text = text, ContextId = request.ContextId,
                    InputItems = new JArray(new JObject { ["role"] = "user", ["content"] = request.Text }),
                    OutputItems = new JArray(new JObject { ["role"] = "assistant", ["content"] = text })
                };
            }
            public LlmServiceOptions GetOptions() => new LlmServiceOptions { ApiKey = "offline" };
            public void UpdateOptions(LlmServiceOptions options) { }
            public UniTask DisposeAsync() => UniTask.CompletedTask;
        }
        private sealed class RecordingAvatar : IAvatarController
        {
            internal readonly ConcurrentQueue<AvatarRequest> Items = new ConcurrentQueue<AvatarRequest>();
            internal Func<AvatarRequest, CancellationToken, UniTask> Handler;
            public UniTask PresentAsync(AvatarRequest presentation, CancellationToken cancellationToken)
            { Items.Enqueue(presentation.Copy()); return Handler?.Invoke(presentation, cancellationToken) ?? UniTask.CompletedTask; }
            public UniTask StopAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        }
        private static SpeechCompletionSource<T> Signal<T>() => new SpeechCompletionSource<T>();
        private static async UniTask Within(UniTask task)
        { task = SpeechAsync.Share(task);
            Assert.That(await UniTask.WhenAny(task, SpeechAsync.Delay(TimeSpan.FromSeconds(5), CancellationToken.None)), Is.EqualTo(0), "Deadlock guard expired."); await task; }
        private static async UniTask<T> Within<T>(UniTask<T> task) { task = SpeechAsync.Share(task); await Within((UniTask)task); return await task; }
    }
}

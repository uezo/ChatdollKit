using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.Remote;
using ChatdollKit.SpeechPipeline.TTS;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NUnitTask = System.Threading.Tasks.Task;

namespace ChatdollKit.Tests.SpeechPipeline.LiveApi
{
    public class AIAvatarSpeechPipelineClientLiveTests
    {
        private static AIAvatarSpeechPipelineClient CreatePipeline() => new AIAvatarSpeechPipelineClient(new AIAvatarSpeechPipelineOptions
        {
            Url = Environment.GetEnvironmentVariable("AIAVATAR_TEST_URL") ?? "ws://localhost:48001/ws",
            ApiKey = Environment.GetEnvironmentVariable("AIAVATAR_TEST_API_KEY"),
            SessionId = "chatdoll-test-" + Guid.NewGuid().ToString("N")
        });

        [Test, Category("LiveApi"), Explicit("Connects to the running AIAvatarKit server and replaces its session.")]
        public async NUnitTask ConnectResetAndDispose()
        {
            var pipeline = CreatePipeline();
            try
            {
                await pipeline.ConnectAsync();
                Assert.That(pipeline.IsConnected, Is.True);
                var session = pipeline.SessionId;
                var remote = pipeline.RemoteSessionId;
                await pipeline.ResetAsync();
                Assert.That(pipeline.IsConnected, Is.True);
                Assert.That(pipeline.SessionId, Is.EqualTo(session));
                Assert.That(pipeline.RemoteSessionId, Is.Not.EqualTo(remote));
            }
            finally { await pipeline.DisposeAsync(); }
            Assert.That(pipeline.IsConnected, Is.False);
        }

        [Test, Category("LiveApi"), Explicit("Sends a configured PCM WAV utterance through the server's VAD/STT/LLM/TTS.")]
        public async NUnitTask MicrophoneAudioProducesStreamingWaveResponse()
        {
            var path = Environment.GetEnvironmentVariable("AIAVATAR_TEST_AUDIO");
            if (string.IsNullOrEmpty(path)) Assert.Ignore("Set AIAVATAR_TEST_AUDIO to a mono 16000 Hz PCM16 WAV utterance.");
            var wave = WavResampler.ReadWave(File.ReadAllBytes(path));
            Assert.That(wave.SampleRate, Is.EqualTo(16000));
            Assert.That(wave.Channels, Is.EqualTo(1));
            Assert.That(wave.Audio.Length, Is.GreaterThan(0), "The speech fixture must contain audio samples.");
            Assert.That(wave.Audio.Any(value => value != 0), Is.True, "The speech fixture must contain speech rather than silence.");
            var pipeline = CreatePipeline();
            var responses = new List<SpeechPipelineResponse>();
            var partials = new List<string>();
            var done = new SpeechCompletionSource<SpeechPipelineResponse>();
            pipeline.SpeechDetecting += partials.Add;
            pipeline.Error += error => done.TrySetException(error);
            pipeline.ResponseReceived += response =>
            {
                responses.Add(response);
                if (response.IsTerminal) done.TrySetResult(response);
                return UniTask.CompletedTask;
            };
            using (var timeout = new CancellationTokenSource())
            using (SpeechAsync.Timeout(timeout, TimeSpan.FromSeconds(120)))
            try
            {
                await pipeline.ConnectAsync(timeout.Token);
                // The live server's VAD uses elapsed time for turn-end silence.
                var pcm = new byte[wave.Audio.Length + 16000 * 2 * 3];
                Buffer.BlockCopy(wave.Audio, 0, pcm, 0, wave.Audio.Length);
                for (var offset = 0; offset < pcm.Length && done.Task.Status == UniTaskStatus.Pending; offset += 1024)
                {
                    var frame = new byte[1024];
                    Buffer.BlockCopy(pcm, offset, frame, 0, Math.Min(frame.Length, pcm.Length - offset));
                    await pipeline.ProcessAudioSamplesAsync(frame, timeout.Token);
                    await SpeechAsync.Delay(TimeSpan.FromMilliseconds(32), timeout.Token);
                }
                var final = await SpeechAsync.WaitAsync(done.Task, timeout.Token);
                Assert.That(final.Type, Is.EqualTo(SpeechPipelineResponseType.Final));
                Assert.That(responses.Any(r => r.Type == SpeechPipelineResponseType.Accepted), Is.True);
                Assert.That(responses.Any(r => r.Type == SpeechPipelineResponseType.Start), Is.True);
                Assert.That(final.ContextId, Is.Not.Null.And.Not.Empty);
                var chunks = responses.Where(r => r.Type == SpeechPipelineResponseType.Chunk).ToArray();
                Assert.That(chunks, Is.Not.Empty);
                Assert.That(chunks.Any(r => !string.IsNullOrEmpty(r.Text)), Is.True);
                var audio = chunks.Where(r => r.AudioData?.Length > 0).ToArray();
                Assert.That(audio, Is.Not.Empty);
                foreach (var chunk in audio) Assert.That(WavResampler.ReadWave(chunk.AudioData).Audio.Length, Is.GreaterThan(0));
                Assert.That(responses.Where(r => r.Type != SpeechPipelineResponseType.Stop).All(r => r.TransactionId == final.TransactionId), Is.True);
                await pipeline.DrainAsync();
                var context = pipeline.ContextId;
                await pipeline.InterruptAsync(timeout.Token);
                Assert.That(pipeline.ContextId, Is.EqualTo(context));
                TestContext.Progress.WriteLine("AIAvatarKit voice turn: " + chunks.Length + " chunks, " + audio.Length + " WAV chunks, " + partials.Count + " partial recognition events.");
            }
            finally { await pipeline.DisposeAsync(); }
        }
    }
}

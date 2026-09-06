using ChatdollKit.SpeechPipeline.VAD.Silero;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnitTask = System.Threading.Tasks.Task;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.VAD;
using ChatdollKit.SpeechPipeline.VAD.Silero.Onnx;
using ChatdollKit.Tests.SpeechPipeline.LiveApi;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline.Onnx
{
    public class SileroStreamApiLiveTests
    {
        [Test]
        [Explicit("Uses the native Silero model and sends at most one live OpenAI transcription request.")]
        [Category("LiveApi")]
        public async NUnitTask OpenAISileroStreamProducesPartialAndFinal()
        {
            if (string.IsNullOrWhiteSpace(SpeechApiTestSettings.OpenAIApiKey))
                Assert.Ignore("Configure the OpenAI API key to run this live test.");
            var wave = ReadConfiguredAudio();
            var modelPath = FindModel();
            await CheckPipelineAsync("OpenAI", "silero-live-openai", wave, modelPath, () =>
                new OpenAISpeechRecognizerClient(new OpenAISpeechRecognizerOptions
                {
                    ApiKey = SpeechApiTestSettings.OpenAIApiKey,
                    SampleRate = wave.SampleRate,
                    MaxAttempts = 1,
                    TimeoutSeconds = 30
                }));
        }

        [Test]
        [Explicit("Uses the native Silero model and sends at most one live Azure transcription request.")]
        [Category("LiveApi")]
        public async NUnitTask AzureSileroStreamProducesPartialAndFinal()
        {
            if (string.IsNullOrWhiteSpace(SpeechApiTestSettings.AzureApiKey) ||
                string.IsNullOrWhiteSpace(SpeechApiTestSettings.AzureRegion))
                Assert.Ignore("Configure the Azure Speech API key and region to run this live test.");
            var wave = ReadConfiguredAudio();
            var modelPath = FindModel();
            await CheckPipelineAsync("Azure", "silero-live-azure", wave, modelPath, () =>
                new AzureSpeechRecognizerClient(new AzureSpeechRecognizerOptions
                {
                    ApiKey = SpeechApiTestSettings.AzureApiKey,
                    Region = SpeechApiTestSettings.AzureRegion,
                    SampleRate = wave.SampleRate,
                    MaxAttempts = 1,
                    TimeoutSeconds = 30
                }));
        }

        private static async UniTask CheckPipelineAsync(string provider, string sessionId, Pcm16Wave wave,
            string modelPath, Func<HttpSpeechRecognizerBase> createRecognizer)
        {
            try { await RunPipelineAsync(provider, sessionId, wave, modelPath, createRecognizer); }
            catch (AssertionException) { throw; }
            catch (Exception exception)
            {
                // Do not write exception messages, HTTP bodies, credentials, or local settings.
                Assert.Fail(provider + " SileroStream integration failed (" + exception.GetType().Name + ").");
            }
        }

        private static async UniTask RunPipelineAsync(string provider, string sessionId, Pcm16Wave wave,
            string modelPath, Func<HttpSpeechRecognizerBase> createRecognizer)
        {
            OnnxSileroVadModel model = null;
            HttpSpeechRecognizerBase recognizer = null;
            SileroStreamSpeechDetectorEngine detector = null;
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
            {
                try
                {
                    model = new OnnxSileroVadModel(File.ReadAllBytes(modelPath));
                    recognizer = createRecognizer();
                    var chunkSamples = wave.SampleRate == 16000 ? 512 : 256;
                    var chunkBytes = chunkSamples * 2;
                    var frameDuration = (double)chunkSamples / wave.SampleRate;
                    var duration = wave.Audio.Length / (2.0 * wave.SampleRate);
                    // No internal pause in this fixture can trigger another paid request.
                    var segmentSilence = duration + 0.25;
                    var finalSilence = segmentSilence + 0.25;
                    detector = new SileroStreamSpeechDetectorEngine(model, recognizer, new SileroStreamSpeechDetectorOptions
                    {
                        SampleRate = wave.SampleRate,
                        ChunkSize = chunkSamples,
                        SegmentSilenceThreshold = segmentSilence,
                        SilenceDurationThreshold = finalSilence,
                        MaxDuration = duration + finalSilence + 2,
                        MinDuration = 0.2
                    });

                    var partials = new ConcurrentQueue<string>();
                    var finals = new ConcurrentQueue<SpeechDetectionResult>();
                    var failures = new ConcurrentQueue<Type>();
                    var recognitionCount = 0;
                    byte[] recognizedAudio = null;
                    recognizer.PreprocessAsync = (id, audio, token) =>
                    {
                        if (Interlocked.Increment(ref recognitionCount) > 1)
                            throw new InvalidOperationException("This live test permits only one transcription request.");
                        recognizedAudio = (byte[])audio.Clone();
                        return UniTask.FromResult(new SpeechPreprocessResult { Audio = audio });
                    };
                    recognizer.Error += error => failures.Enqueue(error.GetType());
                    detector.Error += error => failures.Enqueue(error.GetType());
                    detector.SpeechRecognitionError += (error, id) =>
                    {
                        failures.Enqueue(error.GetType());
                        return UniTask.CompletedTask;
                    };
                    detector.SpeechDetecting += (text, session) => { partials.Enqueue(text); return UniTask.CompletedTask; };
                    detector.SpeechDetected += result => { finals.Enqueue(result); return UniTask.CompletedTask; };

                    var sawRecording = false;
                    for (var offset = 0; offset < wave.Audio.Length; offset += chunkBytes)
                    {
                        var frame = new byte[chunkBytes];
                        Buffer.BlockCopy(wave.Audio, offset, frame, 0, Math.Min(chunkBytes, wave.Audio.Length - offset));
                        sawRecording |= await detector.ProcessSamplesAsync(frame, sessionId, deadline.Token);
                        await SpeechAsync.Yield();
                    }
                    Assert.That(sawRecording, Is.True, "Silero did not detect speech in the configured WAV.");
                    Assert.That(recognitionCount, Is.Zero, "The fixture must reach its end before recognition starts.");

                    var silence = new byte[chunkBytes];
                    var remainingFrames = (int)Math.Ceiling((finalSilence + 1) / frameDuration);
                    while (remainingFrames-- > 0 && partials.IsEmpty)
                    {
                        await detector.ProcessSamplesAsync(silence, sessionId, deadline.Token);
                        // Await a newly started partial request before feeding enough
                        // further silence to finalize; empty drains finish immediately.
                        await BeforeDeadline(detector.DrainAsync(), deadline.Token);
                        Assert.That(failures.IsEmpty, Is.True, "VAD or STT reported a failure during partial recognition.");
                    }
                    Assert.That(recognitionCount, Is.EqualTo(1), "Expected exactly one transcription invocation.");
                    Assert.That(partials.Count, Is.EqualTo(1), "Expected one partial transcript after the short-pause threshold.");
                    Assert.That(finals.IsEmpty, Is.True, "Partial recognition must complete before final turn detection.");
                    Assert.That(await detector.IsRecordingAsync(sessionId, deadline.Token), Is.True);
                    var partialText = partials.Single();
                    Assert.That(partialText, Is.Not.Null.And.Not.Empty, provider + " returned no partial transcript.");

                    while (remainingFrames-- > 0 && finals.IsEmpty)
                    {
                        await detector.ProcessSamplesAsync(silence, sessionId, deadline.Token);
                        await BeforeDeadline(detector.DrainAsync(), deadline.Token);
                    }
                    Assert.That(failures.IsEmpty, Is.True, "VAD or STT reported a failure during final detection.");
                    Assert.That(recognitionCount, Is.EqualTo(1), "Final detection must reuse the partial transcript.");
                    Assert.That(finals.Count, Is.EqualTo(1), "Expected one final speech detection after added silence.");
                    var final = finals.Single();
                    Assert.That(final.Text, Is.EqualTo(partialText));
                    Assert.That(final.SessionId, Is.EqualTo(sessionId));
                    Assert.That(final.RecordedDuration, Is.GreaterThan(0));
                    Assert.That(recognizedAudio, Is.Not.Null.And.Not.Empty);
                    Assert.That(final.Audio.Length, Is.GreaterThanOrEqualTo(recognizedAudio.Length));
                    Assert.That(final.Audio.Take(recognizedAudio.Length).SequenceEqual(recognizedAudio), Is.True,
                        "The final recording must retain the complete cumulative audio sent for partial recognition.");
                    Assert.That(await detector.IsRecordingAsync(sessionId, deadline.Token), Is.False);
                    TestContext.WriteLine(provider + " SileroStream transcript: " + final.Text);
                    var expected = SpeechApiTestSettings.ExpectedTextContains;
                    if (!string.IsNullOrEmpty(expected))
                        Assert.That(final.Text.IndexOf(expected, StringComparison.OrdinalIgnoreCase), Is.GreaterThanOrEqualTo(0),
                            provider + " transcript does not contain ExpectedTextContains.");
                }
                finally
                {
                    try { if (detector != null) await detector.DisposeAsync(); }
                    finally
                    {
                        try { if (recognizer != null) await recognizer.DisposeAsync(); }
                        finally { model?.Dispose(); }
                    }
                }
            }
        }

        private static async UniTask BeforeDeadline(UniTask task, CancellationToken token)
        {
            await SpeechAsync.WaitAsync(task, token);
        }

        private static string FindModel()
        {
            var path = Environment.GetEnvironmentVariable("CHATDOLLKIT_SILERO_MODEL_PATH");
            if (string.IsNullOrEmpty(path)) path = Path.Combine("Assets", "StreamingAssets", "silero_vad.onnx");
            if (!File.Exists(path)) Assert.Ignore("Set CHATDOLLKIT_SILERO_MODEL_PATH to run native Silero live integration tests.");
            return path;
        }

        private static Pcm16Wave ReadConfiguredAudio()
        {
            var path = SpeechApiTestSettings.AudioFilePath;
            if (string.IsNullOrWhiteSpace(path))
                Assert.Ignore("Configure AudioFilePath with a short spoken PCM16 mono WAV file.");
            Assert.That(File.Exists(path), Is.True, "The configured speech-test WAV file does not exist.");
            Assert.That(new FileInfo(path).Length, Is.LessThanOrEqualTo(2 * 1024 * 1024),
                "Use a short speech-test WAV file smaller than 2 MiB.");
            Pcm16Wave wave;
            try { wave = Pcm16Audio.ReadWave(File.ReadAllBytes(path)); }
            catch (ArgumentException)
            {
                Assert.Fail("Silero live tests require an uncompressed PCM16 mono WAV file.");
                return null;
            }
            Assert.That(wave.SampleRate == 8000 || wave.SampleRate == 16000, Is.True,
                "Silero live tests require 8 kHz or 16 kHz audio; convert the fixture before running.");
            var duration = wave.Audio.Length / (2.0 * wave.SampleRate);
            Assert.That(duration, Is.InRange(0.25, 15), "Use a spoken WAV between 0.25 and 15 seconds.");
            return wave;
        }
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using NUnitTask = System.Threading.Tasks.Task;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.STT;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline.LiveApi
{
    public class SpeechApiLiveTests
    {
        [Test]
        [Explicit("Sends one recognition request to OpenAI using locally configured credentials and spoken audio.")]
        [Category("LiveApi")]
        public async NUnitTask OpenAITranscribesConfiguredSpeech()
        {
            if (string.IsNullOrWhiteSpace(SpeechApiTestSettings.OpenAIApiKey))
                Assert.Ignore("Configure the OpenAI API key to run this live test.");
            var wave = ReadConfiguredAudio();
            await CheckRequestAsync("OpenAI", async () =>
            {
                var recognizer = new OpenAISpeechRecognizerClient(new OpenAISpeechRecognizerOptions
                {
                    ApiKey = SpeechApiTestSettings.OpenAIApiKey,
                    SampleRate = wave.SampleRate,
                    MaxAttempts = 1,
                    TimeoutSeconds = 30
                });
                ObserveErrors(recognizer, "OpenAI");
                try { return await recognizer.RecognizeAsync("live-api-openai", wave.Audio); }
                finally { await recognizer.DisposeAsync(); }
            });
        }

        [Test]
        [Explicit("Sends one recognition request to Azure Fast using locally configured credentials and spoken audio.")]
        [Category("LiveApi")]
        public async NUnitTask AzureTranscribesConfiguredSpeech()
        {
            if (string.IsNullOrWhiteSpace(SpeechApiTestSettings.AzureApiKey) ||
                string.IsNullOrWhiteSpace(SpeechApiTestSettings.AzureRegion))
                Assert.Ignore("Configure the Azure Speech API key and region to run this live test.");
            var wave = ReadConfiguredAudio();
            await CheckRequestAsync("Azure Fast", async () =>
            {
                var recognizer = new AzureSpeechRecognizerClient(new AzureSpeechRecognizerOptions
                {
                    ApiKey = SpeechApiTestSettings.AzureApiKey,
                    Region = SpeechApiTestSettings.AzureRegion,
                    SampleRate = wave.SampleRate,
                    MaxAttempts = 1,
                    TimeoutSeconds = 30
                });
                ObserveErrors(recognizer, "Azure Fast");
                try { return await recognizer.RecognizeAsync("live-api-azure-fast", wave.Audio); }
                finally { await recognizer.DisposeAsync(); }
            });
        }

        private static void ObserveErrors(HttpSpeechRecognizerBase recognizer, string provider)
        {
            recognizer.Error += exception =>
            {
                if (exception is SpeechRecognitionException error)
                    TestContext.WriteLine($"{provider}: attempt {error.Attempt}, HTTP status {(error.StatusCode.HasValue ? ((int)error.StatusCode.Value).ToString() : "unavailable")}.");
                else TestContext.WriteLine(provider + ": " + exception.GetType().Name);
            };
        }

        private static Pcm16Wave ReadConfiguredAudio()
        {
            var path = SpeechApiTestSettings.AudioFilePath;
            if (string.IsNullOrWhiteSpace(path))
                Assert.Ignore("Configure AudioFilePath with a short, spoken PCM16 mono WAV file.");
            Assert.That(File.Exists(path), Is.True, "The configured speech-test WAV file does not exist.");
            var wave = Pcm16Audio.ReadWave(File.ReadAllBytes(path));
            Assert.That(wave.Audio, Is.Not.Null.And.Not.Empty, "The configured WAV file contains no PCM audio.");
            return wave;
        }

        private static async UniTask CheckRequestAsync(string provider, Func<UniTask<SpeechRecognitionResult>> request)
        {
            SpeechRecognitionResult result;
            try { result = await request(); }
            catch (Exception exception)
            {
                // Do not print HTTP response bodies, request headers, credentials,
                // or exception messages that could contain those values.
                Assert.Fail($"{provider} live recognition failed ({exception.GetType().Name}). Check the local settings and service connectivity.");
                return;
            }

            Assert.That(result?.Text, Is.Not.Null.And.Not.Empty, provider + " returned no transcript.");
            TestContext.WriteLine(provider + " transcript: " + result.Text);
            var expected = SpeechApiTestSettings.ExpectedTextContains;
            if (!string.IsNullOrEmpty(expected))
                Assert.That(result.Text.IndexOf(expected, StringComparison.OrdinalIgnoreCase), Is.GreaterThanOrEqualTo(0),
                    provider + " transcript does not contain ExpectedTextContains.");
        }
    }
}

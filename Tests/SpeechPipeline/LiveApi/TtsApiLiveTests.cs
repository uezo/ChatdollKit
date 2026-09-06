using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.TTS;
using ChatdollKit.SpeechPipeline.TTS.Preprocessing;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline.LiveApi
{
    public class TtsApiLiveTests
    {
        [TestCase("openai")]
        [TestCase("azure")]
        [TestCase("google")]
        [TestCase("voicevox")]
        [Explicit("Calls a configured TTS service and validates its real PCM WAV response and resampling.")]
        [Category("LiveApi")]
        public async NUnitTask SynthesizesAndResamplesSpeech(string provider)
        {
            var synthesizer = Create(provider);
            try
            {
                var audio = await synthesizer.SynthesizeAsync(new SpeechSynthesisRequest
                { Text = "こんにちは。音声合成の接続テストです。", Language = "ja-JP" });
                var wave = WavResampler.ReadWave(audio);
                Assert.That(wave.SampleRate, Is.EqualTo(16000));
                Assert.That(wave.Channels, Is.EqualTo(1));
                Assert.That(wave.BitsPerSample, Is.EqualTo(16));
                var seconds = wave.Audio.Length / (double)(wave.SampleRate * wave.Channels * wave.BitsPerSample / 8);
                Assert.That(seconds, Is.InRange(0.5, 30));
                Assert.That(wave.Audio.Any(sample => sample != 0), Is.True, "The response must contain audible PCM samples.");
                TestContext.Progress.WriteLine(provider + ": valid mono PCM WAV at 16000 Hz, duration " + seconds.ToString("F2") + " s");
            }
            finally { await synthesizer.DisposeAsync(); }
        }

        [Test]
        [Explicit("Uses the OpenAI key to verify AlphaToKana function calling and learned readings.")]
        [Category("LiveApi")]
        public async NUnitTask AlphaToKanaLearnsReadings()
        {
            Require(TtsApiTestSettings.OpenAIApiKey, "OpenAI API key");
            var processor = new AlphaToKanaPreprocessor(new AlphaToKanaPreprocessorOptions
            { ApiKey = TtsApiTestSettings.OpenAIApiKey, TimeoutSeconds = 60 });
            try
            {
                var request = new SpeechSynthesisRequest { Text = "UnityとOpenAIを使います。", Language = "ja-JP" };
                var result = await processor.ProcessAsync(request, new SpeechSynthesizerOptions());
                Assert.That(result, Does.Contain("と").And.Contain("を使います。"));
                Assert.That(result, Does.Not.Contain("Unity").And.Not.Contain("OpenAI"));
                var map = processor.GetKanaMap();
                Assert.That(map.Keys.Any(key => key.Equals("Unity", StringComparison.OrdinalIgnoreCase)), Is.True);
                Assert.That(map.Keys.Any(key => key.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)), Is.True);
                Assert.That(await processor.ProcessAsync(request, new SpeechSynthesizerOptions()), Is.EqualTo(result));
                TestContext.Progress.WriteLine("AlphaToKana: conversion and learned readings verified");
            }
            finally { await processor.DisposeAsync(); }
        }

        private static ISpeechSynthesizer Create(string provider)
        {
            if (provider == "openai")
            {
                Require(TtsApiTestSettings.OpenAIApiKey, "OpenAI API key");
                return new OpenAISpeechSynthesizerClient(new OpenAISpeechSynthesizerOptions
                {
                    ApiKey = TtsApiTestSettings.OpenAIApiKey, Model = TtsApiTestSettings.OpenAIModel,
                    Speaker = TtsApiTestSettings.OpenAISpeaker, SampleRate = 16000, TimeoutSeconds = 90
                });
            }
            if (provider == "azure")
            {
                Require(TtsApiTestSettings.AzureApiKey, "Azure API key");
                Require(TtsApiTestSettings.AzureRegion, "Azure region");
                return new AzureSpeechSynthesizerClient(new AzureSpeechSynthesizerOptions
                {
                    ApiKey = TtsApiTestSettings.AzureApiKey, Region = TtsApiTestSettings.AzureRegion,
                    Speaker = TtsApiTestSettings.AzureSpeaker, SampleRate = 16000, TimeoutSeconds = 90
                });
            }
            if (provider == "google")
            {
                Require(TtsApiTestSettings.GoogleApiKey, "Google API key");
                return new GoogleSpeechSynthesizerClient(new GoogleSpeechSynthesizerOptions
                {
                    ApiKey = TtsApiTestSettings.GoogleApiKey, Speaker = TtsApiTestSettings.GoogleSpeaker,
                    SampleRate = 16000, TimeoutSeconds = 90
                });
            }
            Require(TtsApiTestSettings.VoicevoxBaseUrl, "VOICEVOX base URL");
            return new VoicevoxSpeechSynthesizerClient(new VoicevoxSpeechSynthesizerOptions
            {
                BaseUrl = TtsApiTestSettings.VoicevoxBaseUrl, Speaker = TtsApiTestSettings.VoicevoxSpeaker,
                SampleRate = 16000, TimeoutSeconds = 90
            });
        }
        private static void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) Assert.Ignore("Configure " + name + " in TtsApiTestSettings.Local.cs.");
        }
    }
}

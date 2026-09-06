using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.LLM;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.TTS;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline.LiveApi
{
    public class SpeechPipelineLiveTests
    {
        [TestCase("chat", "openai", "openai")]
        [TestCase("responses", "azure", "google")]
        [TestCase("websocket", "openai", "voicevox")]
        [Explicit("Sends configured spoken audio through real STT, streaming LLM and TTS, then a text follow-up.")]
        [Category("LiveApi")]
        public async NUnitTask AudioAndFollowupRunThroughThePipeline(string transport, string recognition, string synthesis)
        {
            Require(SpeechApiTestSettings.OpenAIApiKey, "OpenAI key");
            Require(SpeechApiTestSettings.AudioFilePath, "STT test audio path");
            Assert.That(File.Exists(SpeechApiTestSettings.AudioFilePath), Is.True, "The configured speech fixture does not exist.");
            var wave = Pcm16Audio.ReadWave(File.ReadAllBytes(SpeechApiTestSettings.AudioFilePath));
            if (recognition == "azure") { Require(SpeechApiTestSettings.AzureApiKey, "Azure key"); Require(SpeechApiTestSettings.AzureRegion, "Azure region"); }
            if (synthesis == "google") Require(TtsApiTestSettings.GoogleApiKey, "Google key");
            if (synthesis == "voicevox") Require(TtsApiTestSettings.VoicevoxBaseUrl, "VOICEVOX URL");
            LlmServiceOptions llmOptions = transport == "websocket" ? new OpenAIResponsesWebSocketServiceOptions() : new LlmServiceOptions();
            llmOptions.ApiKey = SpeechApiTestSettings.OpenAIApiKey;
            llmOptions.ReasoningEffort = transport == "chat" ? "none" : "low";
            llmOptions.TimeoutSeconds = 60; llmOptions.MaxOutputTokens = 256;
            llmOptions.SystemPrompt = "日本語の短い一文で返答してください。返答は20文字以内にしてください。";
            ILlmService llm = transport == "chat" ? (ILlmService)new ChatCompletionsClient(llmOptions)
                : transport == "responses" ? (ILlmService)new OpenAIResponsesClient(llmOptions)
                : new OpenAIResponsesWebSocketClient((OpenAIResponsesWebSocketServiceOptions)llmOptions);
            ISpeechRecognizer stt = recognition == "openai" ? (ISpeechRecognizer)new OpenAISpeechRecognizerClient(new OpenAISpeechRecognizerOptions
            { ApiKey = SpeechApiTestSettings.OpenAIApiKey, SampleRate = wave.SampleRate, TimeoutSeconds = 30, MaxAttempts = 1 })
                : new AzureSpeechRecognizerClient(new AzureSpeechRecognizerOptions
                { ApiKey = SpeechApiTestSettings.AzureApiKey, Region = SpeechApiTestSettings.AzureRegion, SampleRate = wave.SampleRate, TimeoutSeconds = 30, MaxAttempts = 1 });
            ISpeechSynthesizer tts = synthesis == "openai" ? (ISpeechSynthesizer)new OpenAISpeechSynthesizerClient(new OpenAISpeechSynthesizerOptions
            { ApiKey = TtsApiTestSettings.OpenAIApiKey, Model = TtsApiTestSettings.OpenAIModel, Speaker = TtsApiTestSettings.OpenAISpeaker, SampleRate = 16000, TimeoutSeconds = 60 })
                : synthesis == "google" ? (ISpeechSynthesizer)new GoogleSpeechSynthesizerClient(new GoogleSpeechSynthesizerOptions
                { ApiKey = TtsApiTestSettings.GoogleApiKey, Speaker = TtsApiTestSettings.GoogleSpeaker, SampleRate = 16000, TimeoutSeconds = 60 })
                : new VoicevoxSpeechSynthesizerClient(new VoicevoxSpeechSynthesizerOptions
                { BaseUrl = TtsApiTestSettings.VoicevoxBaseUrl, Speaker = TtsApiTestSettings.VoicevoxSpeaker, SampleRate = 16000, TimeoutSeconds = 60 });
            var pipeline = new SpeechToSpeechPipeline(stt, llm, tts,
                new SpeechPipelineOptions { SessionId = "pipeline-live", InvokeTimeoutSeconds = 120 }, ownsComponents: true);
            var responses = new List<SpeechPipelineResponse>(); var errors = new List<string>();
            pipeline.ResponseReceived += response => { responses.Add(response); return UniTask.CompletedTask; };
            pipeline.Error += error => errors.Add(error.GetType().Name);
            try
            {
                var first = await pipeline.InvokeAsync(new SpeechPipelineRequest
                { TransactionId = "audio-turn", AudioData = wave.Audio, AudioDuration = wave.Audio.Length / (wave.SampleRate * 2.0) });
                Assert.That(first.Type, Is.EqualTo(SpeechPipelineResponseType.Final), "Audio processing must complete successfully.");
                var start = responses.Single(response => response.Type == SpeechPipelineResponseType.Start && response.TransactionId == "audio-turn");
                var recognized = (string)start.Metadata["recognized_text"];
                Assert.That(recognized, Is.Not.Null.And.Not.Empty);
                if (!string.IsNullOrEmpty(SpeechApiTestSettings.ExpectedTextContains))
                    Assert.That(recognized.IndexOf(SpeechApiTestSettings.ExpectedTextContains, StringComparison.OrdinalIgnoreCase), Is.GreaterThanOrEqualTo(0));
                var second = await pipeline.InvokeAsync(new SpeechPipelineRequest
                { TransactionId = "text-turn", Text = "ありがとうございます。短く返事してください。" });
                Assert.That(second.Type, Is.EqualTo(SpeechPipelineResponseType.Final));
                Assert.That(second.ContextId, Is.EqualTo(first.ContextId));
                foreach (var id in new[] { "audio-turn", "text-turn" })
                {
                    var events = responses.Where(response => response.TransactionId == id && response.Type != SpeechPipelineResponseType.Stop).ToArray();
                    Assert.That(events.First().Type, Is.EqualTo(SpeechPipelineResponseType.Accepted));
                    Assert.That(events.Last().Type, Is.EqualTo(SpeechPipelineResponseType.Final));
                    Assert.That(events.Count(response => response.IsTerminal), Is.EqualTo(1));
                    Assert.That(events.All(response => response.SessionId == "pipeline-live"), Is.True);
                    Assert.That(string.Concat(events.Where(response => response.Type == SpeechPipelineResponseType.Chunk).Select(response => response.Text)), Is.EqualTo(events.Last().Text));
                    var audioChunks = events.Where(response => response.AudioData?.Length > 0).ToArray();
                    Assert.That(audioChunks, Is.Not.Empty);
                    foreach (var chunk in audioChunks)
                    {
                        var pcm = WavResampler.ReadWave(chunk.AudioData);
                        Assert.That(pcm.SampleRate, Is.EqualTo(16000));
                        Assert.That(pcm.Audio.Any(sample => sample != 0), Is.True);
                    }
                }
                Assert.That(errors, Is.Empty, "No component errors may be hidden by the pipeline.");
                TestContext.Progress.WriteLine(recognition + " → " + transport + " → " + synthesis + ": audio and text turns completed with PCM WAV output.");
            }
            finally { await pipeline.DisposeAsync(); }
        }
        private static void Require(string value, string name)
        { if (string.IsNullOrWhiteSpace(value)) Assert.Ignore("Configure " + name + " in the existing live API settings."); }
    }
}

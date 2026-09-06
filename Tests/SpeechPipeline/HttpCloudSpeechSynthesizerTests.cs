using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using System.Xml;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.TTS;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class HttpCloudSpeechSynthesizerTests
    {
        [Test]
        public async NUnitTask AzureEscapesPlainTextAndAttributesAndSelectsMappedVoice()
        {
            var audio = Wave();
            var handler = new FakeHandler((_, __) => UniTask.FromResult(Bytes(audio)));
            using (var client = new HttpClient(handler))
            {
                var options = new AzureSpeechSynthesizerOptions
                {
                    ApiKey = "azure-test-key", Region = "japaneast", Speaker = "ja-JP-MayuNeural",
                    VoiceMap = new Dictionary<string, string> { ["en-US"] = "voice<&\"'" }
                };
                var service = new AzureSpeechSynthesizerClient(options, client);
                var text = "A < B & \"quotes\" <break time='1s'/>";
                try
                {
                    var result = await service.GenerateAsync(new SpeechSynthesisRequest { Text = text, Language = "en-US" });
                    Assert.That(result, Is.EqualTo(audio));
                    var sent = handler.Requests.Single();
                    Assert.That(sent.Uri, Is.EqualTo("https://japaneast.tts.speech.microsoft.com/cognitiveservices/v1"));
                    Assert.That(sent.Headers["Ocp-Apim-Subscription-Key"], Is.EqualTo("azure-test-key"));
                    Assert.That(sent.Headers["X-Microsoft-OutputFormat"], Is.EqualTo("riff-16khz-16bit-mono-pcm"));
                    Assert.That(sent.Headers["User-Agent"], Is.EqualTo("ChatdollKit"), "Azure requires an application User-Agent.");
                    Assert.That(sent.ContentType, Does.StartWith("application/ssml+xml"));
                    var xml = new XmlDocument { XmlResolver = null };
                    xml.LoadXml(sent.Body);
                    var voice = (XmlElement)xml.DocumentElement.FirstChild;
                    Assert.That(xml.DocumentElement.GetAttribute("lang", "http://www.w3.org/XML/1998/namespace"), Is.EqualTo("en-US"));
                    Assert.That(voice.GetAttribute("name"), Is.EqualTo("voice<&\"'"));
                    Assert.That(voice.InnerText, Is.EqualTo(text));
                    Assert.That(voice.ChildNodes.Count, Is.EqualTo(1), "User text must not become SSML elements.");
                    Assert.That(client.DefaultRequestHeaders.Contains("Ocp-Apim-Subscription-Key"), Is.False);
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask AzureRejectsUnregisteredLanguageBeforeSending()
        {
            var handler = new FakeHandler((_, __) => UniTask.FromResult(Bytes(Wave())));
            using (var client = new HttpClient(handler))
            {
                var service = Create("azure", client);
                try
                {
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<ArgumentException>(async () => await service.GenerateAsync(new SpeechSynthesisRequest { Text = "hi", Language = "en-US" }));
                    Assert.That(handler.Requests, Is.Empty);
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [TestCase(null, "ja-JP", "default")]
        [TestCase("en-US", "en-US", "english")]
        [TestCase("fr-FR", "ja-JP", "default")]
        [TestCase("zh-CN", "ja-JP", "default")]
        [TestCase("cmn-CN", "cmn-CN", "chinese")]
        public async NUnitTask GoogleVoiceSelectionPreservesDefaultFallbackAndChineseNormalization(string language, string expectedLanguage, string expectedSpeaker)
        {
            var audio = Wave();
            var handler = new FakeHandler((_, __) => UniTask.FromResult(GoogleAudio(audio)));
            using (var client = new HttpClient(handler))
            {
                var options = new GoogleSpeechSynthesizerOptions
                {
                    ApiKey = "key&not_another_parameter=value", Speaker = "default",
                    VoiceMap = new Dictionary<string, string> { ["en-US"] = "english", ["cmn-CN"] = "chinese" }
                };
                var service = new GoogleSpeechSynthesizerClient(options, client);
                try
                {
                    var result = await service.GenerateAsync(new SpeechSynthesisRequest { Text = "hello", Language = language });
                    Assert.That(result, Is.EqualTo(audio));
                    var sent = handler.Requests.Single();
                    Assert.That(sent.Uri, Is.EqualTo("https://texttospeech.googleapis.com/v1/text:synthesize?key=key%26not_another_parameter%3Dvalue"));
                    var body = JObject.Parse(sent.Body);
                    Assert.That((string)body["input"]["text"], Is.EqualTo("hello"));
                    Assert.That((string)body["voice"]["languageCode"], Is.EqualTo(expectedLanguage));
                    Assert.That((string)body["voice"]["name"], Is.EqualTo(expectedSpeaker));
                    Assert.That((string)body["audioConfig"]["audioEncoding"], Is.EqualTo("LINEAR16"));
                    Assert.That(body["audioConfig"]["sampleRateHertz"], Is.Null);
                    Assert.That(options.CacheExtension, Is.EqualTo("pcm"));
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask GoogleRetainsLiteralChineseMapKeyFromUpstream()
        {
            var handler = new FakeHandler((_, __) => UniTask.FromResult(GoogleAudio(Wave())));
            using (var client = new HttpClient(handler))
            {
                var options = new GoogleSpeechSynthesizerOptions { ApiKey = "key", Speaker = "default" };
                options.VoiceMap["cmn-CNCN"] = "legacy-map";
                var service = new GoogleSpeechSynthesizerClient(options, client);
                try
                {
                    await service.GenerateAsync(new SpeechSynthesisRequest { Text = "hi", Language = "zh-CN" });
                    var voice = JObject.Parse(handler.Requests[0].Body)["voice"];
                    Assert.That((string)voice["languageCode"], Is.EqualTo("cmn-CNCN"));
                    Assert.That((string)voice["name"], Is.EqualTo("legacy-map"));
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [TestCase("{}")]
        [TestCase("{\"audioContent\":42}")]
        [TestCase("{\"audioContent\":\"not-base64!\"}")]
        public async NUnitTask GoogleRejectsMissingOrInvalidAudioContent(string json)
        {
            var handler = new FakeHandler((_, __) => UniTask.FromResult(Json(json)));
            using (var client = new HttpClient(handler))
            {
                var service = Create("google", client);
                try
                {
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<InvalidDataException>(async () => await service.GenerateAsync(Request()));
                    Assert.That(handler.Requests.Count, Is.EqualTo(1));
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask OpenAIPreservesSpeechPayloadAndAzureEndpointAuthentication(bool azure)
        {
            var audio = Wave();
            var handler = new FakeHandler((_, __) => UniTask.FromResult(Bytes(audio)));
            using (var client = new HttpClient(handler))
            {
                var options = new OpenAISpeechSynthesizerOptions { ApiKey = "openai-test-key" };
                if (azure) options.BaseUrl = "https://sample.openai.azure.com/openai/deployments/speech/audio/speech?api-version=2024-02-15-preview";
                var service = new OpenAISpeechSynthesizerClient(options, client);
                try
                {
                    var result = await service.GenerateAsync(new SpeechSynthesisRequest { Text = "hello", Language = "en-US", StyleInfo = new JObject { ["style"] = "happy" } });
                    Assert.That(result, Is.EqualTo(audio));
                    var sent = handler.Requests.Single();
                    Assert.That(sent.Uri, Is.EqualTo(azure ? options.BaseUrl : "https://api.openai.com/v1/audio/speech"));
                    Assert.That(sent.Headers[azure ? "api-key" : "Authorization"], Is.EqualTo(azure ? "openai-test-key" : "Bearer openai-test-key"));
                    Assert.That(sent.Headers.ContainsKey(azure ? "Authorization" : "api-key"), Is.False);
                    Assert.That(client.DefaultRequestHeaders.Authorization, Is.Null);
                    var body = JObject.Parse(sent.Body);
                    Assert.That((string)body["model"], Is.EqualTo("tts-1"));
                    Assert.That((string)body["voice"], Is.EqualTo("sage"));
                    Assert.That((string)body["input"], Is.EqualTo("hello"));
                    Assert.That((string)body["response_format"], Is.EqualTo("wav"));
                    Assert.That(body["instructions"], Is.Null, "The API rejects explicit null; omit unspecified instructions.");
                    Assert.That(body["language"], Is.Null);
                    Assert.That(body["style"], Is.Null);
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [TestCase("")]
        [TestCase("Speak softly.")]
        public async NUnitTask OpenAIIncludesExplicitInstructionsAsAString(string instructions)
        {
            var handler = new FakeHandler((_, __) => UniTask.FromResult(Bytes(Wave())));
            using (var client = new HttpClient(handler))
            {
                var service = new OpenAISpeechSynthesizerClient(new OpenAISpeechSynthesizerOptions { ApiKey = "key", Instructions = instructions }, client);
                try
                {
                    await service.GenerateAsync(Request());
                    var actual = JObject.Parse(handler.Requests.Single().Body)["instructions"];
                    Assert.That(actual.Type, Is.EqualTo(JTokenType.String));
                    Assert.That((string)actual, Is.EqualTo(instructions));
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [TestCase("azure")]
        [TestCase("google")]
        [TestCase("openai")]
        public async NUnitTask CacheReusesAudioAndProviderOptionChangeProducesNewEntry(string provider)
        {
            var directory = Path.Combine(Path.GetTempPath(), "chatdoll-cloud-cache-" + Guid.NewGuid().ToString("N"));
            var handler = new FakeHandler((_, __) => UniTask.FromResult(provider == "google" ? GoogleAudio(Wave()) : Bytes(Wave())));
            using (var client = new HttpClient(handler))
            {
                var options = Options(provider);
                options.CacheDirectory = directory;
                var service = Create(provider, client, options);
                try
                {
                    var first = await service.SynthesizeAsync(Request());
                    var second = await service.SynthesizeAsync(Request());
                    Assert.That(second, Is.EqualTo(first));
                    Assert.That(handler.Requests.Count, Is.EqualTo(1));
                    var replacement = service.GetOptions();
                    if (replacement is AzureSpeechSynthesizerOptions a) a.VoiceMap[a.DefaultLanguage] = "new-voice";
                    else if (replacement is GoogleSpeechSynthesizerOptions g) g.Speaker = "new-voice";
                    else ((OpenAISpeechSynthesizerOptions)replacement).Instructions = "Speak softly.";
                    service.UpdateOptions(replacement);
                    await service.SynthesizeAsync(Request());
                    Assert.That(handler.Requests.Count, Is.EqualTo(2));
                    Assert.That(Directory.GetFiles(directory).Length, Is.EqualTo(2));
                    await service.GenerateAsync(Request());
                    Assert.That(handler.Requests.Count, Is.EqualTo(3), "GenerateAsync bypasses the cache.");
                }
                finally
                {
                    await service.DisposeAsync();
                    if (Directory.Exists(directory)) Directory.Delete(directory, true);
                }
            }
        }

        [TestCase("azure")]
        [TestCase("google")]
        public async NUnitTask VoiceMapIsCopiedAtConstructionAndWhenReadingOptions(string provider)
        {
            var handler = new FakeHandler((_, __) => UniTask.FromResult(provider == "google" ? GoogleAudio(Wave()) : Bytes(Wave())));
            using (var client = new HttpClient(handler))
            {
                var options = Options(provider);
                var map = options is AzureSpeechSynthesizerOptions a ? a.VoiceMap : ((GoogleSpeechSynthesizerOptions)options).VoiceMap;
                map["en-US"] = "original";
                var service = Create(provider, client, options);
                try
                {
                    map["en-US"] = "caller-mutation";
                    var copy = service.GetOptions();
                    var copiedMap = copy is AzureSpeechSynthesizerOptions c ? c.VoiceMap : ((GoogleSpeechSynthesizerOptions)copy).VoiceMap;
                    copiedMap["en-US"] = "returned-copy-mutation";
                    await service.GenerateAsync(new SpeechSynthesisRequest { Text = "hello", Language = "en-US" });
                    if (provider == "google") Assert.That((string)JObject.Parse(handler.Requests[0].Body)["voice"]["name"], Is.EqualTo("original"));
                    else Assert.That(handler.Requests[0].Body, Does.Contain("name=\"original\""));
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask OpenAIResamplesWavButLeavesCompressedAudioUnchanged()
        {
            var wav = Wave();
            var compressed = Encoding.ASCII.GetBytes("fake-mp3-audio");
            var handler = new FakeHandler((index, _) => UniTask.FromResult(Bytes(index == 0 ? wav : compressed)));
            using (var client = new HttpClient(handler))
            {
                var options = new OpenAISpeechSynthesizerOptions { ApiKey = "key", SampleRate = 8000 };
                var service = new OpenAISpeechSynthesizerClient(options, client);
                try
                {
                    var result = await service.SynthesizeAsync(Request());
                    var decoded = Pcm16Audio.ReadWave(result);
                    Assert.That(decoded.SampleRate, Is.EqualTo(8000));
                    Assert.That(decoded.Audio.Length, Is.EqualTo(160));
                    var replacement = (OpenAISpeechSynthesizerOptions)service.GetOptions();
                    replacement.AudioFormat = "mp3";
                    service.UpdateOptions(replacement);
                    Assert.That(await service.SynthesizeAsync(Request()), Is.EqualTo(compressed));
                    Assert.That((string)JObject.Parse(handler.Requests[1].Body)["response_format"], Is.EqualTo("mp3"));
                }
                finally { await service.DisposeAsync(); }
            }
        }

        [TestCase("azure")]
        [TestCase("google")]
        [TestCase("openai")]
        public async NUnitTask HttpFailureIsNotAudioAndDoesNotPopulateCache(string provider)
        {
            var directory = Path.Combine(Path.GetTempPath(), "chatdoll-cloud-failure-" + Guid.NewGuid().ToString("N"));
            var handler = new FakeHandler((index, _) => UniTask.FromResult(index == 0
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("private-response-body") }
                : provider == "google" ? GoogleAudio(Wave()) : Bytes(Wave())));
            using (var client = new HttpClient(handler))
            {
                var options = Options(provider);
                options.CacheDirectory = directory;
                var service = Create(provider, client, options);
                try
                {
                    var error = await SpeechAsyncAssert.ThrowsAsync<SpeechSynthesisException>(async () => await service.SynthesizeAsync(Request()));
                    Assert.That(error.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                    Assert.That(error.Code, Is.EqualTo("http_error"));
                    Assert.That(error.Message, Does.Not.Contain("private-response-body"));
                    await service.SynthesizeAsync(Request());
                    await service.SynthesizeAsync(Request());
                    Assert.That(handler.Requests.Count, Is.EqualTo(2));
                }
                finally
                {
                    await service.DisposeAsync();
                    if (Directory.Exists(directory)) Directory.Delete(directory, true);
                }
            }
        }

        [TestCase("azure")]
        [TestCase("google")]
        [TestCase("openai")]
        public async NUnitTask DisposeCancelsRequestAndPreservesInjectedClientOwnership(string provider)
        {
            var started = new SpeechCompletionSource<bool>();
            var handler = new FakeHandler(async (_, token) =>
            {
                started.TrySetResult(true);
                await UniTask.Never(token);
                throw new InvalidOperationException("Cancelled request unexpectedly continued.");
            });
            using (var client = new HttpClient(handler))
            {
                var service = Create(provider, client);
                var operation = service.GenerateAsync(Request());
                try
                {
                    await Bounded(started.Task);
                    await Bounded(service.DisposeAsync());
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await operation);
                    Assert.That(handler.Disposed, Is.False);
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<ObjectDisposedException>(async () => await service.GenerateAsync(Request()));
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<ObjectDisposedException>(async () => await handler.Requests[0].Message.Content.ReadAsStringAsync());
                }
                finally { await Bounded(service.DisposeAsync()); }
            }
            Assert.That(handler.Disposed, Is.True);
        }

        [TestCase("azure")]
        [TestCase("google")]
        [TestCase("openai")]
        public async NUnitTask EmptyTextSkipsHttp(string provider)
        {
            var handler = new FakeHandler((_, __) => throw new AssertionException("Empty text must not issue HTTP."));
            using (var client = new HttpClient(handler))
            {
                var service = Create(provider, client);
                try
                {
                    Assert.That(await service.SynthesizeAsync(new SpeechSynthesisRequest { Text = "  \r\n " }), Is.Empty);
                    Assert.That(handler.Requests, Is.Empty);
                }
                finally { await service.DisposeAsync(); }
            }
        }

        private static SpeechSynthesisRequest Request() => new SpeechSynthesisRequest { Text = "hello" };
        private static byte[] Wave() => Pcm16Audio.WriteWave(new byte[480], 24000);
        private static HttpResponseMessage Bytes(byte[] audio) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(audio) };
        private static HttpResponseMessage Json(string json) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        private static HttpResponseMessage GoogleAudio(byte[] audio) => Json(new JObject { ["audioContent"] = Convert.ToBase64String(audio) }.ToString());
        private static SpeechSynthesizerOptions Options(string provider)
        {
            if (provider == "azure") return new AzureSpeechSynthesizerOptions { ApiKey = "key", Region = "japaneast", Speaker = "azure-voice" };
            if (provider == "google") return new GoogleSpeechSynthesizerOptions { ApiKey = "key", Speaker = "google-voice" };
            return new OpenAISpeechSynthesizerOptions { ApiKey = "key" };
        }
        private static ISpeechSynthesizer Create(string provider, HttpClient client, SpeechSynthesizerOptions options = null)
        {
            options = options ?? Options(provider);
            if (provider == "azure") return new AzureSpeechSynthesizerClient((AzureSpeechSynthesizerOptions)options, client);
            if (provider == "google") return new GoogleSpeechSynthesizerClient((GoogleSpeechSynthesizerOptions)options, client);
            return new OpenAISpeechSynthesizerClient((OpenAISpeechSynthesizerOptions)options, client);
        }
        private static async UniTask Bounded(UniTask task)
        {
            task = SpeechAsync.Share(task);
            Assert.That(await UniTask.WhenAny(task, SpeechAsync.Delay(TimeSpan.FromMilliseconds(3000))), Is.EqualTo(0), "Operation did not settle within three seconds.");
            await task;
        }

        private sealed class Snapshot
        {
            internal HttpRequestMessage Message;
            internal string Uri;
            internal string Body;
            internal string ContentType;
            internal Dictionary<string, string> Headers;
        }
        private sealed class FakeHandler : HttpMessageHandler
        {
            internal readonly List<Snapshot> Requests = new List<Snapshot>();
            private readonly Func<int, CancellationToken, UniTask<HttpResponseMessage>> respond;
            internal bool Disposed;
            internal FakeHandler(Func<int, CancellationToken, UniTask<HttpResponseMessage>> respond) { this.respond = respond; }
            protected override async System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Requests.Add(new Snapshot
                {
                    Message = request, Uri = request.RequestUri.AbsoluteUri, Body = await request.Content.ReadAsStringAsync(),
                    ContentType = request.Content.Headers.ContentType.ToString(),
                    Headers = request.Headers.ToDictionary(pair => pair.Key, pair => string.Join(",", pair.Value), StringComparer.OrdinalIgnoreCase)
                });
                return await respond(Requests.Count - 1, token);
            }
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        }
    }
}

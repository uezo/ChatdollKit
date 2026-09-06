using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.TTS;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class LocalSpeechSynthesizerTests
    {
        private static readonly byte[] Wave = Pcm16Audio.WriteWave(new byte[] { 0, 0, 100, 0 }, 24000);

        [Test]
        public async NUnitTask VoicevoxPreservesAudioQueryAndEscapesText()
        {
            const string text = "こんにちは & # + ?";
            var query = JObject.Parse("{\"accent_phrases\":[{\"moras\":[]}],\"speedScale\":1.25,\"futureField\":{\"value\":true}}");
            var calls = new List<string>();
            using (var client = Client(async (request, token) =>
            {
                calls.Add(request.RequestUri.AbsoluteUri);
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
                if (calls.Count == 1)
                {
                    Assert.That(request.RequestUri.Query, Does.Contain("speaker=46"));
                    Assert.That(request.RequestUri.Query, Does.Contain("text=" + Uri.EscapeDataString(text)));
                    return Json(query);
                }
                Assert.That(request.RequestUri.AbsolutePath, Is.EqualTo("/synthesis"));
                Assert.That(request.RequestUri.Query, Is.EqualTo("?speaker=46"));
                Assert.That(request.Content.Headers.ContentType.MediaType, Is.EqualTo("application/json"));
                Assert.That(JToken.DeepEquals(query, JObject.Parse(await request.Content.ReadAsStringAsync())), Is.True);
                return Audio();
            }))
            {
                var synthesizer = new VoicevoxSpeechSynthesizerClient(new VoicevoxSpeechSynthesizerOptions(), client);
                try { CollectionAssert.AreEqual(Wave, await synthesizer.GenerateAsync(new SpeechSynthesisRequest { Text = text })); }
                finally { await synthesizer.DisposeAsync(); }
            }
            Assert.That(calls.Count, Is.EqualTo(2));
        }

        [TestCase("[happy]hello", "77")]
        [TestCase("hello", "46")]
        public async NUnitTask VoicevoxStyleSelectsSpeakerForBothRequests(string styledText, string expectedSpeaker)
        {
            var calls = 0;
            using (var client = Client((request, token) =>
            {
                calls++;
                Assert.That(request.RequestUri.Query, Does.Contain("speaker=" + expectedSpeaker));
                return UniTask.FromResult(calls % 2 == 1 ? Json(new JObject()) : Audio());
            }))
            {
                var synthesizer = new VoicevoxSpeechSynthesizerClient(new VoicevoxSpeechSynthesizerOptions
                { StyleMapper = new Dictionary<string, string> { ["[happy]"] = "77" } }, client);
                try { await synthesizer.GenerateAsync(new SpeechSynthesisRequest { Text = "hello", StyleInfo = new JObject { ["styled_text"] = styledText } }); }
                finally { await synthesizer.DisposeAsync(); }
            }
            Assert.That(calls, Is.EqualTo(2));
        }

        [TestCase("invalid")]
        [TestCase("-1")]
        public async NUnitTask VoicevoxRejectsInvalidMappedSpeakerBeforeSending(string speaker)
        {
            var calls = 0;
            using (var client = Client((request, token) => { calls++; return UniTask.FromResult(Audio()); }))
            {
                var synthesizer = new VoicevoxSpeechSynthesizerClient(new VoicevoxSpeechSynthesizerOptions
                { StyleMapper = new Dictionary<string, string> { ["happy"] = speaker } }, client);
                try
                {
                    await SpeechAsyncAssert.ThrowsAsync<ArgumentException>(async () => await synthesizer.GenerateAsync(new SpeechSynthesisRequest
                    { Text = "hello", StyleInfo = new JObject { ["styled_text"] = "happy" } }));
                    Assert.That(calls, Is.Zero);
                }
                finally { await synthesizer.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask VoicevoxPublicAudioQueryUsesExplicitSpeaker()
        {
            var calls = 0;
            using (var client = Client((request, token) =>
            {
                calls++;
                Assert.That(request.RequestUri.AbsolutePath, Is.EqualTo("/audio_query"));
                Assert.That(request.RequestUri.Query, Does.Contain("speaker=3"));
                return UniTask.FromResult(Json(new JObject { ["speedScale"] = 1.0 }));
            }))
            {
                var synthesizer = new VoicevoxSpeechSynthesizerClient(new VoicevoxSpeechSynthesizerOptions(), client);
                try { Assert.That((double)(await synthesizer.GetAudioQueryAsync("hello", 3))["speedScale"], Is.EqualTo(1.0)); }
                finally { await synthesizer.DisposeAsync(); }
            }
            Assert.That(calls, Is.EqualTo(1));
        }

        [TestCase(1)]
        [TestCase(2)]
        public async NUnitTask VoicevoxHttpErrorsDoNotReturnAudio(int failingRequest)
        {
            var calls = 0;
            using (var client = Client((request, token) =>
            {
                calls++;
                return UniTask.FromResult(calls == failingRequest
                    ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("engine error") }
                    : Json(new JObject()));
            }))
            {
                var synthesizer = new VoicevoxSpeechSynthesizerClient(new VoicevoxSpeechSynthesizerOptions(), client);
                try
                {
                    await SpeechAsyncAssert.ThrowsAsync<SpeechSynthesisException>(async () => await synthesizer.GenerateAsync(new SpeechSynthesisRequest { Text = "hello" }));
                    Assert.That(calls, Is.EqualTo(failingRequest));
                }
                finally { await synthesizer.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask VoicevoxCancellationStopsBeforeSynthesis()
        {
            var entered = new SpeechCompletionSource<bool>();
            var calls = 0;
            using (var cancellation = new CancellationTokenSource())
            using (var client = Client(async (request, token) =>
            {
                calls++;
                entered.TrySetResult(true);
                await UniTask.Never(token);
                return Json(new JObject());
            }))
            {
                var synthesizer = new VoicevoxSpeechSynthesizerClient(new VoicevoxSpeechSynthesizerOptions(), client);
                try
                {
                    var pending = synthesizer.GenerateAsync(new SpeechSynthesisRequest { Text = "hello" }, cancellation.Token);
                    await entered.Task;
                    cancellation.Cancel();
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await pending);
                    Assert.That(calls, Is.EqualTo(1));
                }
                finally { await synthesizer.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask VoicevoxDisposeCancelsAndDrainsPublicQuery()
        {
            var entered = new SpeechCompletionSource<bool>();
            using (var client = Client(async (request, token) =>
            {
                entered.TrySetResult(true);
                await UniTask.Never(token);
                return Json(new JObject());
            }))
            {
                var synthesizer = new VoicevoxSpeechSynthesizerClient(new VoicevoxSpeechSynthesizerOptions(), client);
                var pending = synthesizer.GetAudioQueryAsync("hello", 1);
                await entered.Task;
                await synthesizer.DisposeAsync();
                Assert.That(pending.Status.IsCompleted(), Is.True);
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await pending);
                await SpeechAsyncAssert.ThrowsAsync<ObjectDisposedException>(async () => await synthesizer.GetAudioQueryAsync("hello", 1));
            }
        }

        [Test]
        public async NUnitTask VoicevoxOptionsAreStableAcrossTwoRequestSequence()
        {
            var entered = new SpeechCompletionSource<bool>();
            var resume = new SpeechCompletionSource<bool>();
            var urls = new List<string>();
            using (var client = Client(async (request, token) =>
            {
                urls.Add(request.RequestUri.AbsoluteUri);
                if (urls.Count == 1) { entered.TrySetResult(true); await resume.Task; }
                return urls.Count % 2 == 1 ? Json(new JObject()) : Audio();
            }))
            {
                var synthesizer = new VoicevoxSpeechSynthesizerClient(new VoicevoxSpeechSynthesizerOptions(), client);
                try
                {
                    var pending = synthesizer.GenerateAsync(new SpeechSynthesisRequest { Text = "hello" });
                    await entered.Task;
                    synthesizer.UpdateOptions(new VoicevoxSpeechSynthesizerOptions { BaseUrl = "http://localhost:50022", Speaker = 9 });
                    resume.TrySetResult(true);
                    await pending;
                    await synthesizer.GenerateAsync(new SpeechSynthesisRequest { Text = "hello" });
                    Assert.That(urls[1], Is.EqualTo("http://127.0.0.1:50021/synthesis?speaker=46"));
                    Assert.That(urls[3], Is.EqualTo("http://localhost:50022/synthesis?speaker=9"));
                }
                finally { resume.TrySetResult(true); await synthesizer.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask VoicevoxCacheDistinguishesSpeakerAndReusesIdenticalRequest()
        {
            var directory = Path.Combine(Path.GetTempPath(), "chatdoll-voicevox-" + Guid.NewGuid().ToString("N"));
            var calls = 0;
            using (var client = Client((request, token) => UniTask.FromResult(++calls % 2 == 1 ? Json(new JObject()) : Audio())))
            {
                var synthesizer = new VoicevoxSpeechSynthesizerClient(new VoicevoxSpeechSynthesizerOptions { CacheDirectory = directory }, client);
                try
                {
                    var request = new SpeechSynthesisRequest { Text = "hello" };
                    CollectionAssert.AreEqual(Wave, await synthesizer.SynthesizeAsync(request));
                    CollectionAssert.AreEqual(Wave, await synthesizer.SynthesizeAsync(request));
                    Assert.That(calls, Is.EqualTo(2));
                    synthesizer.UpdateOptions(new VoicevoxSpeechSynthesizerOptions { CacheDirectory = directory, Speaker = 3 });
                    await synthesizer.SynthesizeAsync(request);
                    Assert.That(calls, Is.EqualTo(4));
                }
                finally
                {
                    await synthesizer.DisposeAsync();
                    if (Directory.Exists(directory)) Directory.Delete(directory, true);
                }
            }
        }

        [TestCase("http://127.0.0.1:50021", true)]
        [TestCase("http://192.168.1.10:50021", true)]
        [TestCase("https://voice.example.com", true)]
        [TestCase("http://user:password@localhost", false)]
        [TestCase("http://localhost?query=1", false)]
        [TestCase("http://localhost#fragment", false)]
        public void VoicevoxValidatesEngineUrl(string url, bool valid)
        {
            var options = new VoicevoxSpeechSynthesizerOptions { BaseUrl = url };
            if (valid) Assert.DoesNotThrow(options.Validate);
            else Assert.Throws<ArgumentException>(options.Validate);
        }

        private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, UniTask<HttpResponseMessage>> send)
            => new HttpClient(new Handler(send));
        private static HttpResponseMessage Json(JObject value) => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(value.ToString(), Encoding.UTF8, "application/json") };
        private static HttpResponseMessage Audio() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Wave) };
        private sealed class Handler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, UniTask<HttpResponseMessage>> send;
            public Handler(Func<HttpRequestMessage, CancellationToken, UniTask<HttpResponseMessage>> send) { this.send = send; }
            protected override System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token).AsTask();
        }
    }
}

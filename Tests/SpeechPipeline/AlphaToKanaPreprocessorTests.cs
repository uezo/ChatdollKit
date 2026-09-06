using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.TTS;
using ChatdollKit.SpeechPipeline.TTS.Preprocessing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class AlphaToKanaPreprocessorTests
    {
        [Test]
        public async NUnitTask AppliesKnownReadingsCaseInsensitivelyWithoutHttpAndReturnsIndependentMap()
        {
            var handler = new FakeHandler((_, __) => throw new AssertionException("Known readings must not use HTTP."));
            using (var client = new HttpClient(handler))
            {
                var settings = Options();
                settings.KanaMap["hello"] = "ハロー";
                var processor = new AlphaToKanaPreprocessor(settings, client);
                try
                {
                    settings.KanaMap["hello"] = "caller mutation";
                    processor.GetKanaMap()["hello"] = "export mutation";
                    processor.GetOptions().KanaMap["hello"] = "options mutation";
                    Assert.That(await Process(processor, "Hello HELLO hello"), Is.EqualTo("ハロー ハロー ハロー"));
                    SpeechAsyncAssert.NoPendingOperations(processor, "pending");
                    Assert.That(handler.Calls, Is.Zero);
                }
                finally { await processor.DisposeAsync(); }
            }
        }

        [TestCase("en-US", "Hello", true)]
        [TestCase("ja-JP", "こんにちは", true)]
        [TestCase(null, "AI x", true)]
        [TestCase(null, "こんにちは", false)]
        public async NUnitTask SkipsNonJapaneseNonAlphabetAndShortMapInput(string language, string text, bool withMap)
        {
            var handler = new FakeHandler((_, __) => throw new AssertionException("This input must not use HTTP."));
            using (var client = new HttpClient(handler))
            {
                var options = Options(); options.UseKanaMap = withMap;
                var processor = new AlphaToKanaPreprocessor(options, client);
                try
                {
                    Assert.That(await Process(processor, text, language), Is.EqualTo(text));
                    Assert.That(handler.Calls, Is.Zero);
                }
                finally { await processor.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask ForcedFunctionConvertsSpecialWordsLearnsReadingsAndFlattensExtraBody()
        {
            JObject sent = null;
            var handler = new FakeHandler((body, _) =>
            {
                sent = body;
                return UniTask.FromResult(Conversions(("Wi-Fi", "ワイファイ"), ("Python", "パイソン")));
            });
            using (var client = new HttpClient(handler))
            {
                var options = Options();
                options.AlphabetLength = 6;
                options.ReasoningEffort = "none";
                options.ExtraBody = new JObject { ["temperature"] = 0, ["custom"] = new JObject { ["field"] = "value" } };
                var processor = new AlphaToKanaPreprocessor(options, client);
                try
                {
                    Assert.That(await Process(processor, "Wi-Fi Python AI"), Is.EqualTo("ワイファイ パイソン AI"));
                    Assert.That((string)sent["messages"][1]["content"], Is.EqualTo("Wi-Fi\nPython"));
                    Assert.That((string)sent["tool_choice"]["function"]["name"], Is.EqualTo("convert_alphabet_to_kana"));
                    Assert.That((string)sent["tools"][0]["function"]["parameters"]["properties"]["conversions"]["type"], Is.EqualTo("array"));
                    Assert.That((string)sent["reasoning_effort"], Is.EqualTo("none"));
                    Assert.That((int)sent["temperature"], Is.Zero);
                    Assert.That((string)sent["custom"]["field"], Is.EqualTo("value"));
                    Assert.That(sent["extra_body"], Is.Null);
                    Assert.That(await Process(processor, "python WI-FI"), Is.EqualTo("パイソン ワイファイ"));
                    Assert.That(handler.Calls, Is.EqualTo(1));
                    Assert.That(processor.GetKanaMap().Count, Is.EqualTo(2));
                }
                finally { await processor.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask RegexMetacharactersAreLiteralSpecialCharacters()
        {
            JObject sent = null;
            var handler = new FakeHandler((body, _) => { sent = body; return UniTask.FromResult(Conversions(("A]B", "エービー"))); });
            using (var client = new HttpClient(handler))
            {
                var options = Options(); options.SpecialChars = "]\\^-"; options.AlphabetLength = 100;
                var processor = new AlphaToKanaPreprocessor(options, client);
                try
                {
                    Assert.That(await Process(processor, "A]B"), Is.EqualTo("エービー"));
                    Assert.That((string)sent["messages"][1]["content"], Is.EqualTo("A]B"));
                }
                finally { await processor.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask DirectModeConvertsWholeTextAndIgnoresMinimumLength()
        {
            JObject sent = null;
            var handler = new FakeHandler((body, _) => { sent = body; return UniTask.FromResult(Content("prefix <converted>エー\n読み</converted> suffix")); });
            using (var client = new HttpClient(handler))
            {
                var options = Options(); options.UseKanaMap = false; options.AlphabetLength = 100;
                var processor = new AlphaToKanaPreprocessor(options, client);
                try
                {
                    Assert.That(await Process(processor, "A"), Is.EqualTo("エー\n読み"));
                    Assert.That((string)sent["messages"][1]["content"], Is.EqualTo("A"));
                    Assert.That((string)sent["messages"][0]["content"], Is.EqualTo(AlphaToKanaPreprocessorOptions.DefaultSystemPrompt));
                    Assert.That(sent["tools"], Is.Null);
                    Assert.That(processor.GetKanaMap(), Is.Empty);
                }
                finally { await processor.DisposeAsync(); }
            }
        }

        [TestCase("http")]
        [TestCase("json")]
        [TestCase("shape")]
        [TestCase("arguments")]
        [TestCase("transport")]
        public async NUnitTask FailuresKeepPreviouslyMappedText(string kind)
        {
            var handler = new FakeHandler((_, __) =>
            {
                if (kind == "transport") throw new HttpRequestException("private details");
                if (kind == "http") return UniTask.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("private body") });
                if (kind == "json") return UniTask.FromResult(Json("{broken"));
                if (kind == "shape") return UniTask.FromResult(Json("{\"choices\":{}}"));
                return UniTask.FromResult(Json("{\"choices\":[{\"message\":{\"tool_calls\":[{\"function\":{\"arguments\":\"not-json\"}}]}}]}"));
            });
            using (var client = new HttpClient(handler))
            {
                var options = Options(); options.KanaMap["Hello"] = "ハロー";
                var processor = new AlphaToKanaPreprocessor(options, client);
                try
                {
                    Assert.That(await Process(processor, "Hello Python"), Is.EqualTo("ハロー Python"));
                    Assert.That(processor.GetKanaMap().Count, Is.EqualTo(1));
                    Assert.That(handler.Calls, Is.EqualTo(1));
                }
                finally { await processor.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask DirectResponseWithoutConvertedTagsReturnsOriginalText()
        {
            var handler = new FakeHandler((_, __) => UniTask.FromResult(Content("No tags here")));
            using (var client = new HttpClient(handler))
            {
                var options = Options(); options.UseKanaMap = false;
                var processor = new AlphaToKanaPreprocessor(options, client);
                try { Assert.That(await Process(processor, "Hello"), Is.EqualTo("Hello")); }
                finally { await processor.DisposeAsync(); }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask AzureAuthenticationMatchesModeSpecificSourceBehavior(bool withMap)
        {
            var handler = new FakeHandler((_, __) => UniTask.FromResult(Content("<converted>ハロー</converted>")));
            using (var client = new HttpClient(handler))
            {
                var options = Options(); options.UseKanaMap = withMap; options.BaseUrl = "https://sample.azure.com/openai/v1";
                var processor = new AlphaToKanaPreprocessor(options, client);
                try
                {
                    await Process(processor, "Hello");
                    Assert.That(handler.LastUri, Is.EqualTo("https://sample.azure.com/openai/v1/chat/completions"));
                    Assert.That(handler.LastApiKey, Is.EqualTo(withMap ? "test-key" : null));
                    Assert.That(handler.LastAuthorization, Is.EqualTo(withMap ? null : "Bearer test-key"));
                    Assert.That(client.DefaultRequestHeaders.Authorization, Is.Null);
                }
                finally { await processor.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask ConcurrentRequestsLearnDistinctReadingsWithoutMixingTheirResults()
        {
            var firstEntered = NewSignal();
            var releaseFirst = NewSignal();
            var handler = new FakeHandler(async (body, token) =>
            {
                var word = (string)body["messages"][1]["content"];
                if (word == "Python") { firstEntered.TrySetResult(true); await releaseFirst.Task; token.ThrowIfCancellationRequested(); }
                return Conversions((word, word == "Python" ? "パイソン" : "グーグル"));
            });
            using (var client = new HttpClient(handler))
            {
                var processor = new AlphaToKanaPreprocessor(Options(), client);
                try
                {
                    var first = Process(processor, "Python");
                    await Bounded(firstEntered.Task);
                    Assert.That(await Process(processor, "Google"), Is.EqualTo("グーグル"));
                    releaseFirst.TrySetResult(true);
                    Assert.That(await first, Is.EqualTo("パイソン"));
                    Assert.That(processor.GetKanaMap().Keys, Is.EquivalentTo(new[] { "Python", "Google" }));
                }
                finally { releaseFirst.TrySetResult(true); await processor.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask UpdatingOptionsDuringRequestKeepsSnapshotAndDoesNotRepopulateReplacementMap()
        {
            var entered = NewSignal(); var release = NewSignal(); JObject sent = null;
            var handler = new FakeHandler(async (body, token) => { sent = body; entered.TrySetResult(true); await release.Task; token.ThrowIfCancellationRequested(); return Conversions(("Python", "パイソン")); });
            using (var client = new HttpClient(handler))
            {
                var options = Options(); var processor = new AlphaToKanaPreprocessor(options, client);
                var request = new SpeechSynthesisRequest { Text = "Python", Language = "ja-JP" };
                try
                {
                    var operation = processor.ProcessAsync(request, new SpeechSynthesizerOptions());
                    await Bounded(entered.Task);
                    request.Text = "caller changed";
                    var replacement = processor.GetOptions(); replacement.Model = "replacement"; replacement.KanaMap["New"] = "ニュー";
                    processor.UpdateOptions(replacement);
                    release.TrySetResult(true);
                    Assert.That(await operation, Is.EqualTo("パイソン"));
                    Assert.That((string)sent["messages"][1]["content"], Is.EqualTo("Python"));
                    Assert.That((string)sent["model"], Is.EqualTo("gpt-4.1-mini"));
                    Assert.That(processor.GetKanaMap().Keys, Is.EquivalentTo(new[] { "New" }));
                }
                finally { release.TrySetResult(true); await processor.DisposeAsync(); }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask CallerCancellationAndDisposeSettleActiveHttpWithoutTakingInjectedClient(bool dispose)
        {
            var entered = NewSignal();
            var handler = new FakeHandler(async (_, token) => { entered.TrySetResult(true); await UniTask.Never(token); return Content("unreachable"); });
            using (var client = new HttpClient(handler))
            using (var cancellation = new CancellationTokenSource())
            {
                var processor = new AlphaToKanaPreprocessor(Options(), client);
                var operation = processor.ProcessAsync(new SpeechSynthesisRequest { Text = "Hello" }, new SpeechSynthesizerOptions(), cancellation.Token);
                try
                {
                    await Bounded(entered.Task);
                    if (dispose) await Bounded(processor.DisposeAsync()); else cancellation.Cancel();
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await operation);
                    Assert.That(processor.GetKanaMap(), Is.Empty);
                    await Bounded(processor.DisposeAsync());
                    Assert.That(handler.Disposed, Is.False);
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<ObjectDisposedException>(async () => await Process(processor, "Hello"));
                }
                finally { cancellation.Cancel(); await Bounded(processor.DisposeAsync()); }
            }
        }

        [Test]
        public async NUnitTask DisposeClosesBlockedHttpResponseBodyAndWaitsForReadToSettle()
        {
            var stream = new BlockingBodyStream();
            var handler = new FakeHandler((_, __) => UniTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
            using (var client = new HttpClient(handler))
            {
                var processor = new AlphaToKanaPreprocessor(Options(), client);
                var operation = Process(processor, "Hello");
                try
                {
                    await Bounded(stream.ReadStarted.Task);
                    await Bounded(processor.DisposeAsync());
                    await SpeechAsyncAssert.ThrowsInstanceOfAsync<OperationCanceledException>(async () => await operation);
                    Assert.That(stream.Disposed, Is.True);
                    Assert.That(handler.Disposed, Is.False);
                }
                finally { stream.Dispose(); await Bounded(processor.DisposeAsync()); }
            }
        }

        [Test]
        public async NUnitTask ReentrantDisposeFromInjectedTransportFailsPromptly()
        {
            AlphaToKanaPreprocessor processor = null;
            var handler = new FakeHandler(async (_, __) =>
            {
                await SpeechAsyncAssert.ThrowsInstanceOfAsync<InvalidOperationException>(async () => await processor.DisposeAsync());
                await SpeechAsync.Yield();
                return Conversions(("Hello", "ハロー"));
            });
            using (var client = new HttpClient(handler))
            {
                processor = new AlphaToKanaPreprocessor(Options(), client);
                try { Assert.That(await Process(processor, "Hello"), Is.EqualTo("ハロー")); }
                finally { await Bounded(processor.DisposeAsync()); }
            }
        }

        private static AlphaToKanaPreprocessorOptions Options() => new AlphaToKanaPreprocessorOptions { ApiKey = "test-key" };
        private static UniTask<string> Process(AlphaToKanaPreprocessor processor, string text, string language = null) =>
            processor.ProcessAsync(new SpeechSynthesisRequest { Text = text, Language = language }, new SpeechSynthesizerOptions());
        private static SpeechCompletionSource<bool> NewSignal() => new SpeechCompletionSource<bool>();
        private static HttpResponseMessage Json(string json) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        private static HttpResponseMessage Content(string text) => Json(new JObject { ["choices"] = new JArray(new JObject { ["message"] = new JObject { ["content"] = text } }) }.ToString());
        private static HttpResponseMessage Conversions(params (string original, string kana)[] conversions)
        {
            var arguments = new JObject { ["conversions"] = new JArray(conversions.Select(item => new JObject { ["original"] = item.original, ["kana"] = item.kana })) };
            return Json(new JObject { ["choices"] = new JArray(new JObject { ["message"] = new JObject
            { ["tool_calls"] = new JArray(new JObject { ["function"] = new JObject { ["arguments"] = arguments.ToString() } }) } }) }.ToString());
        }
        private static async UniTask Bounded(UniTask task)
        {
            task = SpeechAsync.Share(task);
            Assert.That(await UniTask.WhenAny(task, SpeechAsync.Delay(TimeSpan.FromMilliseconds(3000))), Is.EqualTo(0), "Operation did not settle within three seconds.");
            await task;
        }

        private sealed class BlockingBodyStream : Stream
        {
            internal readonly SpeechCompletionSource<bool> ReadStarted = NewSignal();
            private readonly SpeechCompletionSource<int> read = new SpeechCompletionSource<int>();
            internal bool Disposed;
            public override System.Threading.Tasks.Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            { ReadStarted.TrySetResult(true); return read.Task.AsTask(); }
            protected override void Dispose(bool disposing) { Disposed = true; read.TrySetException(new ObjectDisposedException(nameof(BlockingBodyStream))); base.Dispose(disposing); }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<JObject, CancellationToken, UniTask<HttpResponseMessage>> respond;
            internal int Calls;
            internal bool Disposed;
            internal string LastUri;
            internal string LastApiKey;
            internal string LastAuthorization;
            internal FakeHandler(Func<JObject, CancellationToken, UniTask<HttpResponseMessage>> respond) { this.respond = respond; }
            protected override async System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Interlocked.Increment(ref Calls);
                LastUri = request.RequestUri.AbsoluteUri;
                LastAuthorization = request.Headers.Authorization?.ToString();
                LastApiKey = request.Headers.TryGetValues("api-key", out var keys) ? keys.Single() : null;
                return await respond(JObject.Parse(await request.Content.ReadAsStringAsync()), token);
            }
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        }
    }
}

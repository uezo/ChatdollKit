using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnitTask = System.Threading.Tasks.Task;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.STT;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class HttpSpeechRecognizerTests
    {
        private sealed class FakeHandler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, CancellationToken, UniTask<HttpResponseMessage>> Respond;
            public int Calls;
            public bool Disposed;
            protected override System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Calls);
                return Respond(request, cancellationToken).AsTask();
            }
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        }

        private sealed class Provider
        {
            public Func<byte[], CancellationToken, UniTask<string>> Transcribe;
            public Func<string, byte[], CancellationToken, UniTask<SpeechRecognitionResult>> Recognize;
            public Func<UniTask> Close;
            public Action<Action<Exception>> ObserveErrors;
        }

        private sealed class Part
        {
            public string Headers;
            public byte[] Data;
            public string Text => Encoding.UTF8.GetString(Data);
        }

        private sealed class RequestSnapshot
        {
            public string Uri;
            public string Method;
            public string Authorization;
            public string AzureKey;
            public Dictionary<string, Part> Parts;
        }

        private static Provider CreateProvider(bool azure, HttpClient client, int attempts = 2, double timeout = 10)
        {
            if (azure)
            {
                var recognizer = new AzureSpeechRecognizerClient(new AzureSpeechRecognizerOptions
                {
                    ApiKey = "azure-test-key", Region = "japaneast", MaxAttempts = attempts, TimeoutSeconds = timeout
                }, client);
                return new Provider
                {
                    Transcribe = (pcm, token) => recognizer.TranscribeAsync(pcm, token),
                    Recognize = (id, pcm, token) => recognizer.RecognizeAsync(id, pcm, token),
                    Close = recognizer.DisposeAsync, ObserveErrors = handler => recognizer.Error += handler
                };
            }
            else
            {
                var recognizer = new OpenAISpeechRecognizerClient(new OpenAISpeechRecognizerOptions
                {
                    ApiKey = "openai-test-key", MinDataLength = 0, MaxAttempts = attempts, TimeoutSeconds = timeout
                }, client);
                return new Provider
                {
                    Transcribe = (pcm, token) => recognizer.TranscribeAsync(pcm, token),
                    Recognize = (id, pcm, token) => recognizer.RecognizeAsync(id, pcm, token),
                    Close = recognizer.DisposeAsync, ObserveErrors = handler => recognizer.Error += handler
                };
            }
        }

        private static HttpResponseMessage Response(bool azure, string text = "recognized", HttpStatusCode status = HttpStatusCode.OK)
            => new HttpResponseMessage(status)
            {
                Content = new StringContent(azure ? "{\"combinedPhrases\":[{\"text\":\"" + text + "\"}]}" : "{\"text\":\"" + text + "\"}", Encoding.UTF8, "application/json")
            };

        private static byte[] Pcm(short amplitude = 1000, int count = 2048)
        {
            var bytes = new byte[count * 2];
            for (int i = 0; i < count; i++) { bytes[i * 2] = (byte)amplitude; bytes[i * 2 + 1] = (byte)(amplitude >> 8); }
            return bytes;
        }

        private static SpeechCompletionSource<T> Completion<T>() => new SpeechCompletionSource<T>();

        private static async UniTask AwaitEnteredAsync(UniTask entered, UniTask operation)
        {
            await UniTask.WhenAny(entered, operation);
            if (!entered.Status.IsCompleted())
            {
                await operation;
                Assert.Fail("Recognition completed before the expected HTTP request started.");
            }
            await entered;
        }

        // Read serialized MIME instead of depending on a particular HttpContent implementation.
        private static async UniTask<RequestSnapshot> SnapshotAsync(HttpRequestMessage request)
        {
            byte[] body = await request.Content.ReadAsByteArrayAsync();
            string boundary = request.Content.Headers.ContentType.Parameters.Single(parameter => parameter.Name == "boundary").Value.Trim('"');
            byte[] marker = Encoding.ASCII.GetBytes("--" + boundary);
            byte[] nextMarker = Encoding.ASCII.GetBytes("\r\n--" + boundary);
            byte[] headerEnd = Encoding.ASCII.GetBytes("\r\n\r\n");
            var parts = new Dictionary<string, Part>();
            int position = IndexOf(body, marker, 0);
            Assert.That(position, Is.GreaterThanOrEqualTo(0));
            while (position >= 0)
            {
                int start = position + marker.Length;
                if (body[start] == '-' && body[start + 1] == '-') break;
                start += 2;
                int separator = IndexOf(body, headerEnd, start);
                Assert.That(separator, Is.GreaterThanOrEqualTo(start));
                string headers = Encoding.ASCII.GetString(body, start, separator - start);
                int end = IndexOf(body, nextMarker, separator + headerEnd.Length);
                Assert.That(end, Is.GreaterThanOrEqualTo(separator));
                var nameMatch = Regex.Match(headers, @"(?:^|;)\s*name=(?:""([^""]*)""|([^;\r\n]*))", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                Assert.That(nameMatch.Success, Is.True, headers);
                string name = nameMatch.Groups[1].Success ? nameMatch.Groups[1].Value : nameMatch.Groups[2].Value;
                var data = new byte[end - separator - headerEnd.Length];
                Buffer.BlockCopy(body, separator + headerEnd.Length, data, 0, data.Length);
                parts.Add(name, new Part { Headers = headers, Data = data });
                position = end + 2;
            }
            return new RequestSnapshot
            {
                Uri = request.RequestUri.ToString(), Method = request.Method.Method,
                Authorization = request.Headers.Authorization?.ToString(),
                AzureKey = request.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out var values) ? values.Single() : null,
                Parts = parts
            };
        }

        private static int IndexOf(byte[] bytes, byte[] pattern, int start)
        {
            for (int i = start; i <= bytes.Length - pattern.Length; i++)
            {
                int j = 0;
                while (j < pattern.Length && bytes[i + j] == pattern[j]) j++;
                if (j == pattern.Length) return i;
            }
            return -1;
        }

        private static int ReadInt32(byte[] bytes, int offset)
            => bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24;

        private static void AssertWave(byte[] wav, byte[] pcm, int sampleRate)
        {
            Assert.That(wav.Length, Is.EqualTo(pcm.Length + 44));
            Assert.That(Encoding.ASCII.GetString(wav, 0, 4), Is.EqualTo("RIFF"));
            Assert.That(Encoding.ASCII.GetString(wav, 8, 4), Is.EqualTo("WAVE"));
            Assert.That(ReadInt32(wav, 4), Is.EqualTo(wav.Length - 8));
            Assert.That(wav[22], Is.EqualTo(1));
            Assert.That(wav[23], Is.Zero);
            Assert.That(ReadInt32(wav, 24), Is.EqualTo(sampleRate));
            Assert.That(ReadInt32(wav, 28), Is.EqualTo(sampleRate * 2));
            Assert.That(wav[34], Is.EqualTo(16));
            Assert.That(ReadInt32(wav, 40), Is.EqualTo(pcm.Length));
            CollectionAssert.AreEqual(pcm, wav.Skip(44));
        }

        [Test]
        public async NUnitTask OpenAIDefaultsAndWireUseGptTranscribeLanguagesArrayAndWave()
        {
            RequestSnapshot request = null;
            var handler = new FakeHandler { Respond = async (message, token) => { request = await SnapshotAsync(message); return Response(false, "こんにちは"); } };
            using (var client = new HttpClient(handler))
            {
                var recognizer = new OpenAISpeechRecognizerClient(new OpenAISpeechRecognizerOptions { ApiKey = "test-key", Language = "ja-JP" }, client);
                try
                {
                    var options = (OpenAISpeechRecognizerOptions)recognizer.GetOptions();
                    Assert.That(options.Model, Is.EqualTo("gpt-transcribe"));
                    Assert.That(options.MinDataLength, Is.EqualTo(4096));
                    Assert.That(options.SampleRate, Is.EqualTo(16000));
                    Assert.That(options.TimeoutSeconds, Is.EqualTo(10));
                    Assert.That(options.MaxAttempts, Is.EqualTo(2));
                    Assert.That(new OpenAISpeechRecognizerOptions().Language, Is.EqualTo("ja"));
                    byte[] pcm = Pcm(-12345);
                    Assert.That(await recognizer.TranscribeAsync(pcm), Is.EqualTo("こんにちは"));
                    Assert.That(request.Uri, Is.EqualTo("https://api.openai.com/v1/audio/transcriptions"));
                    Assert.That(request.Method, Is.EqualTo("POST"));
                    Assert.That(request.Authorization, Is.EqualTo("Bearer test-key"));
                    Assert.That(request.Parts["model"].Text, Is.EqualTo("gpt-transcribe"));
                    Assert.That(request.Parts["response_format"].Text, Is.EqualTo("json"));
                    Assert.That(request.Parts["languages[]"].Text, Is.EqualTo("ja"));
                    Assert.That(request.Parts.ContainsKey("language"), Is.False);
                    StringAssert.Contains("voice.wav", request.Parts["file"].Headers);
                    StringAssert.Contains("audio/wav", request.Parts["file"].Headers);
                    AssertWave(request.Parts["file"].Data, pcm, 16000);
                }
                finally { await recognizer.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask OpenAIShortDataSkipsHttpButExactMinimumIsSent()
        {
            var handler = new FakeHandler { Respond = (request, token) => UniTask.FromResult(Response(false)) };
            using (var client = new HttpClient(handler))
            {
                var recognizer = new OpenAISpeechRecognizerClient(new OpenAISpeechRecognizerOptions { ApiKey = "test-key" }, client);
                try
                {
                    Assert.That(await recognizer.TranscribeAsync(Pcm(count: 2047)), Is.Null);
                    Assert.That(handler.Calls, Is.Zero);
                    Assert.That(await recognizer.TranscribeAsync(Pcm(count: 2048)), Is.EqualTo("recognized"));
                    Assert.That(handler.Calls, Is.EqualTo(1));
                }
                finally { await recognizer.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask OpenAIAlternativeLanguagesOmitHintsAndCustomBaseUrlTrimsTrailingSlash()
        {
            RequestSnapshot request = null;
            var handler = new FakeHandler { Respond = async (message, token) => { request = await SnapshotAsync(message); return Response(false); } };
            using (var client = new HttpClient(handler))
            {
                var recognizer = new OpenAISpeechRecognizerClient(new OpenAISpeechRecognizerOptions
                {
                    ApiKey = "test-key", BaseUrl = "https://stt.example/v1/", Language = "ja-JP", AlternativeLanguages = new[] { "en-US" }
                }, client);
                try
                {
                    await recognizer.TranscribeAsync(Pcm());
                    Assert.That(request.Uri, Is.EqualTo("https://stt.example/v1/audio/transcriptions"));
                    Assert.That(request.Parts.ContainsKey("languages[]"), Is.False);
                    Assert.That(request.Parts.ContainsKey("language"), Is.False);
                    Assert.That(request.Parts.ContainsKey("alternative_languages"), Is.False);
                }
                finally { await recognizer.DisposeAsync(); }
            }
        }

        [Test]
        public async NUnitTask AzureDefaultsLocalesDefinitionAndFirstCombinedPhraseMatchSource()
        {
            RequestSnapshot request = null;
            var handler = new FakeHandler
            {
                Respond = async (message, token) =>
                {
                    request = await SnapshotAsync(message);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"combinedPhrases\":[{\"text\":\"first\"},{\"text\":\"second\"}]}") };
                }
            };
            using (var client = new HttpClient(handler))
            {
                var recognizer = new AzureSpeechRecognizerClient(new AzureSpeechRecognizerOptions
                {
                    ApiKey = "azure-key", Region = "japaneast", AlternativeLanguages = new[] { "en-US" }
                }, client);
                try
                {
                    var options = (AzureSpeechRecognizerOptions)recognizer.GetOptions();
                    Assert.That(options.Language, Is.EqualTo("ja-JP"));
                    Assert.That(options.SampleRate, Is.EqualTo(16000));
                    Assert.That(options.TimeoutSeconds, Is.EqualTo(5));
                    Assert.That(options.MaxAttempts, Is.EqualTo(2));
                    Assert.That(options.ApiVersion, Is.EqualTo("2024-11-15"));
                    byte[] pcm = Pcm(1234, 3);
                    Assert.That(await recognizer.TranscribeAsync(pcm), Is.EqualTo("first"));
                    Assert.That(request.Uri, Is.EqualTo("https://japaneast.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version=2024-11-15"));
                    Assert.That(request.Method, Is.EqualTo("POST"));
                    Assert.That(request.AzureKey, Is.EqualTo("azure-key"));
                    Assert.That(request.Authorization, Is.Null);
                    string definition = Regex.Replace(request.Parts["definition"].Text, @"\s+", "");
                    StringAssert.Contains("\"locales\":[\"ja-JP\",\"en-US\"]", definition);
                    StringAssert.Contains("\"channels\":[0,1]", definition);
                    StringAssert.Contains("application/json", request.Parts["definition"].Headers);
                    AssertWave(request.Parts["audio"].Data, pcm, 16000);
                }
                finally { await recognizer.DisposeAsync(); }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask ServerFailureRetriesWithIdenticalWavePayload(bool azure)
        {
            var requests = new List<RequestSnapshot>();
            var handler = new FakeHandler
            {
                Respond = async (message, token) =>
                {
                    requests.Add(await SnapshotAsync(message));
                    return Response(azure, status: requests.Count == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
                }
            };
            using (var client = new HttpClient(handler))
            {
                var provider = CreateProvider(azure, client);
                try
                {
                    byte[] pcm = Pcm();
                    Assert.That(await provider.Transcribe(pcm, default), Is.EqualTo("recognized"));
                    Assert.That(handler.Calls, Is.EqualTo(2));
                    string audioPart = azure ? "audio" : "file";
                    AssertWave(requests[0].Parts[audioPart].Data, pcm, 16000);
                    CollectionAssert.AreEqual(requests[0].Parts[audioPart].Data, requests[1].Parts[audioPart].Data);
                }
                finally { await provider.Close(); }
            }
        }

        [TestCase(false, 400)]
        [TestCase(false, 408)]
        [TestCase(false, 429)]
        [TestCase(true, 400)]
        [TestCase(true, 408)]
        [TestCase(true, 429)]
        public async NUnitTask ClientErrorsReturnNullWithoutRetry(bool azure, int status)
        {
            var handler = new FakeHandler { Respond = (request, token) => UniTask.FromResult(Response(azure, status: (HttpStatusCode)status)) };
            using (var client = new HttpClient(handler))
            {
                var provider = CreateProvider(azure, client);
                try
                {
                    Assert.That(await provider.Transcribe(Pcm(), default), Is.Null);
                    Assert.That(handler.Calls, Is.EqualTo(1));
                }
                finally { await provider.Close(); }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask CommunicationErrorRetriesAndExhaustedServerErrorsUseTotalAttemptCount(bool azure)
        {
            int attempt = 0;
            var handler = new FakeHandler
            {
                Respond = (request, token) => ++attempt == 1
                    ? UniTask.FromException<HttpResponseMessage>(new HttpRequestException("network test"))
                    : UniTask.FromResult(Response(azure))
            };
            using (var client = new HttpClient(handler))
            {
                var provider = CreateProvider(azure, client);
                try
                {
                    Assert.That(await provider.Transcribe(Pcm(), default), Is.EqualTo("recognized"));
                    Assert.That(handler.Calls, Is.EqualTo(2));
                    handler.Respond = (request, token) => UniTask.FromResult(Response(azure, status: HttpStatusCode.InternalServerError));
                    Assert.That(await provider.Transcribe(Pcm(), default), Is.Null);
                    Assert.That(handler.Calls, Is.EqualTo(4), "MaxAttempts=2 includes the initial attempt.");
                }
                finally { await provider.Close(); }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask MalformedOrMissingTranscriptionReturnsNullWithoutHttpRetry(bool azure)
        {
            var bodies = new Queue<string>(new[] { "not json", "{}", azure ? "{\"combinedPhrases\":[]}" : "{\"text\":null}" });
            var handler = new FakeHandler
            {
                Respond = (request, token) => UniTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(bodies.Dequeue()) })
            };
            using (var client = new HttpClient(handler))
            {
                var provider = CreateProvider(azure, client);
                try
                {
                    for (int call = 1; call <= 3; call++)
                    {
                        Assert.That(await provider.Transcribe(Pcm(), default), Is.Null);
                        Assert.That(handler.Calls, Is.EqualTo(call));
                    }
                }
                finally { await provider.Close(); }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask ErrorNotificationsExcludeResponseBodyAndApiKey(bool azure)
        {
            const string privateBody = "PRIVATE_TRANSCRIPTION_RESPONSE_BODY";
            var handler = new FakeHandler
            {
                Respond = (request, token) => UniTask.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(privateBody) })
            };
            using (var client = new HttpClient(handler))
            {
                var provider = CreateProvider(azure, client);
                var errors = new List<Exception>();
                provider.ObserveErrors(errors.Add);
                try
                {
                    Assert.That(await provider.Transcribe(Pcm(), default), Is.Null);
                    Assert.That(errors, Is.Not.Empty);
                    foreach (var error in errors)
                    {
                        StringAssert.DoesNotContain(privateBody, error.ToString());
                        StringAssert.DoesNotContain(azure ? "azure-test-key" : "openai-test-key", error.ToString());
                    }
                }
                finally { await provider.Close(); }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask CallerCancellationAbortsHttpAndDoesNotRetry(bool azure)
        {
            var entered = Completion<bool>();
            var handler = new FakeHandler
            {
                Respond = async (request, token) => { entered.TrySetResult(true); await SpeechAsync.Delay(Timeout.InfiniteTimeSpan, token); return Response(azure); }
            };
            using (var client = new HttpClient(handler))
            using (var cancellation = new CancellationTokenSource())
            {
                var provider = CreateProvider(azure, client);
                var operation = provider.Transcribe(Pcm(), cancellation.Token);
                try
                {
                    await AwaitEnteredAsync(entered.Task, operation);
                    cancellation.Cancel();
                    try { await operation; Assert.Fail("Cancellation must propagate."); }
                    catch (OperationCanceledException) { }
                    Assert.That(handler.Calls, Is.EqualTo(1));
                }
                finally { cancellation.Cancel(); try { await operation; } catch (OperationCanceledException) { } await provider.Close(); }
            }
        }

        [Test]
        public async NUnitTask RequestTimeoutRetriesAndReturnsNullWithoutMutatingSharedClientTimeout()
        {
            var handler = new FakeHandler
            {
                Respond = async (request, token) => { await SpeechAsync.Delay(Timeout.InfiniteTimeSpan, token); return Response(false); }
            };
            using (var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan })
            using (var testLimit = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                var provider = CreateProvider(false, client, timeout: 0.01);
                try
                {
                    Assert.That(await provider.Transcribe(Pcm(), testLimit.Token), Is.Null);
                    Assert.That(handler.Calls, Is.EqualTo(2));
                    Assert.That(client.Timeout, Is.EqualTo(Timeout.InfiniteTimeSpan));
                }
                finally { await provider.Close(); }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask DisposeCancelsInflightRequestAndPreservesInjectedHttpClient(bool azure)
        {
            var entered = Completion<CancellationToken>();
            var handler = new FakeHandler
            {
                Respond = async (request, token) =>
                {
                    if (request.RequestUri.Host == "ownership.example") return Response(azure);
                    entered.TrySetResult(token);
                    await SpeechAsync.Delay(Timeout.InfiniteTimeSpan, token);
                    return Response(azure);
                }
            };
            using (var client = new HttpClient(handler))
            using (var emergencyCancellation = new CancellationTokenSource())
            {
                var provider = CreateProvider(azure, client);
                var operation = provider.Transcribe(Pcm(), emergencyCancellation.Token);
                UniTask? closing = null;
                try
                {
                    await AwaitEnteredAsync(entered.Task, operation);
                    var requestToken = await entered.Task;
                    closing = provider.Close();
                    Assert.That(requestToken.IsCancellationRequested, Is.True);
                    try { await operation; Assert.Fail("Disposal must cancel the active recognition."); }
                    catch (OperationCanceledException) { }
                    await closing.Value;
                    await provider.Close();
                    Assert.That(handler.Disposed, Is.False);
                    using (var response = await client.GetAsync("https://ownership.example/"))
                        Assert.That(response.IsSuccessStatusCode, Is.True);
                }
                finally
                {
                    emergencyCancellation.Cancel();
                    try { await operation; } catch (OperationCanceledException) { }
                    if (closing != null) await closing.Value;
                    else await provider.Close();
                }
            }
        }

        [Test]
        public async NUnitTask SharedHttpClientKeepsConcurrentProviderKeysAndAudioSeparate()
        {
            var bothEntered = Completion<bool>();
            var release = Completion<bool>();
            var requests = new Dictionary<string, RequestSnapshot>();
            var handler = new FakeHandler
            {
                Respond = async (request, token) =>
                {
                    var snapshot = await SnapshotAsync(request);
                    lock (requests)
                    {
                        requests.Add(snapshot.Authorization, snapshot);
                        if (requests.Count == 2) bothEntered.TrySetResult(true);
                    }
                    await release.Task;
                    return Response(false, snapshot.Authorization.EndsWith("key-a") ? "a" : "b");
                }
            };
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(77) })
            {
                client.DefaultRequestHeaders.Add("X-Existing", "kept");
                var first = new OpenAISpeechRecognizerClient(new OpenAISpeechRecognizerOptions { ApiKey = "key-a" }, client);
                var second = new OpenAISpeechRecognizerClient(new OpenAISpeechRecognizerOptions { ApiKey = "key-b" }, client);
                byte[] pcmA = Pcm(1000), pcmB = Pcm(2000);
                var taskA = first.RecognizeAsync("session-a", pcmA);
                var taskB = second.RecognizeAsync("session-b", pcmB);
                try
                {
                    await UniTask.WhenAny(bothEntered.Task, taskA, taskB);
                    if (!bothEntered.Task.Status.IsCompleted())
                    {
                        if (taskA.Status.IsCompleted()) await taskA;
                        if (taskB.Status.IsCompleted()) await taskB;
                        Assert.Fail("Both HTTP requests must be able to enter concurrently.");
                    }
                    Assert.That(taskA.Status.IsCompleted(), Is.False);
                    Assert.That(taskB.Status.IsCompleted(), Is.False);
                    release.TrySetResult(true);
                    Assert.That((await taskA).Text, Is.EqualTo("a"));
                    Assert.That((await taskB).Text, Is.EqualTo("b"));
                    AssertWave(requests["Bearer key-a"].Parts["file"].Data, pcmA, 16000);
                    AssertWave(requests["Bearer key-b"].Parts["file"].Data, pcmB, 16000);
                    Assert.That(client.DefaultRequestHeaders.Authorization, Is.Null);
                    Assert.That(client.DefaultRequestHeaders.GetValues("X-Existing").Single(), Is.EqualTo("kept"));
                    Assert.That(client.Timeout, Is.EqualTo(TimeSpan.FromSeconds(77)));
                }
                finally
                {
                    release.TrySetResult(true);
                    await UniTask.WhenAll(taskA, taskB);
                    await first.DisposeAsync();
                    await second.DisposeAsync();
                }
            }
        }

        [Test]
        public async NUnitTask OptionsUpdatesAreCopiedAndDoNotChangeAnInflightRetry()
        {
            var entered = Completion<bool>();
            var release = Completion<bool>();
            var requests = new List<RequestSnapshot>();
            var handler = new FakeHandler
            {
                Respond = async (request, token) =>
                {
                    requests.Add(await SnapshotAsync(request));
                    if (requests.Count == 1)
                    {
                        entered.TrySetResult(true);
                        await release.Task;
                        return Response(false, status: HttpStatusCode.ServiceUnavailable);
                    }
                    return Response(false);
                }
            };
            using (var client = new HttpClient(handler))
            {
                var supplied = new OpenAISpeechRecognizerOptions { ApiKey = "original-key", BaseUrl = "https://old.example/v1", AlternativeLanguages = new[] { "en-US" } };
                var recognizer = new OpenAISpeechRecognizerClient(supplied, client);
                var operation = recognizer.TranscribeAsync(Pcm());
                try
                {
                    await AwaitEnteredAsync(entered.Task, operation);
                    var updated = (OpenAISpeechRecognizerOptions)recognizer.GetOptions();
                    updated.ApiKey = "updated-key";
                    updated.BaseUrl = "https://new.example/v1";
                    updated.AlternativeLanguages[0] = "fr-FR";
                    Assert.That(((OpenAISpeechRecognizerOptions)recognizer.GetOptions()).AlternativeLanguages[0], Is.EqualTo("en-US"));
                    recognizer.UpdateOptions(updated);
                    updated.ApiKey = "mutated-after-update";
                    updated.AlternativeLanguages[0] = "de-DE";
                    Assert.That(((OpenAISpeechRecognizerOptions)recognizer.GetOptions()).ApiKey, Is.EqualTo("updated-key"));
                    Assert.That(((OpenAISpeechRecognizerOptions)recognizer.GetOptions()).AlternativeLanguages[0], Is.EqualTo("fr-FR"));
                    release.TrySetResult(true);
                    Assert.That(await operation, Is.EqualTo("recognized"));
                    Assert.That(requests.Count, Is.EqualTo(2));
                    Assert.That(requests[0].Authorization, Is.EqualTo("Bearer original-key"));
                    Assert.That(requests[1].Authorization, Is.EqualTo("Bearer original-key"));
                    Assert.That(requests[1].Uri, Is.EqualTo("https://old.example/v1/audio/transcriptions"));
                    Assert.That(await recognizer.TranscribeAsync(Pcm()), Is.EqualTo("recognized"));
                    Assert.That(requests[2].Authorization, Is.EqualTo("Bearer updated-key"));
                    Assert.That(requests[2].Uri, Is.EqualTo("https://new.example/v1/audio/transcriptions"));
                }
                finally { release.TrySetResult(true); await operation; await recognizer.DisposeAsync(); }
            }
        }
    }
}

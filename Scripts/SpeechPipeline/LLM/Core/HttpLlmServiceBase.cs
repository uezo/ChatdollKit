using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.LLM
{
    /// <summary>Native-platform streaming HTTP. Injected clients remain caller-owned.</summary>
    public abstract class HttpLlmServiceBase : LlmServiceBase
    {
        private readonly HttpClient client;
        private readonly bool ownsClient;

        protected HttpLlmServiceBase(LlmServiceOptions options, HttpClient httpClient = null) : base(options)
        {
            ownsClient = httpClient == null;
            client = httpClient ?? CreateClient();
        }

        private static HttpClient CreateClient()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException("HTTP LLM services require a native platform; WebGL needs a UnityWebRequest transport.");
#else
            return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, MaxConnectionsPerServer = 100 })
            { Timeout = Timeout.InfiniteTimeSpan };
#endif
        }

        // Request creation failures are returned separately so only they can trigger missing-ID recovery.
        protected async UniTask<LlmError> StreamRequestAsync(string path, JObject body, LlmServiceOptions options,
            Func<string, UniTask<bool>> onData, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, options.BaseUrl.TrimEnd('/') + "/" + path))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                try { response = await SpeechAsync.FromTask(client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)); }
                catch (HttpRequestException)
                {
                    token.ThrowIfCancellationRequested();
                    throw LlmErrorParser.Failure("transport_error", "LLM HTTP request failed.");
                }
                using (response)
                using (var responseCancellation = token.Register(() => { try { response.Dispose(); } catch { } }))
                {
                    token.ThrowIfCancellationRequested();
                    if (!response.IsSuccessStatusCode)
                    {
                        var payload = await ReadErrorAsync(response, token);
                        return LlmErrorParser.Parse(payload, (int)response.StatusCode);
                    }
                    Stream stream;
                    try { stream = await SpeechAsync.FromTask(response.Content.ReadAsStreamAsync()); }
                    catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is HttpRequestException)
                    {
                        token.ThrowIfCancellationRequested();
                        throw LlmErrorParser.Failure("stream_read_error", "LLM response stream could not be opened.");
                    }
                    await LlmSseReader.ReadAsync(stream, onData, token);
                    return null;
                }
            }
        }

        private static async UniTask<JObject> ReadErrorAsync(HttpResponseMessage response, CancellationToken token)
        {
            if (response.Content == null) return null;
            try
            {
                using (var stream = await SpeechAsync.FromTask(response.Content.ReadAsStreamAsync()))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var buffer = new char[65536];
                    var length = 0;
                    while (length < buffer.Length)
                    {
                        token.ThrowIfCancellationRequested();
                        var count = await SpeechAsync.FromTask(reader.ReadAsync(buffer, length, buffer.Length - length));
                        if (count == 0) return JObject.Parse(new string(buffer, 0, length));
                        length += count;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is JsonException || ex is HttpRequestException)
            { token.ThrowIfCancellationRequested(); }
            token.ThrowIfCancellationRequested();
            return null;
        }

        protected static JObject ParseEvent(string data)
        {
            try { return JObject.Parse(data); }
            catch (JsonException) { throw LlmErrorParser.Failure("invalid_json", "LLM stream returned invalid JSON."); }
        }

        protected override UniTask DisposeResourcesAsync()
        {
            if (ownsClient) client.Dispose();
            return base.DisposeResourcesAsync();
        }
    }
}

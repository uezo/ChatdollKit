using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.TTS
{
    public sealed class SpeechSynthesisException : Exception
    {
        public HttpStatusCode? StatusCode { get; }
        public string Code { get; }
        public SpeechSynthesisException(string message, string code = "synthesis_error", HttpStatusCode? statusCode = null)
            : base(message) { Code = code; StatusCode = statusCode; }
    }

    public abstract class HttpSpeechSynthesizerBase : SpeechSynthesizerBase
    {
        private readonly HttpClient client;
        private readonly bool ownsClient;
        protected HttpSpeechSynthesizerBase(SpeechSynthesizerOptions options, HttpClient httpClient = null) : base(options)
        {
            ownsClient = httpClient == null;
            client = httpClient ?? CreateClient();
        }
        private static HttpClient CreateClient()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException("Native HTTP synthesis requires a UnityWebRequest transport for WebGL.");
#else
            return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, MaxConnectionsPerServer = 1 })
            { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
#endif
        }

        protected async UniTask<byte[]> SendBytesAsync(HttpRequestMessage request, CancellationToken token)
        {
            using (request)
            {
                try
                {
                    using (var response = await SpeechAsync.FromTask(client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)))
                    using (token.Register(() => { try { response.Dispose(); } catch { } }))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!response.IsSuccessStatusCode)
                            throw new SpeechSynthesisException("Speech synthesis returned HTTP " + (int)response.StatusCode + ".", "http_error", response.StatusCode);
                        if (response.Content == null) return Array.Empty<byte>();
                        using (var input = await SpeechAsync.FromTask(response.Content.ReadAsStreamAsync()))
                        using (var output = new MemoryStream())
                        {
                            await SpeechAsync.FromTask(input.CopyToAsync(output, 81920, token));
                            token.ThrowIfCancellationRequested();
                            return output.ToArray();
                        }
                    }
                }
                catch (Exception error) when (error is IOException || error is HttpRequestException || error is ObjectDisposedException)
                {
                    token.ThrowIfCancellationRequested();
                    throw new SpeechSynthesisException("Speech synthesis transport failed.", "transport_error");
                }
            }
        }

        protected async UniTask<JObject> SendJsonAsync(HttpRequestMessage request, CancellationToken token)
        {
            var bytes = await SendBytesAsync(request, token);
            try { return JObject.Parse(Encoding.UTF8.GetString(bytes)); }
            catch (JsonException) { throw new SpeechSynthesisException("Speech synthesis returned invalid JSON.", "invalid_json"); }
        }
        protected static HttpRequestMessage JsonRequest(HttpMethod method, string url, JObject body) => new HttpRequestMessage(method, url)
        { Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json") };
        protected override UniTask DisposeResourcesAsync()
        {
            if (ownsClient) client.Dispose();
            return UniTask.CompletedTask;
        }
    }
}

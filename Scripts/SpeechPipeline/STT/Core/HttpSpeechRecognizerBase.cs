// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.STT
{
    /// <summary>HTTP provider failure without request headers, audio, or response body.</summary>
    public sealed class SpeechRecognitionException : Exception
    {
        public HttpStatusCode? StatusCode { get; }
        public int Attempt { get; }
        public SpeechRecognitionException(string message, int attempt, HttpStatusCode? statusCode = null)
            : base(message) { Attempt = attempt; StatusCode = statusCode; }
    }

    /// <summary>Native-platform HTTP implementation. An injected HttpClient remains caller-owned.</summary>
    public abstract class HttpSpeechRecognizerBase : SpeechRecognizerBase
    {
        private readonly HttpClient client;
        private readonly bool ownsClient;
        public event Action<Exception> Error;

        protected HttpSpeechRecognizerBase(SpeechRecognizerOptions options, HttpClient httpClient = null) : base(options)
        {
            ownsClient = httpClient == null;
            client = httpClient ?? CreateClient();
        }

        private static HttpClient CreateClient()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException("These HTTP speech recognizers require a native platform; WebGL needs a UnityWebRequest transport.");
#else
            return new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                MaxConnectionsPerServer = 100
            }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
#endif
        }

        /// <summary>Retries transport failures, timeouts and HTTP 5xx, with a fresh request each time.
        /// MaxAttempts includes the first attempt. HTTP 4xx (including 429) is not retried, matching AIAvatarKit.</summary>
        protected async UniTask<JObject> SendJsonAsync(Func<HttpRequestMessage> createRequest,
            SpeechRecognizerOptions options, CancellationToken token)
        {
            for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                using (var request = createRequest())
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    using var timeoutTimer = SpeechAsync.Timeout(timeout, TimeSpan.FromSeconds(options.TimeoutSeconds));
                    try
                    {
                        using (var response = await SpeechAsync.FromTask(client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)))
                        {
                            timeout.Token.ThrowIfCancellationRequested();
                            if (!response.IsSuccessStatusCode)
                            {
                                var retry = (int)response.StatusCode >= 500 && attempt < options.MaxAttempts;
                                if (!retry) ReportError(new SpeechRecognitionException(
                                    "Speech recognition returned HTTP " + (int)response.StatusCode + ".", attempt, response.StatusCode));
                                if (!retry) return null;
                                continue;
                            }
                            var json = response.Content == null ? string.Empty : await SpeechAsync.FromTask(response.Content.ReadAsStringAsync());
                            timeout.Token.ThrowIfCancellationRequested();
                            try { return JObject.Parse(json); }
                            catch (JsonException)
                            {
                                ReportError(new SpeechRecognitionException("Speech recognition returned invalid JSON.", attempt));
                                return null;
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        if (attempt == options.MaxAttempts)
                            ReportError(new SpeechRecognitionException("Speech recognition request timed out or its HTTP client was cancelled.", attempt));
                    }
                    catch (HttpRequestException)
                    {
                        token.ThrowIfCancellationRequested();
                        if (attempt == options.MaxAttempts)
                            ReportError(new SpeechRecognitionException("Speech recognition transport failed.", attempt));
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            return null;
        }

        protected void ReportError(Exception exception)
        {
            var handlers = Error;
            if (handlers == null) return;
            foreach (Action<Exception> handler in handlers.GetInvocationList())
                try { handler(exception); } catch { /* Observers must not break transcription. */ }
        }

        protected override void DisposeResources()
        {
            if (ownsClient) client.Dispose();
            base.DisposeResources();
        }

        protected static string TextValue(JToken token) => token?.Type == JTokenType.String ? (string)token : null;
        protected static void ValidateApiKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("An API key is required and must not contain line breaks.", nameof(key));
        }
        protected static void ValidateBaseUrl(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("Use an absolute HTTPS base URL without credentials, query or fragment (HTTP is allowed for loopback tests).");
        }
    }
}

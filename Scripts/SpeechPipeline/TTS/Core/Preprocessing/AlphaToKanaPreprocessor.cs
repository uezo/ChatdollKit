using ChatdollKit.SpeechPipeline.TTS;
// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.TTS.Preprocessing
{
    public sealed class AlphaToKanaPreprocessorOptions
    {
        public const string DefaultSystemPrompt = "与えられた文字列に含まれる外国語（アルファベット、中国語、ハングルなど）をカタカナ読みに変換してください。変換後の文字列で置換した文章全体を<converted>~</converted>に出力してください。";
        public const string DefaultSystemPromptWithCache = "与えられたアルファベット文字列をカタカナ読みに変換してください。convert_alphabet_to_kana関数で出力すること。originalにはアポストロフィーやピリオド等の記号も省略せずに出力すること。";
        public string ApiKey { get; set; }
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public string Model { get; set; } = "gpt-4.1-mini";
        public string ReasoningEffort { get; set; }
        public JObject ExtraBody { get; set; }
        public string SystemPrompt { get; set; } = DefaultSystemPrompt;
        public string SystemPromptWithCache { get; set; } = DefaultSystemPromptWithCache;
        public int AlphabetLength { get; set; } = 3;
        public string SpecialChars { get; set; } = ".'-'−–";
        public bool UseKanaMap { get; set; } = true;
        public Dictionary<string, string> KanaMap { get; set; } = new Dictionary<string, string>();
        public double TimeoutSeconds { get; set; } = 10;

        public AlphaToKanaPreprocessorOptions Copy()
        {
            var copy = (AlphaToKanaPreprocessorOptions)MemberwiseClone();
            copy.ExtraBody = (JObject)ExtraBody?.DeepClone();
            copy.KanaMap = KanaMap == null ? new Dictionary<string, string>() : new Dictionary<string, string>(KanaMap);
            return copy;
        }
        public void Validate()
        {
            SpeechSynthesisValidation.ApiKey(ApiKey);
            SpeechSynthesisValidation.HttpUrl(BaseUrl);
            if (string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("A model is required.", nameof(Model));
            if (AlphabetLength < 0) throw new ArgumentOutOfRangeException(nameof(AlphabetLength));
            if (SpecialChars == null) throw new ArgumentNullException(nameof(SpecialChars));
            if (double.IsNaN(TimeoutSeconds) || double.IsInfinity(TimeoutSeconds) || TimeoutSeconds <= 0 || TimeoutSeconds > int.MaxValue / 1000.0)
                throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds));
            foreach (var pair in KanaMap ?? new Dictionary<string, string>())
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null) throw new ArgumentException("KanaMap requires nonempty keys and nonnull values.", nameof(KanaMap));
        }
    }

    /// <summary>Japanese pronunciation preprocessing with an optional learned, in-memory reading dictionary.
    /// Injected HttpClient instances remain caller-owned. This preprocessor must be disposed by its owner.</summary>
    public sealed class AlphaToKanaPreprocessor : ITtsPreprocessor
    {
        private static readonly Regex ConvertedPattern = new Regex("<converted>(.*?)</converted>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        private readonly object sync = new object();
        private readonly HttpClient client;
        private readonly bool ownsClient;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly HashSet<UniTask> pending = new HashSet<UniTask>();
        private readonly SpeechCallbackGuard callbacks = new SpeechCallbackGuard();
        private AlphaToKanaPreprocessorOptions options;
        private long revision;
        private bool disposing;
        private UniTask? disposeTask;

        public AlphaToKanaPreprocessor(AlphaToKanaPreprocessorOptions options, HttpClient httpClient = null)
        {
            this.options = Snapshot(options);
            ownsClient = httpClient == null;
            client = httpClient ?? CreateClient();
        }

        private static HttpClient CreateClient()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException("AlphaToKana requires a native HTTP transport on this platform.");
#else
            return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, MaxConnectionsPerServer = 100 })
            { Timeout = Timeout.InfiniteTimeSpan };
#endif
        }

        /// <summary>Includes a copy of all learned readings.</summary>
        public AlphaToKanaPreprocessorOptions GetOptions() { lock (sync) return options.Copy(); }
        public Dictionary<string, string> GetKanaMap() { lock (sync) return new Dictionary<string, string>(options.KanaMap); }
        /// <summary>Replaces settings and the dictionary. In-flight calls retain their original settings and cannot repopulate the replacement map.</summary>
        public void UpdateOptions(AlphaToKanaPreprocessorOptions replacement)
        {
            var snapshot = Snapshot(replacement);
            lock (sync) { ThrowIfDisposing(); options = snapshot; revision++; }
        }

        public UniTask<string> ProcessAsync(SpeechSynthesisRequest request, SpeechSynthesizerOptions synthesizerOptions, CancellationToken cancellationToken = default)
        {
            RejectReentry();
            var requestSnapshot = request?.Copy() ?? throw new ArgumentNullException(nameof(request));
            AlphaToKanaPreprocessorOptions settings;
            long callRevision;
            var completion = new SpeechCompletionSource<string>();
            var activeTask = completion.Task.AsUniTask();
            lock (sync)
            {
                ThrowIfDisposing();
                cancellationToken.ThrowIfCancellationRequested();
                settings = options.Copy();
                callRevision = revision;
                pending.Add(activeTask);
            }
            _ = RunAsync(requestSnapshot, settings, callRevision, cancellationToken, completion, activeTask);
            return completion.Task;
        }

        private async UniTask RunAsync(SpeechSynthesisRequest request, AlphaToKanaPreprocessorOptions settings, long callRevision,
            CancellationToken callerToken, SpeechCompletionSource<string> completion, UniTask activeTask)
        {

            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, lifetime.Token))
                {
                    using var timeout = SpeechAsync.Timeout(linked, TimeSpan.FromSeconds(settings.TimeoutSeconds));
                    linked.Token.ThrowIfCancellationRequested();
                    var result = await ProcessCoreAsync(request, settings, callRevision, linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    completion.TrySetResult(result);
                }
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception error) { completion.TrySetException(error); }
            finally { lock (sync) pending.Remove(activeTask); }
        }

        private async UniTask<string> ProcessCoreAsync(SpeechSynthesisRequest request, AlphaToKanaPreprocessorOptions settings, long callRevision, CancellationToken token)
        {
            var text = request.Text;
            if (string.IsNullOrEmpty(text) || (!string.IsNullOrEmpty(request.Language) && !request.Language.StartsWith("ja", StringComparison.Ordinal))) return text;
            var escaped = new StringBuilder();
            foreach (var character in settings.SpecialChars)
            {
                if (character == '\\' || character == ']' || character == '[' || character == '-' || character == '^') escaped.Append('\\');
                escaped.Append(character);
            }
            var special = escaped.ToString();
            var pattern = new Regex(special.Length == 0 ? "[A-Za-z]+" : "[A-Za-z]+(?:[" + special + "][A-Za-z]+)*", RegexOptions.CultureInvariant);
            if (!settings.UseKanaMap)
            {
                // As in Python, direct mode does not apply the minimum word-length filter.
                if (!pattern.IsMatch(text)) return text;
                var direct = await SendAsync(BuildBody(settings, text, false), settings, false, token);
                var content = FirstMessage(direct)?["content"];
                if (content?.Type != JTokenType.String) return text;
                var converted = ConvertedPattern.Match((string)content);
                return converted.Success ? converted.Groups[1].Value : text;
            }

            foreach (var mapping in settings.KanaMap) text = Replace(text, mapping.Key, mapping.Value);
            var known = new HashSet<string>(settings.KanaMap.Keys, StringComparer.OrdinalIgnoreCase);
            var uncached = pattern.Matches(text).Cast<Match>().Select(match => match.Value)
                .Where(word => (word.Length >= settings.AlphabetLength || word.IndexOfAny(settings.SpecialChars.ToCharArray()) >= 0) && !known.Contains(word)).ToArray();
            if (uncached.Length == 0) return text;
            var response = await SendAsync(BuildBody(settings, string.Join("\n", uncached), true), settings, true, token);
            var toolCalls = FirstMessage(response)?["tool_calls"] as JArray;
            if (toolCalls == null || toolCalls.Count == 0) return text;
            var arguments = (toolCalls[0] as JObject)?["function"] as JObject;
            if (arguments?["arguments"]?.Type != JTokenType.String) return text;
            JObject parsed;
            try { parsed = JObject.Parse((string)arguments["arguments"]); }
            catch (JsonException) { return text; }
            var conversions = parsed["conversions"] as JArray;
            if (conversions == null) return text;
            foreach (var item in conversions.OfType<JObject>())
            {
                var original = item["original"]?.Type == JTokenType.String ? (string)item["original"] : null;
                var kana = item["kana"]?.Type == JTokenType.String ? (string)item["kana"] : null;
                if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(kana)) continue;
                token.ThrowIfCancellationRequested();
                lock (sync) if (!disposing && revision == callRevision) options.KanaMap[original] = kana;
                text = Replace(text, original, kana);
            }
            return text;
        }

        private static JObject FirstMessage(JObject response)
        {
            var choices = response?["choices"] as JArray;
            return choices != null && choices.Count > 0 ? (choices[0] as JObject)?["message"] as JObject : null;
        }

        private static string Replace(string text, string original, string kana) =>
            Regex.Replace(text, Regex.Escape(original), _ => kana, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static JObject BuildBody(AlphaToKanaPreprocessorOptions settings, string text, bool withMap)
        {
            var prompt = withMap ? settings.SystemPromptWithCache : settings.SystemPrompt;
            if (string.IsNullOrEmpty(prompt)) prompt = withMap ? AlphaToKanaPreprocessorOptions.DefaultSystemPromptWithCache : AlphaToKanaPreprocessorOptions.DefaultSystemPrompt;
            var body = new JObject
            {
                ["model"] = settings.Model,
                ["messages"] = new JArray(new JObject { ["role"] = "system", ["content"] = prompt }, new JObject { ["role"] = "user", ["content"] = text })
            };
            if (withMap)
            {
                body["tools"] = new JArray(JObject.Parse(ConvertTool));
                body["tool_choice"] = new JObject { ["type"] = "function", ["function"] = new JObject { ["name"] = "convert_alphabet_to_kana" } };
            }
            if (!string.IsNullOrEmpty(settings.ReasoningEffort)) body["reasoning_effort"] = settings.ReasoningEffort;
            if (settings.ExtraBody != null)
                foreach (var property in settings.ExtraBody.Properties()) body[property.Name] = property.Value.DeepClone();
            return body;
        }

        private async UniTask<JObject> SendAsync(JObject body, AlphaToKanaPreprocessorOptions settings, bool withMap, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, settings.BaseUrl.TrimEnd('/') + "/chat/completions"))
            {
                // Upstream uses Azure api-key only for map mode; direct mode always uses Bearer.
                if (withMap && settings.BaseUrl.Contains("azure")) request.Headers.Add("api-key", settings.ApiKey);
                else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                try
                {
                    using (var response = await SpeechAsync.FromTask(callbacks.Invoke(() => client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))))
                    using (token.Register(() => { try { response.Dispose(); } catch { } }))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!response.IsSuccessStatusCode || response.Content == null) return null;
                        using (var input = await SpeechAsync.FromTask(response.Content.ReadAsStreamAsync()))
                        using (var output = new MemoryStream())
                        {
                            await SpeechAsync.FromTask(input.CopyToAsync(output, 81920, token));
                            token.ThrowIfCancellationRequested();
                            return JObject.Parse(Encoding.UTF8.GetString(output.ToArray()));
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) when (error is IOException || error is HttpRequestException || error is ObjectDisposedException || error is JsonException)
                { token.ThrowIfCancellationRequested(); return null; }
            }
        }

        public UniTask DisposeAsync()
        {
            RejectReentry();
            UniTask[] active;
            SpeechCompletionSource<bool> completion;
            lock (sync)
            {
                if (disposeTask != null) return disposeTask.Value;
                disposing = true;
                active = pending.ToArray();
                completion = new SpeechCompletionSource<bool>();
                disposeTask = completion.Task;
            }
            _ = DisposeCoreAsync(active, completion);
            return disposeTask.Value;
        }

        private async UniTask DisposeCoreAsync(UniTask[] active, SpeechCompletionSource<bool> completion)
        {
            Exception failure = null;
            try { lifetime.Cancel(); } catch (Exception error) { failure = error; }
            try { await SpeechAsync.WhenAll(active); } catch { }
            try { if (ownsClient) client.Dispose(); }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            finally { lifetime.Dispose(); }
            if (failure == null) completion.TrySetResult(true); else completion.TrySetException(failure);
        }
        private static AlphaToKanaPreprocessorOptions Snapshot(AlphaToKanaPreprocessorOptions value)
        {
            var copy = value?.Copy() ?? throw new ArgumentNullException(nameof(value));
            copy.Validate();
            return copy;
        }
        private void ThrowIfDisposing() { if (disposing) throw new ObjectDisposedException(nameof(AlphaToKanaPreprocessor)); }
        private void RejectReentry() { callbacks.ThrowIfActive("Schedule preprocessing or disposal after the current preprocessing call returns."); }

        private const string ConvertTool = @"{""type"":""function"",""function"":{""name"":""convert_alphabet_to_kana"",""description"":""Output the result of converting alphabet to katakana reading"",""parameters"":{""type"":""object"",""properties"":{""conversions"":{""type"":""array"",""description"":""List of pairs of words to convert and their readings"",""items"":{""type"":""object"",""properties"":{""original"":{""type"":""string"",""description"":""Original alphabet notation before conversion""},""kana"":{""type"":""string"",""description"":""Katakana reading""}},""required"":[""original"",""kana""]}}},""required"":[""conversions""]}}}";
    }
}

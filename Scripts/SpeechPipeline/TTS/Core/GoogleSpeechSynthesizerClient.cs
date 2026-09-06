// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.TTS
{
    public sealed class GoogleSpeechSynthesizerOptions : SpeechSynthesizerOptions
    {
        public string ApiKey { get; set; }
        public string Speaker { get; set; }
        public string DefaultLanguage { get; set; } = "ja-JP";
        public string AudioFormat { get; set; } = "LINEAR16";
        /// <summary>Language overrides; unregistered languages use the configured default voice.</summary>
        public Dictionary<string, string> VoiceMap { get; set; } = new Dictionary<string, string>();

        public GoogleSpeechSynthesizerOptions() { CacheExtension = "pcm"; }

        public override SpeechSynthesizerOptions Copy()
        {
            var copy = (GoogleSpeechSynthesizerOptions)base.Copy();
            copy.VoiceMap = VoiceMap == null ? new Dictionary<string, string>() : new Dictionary<string, string>(VoiceMap);
            return copy;
        }

        public override void Validate()
        {
            base.Validate();
            SpeechSynthesisValidation.ApiKey(ApiKey);
            if (string.IsNullOrWhiteSpace(Speaker)) throw new ArgumentException("A speaker is required.", nameof(Speaker));
            if (string.IsNullOrWhiteSpace(DefaultLanguage)) throw new ArgumentException("A default language is required.", nameof(DefaultLanguage));
            if (string.IsNullOrWhiteSpace(AudioFormat)) throw new ArgumentException("An audio format is required.", nameof(AudioFormat));
            foreach (var entry in VoiceMap ?? new Dictionary<string, string>())
                if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
                    throw new ArgumentException("VoiceMap requires nonempty languages and speakers.", nameof(VoiceMap));
        }
    }

    public sealed class GoogleSpeechSynthesizerClient : HttpSpeechSynthesizerBase
    {
        public GoogleSpeechSynthesizerClient(GoogleSpeechSynthesizerOptions options, HttpClient httpClient = null) : base(options, httpClient) { }

        protected override async UniTask<byte[]> GenerateCoreAsync(SpeechSynthesisRequest request, SpeechSynthesizerOptions options, CancellationToken token)
        {
            var settings = (GoogleSpeechSynthesizerOptions)options;
            var language = settings.DefaultLanguage;
            var speaker = settings.Speaker;
            if (!string.IsNullOrEmpty(request.Language))
            {
                // Retain AIAvatarKit's literal normalization (e.g. zh-CN becomes cmn-CNCN).
                var requested = request.Language.StartsWith("zh-", StringComparison.Ordinal)
                    ? request.Language.Replace("zh-", "cmn-CN") : request.Language;
                if (settings.VoiceMap.TryGetValue(requested, out var mapped)) { language = requested; speaker = mapped; }
                else if (requested == settings.DefaultLanguage) { language = requested; speaker = settings.Speaker; }
            }
            var body = new JObject
            {
                ["input"] = new JObject { ["text"] = request.Text },
                ["voice"] = new JObject { ["languageCode"] = language, ["name"] = speaker },
                ["audioConfig"] = new JObject { ["audioEncoding"] = settings.AudioFormat }
            };
            var message = JsonRequest(HttpMethod.Post,
                "https://texttospeech.googleapis.com/v1/text:synthesize?key=" + Uri.EscapeDataString(settings.ApiKey), body);
            var response = await SendJsonAsync(message, token);
            var encoded = response?["audioContent"];
            if (encoded?.Type != JTokenType.String) throw new InvalidDataException("Google speech synthesis returned no audioContent string.");
            try { return Convert.FromBase64String((string)encoded); }
            catch (FormatException) { throw new InvalidDataException("Google speech synthesis returned invalid base64 audio."); }
        }
    }
}

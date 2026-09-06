// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.STT
{
    public sealed class AzureSpeechRecognizerOptions : SpeechRecognizerOptions
    {
        public string ApiKey { get; set; }
        public string Region { get; set; }
        public string ApiVersion { get; set; } = "2024-11-15";
        public AzureSpeechRecognizerOptions() { Language = "ja-JP"; TimeoutSeconds = 5; }
        public override void Validate()
        {
            base.Validate();
            if (string.IsNullOrWhiteSpace(Region) || Region.Any(c => !((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')))
                throw new ArgumentException("An Azure region code such as japaneast is required.", nameof(Region));
            if (string.IsNullOrWhiteSpace(ApiVersion)) throw new ArgumentException("An API version is required.", nameof(ApiVersion));
            AzureSpeechRecognizerClient.ValidateOptions(this);
        }
    }

    /// <summary>Azure Fast Transcription REST API. Classic/CID and speaker recognition are not included.</summary>
    public sealed class AzureSpeechRecognizerClient : HttpSpeechRecognizerBase
    {
        public AzureSpeechRecognizerClient(AzureSpeechRecognizerOptions options, HttpClient httpClient = null)
            : base(options ?? throw new ArgumentNullException(nameof(options)), httpClient) { }
        internal static void ValidateOptions(AzureSpeechRecognizerOptions options) => ValidateApiKey(options.ApiKey);

        protected override async UniTask<string> TranscribeCoreAsync(byte[] audio, SpeechRecognizerOptions options, CancellationToken token)
        {
            var settings = (AzureSpeechRecognizerOptions)options;
            var wave = Pcm16Audio.WriteWave(audio, settings.SampleRate);
            var locales = new JArray();
            if (!string.IsNullOrEmpty(settings.Language)) locales.Add(settings.Language);
            foreach (var locale in settings.AlternativeLanguages) locales.Add(locale);
            var definition = new JObject { ["locales"] = locales, ["channels"] = new JArray(0, 1) }.ToString(Formatting.None);
            var response = await SendJsonAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post,
                    "https://" + settings.Region + ".api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version=" + Uri.EscapeDataString(settings.ApiVersion));
                request.Headers.Add("Ocp-Apim-Subscription-Key", settings.ApiKey);
                var form = new MultipartFormDataContent();
                request.Content = form;
                var file = new ByteArrayContent(wave);
                file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                form.Add(file, "audio", "voice.wav");
                form.Add(new StringContent(definition, Encoding.UTF8, "application/json"), "definition");
                return request;
            }, settings, token);
            var phrases = response?["combinedPhrases"] as JArray;
            var first = phrases?.First as JObject;
            return TextValue(first?["text"]);
        }
    }
}

// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.STT
{
    public sealed class OpenAISpeechRecognizerOptions : SpeechRecognizerOptions
    {
        public string ApiKey { get; set; }
        public string Model { get; set; } = "gpt-transcribe";
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public int MinDataLength { get; set; } = 4096;
        public OpenAISpeechRecognizerOptions() { Language = "ja"; }
        public override void Validate()
        {
            base.Validate();
            if (MinDataLength < 0) throw new ArgumentOutOfRangeException(nameof(MinDataLength));
            if (string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("A transcription model is required.", nameof(Model));
            OpenAISpeechRecognizerClient.ValidateOptions(this);
        }
    }

    /// <summary>Completed-audio transcription with gpt-transcribe. No Realtime or speaker recognition.</summary>
    public sealed class OpenAISpeechRecognizerClient : HttpSpeechRecognizerBase
    {
        public OpenAISpeechRecognizerClient(OpenAISpeechRecognizerOptions options, HttpClient httpClient = null)
            : base(options ?? throw new ArgumentNullException(nameof(options)), httpClient) { }

        internal static void ValidateOptions(OpenAISpeechRecognizerOptions options)
        {
            ValidateApiKey(options.ApiKey);
            ValidateBaseUrl(NormalizeBaseUrl(options.BaseUrl));
        }

        private static string NormalizeBaseUrl(string url) => string.IsNullOrEmpty(url) ? "https://api.openai.com/v1" : url.TrimEnd('/');

        protected override async UniTask<string> TranscribeCoreAsync(byte[] audio, SpeechRecognizerOptions options, CancellationToken token)
        {
            var settings = (OpenAISpeechRecognizerOptions)options;
            if (audio.Length < settings.MinDataLength) return null;
            var wave = Pcm16Audio.WriteWave(audio, settings.SampleRate);
            var response = await SendJsonAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, NormalizeBaseUrl(settings.BaseUrl) + "/audio/transcriptions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                var form = new MultipartFormDataContent();
                request.Content = form;
                form.Add(new StringContent(settings.Model), "model");
                form.Add(new StringContent("json"), "response_format");
                // Preserve upstream's automatic language detection when alternatives are supplied.
                // GPT-Transcribe's current API uses languages[] instead of the legacy language field.
                if (!string.IsNullOrEmpty(settings.Language) && settings.AlternativeLanguages.Length == 0)
                    form.Add(new StringContent(settings.Language.Split('-')[0]), "languages[]");
                var file = new ByteArrayContent(wave);
                file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                form.Add(file, "file", "voice.wav");
                return request;
            }, settings, token);
            return TextValue(response?["text"]);
        }
    }
}

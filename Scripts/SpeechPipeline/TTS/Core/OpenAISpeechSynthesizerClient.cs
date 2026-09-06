// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.TTS
{
    public sealed class OpenAISpeechSynthesizerOptions : SpeechSynthesizerOptions
    {
        public string ApiKey { get; set; }
        /// <summary>OpenAI-compatible base URL, or the complete Azure OpenAI speech endpoint including api-version.</summary>
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public string Speaker { get; set; } = "sage";
        public string Model { get; set; } = "tts-1";
        public string Instructions { get; set; }
        public string AudioFormat { get; set; } = "wav";

        public override void Validate()
        {
            base.Validate();
            SpeechSynthesisValidation.ApiKey(ApiKey);
            SpeechSynthesisValidation.HttpUrl(BaseUrl, allowQuery: BaseUrl?.Contains("azure") == true);
            if (string.IsNullOrWhiteSpace(Speaker)) throw new ArgumentException("A speaker is required.", nameof(Speaker));
            if (string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("A model is required.", nameof(Model));
            if (string.IsNullOrWhiteSpace(AudioFormat)) throw new ArgumentException("An audio format is required.", nameof(AudioFormat));
        }
    }

    public sealed class OpenAISpeechSynthesizerClient : HttpSpeechSynthesizerBase
    {
        public OpenAISpeechSynthesizerClient(OpenAISpeechSynthesizerOptions options, HttpClient httpClient = null) : base(options, httpClient) { }

        protected override UniTask<byte[]> GenerateCoreAsync(SpeechSynthesisRequest request, SpeechSynthesizerOptions options, CancellationToken token)
        {
            var settings = (OpenAISpeechSynthesizerOptions)options;
            // Preserve the source's Azure endpoint detection and authentication behavior.
            var azure = settings.BaseUrl.Contains("azure");
            var body = new JObject
            {
                ["model"] = settings.Model, ["voice"] = settings.Speaker, ["input"] = request.Text,
                ["response_format"] = settings.AudioFormat
            };
            // The API rejects explicit null for this optional string.
            if (settings.Instructions != null) body["instructions"] = settings.Instructions;
            var message = JsonRequest(HttpMethod.Post, azure ? settings.BaseUrl : settings.BaseUrl.TrimEnd('/') + "/audio/speech", body);
            if (azure) message.Headers.Add("api-key", settings.ApiKey);
            else message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            return SendBytesAsync(message, token);
        }
    }
}

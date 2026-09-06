// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.TTS
{
    public sealed class VoicevoxSpeechSynthesizerOptions : SpeechSynthesizerOptions
    {
        public string BaseUrl { get; set; } = "http://127.0.0.1:50021";
        public int Speaker { get; set; } = 46;

        public override void Validate()
        {
            base.Validate();
            SpeechSynthesisValidation.HttpUrl(BaseUrl, allowHttp: true);
            if (Speaker < 0) throw new ArgumentOutOfRangeException(nameof(Speaker));
        }

    }

    /// <summary>VOICEVOX audio_query and synthesis HTTP endpoints. Returns the engine's audio bytes.</summary>
    public sealed class VoicevoxSpeechSynthesizerClient : HttpSpeechSynthesizerBase
    {
        public VoicevoxSpeechSynthesizerClient(VoicevoxSpeechSynthesizerOptions options, HttpClient httpClient = null)
            : base(options ?? throw new ArgumentNullException(nameof(options)), httpClient) { }

        /// <summary>Requests an editable VOICEVOX query for an explicit speaker without synthesizing audio.</summary>
        public UniTask<JObject> GetAudioQueryAsync(string text, int speaker, CancellationToken cancellationToken = default)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (speaker < 0) throw new ArgumentOutOfRangeException(nameof(speaker));
            return RunOperationAsync((options, token) => GetAudioQueryCoreAsync(text, speaker,
                (VoicevoxSpeechSynthesizerOptions)options, token), cancellationToken);
        }

        protected override async UniTask<byte[]> GenerateCoreAsync(SpeechSynthesisRequest request,
            SpeechSynthesizerOptions options, CancellationToken token)
        {
            var settings = (VoicevoxSpeechSynthesizerOptions)options;
            var speaker = settings.Speaker;
            var style = ParseStyle(request.StyleInfo, settings);
            if (!string.IsNullOrEmpty(style) &&
                (!int.TryParse(style, NumberStyles.Integer, CultureInfo.InvariantCulture, out speaker) || speaker < 0))
                throw new ArgumentException("VOICEVOX style mappings must contain nonnegative integer speaker IDs.", nameof(request));
            var query = await GetAudioQueryCoreAsync(request.Text, speaker, settings, token);
            token.ThrowIfCancellationRequested();
            // Preserve all fields returned by the engine, including fields introduced by newer versions.
            var message = JsonRequest(HttpMethod.Post, settings.BaseUrl.TrimEnd('/') + "/synthesis?speaker=" +
                speaker.ToString(CultureInfo.InvariantCulture), query);
            return await SendBytesAsync(message, token);
        }

        private UniTask<JObject> GetAudioQueryCoreAsync(string text, int speaker,
            VoicevoxSpeechSynthesizerOptions settings, CancellationToken token)
        {
            var url = settings.BaseUrl.TrimEnd('/') + "/audio_query?speaker=" + speaker.ToString(CultureInfo.InvariantCulture) +
                "&text=" + Uri.EscapeDataString(text);
            return SendJsonAsync(new HttpRequestMessage(HttpMethod.Post, url), token);
        }
    }
}

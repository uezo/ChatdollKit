// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using System.Xml;

namespace ChatdollKit.SpeechPipeline.TTS
{
    public sealed class AzureSpeechSynthesizerOptions : SpeechSynthesizerOptions
    {
        public string ApiKey { get; set; }
        public string Region { get; set; }
        public string Speaker { get; set; }
        public string DefaultLanguage { get; set; } = "ja-JP";
        public string AudioFormat { get; set; } = "riff-16khz-16bit-mono-pcm";
        /// <summary>Language overrides; the default language uses Speaker when no override is present.</summary>
        public Dictionary<string, string> VoiceMap { get; set; } = new Dictionary<string, string>();

        public override SpeechSynthesizerOptions Copy()
        {
            var copy = (AzureSpeechSynthesizerOptions)base.Copy();
            copy.VoiceMap = VoiceMap == null ? new Dictionary<string, string>() : new Dictionary<string, string>(VoiceMap);
            return copy;
        }

        public override void Validate()
        {
            base.Validate();
            SpeechSynthesisValidation.ApiKey(ApiKey);
            if (string.IsNullOrEmpty(Region) || !Regex.IsMatch(Region, @"\A[A-Za-z0-9-]+\z"))
                throw new ArgumentException("A valid Azure region is required.", nameof(Region));
            if (string.IsNullOrWhiteSpace(Speaker)) throw new ArgumentException("A speaker is required.", nameof(Speaker));
            if (string.IsNullOrWhiteSpace(DefaultLanguage)) throw new ArgumentException("A default language is required.", nameof(DefaultLanguage));
            if (string.IsNullOrWhiteSpace(AudioFormat) || AudioFormat.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("An audio format without line breaks is required.", nameof(AudioFormat));
            foreach (var entry in VoiceMap ?? new Dictionary<string, string>())
                if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
                    throw new ArgumentException("VoiceMap requires nonempty languages and speakers.", nameof(VoiceMap));
        }
    }

    public sealed class AzureSpeechSynthesizerClient : HttpSpeechSynthesizerBase
    {
        public AzureSpeechSynthesizerClient(AzureSpeechSynthesizerOptions options, HttpClient httpClient = null) : base(options, httpClient) { }

        protected override UniTask<byte[]> GenerateCoreAsync(SpeechSynthesisRequest request, SpeechSynthesizerOptions options, CancellationToken token)
        {
            var settings = (AzureSpeechSynthesizerOptions)options;
            var language = string.IsNullOrEmpty(request.Language) ? settings.DefaultLanguage : request.Language;
            if (!settings.VoiceMap.TryGetValue(language, out var speaker))
            {
                if (language != settings.DefaultLanguage) throw new ArgumentException("The requested language has no Azure voice mapping.", nameof(request));
                speaker = settings.Speaker;
            }
            var ssml = new StringBuilder();
            using (var writer = XmlWriter.Create(ssml, new XmlWriterSettings { OmitXmlDeclaration = true }))
            {
                writer.WriteStartElement("speak");
                writer.WriteAttributeString("version", "1.0");
                writer.WriteAttributeString("xml", "lang", "http://www.w3.org/XML/1998/namespace", language);
                writer.WriteStartElement("voice");
                writer.WriteAttributeString("xml", "lang", "http://www.w3.org/XML/1998/namespace", language);
                writer.WriteAttributeString("name", speaker);
                writer.WriteString(request.Text);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            var message = new HttpRequestMessage(HttpMethod.Post,
                "https://" + settings.Region + ".tts.speech.microsoft.com/cognitiveservices/v1");
            message.Headers.Add("Ocp-Apim-Subscription-Key", settings.ApiKey);
            message.Headers.Add("X-Microsoft-OutputFormat", settings.AudioFormat);
            message.Headers.UserAgent.ParseAdd("ChatdollKit");
            message.Content = new StringContent(ssml.ToString(), Encoding.UTF8, "application/ssml+xml");
            return SendBytesAsync(message, token);
        }
    }
}

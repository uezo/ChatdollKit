using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.IO;
using ChatdollKit.Network;

namespace ChatdollKit.SpeechSynthesizer
{
    public class GoogleSpeechSynthesizer : SpeechSynthesizerBase
    {
        public bool _IsEnabled = true;
        public override bool IsEnabled
        {
            get
            {
                return _IsEnabled;
            }
            set
            {
                _IsEnabled = value;
            }
        }

        public string ApiKey;
        public string Language = "ja-JP";
        public string SpeakerName = "ja-JP-Standard-A";
        public Dictionary<string, string> SpeakerMap = new ();

        private ChatdollHttp client;

        private void Start()
        {
            client = new ChatdollHttp(Timeout);
        }

        // See https://cloud.google.com/text-to-speech/docs/voices
        protected override async UniTask<AudioClip> DownloadAudioClipAsync(string text, Dictionary<string, object> parameters, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Language))
            {
                Debug.LogError("API Key or Language are missing from GoogleTTSLoader");
            }

            try
            {
                var url = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={ApiKey}";

                string language;
                string speaker;
                if (parameters.ContainsKey("language"))
                {
                    language = parameters["language"] as string;
                    speaker = SpeakerMap[language];
                }
                else
                {
                    language = Language;
                    speaker = SpeakerName;
                }

                var ttsRequest = new GoogleTextToSpeechRequest(text, language, speaker, "LINEAR16");
                var ttsResponse = await client.PostJsonAsync<GoogleTextToSpeechResponse>(url, ttsRequest, cancellationToken: token);

                if (!string.IsNullOrEmpty(ttsResponse.audioContent))
                {
                    var audioBin = Convert.FromBase64String(ttsResponse.audioContent);
                    return AudioConverter.PCMToAudioClip(audioBin);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured while processing text-to-speech voice: {ex.Message}\n{ex.StackTrace}");
            }
            return null;
        }

		class GoogleTextToSpeechInput
        {
            public string text;
        }

        class GoogleTextToSpeechVoice
        {
            public string languageCode;
            public string name;
        }

        class GoogleTextToSpeechAudioConfig
        {
            public string audioEncoding;
        }

        class GoogleTextToSpeechRequest
        {
            public GoogleTextToSpeechInput input;
            public GoogleTextToSpeechVoice voice;
            public GoogleTextToSpeechAudioConfig audioConfig;

            public GoogleTextToSpeechRequest(string text, string language, string speakerName, string audioEncoding)
            {
                input = new GoogleTextToSpeechInput() { text = text };
                voice = new GoogleTextToSpeechVoice() { languageCode = language, name = speakerName };
                audioConfig = new GoogleTextToSpeechAudioConfig() { audioEncoding = audioEncoding };
            }
        }

        class GoogleTextToSpeechResponse
        {
            public string audioContent;
        }
    }
}

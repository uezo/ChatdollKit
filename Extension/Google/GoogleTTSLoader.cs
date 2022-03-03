using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.IO;
using ChatdollKit.Model;
using ChatdollKit.Network;

namespace ChatdollKit.Extension.Google
{
    public class GoogleTTSLoader : WebVoiceLoaderBase
    {
        public override VoiceLoaderType Type { get; } = VoiceLoaderType.TTS;
        public string _Name = "Google";
        public override string Name
        {
            get
            {
                return _Name;
            }
        }
        public bool _IsDefault = true;
        public override bool IsDefault
        {
            get
            {
                return _IsDefault;
            }
            set
            {
                _IsDefault = value;
            }
        }

        public string ApiKey;
        public string Language = "ja-JP";
        public string Gender = "FEMALE";
        public string SpeakerName = "ja-JP-Standard-A";

        public void Configure(string apiKey, string language, string gender, string speakerName, bool overwrite = false)
        {
            ApiKey = string.IsNullOrEmpty(ApiKey) || overwrite ? apiKey : ApiKey;
            Language = string.IsNullOrEmpty(Language) || overwrite ? language : Language;
            Gender = string.IsNullOrEmpty(Gender) || overwrite ? gender : Gender;
            SpeakerName = string.IsNullOrEmpty(SpeakerName) || overwrite ? speakerName : SpeakerName;
        }

        protected override async UniTask<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Language))
            {
                Debug.LogError("API Key or Language are missing from GoogleTTSLoader");
            }

            try
            {
                var url = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={ApiKey}";

                var ttsRequest = new GoogleTextToSpeechRequest(
                    voice.Text,
                    voice.GetTTSParam("language") as string ?? Language,
                    voice.GetTTSParam("speakerName") as string ?? SpeakerName,
                    voice.GetTTSParam("gender") as string ?? Gender,
                    "LINEAR16");

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
            public string ssmlGender;
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

            public GoogleTextToSpeechRequest(string text, string language, string speakerName, string speakerGender, string audioEncoding)
            {
                input = new GoogleTextToSpeechInput() { text = text };
                voice = new GoogleTextToSpeechVoice() { languageCode = language, name = speakerName, ssmlGender = speakerGender };
                audioConfig = new GoogleTextToSpeechAudioConfig() { audioEncoding = audioEncoding };
            }
        }

        class GoogleTextToSpeechResponse
        {
            public string audioContent;
        }
    }
}

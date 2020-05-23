using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.IO;


namespace ChatdollKit.Extension
{
    [RequireComponent(typeof(VoiceRecorder))]
    public class GoogleVoiceRequestProvider : VoiceRequestProviderBase
    {
        public string ApiKey = string.Empty;
        public string Language = "ja-JP";
        public bool UseEnhancedModel = false;

        protected override async Task<string> RecognizeSpeechAsync(AudioClip recordedVoice)
        {
            var response = await client.PostJsonAsync<SpeechRecognitionResponse>(
                $"https://speech.googleapis.com/v1/speech:recognize?key={ApiKey}",
                new SpeechRecognitionRequest(recordedVoice, Language, UseEnhancedModel));

            return response.results[0].alternatives[0].transcript;
        }

        // Models for request and response
        class SpeechRecognitionRequest
        {
            public SpeechRecognitionConfig config;
            public SpeechRecognitionAudio audio;

            public SpeechRecognitionRequest(AudioClip audioClip, string languageCode, bool useEnhancedModel)
            {
                config = new SpeechRecognitionConfig(audioClip, languageCode, useEnhancedModel);
                audio = new SpeechRecognitionAudio(AudioConverter.AudioClipToBase64(audioClip));
            }
        }

        class SpeechRecognitionConfig
        {
            public int encoding;
            public double sampleRateHertz;
            public double audioChannelCount;
            public bool enableSeparateRecognitionPerChannel;
            public string languageCode;
            public string model;
            public bool useEnhanced;

            public SpeechRecognitionConfig(AudioClip audioClip, string languageCode, bool useEnhancedModel)
            {
                encoding = 1;   // 1: 16-bit linear PCM
                sampleRateHertz = audioClip.frequency;
                audioChannelCount = audioClip.channels;
                enableSeparateRecognitionPerChannel = false;
                this.languageCode = languageCode;
                model = useEnhancedModel ? null : "default";
                useEnhanced = useEnhancedModel;
            }
        }

        class SpeechRecognitionAudio
        {
            public string content;

            public SpeechRecognitionAudio(string content)
            {
                this.content = content;
            }
        }

        class SpeechRecognitionResponse
        {
            public SpeechRecognitionResult[] results;
        }

        class SpeechRecognitionResult
        {
            public SpeechRecognitionAlternative[] alternatives;
        }

        class SpeechRecognitionAlternative
        {
            public string transcript;
            public double confidence;
        }
    }
}

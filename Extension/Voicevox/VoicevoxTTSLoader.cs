using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using ChatdollKit.Model;
using ChatdollKit.Network;

namespace ChatdollKit.Extension.Voicevox
{
    public class VoicevoxTTSLoader : WebVoiceLoaderBase
    {
        public enum SpeakerName
        {
            四国めたん = 0,
            ずんだもん = 1
        }

        public override VoiceLoaderType Type { get; } = VoiceLoaderType.TTS;
        public string _Name = "Voicevox";
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
        public string EndpointUrl;

        [Header("Voice Settings")]
        public SpeakerName Speaker = SpeakerName.四国めたん;

        public void Configure(string endpointUrl, bool overwrite = false)
        {
            EndpointUrl = string.IsNullOrEmpty(EndpointUrl) || overwrite ? endpointUrl : EndpointUrl;
        }

        // Get audio clip from VOICEVOX engine
        // https://github.com/Hiroshiba/voicevox_engine
        protected override async Task<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            var audio_query = string.Empty;
            using (var www = UnityWebRequest.Post(EndpointUrl + $"/audio_query?speaker={(decimal)Speaker}&text={UnityWebRequest.EscapeURL(voice.Text, Encoding.UTF8)}", ""))
            {
                www.timeout = Timeout;
                www.downloadHandler = new DownloadHandlerBuffer();
                

                await www.SendWebRequest();

                if (www.isNetworkError || www.isHttpError)
                {
                    Debug.LogError($"Error occured while processing voicevox get-query: {www.error}");
                    return null;
                }
                else if (www.isDone)
                {
                    audio_query = www.downloadHandler.text;
                }
            }

            if (string.IsNullOrEmpty(audio_query))
            {
                Debug.LogError("Query for VOICEVOX is empty");
                return null;
            }
            if (Speaker == SpeakerName.四国めたん)
            {
                audio_query = audio_query.Replace("\"speedScale\":1.0", "\"speedScale\":0.9");
            }

            using (var www = UnityWebRequestMultimedia.GetAudioClip(EndpointUrl + $"/synthesis?speaker={(decimal)Speaker}", AudioType.WAV))
            {
                www.timeout = Timeout;
                www.method = "POST";

                // Header
                www.SetRequestHeader("Content-Type", "application/json");


                // Body
                www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(audio_query));

                // Send request
                await www.SendWebRequest();

                if (www.isNetworkError || www.isHttpError)
                {
                    Debug.LogError($"Error occured while processing voicevox text-to-speech: {www.error}");
                }
                else if (www.isDone)
                {
                    return DownloadHandlerAudioClip.GetContent(www);
                }
            }
            return null;
        }
    }
}

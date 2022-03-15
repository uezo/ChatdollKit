using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using ChatdollKit.Model;
using ChatdollKit.IO;

namespace ChatdollKit.Extension.Voicevox
{
    public class VoicevoxTTSLoader : WebVoiceLoaderBase
    {
        public enum SpeakerName
        {
            四国めたん_あまあま = 0,
            四国めたん_ノーマル = 2,
            四国めたん_ツンツン = 6,
            四国めたん_セクシー = 4,

            ずんだもん_あまあま = 1,
            ずんだもん_ノーマル = 3,
            ずんだもん_ツンツン = 7,
            ずんだもん_セクシー = 5,

            春日部つむぎ_ノーマル = 8,
            雨晴はう_ノーマル = 10,
            波音リツ_ノーマル = 9,
            玄野武宏_ノーマル = 11,
            白上虎太郎_ノーマル = 12,
            青山龍星_ノーマル = 13,
            冥鳴ひまり_ノーマル = 14,
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
        public SpeakerName Speaker = SpeakerName.雨晴はう_ノーマル;

        public void Configure(string endpointUrl, bool overwrite = false)
        {
            EndpointUrl = string.IsNullOrEmpty(EndpointUrl) || overwrite ? endpointUrl : EndpointUrl;
        }

        // Get audio clip from VOICEVOX engine
        // https://github.com/Hiroshiba/voicevox_engine
        protected override async UniTask<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            // Convert text to query for TTS from VOICEVOX server
            var queryResp = await client.PostFormAsync(
                EndpointUrl + $"/audio_query?speaker={(decimal)Speaker}&text={UnityWebRequest.EscapeURL(voice.Text, Encoding.UTF8)}",
                new Dictionary<string, string>(), cancellationToken: token);

            var audioQuery = queryResp.Text;

            if (string.IsNullOrEmpty(audioQuery))
            {
                Debug.LogError("Query for VOICEVOX is empty");
                return null;
            }

            if (token.IsCancellationRequested) { return null; };

            // Get audio data from VOICEBOX server
            var url = EndpointUrl + $"/synthesis?speaker={(decimal)Speaker}";
            var headers = new Dictionary<string, string>() { { "Content-Type", "application/json" } };
            var data = Encoding.UTF8.GetBytes(audioQuery);

#if UNITY_WEBGL && !UNITY_EDITOR
            return await DownloadAudioClipWebGLAsync(url, data, headers, token);
#else
            return await DownloadAudioClipNativeAsync(url, data, headers, token);
#endif
        }

        protected async UniTask<AudioClip> DownloadAudioClipNativeAsync(string url, byte[] data, Dictionary<string, string> headers, CancellationToken token)
        {
            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
            {
                www.timeout = Timeout;
                www.method = "POST";

                // Header
                www.SetRequestHeader("Content-Type", headers["Content-Type"]);

                // Body
                www.uploadHandler = new UploadHandlerRaw(data);

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

        protected async UniTask<AudioClip> DownloadAudioClipWebGLAsync(string url, byte[] data, Dictionary<string, string> headers, CancellationToken token)
        {
            var audioResp = await client.PostBytesAsync(url, data, headers, cancellationToken: token);
            return AudioConverter.PCMToAudioClip(audioResp.Data);
        }
    }
}

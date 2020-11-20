using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using ChatdollKit.Network;

namespace ChatdollKit.Model
{
    public class WebVoiceLoader : WebVoiceLoaderBase
    {
        public AudioType AudioType = AudioType.WAV;

        protected override async Task<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            using (var www = UnityWebRequestMultimedia.GetAudioClip(voice.Url, AudioType))
            {
                www.timeout = Timeout;
                await www.SendWebRequest();

                if (www.isNetworkError || www.isHttpError)
                {
                    Debug.LogError($"Error occured while downloading voice: {www.error}");
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

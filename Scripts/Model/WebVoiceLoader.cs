using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Model
{
    public class WebVoiceLoader : WebVoiceLoaderBase
    {
        public AudioType AudioType = AudioType.WAV;

        protected override async UniTask<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; };

            using (var www = UnityWebRequestMultimedia.GetAudioClip(voice.Url, AudioType))
            {
                www.timeout = Timeout;
                await www.SendWebRequest().ToUniTask();

                if (www.result != UnityWebRequest.Result.Success)
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

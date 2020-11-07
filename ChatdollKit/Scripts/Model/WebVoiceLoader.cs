using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using ChatdollKit.Network;


namespace ChatdollKit.Model
{
    public class WebVoiceLoader : WebVoiceLoaderBase
    {
        public AudioType AudioType = AudioType.WAV;

        protected override async Task<AudioClip> DownloadAudioClipAsync(Voice voice)
        {
            using (var www = UnityWebRequestMultimedia.GetAudioClip(voice.Url, AudioType))
            {
                www.timeout = 10;
                await www.SendWebRequest();

                if (www.isNetworkError || www.isHttpError)
                {
                    Debug.LogError($"Error occured while downloading voice: {www.error}");
                }
                else if (www.isDone)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(www);
                    if (clip != null)
                    {
                        audioCache[voice.CacheKey] = clip;
                    }
                    return clip;
                }
            }
            return null;
        }
    }
}

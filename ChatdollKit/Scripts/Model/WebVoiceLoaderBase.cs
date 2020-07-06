using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Network;


namespace ChatdollKit.Model
{
    public class WebVoiceLoaderBase : MonoBehaviour, IVoiceLoader
    {
        public virtual VoiceLoaderType Type { get; } = VoiceLoaderType.Web;
        public virtual string Name { get; } = string.Empty;
        public virtual bool IsDefault { get; set; } = false;

        protected Dictionary<string, AudioClip> audioCache { get; set; } = new Dictionary<string, AudioClip>();
        protected Dictionary<string, Task<AudioClip>> audioDownloadTasks { get; set; } = new Dictionary<string, Task<AudioClip>>();

        public async Task<AudioClip> GetAudioClipAsync(Voice voice)
        {
            if (IsLoading(voice))
            {
                await audioDownloadTasks[voice.Name];
            }

            if (HasCache(voice))
            {
                return audioCache[voice.Name];
            }

            audioDownloadTasks[voice.Name] = DownloadAudioClipAsync(voice);
            var clip = await audioDownloadTasks[voice.Name];
            audioDownloadTasks.Remove(voice.Name);
            return clip;
        }

#pragma warning disable CS1998
        protected virtual async Task<AudioClip> DownloadAudioClipAsync(Voice voice)
        {
            throw new NotImplementedException("DownloadAudioClipAsync must be implemented");
        }
#pragma warning restore CS1998

        public bool HasCache(Voice voice)
        {
            // Return true when name is set and it's cached
            return !string.IsNullOrEmpty(voice.Name) && audioCache.ContainsKey(voice.Name);
        }

        public bool IsLoading(Voice voice)
        {
            if (audioDownloadTasks.ContainsKey(voice.Name))
            {
                var task = audioDownloadTasks[voice.Name];
                if (task.Status < TaskStatus.RanToCompletion)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

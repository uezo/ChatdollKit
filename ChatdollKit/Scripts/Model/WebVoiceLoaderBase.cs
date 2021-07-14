using System;
using System.Collections.Generic;
using System.Threading;
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
        public int Timeout = 10;

        protected Dictionary<string, AudioClip> audioCache { get; set; } = new Dictionary<string, AudioClip>();
        protected Dictionary<string, Task<AudioClip>> audioDownloadTasks { get; set; } = new Dictionary<string, Task<AudioClip>>();

        public async Task<AudioClip> GetAudioClipAsync(Voice voice, CancellationToken cancellationToken)
        {
            if (IsLoading(voice))
            {
                // Wait complete download task if download is now in progress.
                // Cache is controlled by another task that originally invoked the download task
                await WaitDownloadCancellable(audioDownloadTasks[voice.CacheKey], cancellationToken);
            }

            if (HasCache(voice))
            {
                // Return cache if exists
                var cachedClip = audioCache[voice.CacheKey];
                if (!voice.UseCache)
                {
                    audioCache.Remove(voice.CacheKey);
                }
                return cachedClip;
            }

            // Start download when download in not in progress and cache doesn't exist
            audioDownloadTasks[voice.CacheKey] = DownloadAudioClipAsync(voice, cancellationToken);
            await WaitDownloadCancellable(audioDownloadTasks[voice.CacheKey], cancellationToken);
            if (audioDownloadTasks[voice.CacheKey].IsCompleted)
            {
                var clip = audioDownloadTasks[voice.CacheKey].Result;
                if (clip != null && clip.length > 0)
                {
                    // Cache once regardless of UseCache to enable PreFetch
                    audioCache[voice.CacheKey] = clip;
                    audioDownloadTasks.Remove(voice.CacheKey);
                    return clip;
                }
                else
                {
                    Debug.LogWarning("Downloaded AudioClip is null or lts length is zero.");
                }
            }

            return null;
        }

#pragma warning disable CS1998
        protected virtual async Task<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("DownloadAudioClipAsync must be implemented");
        }
#pragma warning restore CS1998

        protected async Task WaitDownloadCancellable(Task<AudioClip> downloadTask, CancellationToken cancellationToken)
        {
            // NOTE: downloadTask continues (= IsLoading() is true) after cancel because it doesn't have cancellation token

            if (cancellationToken.IsCancellationRequested) { return; };

            var cancellableTask = Task.Delay(Timeout * 1000, cancellationToken);
            await Task.WhenAny(downloadTask, cancellableTask);  // WhenAny doesn't throw TaskCanceledException
        }

        public bool HasCache(Voice voice)
        {
            return audioCache.ContainsKey(voice.CacheKey);
        }

        public bool IsLoading(Voice voice)
        {
            if (audioDownloadTasks.ContainsKey(voice.CacheKey))
            {
                var task = audioDownloadTasks[voice.CacheKey];
                if (task.Status < TaskStatus.RanToCompletion)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

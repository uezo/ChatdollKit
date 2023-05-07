using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Network;

namespace ChatdollKit.Model
{
    public class WebVoiceLoaderBase : MonoBehaviour, IVoiceLoader
    {
        public virtual VoiceLoaderType Type { get; } = VoiceLoaderType.Web;
        public virtual string Name { get; } = string.Empty;
        public virtual bool IsDefault { get; set; } = false;
        public int Timeout = 10;
        protected ChatdollHttp client;

        protected Dictionary<string, AudioClip> audioCache { get; set; } = new Dictionary<string, AudioClip>();
        protected Dictionary<string, UniTask<AudioClip>> audioDownloadTasks { get; set; } = new Dictionary<string, UniTask<AudioClip>>();

        [SerializeField]
        protected bool isDebug;

        protected virtual void Start()
        {
            // Instantiate at Start() to allow user to update Timeout at Awake()
            client = new ChatdollHttp(Timeout * 1000);
        }

        public async UniTask<AudioClip> GetAudioClipAsync(Voice voice, CancellationToken cancellationToken)
        {
            if (IsLoading(voice))
            {
                // Wait for cache is ready or task is canceled
                while (!audioCache.ContainsKey(voice.CacheKey) || cancellationToken.IsCancellationRequested)
                {
                    await UniTask.Delay(50, cancellationToken: cancellationToken);
                }
            }

            if (HasCache(voice))
            {
                // Return cache if exists
                var cachedClip = audioCache[voice.CacheKey];
                if (!voice.UseCache)
                {
                    audioCache.Remove(voice.CacheKey);
                }
                if (isDebug)
                {
                    Debug.Log($"Return cached audio clip: {voice.CacheKey}");
                }
                return cachedClip;
            }

            // Start download when download in not in progress and cache doesn't exist
            if (isDebug)
            {
                Debug.Log($"Start downloading: {voice.CacheKey}");
            }
            audioDownloadTasks[voice.CacheKey] = DownloadAudioClipAsync(voice, cancellationToken).Preserve();
            await WaitDownloadCancellable(audioDownloadTasks[voice.CacheKey], cancellationToken);

            if (audioDownloadTasks.ContainsKey(voice.CacheKey) && audioDownloadTasks[voice.CacheKey].GetAwaiter().IsCompleted)
            {
                var clip = audioDownloadTasks[voice.CacheKey].GetAwaiter().GetResult();
                if (clip != null && clip.length > 0)
                {
                    // Cache once regardless of UseCache to enable PreFetch
                    audioCache[voice.CacheKey] = clip;
                    audioDownloadTasks.Remove(voice.CacheKey);
                    if (isDebug)
                    {
                        Debug.Log($"Return downloaded audio clip: {voice.CacheKey}");
                    }
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
        protected virtual async UniTask<AudioClip> DownloadAudioClipAsync(Voice voice, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("DownloadAudioClipAsync must be implemented");
        }
#pragma warning restore CS1998

        protected async UniTask WaitDownloadCancellable(UniTask<AudioClip> downloadTask, CancellationToken cancellationToken)
        {
            // NOTE: downloadTask continues (= IsLoading() is true) after cancel because it doesn't have cancellation token

            if (cancellationToken.IsCancellationRequested) { return; };

            var cancellableTask = UniTask.Delay(Timeout * 1000, cancellationToken: cancellationToken);
            if (!downloadTask.GetAwaiter().IsCompleted)
            {
                await UniTask.WhenAny(downloadTask, cancellableTask);  // WhenAny doesn't throw OperationCanceledException
            }
        }

        public bool HasCache(Voice voice)
        {
            return audioCache.ContainsKey(voice.CacheKey);
        }

        public void ClearCache()
        {
            audioCache.Clear();
        }

        public bool IsLoading(Voice voice)
        {
            if (audioDownloadTasks.ContainsKey(voice.CacheKey))
            {
                var task = audioDownloadTasks[voice.CacheKey];
                if (!task.GetAwaiter().IsCompleted)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

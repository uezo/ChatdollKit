using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace ChatdollKit.SpeechSynthesizer
{
    public class SpeechSynthesizerBase : MonoBehaviour, ISpeechSynthesizer
    {
        public virtual bool IsEnabled { get; set; } = false;
        public int Timeout = 10;

        protected Dictionary<string, AudioClip> audioCache { get; set; } = new Dictionary<string, AudioClip>();
        protected Dictionary<string, UniTask<AudioClip>> audioDownloadTasks { get; set; } = new Dictionary<string, UniTask<AudioClip>>();

        [SerializeField]
        protected bool isDebug;

        public Func<string, Dictionary<string, object>, CancellationToken, UniTask<string>> PreprocessText;

        protected virtual string GetCacheKey(string text, Dictionary<string, object> parameters)
        {
            return $"{text}_{JsonConvert.SerializeObject(parameters)}".GetHashCode().ToString();
        }

        public async UniTask<AudioClip> GetAudioClipAsync(string text, Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(text.Trim()))
            {
                return null;
            }

            // Get cache key
            var cacheKey = GetCacheKey(text, parameters);

            // Return cache if exists
            if (HasCache(cacheKey))
            {
                var cachedClip = audioCache[cacheKey];
                if (isDebug)
                {
                    Debug.Log($"Return cached audio clip: {cacheKey}");
                }
                return cachedClip;
            }

            // Wait downloading task if exists and return from cache
            if (audioDownloadTasks.ContainsKey(cacheKey))
            {
                await WaitDownloadingTask(cacheKey, Timeout, cancellationToken);
                if (audioCache.ContainsKey(cacheKey))
                {
                    return audioCache[cacheKey];
                }
                else
                {
                    Debug.LogWarning($"Download task ends with no cache: {cacheKey}");
                    return null;
                }
            }

            if (isDebug)
            {
                Debug.Log($"Start downloading: {cacheKey}");
            }

            // Preprocess text (e.g. convert alphabet -> kana)
            var preprocessedText = PreprocessText == null
                ? text
                : await PreprocessText(text, parameters, cancellationToken);

            // Start new downloading task
            audioDownloadTasks[cacheKey] = DownloadAudioClipAsync(preprocessedText, parameters, cancellationToken);

            try
            {
                var clip = await audioDownloadTasks[cacheKey];
                if (clip != null && clip.length > 0)
                {
                    // Cache once regardless of UseCache to enable PreFetch
                    audioCache[cacheKey] = clip;
                    if (isDebug)
                    {
                        Debug.Log($"Return downloaded audio clip: {cacheKey}");
                    }
                    return clip;
                }
                else
                {
                    Debug.LogWarning("Downloaded AudioClip is null or lts length is zero.");
                }
            }
            finally
            {
                // Remove download task after caching data
                audioDownloadTasks.Remove(cacheKey);
            }

            return null;
        }

#pragma warning disable CS1998
        protected virtual async UniTask<AudioClip> DownloadAudioClipAsync(string text, Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("DownloadAudioClipAsync must be implemented");
        }
#pragma warning restore CS1998

        private bool HasCache(string cacheKey)
        {
            return audioCache.ContainsKey(cacheKey);
        }

        public void ClearCache()
        {
            audioCache.Clear();
        }

        private async UniTask WaitDownloadingTask(string cacheKey, float timeout, CancellationToken cancellationToken)
        {
            var startTime = Time.time;
            while (audioDownloadTasks.ContainsKey(cacheKey) && !cancellationToken.IsCancellationRequested)
            {
                if (Time.time - startTime > timeout)
                {
                    Debug.LogWarning($"Download task for {cacheKey} timed out.");
                    break;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    if (isDebug)
                    {
                        Debug.LogWarning($"Download task for {cacheKey} canceled.");
                    }
                    break;
                }

                if (!audioDownloadTasks.ContainsKey(cacheKey))
                {
                    if (isDebug)
                    {
                        Debug.Log($"Download task for {cacheKey} ends.");
                    }
                    break;
                }

                await UniTask.Delay(50, cancellationToken: cancellationToken);
            }
        }
    }
}

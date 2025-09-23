using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Model
{
    public class SpeechController : MonoBehaviour
    {
        public AudioSource AudioSource;
        public Func<string, Dictionary<string, object>, CancellationToken, UniTask<AudioClip>> SpeechSynthesizerFunc;
        public Action<Voice, CancellationToken> OnSayStart;
        public Action OnSayEnd;
        private ConcurrentQueue<Voice> voicePrefetchQueue = new ConcurrentQueue<Voice>();
        private CancellationTokenSource voicePrefetchCancellationTokenSource;
        public Action<float[]> HandlePlayingSamples;
        private ILipSyncHelper lipSyncHelper;

        private void Awake()
        {
            // LipSyncHelper
            lipSyncHelper = gameObject.GetComponent<ILipSyncHelper>();
        }

        private void Start()
        {
            StartVoicePrefetchTask().Forget();
        }

        private void OnDestroy()
        {
            voicePrefetchCancellationTokenSource?.Cancel();
            voicePrefetchCancellationTokenSource?.Dispose();
        }

        public async UniTask Say(List<Voice> voices, CancellationToken token)
        {
            // Stop speech
            StopSpeech();
            PrefetchVoices(voices, token);

            // Speak sequentially
            foreach (var v in voices)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                OnSayStart?.Invoke(v, token);

                try
                {
                    // Download voice from web or TTS service
                    var downloadStartTime = Time.time;
                    AudioClip clip = null;

                    var parameters = v.TTSConfig != null ? v.TTSConfig.Params : new Dictionary<string, object>();
                    clip = await SpeechSynthesizerFunc(v.Text, parameters, token);

                    if (clip != null)
                    {
                        // Wait for PreGap remains after download
                        var preGap = v.PreGap - (Time.time - downloadStartTime);
                        if (preGap > 0)
                        {
                            if (!token.IsCancellationRequested)
                            {
                                try
                                {
                                    await UniTask.Delay((int)(preGap * 1000), cancellationToken: token);
                                }
                                catch (OperationCanceledException)
                                {
                                    // OperationCanceledException raises
                                    Debug.Log("Task canceled in waiting PreGap");
                                }
                            }
                        }

                        if (HandlePlayingSamples != null)
                        {
                            // Wait while voice playing with processing LipSync
                            var startTime = Time.realtimeSinceStartup;
                            var bufferSize = clip.channels == 2 ? 2048 : 1024;  // Optimized for 44100Hz / 30FPS
                            var sampleBuffer = new float[bufferSize];
                            var nextPosition = 0;
                            var samples = new float[clip.samples * clip.channels];

                            if (!clip.GetData(samples, 0))
                            {
                                Debug.LogWarning("Failed to get audio data from clip");
                            }
                            else
                            {
                                // Play audio
                                AudioSource.PlayOneShot(clip);

                                // Process samples by estimating current playing position by time
                                while (Time.realtimeSinceStartup - startTime < clip.length && !token.IsCancellationRequested)
                                {
                                    var elapsedTime = Time.realtimeSinceStartup - startTime;
                                    var currentPosition = Mathf.FloorToInt(elapsedTime * clip.frequency) * clip.channels;

                                    while (nextPosition + bufferSize <= currentPosition &&
                                        nextPosition + bufferSize <= samples.Length)
                                    {
                                        System.Array.Copy(samples, nextPosition, sampleBuffer, 0, bufferSize);
                                        HandlePlayingSamples(sampleBuffer);
                                        nextPosition += bufferSize;
                                    }

                                    await UniTask.Delay(33, cancellationToken: token);  // 30FPS
                                }

                                // Remaining samples
                                if (nextPosition < samples.Length)
                                {
                                    var remaining = samples.Length - nextPosition;
                                    var lastBuffer = new float[remaining];
                                    System.Array.Copy(samples, nextPosition, lastBuffer, 0, remaining);
                                    HandlePlayingSamples(lastBuffer);
                                }
                            }
                        }
                        else
                        {
                            // Play audio
                            AudioSource.PlayOneShot(clip);

                            // Wait while voice playing
                            while (AudioSource.isPlaying && !token.IsCancellationRequested)
                            {
                                await UniTask.Delay(33, cancellationToken: token);  // 30FPS
                            }
                        }

                        if (!token.IsCancellationRequested)
                        {
                            try
                            {
                                // Wait for PostGap
                                await UniTask.Delay((int)(v.PostGap * 1000), cancellationToken: token);
                            }
                            catch (OperationCanceledException)
                            {
                                Debug.Log("Task canceled in waiting PostGap");
                            }
                        }

                    }
                }

                catch (Exception ex)
                {
                    Debug.LogError($"Error at Say: {ex.Message}\n{ex.StackTrace}");
                }

                finally
                {
                    OnSayEnd?.Invoke();
                }
            }

            // Reset viseme
            lipSyncHelper?.ResetViseme();
        }

        // Start downloading voices from web/TTS
        public void PrefetchVoices(List<Voice> voices, CancellationToken token)
        {
            foreach (var voice in voices)
            {
                voicePrefetchQueue.Enqueue(voice);
            }
        }

        private async UniTaskVoid StartVoicePrefetchTask()
        {
            voicePrefetchCancellationTokenSource = new CancellationTokenSource();
            var token = voicePrefetchCancellationTokenSource.Token;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (voicePrefetchQueue.TryDequeue(out var voice))
                    {
                        var parameters = voice.TTSConfig != null ? voice.TTSConfig.Params : new Dictionary<string, object>();
                        await SpeechSynthesizerFunc(voice.Text, parameters, token);
                    }
                    else
                    {
                        await UniTask.Delay(10, cancellationToken: token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore OperationCanceledException
            }
        }

        // Stop speech
        public void StopSpeech()
        {
            AudioSource.Stop();
        }
    }
}

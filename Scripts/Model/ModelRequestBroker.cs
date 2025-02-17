using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Model
{
    public class ModelRequestBroker : MonoBehaviour
    {
        [Header("Performance settings")]
        [SerializeField]
        private List<string> splitChars = new List<string>() { "。", "！", "？", ". ", "!", "?" };
        private List<string> splitCharsWithNewLine;
        [SerializeField]
        private List<string> optionalSplitChars = new List<string>() { "、", ", " };
        [SerializeField]
        private int maxLengthBeforeOptionalSplit = 0;
        private ModelController modelController;
        private Queue<AnimatedVoiceRequest> modelRequestQueue = new Queue<AnimatedVoiceRequest>();

        private bool isCancelled = false;
        
        private CancellationTokenSource modelTokenSource;

        private void Start()
        {
            modelController = gameObject.GetComponent<ModelController>();
            if (modelController != null)
            {
                _ = StartListening();
            }
        }

        private void OnDestroy()
        {
            isCancelled = true;
            modelTokenSource?.Cancel();
            modelTokenSource?.Dispose();
        }

        public async UniTask StartListening()
        {
            while (!isCancelled)
            {
                if (modelRequestQueue.Count > 0)
                {
                    var avreq = modelRequestQueue.Dequeue();
                    try
                    {
                        await modelController.AnimatedSay(avreq, modelTokenSource.Token);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error in processing animated voice request: {ex.Message}");
                    }
                }
                else
                {
                    await UniTask.Delay(10);
                }
            }
        }

        public void SetRequest(string text)
        {
            // Stop ongoing speech and clear remaining requests
            modelTokenSource?.Cancel();
            modelTokenSource?.Dispose();
            modelRequestQueue.Clear();
            modelController.StopSpeech();

            // Start new speech
            modelTokenSource = new CancellationTokenSource();
            foreach (var avreq in ToAnimatedVoiceRequests(text))
            {
                modelRequestQueue.Enqueue(avreq);
            }
        }

        private List<AnimatedVoiceRequest> ToAnimatedVoiceRequests(string taggedText, string language = null)
        {
            var animatedVoiceRequests = new List<AnimatedVoiceRequest>();
            var isFirstAnimatedVoice = true;

            try
            {
                // Process each splitted sentence
                splitCharsWithNewLine = new List<string>(splitChars) { "\n" };
                foreach (var text in SplitString(taggedText))
                {
                    if (!string.IsNullOrEmpty(text.Trim()))
                    {
                        var avreq = modelController.ToAnimatedVoiceRequest(text, language);
                        avreq.StartIdlingOnEnd = isFirstAnimatedVoice;
                        isFirstAnimatedVoice = false;

                        Debug.Log($"ModelRequestBroker: {text}");

                        // Set AnimatedVoiceRequest to queue
                        animatedVoiceRequests.Add(avreq);

                        // Prefetch the voice from TTS service
                        foreach (var av in avreq.AnimatedVoices)
                        {
                            foreach (var v in av.Voices)
                            {
                                if (v.Text.Trim() == string.Empty) continue;

                                modelController.PrefetchVoices(new List<Voice>(){new Voice(
                                    v.Text, 0.0f, 0.0f, v.TTSConfig, true, string.Empty
                                )}, modelTokenSource.Token);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error at ToAnimatedVoiceRequests: {ex.Message}\n{ex.StackTrace}");
            }

            return animatedVoiceRequests;
        }

        private List<string> SplitString(string input)
        {
            var result = new List<string>();
            var tempBuffer = "";

            for (int i = 0; i < input.Length; i++)
            {
                tempBuffer += input[i];

                if (IsSplitChar(input[i].ToString()))
                {
                    // Check if the next character is also a split character
                    if (i + 1 < input.Length && IsSplitChar(input[i + 1].ToString()))
                    {
                        // Continue the buffer if the next character is also a split character
                        continue;
                    }
                    else
                    {
                        // Add to result if it's the end of the sequence of split characters
                        result.Add(tempBuffer);
                        tempBuffer = "";
                    }
                }
                else if (IsOptionalSplitChar(input[i].ToString()))
                {
                    if (tempBuffer.Length >= maxLengthBeforeOptionalSplit)
                    {
                        result.Add(tempBuffer);
                        tempBuffer = "";
                    }
                }
            }

            if (!string.IsNullOrEmpty(tempBuffer))
            {
                result.Add(tempBuffer);
            }

            return result;
        }

        private bool IsSplitChar(string character)
        {
            foreach (var splitChar in splitCharsWithNewLine)
            {
                if (character == splitChar)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsOptionalSplitChar(string character)
        {
            foreach (var splitChar in optionalSplitChars)
            {
                if (character == splitChar)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

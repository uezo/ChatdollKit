using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Model
{
    public class ModelRequestBroker : MonoBehaviour
    {
        [Header("Performance settings")]
        [SerializeField]
        private List<string> splitChars = new List<string>() { "。", "！", "？", ".", "!", "?" };
        private List<string> splitCharsWithNewLine;
        [SerializeField]
        private List<string> optionalSplitChars = new List<string>() { "、", "," };
        [SerializeField]
        private int maxLengthBeforeOptionalSplit = 0;
        [Header("Face Expression")]
        [SerializeField]
        private float faceExpressionDuration = 5.0f;

        private ModelController modelController;
        private Queue<AnimatedVoiceRequest> modelRequestQueue = new Queue<AnimatedVoiceRequest>();
        private Dictionary<string, Animation> animationsToPerform { get; set; } = new Dictionary<string, Animation>();

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

        private List<AnimatedVoiceRequest> ToAnimatedVoiceRequests(string taggedText)
        {
            var animatedVoiceRequests = new List<AnimatedVoiceRequest>();
            var facePattern = @"\[face:(.+?)\]";
            var animPattern = @"\[anim:(.+?)\]";
            var isFirstAnimatedVoice = true;

            try
            {
                // Process each splitted sentence
                splitCharsWithNewLine = new List<string>(splitChars) { "\n" };
                foreach (var text in SplitString(taggedText))
                {
                    if (!string.IsNullOrEmpty(text.Trim()))
                    {
                        var avreq = new AnimatedVoiceRequest(startIdlingOnEnd: isFirstAnimatedVoice);
                        var textToSay = text;
                        var ttsConfig = new TTSConfiguration();

                        // Parse face tags and remove it from text to say
                        var faceMatches = Regex.Matches(textToSay, facePattern);
                        textToSay = Regex.Replace(textToSay, facePattern, "");

                        // Parse animation tags and remove it from text to say
                        var animMatches = Regex.Matches(textToSay, animPattern);
                        textToSay = Regex.Replace(textToSay, animPattern, "");

                        // Remove other tags (sometimes invalid format like `[smile]` remains)
                        textToSay = Regex.Replace(textToSay, @"\[(.+?)\]", "");

                        // Add voice
                        avreq.AddVoiceTTS(textToSay, postGap: textToSay.EndsWith("。") ? 0 : 0.3f);

                        var logMessage = textToSay;

                        if (faceMatches.Count > 0)
                        {
                            // Add face if face tag included
                            var face = faceMatches[0].Groups[1].Value;
                            avreq.AddFace(face, duration: faceExpressionDuration);
                            logMessage = $"[face:{face}]" + logMessage;
                            // Set face as style parameter to voice
                            ttsConfig.Params["style"] = face;                                   
                            avreq.AnimatedVoices.Last().Voices.Last().TTSConfig = ttsConfig;
                        }
                        else if (isFirstAnimatedVoice)
                        {
                            // Reset face expression at the beginning of animated voice
                            avreq.AddFace("Neutral");
                        }

                        if (animMatches.Count > 0)
                        {
                            // Add animation if anim tag included
                            var anim = animMatches[0].Groups[1].Value;
                            if (animationsToPerform.ContainsKey(anim))
                            {
                                var a = animationsToPerform[anim];
                                avreq.AddAnimation(a.ParameterKey, a.ParameterValue, a.Duration, a.LayeredAnimationName, a.LayeredAnimationLayerName);
                                logMessage = $"[anim:{anim}]" + logMessage;
                            }
                            else
                            {
                                Debug.LogWarning($"Animation {anim} is not registered.");
                            }
                        }

                        Debug.Log($"ModelRequestBroker: {logMessage}");

                        isFirstAnimatedVoice = false;

                        // Set AnimatedVoiceRequest to queue
                        animatedVoiceRequests.Add(avreq);

                        // Prefetch the voice from TTS service
                        _ = modelController.TextToSpeechFunc.Invoke(new Voice(string.Empty, 0.0f, 0.0f, textToSay, string.Empty, ttsConfig, VoiceSource.TTS, true, string.Empty), modelTokenSource.Token);
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

        public void RegisterAnimation(string name, Animation animation)
        {
            animationsToPerform.Add(name, animation);
        }
    }
}

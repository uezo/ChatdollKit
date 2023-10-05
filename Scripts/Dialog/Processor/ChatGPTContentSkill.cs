using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Model;

namespace ChatdollKit.Dialog.Processor
{
    public class ChatGPTContentSkill : SkillBase
    {
        protected ChatGPTService chatGPT;
        protected List<AnimatedVoiceRequest> responseAnimations = new List<AnimatedVoiceRequest>();
        public bool IsParsing { get; protected set; } = false;

        protected override void Awake()
        {
            base.Awake();

#if UNITY_WEBGL && !UNITY_EDITOR
            chatGPT = GetComponent<ChatGPTServiceWebGL>();
            if (chatGPT == null)
            {
                Debug.LogWarning("ChatGPTService doesn't support stream. Use ChatGPTServiceWebGL instead.");
                chatGPT = GetComponent<ChatGPTService>();
            }
#else
            foreach (var gpt in GetComponents<ChatGPTService>())
            {
                if (gpt is not ChatGPTServiceWebGL)
                {
                    chatGPT = gpt;
                    break;
                }
            }
#endif
        }

#pragma warning disable CS1998
        public override async UniTask<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
        {
            var apiStreamTask = (UniTask)request.Payloads[0];

            // Clear responseAnimations before parsing and performing
            responseAnimations.Clear();

            // Start parsing voices, faces and animations and performing them concurrently
            var parseTask = ParseAnimatedVoiceAsync(token);
            var performTask = PerformAnimatedVoiceAsync(token);

            // Make response
            var response = new Response(request.Id, endTopic: false);
            response.Payloads = new Dictionary<string, object>()
            {
                { "ApiStreamTask", apiStreamTask },
                { "ParseTask", parseTask },
                { "PerformTask", performTask },
                { "UserMessage", new ChatGPTMessage("user", request.Text) }
            };

            return response;
        }
#pragma warning restore CS1998

        public override async UniTask ShowResponseAsync(Response response, Request request, State state, CancellationToken token)
        {
            // Parse payloads
            var payloads = (Dictionary<string, object>)response.Payloads;
            var apiStreamTask = (UniTask)payloads["ApiStreamTask"];
            var parseTask = (UniTask)payloads["ParseTask"];
            var performTask = (UniTask)payloads["PerformTask"];
            var userMessage = (ChatGPTMessage)payloads["UserMessage"];

            // Wait API stream ends
            await apiStreamTask;

            // Wait parsing and performance
            await parseTask;
            await performTask;

            // Update histories
            chatGPT.AddHistory(state, userMessage);
            chatGPT.AddHistory(state, new ChatGPTMessage("assistant", chatGPT.StreamBuffer));
        }

        protected async UniTask ParseAnimatedVoiceAsync(CancellationToken token)
        {
            IsParsing = true;

            try
            {
                var pattern = @"\[face:(.+?)\]";
                var splitIndex = 0;

                while (!token.IsCancellationRequested)
                {
                    // Split current buffer with the marks that represents the end of a sentence
                    var splittedBuffer = chatGPT.StreamBuffer.Replace("。", "。|").Replace("、", "、|").Replace("！", "！|").Replace("？", "？|").Replace(". ", ". |").Replace(", ", ", |").Replace("\n", "").Split('|');

                    if (chatGPT.IsResponseDone && splitIndex == splittedBuffer.Length)
                    {
                        // Exit while loop when stream response ends and all sentences has been processed
                        break;
                    }

                    if (splittedBuffer.Count() > splitIndex + 1 || chatGPT.IsResponseDone)
                    {
                        // Process each splitted unprocessed sentence
                        foreach (var text in splittedBuffer.Skip(splitIndex).Take(chatGPT.IsResponseDone ? splittedBuffer.Length - splitIndex : 1))
                        {
                            splitIndex += 1;
                            if (!string.IsNullOrEmpty(text.Trim()))
                            {
                                var avreq = new AnimatedVoiceRequest();
                                var textToSay = text;

                                // Parse face tags and remove it from text to say
                                var matches = Regex.Matches(textToSay, pattern);
                                textToSay = Regex.Replace(textToSay, pattern, "");

                                // Add voice
                                avreq.AddVoiceTTS(textToSay, postGap: textToSay.EndsWith("。") ? 0 : 0.3f);

                                if (matches.Count > 0)
                                {
                                    // Add face if face tag included
                                    var face = matches[0].Groups[1].Value;
                                    avreq.AddFace(face, duration: 7.0f);
                                    Debug.Log($"Assistant: [{face}] {textToSay}");
                                }
                                else
                                {
                                    Debug.Log($"Assistant: {textToSay}");
                                }

                                // Set AnimatedVoiceRequest to queue
                                responseAnimations.Add(avreq);

                                // Prefetch the voice from TTS service
                                _ = modelController.TextToSpeechFunc.Invoke(new Voice(string.Empty, 0.0f, 0.0f, textToSay, string.Empty, null, VoiceSource.TTS, true, string.Empty), token);
                            }
                        }
                    }

                    // Wait for a bit before processing buffer next time
                    await UniTask.Delay(100, cancellationToken: token);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error at ParseAnimatedVoiceAsync: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                IsParsing = false;
            }
        }

        protected async UniTask PerformAnimatedVoiceAsync(CancellationToken token)
        {
            var isFirstVoice = true;
            while (!token.IsCancellationRequested)
            {
                // Performance ends when streaming response and parsing ends and all response animated voices are done
                if (chatGPT.IsResponseDone && !IsParsing && responseAnimations.Count == 0) break;

                if (responseAnimations.Count > 0)
                {
                    // Retrive AnimatedVoice from queue
                    var req = responseAnimations[0];
                    responseAnimations.RemoveAt(0);

                    if (isFirstVoice)
                    {
                        if (req.AnimatedVoices[0].Faces.Count == 0)
                        {
                            // Reset face expression at the beginning of animated voice
                            req.AddFace("Neutral");
                        }
                        isFirstVoice = false;
                    }

                    // Perform
                    await modelController.AnimatedSay(req, token);
                }
                else
                {
                    // Do nothing (just wait a bit) when no AnimatedVoice in the queue while receiving data
                    await UniTask.Delay(100, cancellationToken: token);
                }
            }
        }
    }
}

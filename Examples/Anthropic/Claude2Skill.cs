using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.Model;

namespace ChatdollKit.Examples.Anthropic
{
    public class Claude2Skill : SkillBase
    {
        [Header("API configuration")]

        // Implement auth on server side. Use AIProxy https://github.com/uezo/aiproxy
        //public string AwsAccessKeyId;
        //public string AwsSecretAccessKey;
        //public string RegionName;

        public int MaxTokensToSample = 200;
        public float Temperature = 0.5f;
        public string Claude2APIUrl;

        [Header("Context configuration")]
        [TextArea(1, 10)]
        public string SystemContent;
        public int HistoryTurns = 10;

        protected List<Claude2Message> histories = new List<Claude2Message>();
        protected bool isResponseDone = false;
        protected string streamBuffer;
        protected bool isParsing = false;
        protected List<AnimatedVoiceRequest> responseAnimations = new List<AnimatedVoiceRequest>();
        protected Dictionary<string, Model.Animation> animationsToPerform = new Dictionary<string, Model.Animation>();

        protected void Start()
        {
            // This is an example of the animation and face expression while processing request.
            // If you want make multi-skill virtual agent move this code to where common logic should be implemented like main app.
            var processingAnimation = new List<Model.Animation>();
            processingAnimation.Add(new Model.Animation("BaseParam", 3, 0.3f));
            processingAnimation.Add(new Model.Animation("BaseParam", 3, 20.0f, "AGIA_Layer_nodding_once_01", "Additive Layer"));
            var processingFace = new List<FaceExpression>();
            processingFace.Add(new FaceExpression("Blink", 3.0f));

            var neutralFaceRequest = new List<FaceExpression>();
            neutralFaceRequest.Add(new FaceExpression("Neutral"));

            var dialogController = gameObject.GetComponent<DialogController>();
#pragma warning disable CS1998
            dialogController.OnRequestAsync = async (request, token) =>
#pragma warning restore CS1998
            {
                modelController.StopIdling();
                modelController.Animate(processingAnimation);
                modelController.SetFace(processingFace);
            };
#pragma warning disable CS1998
            dialogController.OnStartShowingResponseAsync = async (response, token) =>
#pragma warning restore CS1998
            {
                modelController.SetFace(neutralFaceRequest);
            };

            var animationOnStart = new List<Model.Animation>();
            animationOnStart.Add(new Model.Animation("BaseParam", 6, 0.5f));
            animationOnStart.Add(new Model.Animation("BaseParam", 10, 3.0f));
            modelController.Animate(animationOnStart);

            var faceOnStart = new List<FaceExpression>();
            faceOnStart.Add(new FaceExpression("Joy", 3.0f));
            modelController.SetFace(faceOnStart);
        }

        public override async UniTask<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
        {
            try
            {
                if (state.IsNew)
                {
                    histories.Clear();
                }

                var messages = new List<Claude2Message>();
                var promptString = string.Empty;

                // Set system content
                if (!string.IsNullOrEmpty(SystemContent)) {
                    promptString += $"Human: {SystemContent.Replace("{now}", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"))}\n";
                }

                // Histories
                messages.AddRange(histories.Skip(histories.Count - HistoryTurns * 2).ToList());
                foreach (var history in histories.Skip(histories.Count - HistoryTurns * 2).ToList())
                {
                    promptString += $"{history.role}: {history.content}\n";
                }

                // Input
                promptString += $"Human: {request.Text}\nAssistant: ";

                // Start API stream
                var apiStreamTask = InvokeWithStreamAsync(promptString);

                // Start parsing and performing AnimatedVoice from buffer
                responseAnimations.Clear();

                // Start parsing voices, faces and animations and performing them concurrently
                var parseTask = ParseAnimatedVoiceAsync(token);
                var performTask = PerformAnimatedVoiceAsync(token);

                // Make response
                var response = new Response(request.Id);
                response.Payloads = new Dictionary<string, object>()
                {
                    { "ApiStreamTask", apiStreamTask },
                    { "ParseTask", parseTask },
                    { "PerformTask", performTask },
                    { "UserMessage", request.Text }
                };

                return response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error at ProcessAsyncStream: {ex.Message}\n{ex.StackTrace}");
                throw ex;
            }
        }

        public override async UniTask ShowResponseAsync(Response response, Request request, State state, CancellationToken token)
        {
            // Parse payloads
            var payloads = (Dictionary<string, object>)response.Payloads;
            var apiStreamTask = (UniTask)payloads["ApiStreamTask"];
            var parseTask = (UniTask)payloads["ParseTask"];
            var performTask = (UniTask)payloads["PerformTask"];
            var userMessage = (string)payloads["UserMessage"];

            // Wait API stream ends
            await apiStreamTask;
            isResponseDone = true;

            // Wait parsing and performance
            await parseTask;
            await performTask;

            // Update histories after performance
            histories.Add(new Claude2Message() { role = "Human", content = userMessage });
            histories.Add(new Claude2Message() { role = "Assistant", content = streamBuffer });
        }

        protected async UniTask InvokeWithStreamAsync(string prompt)
        {
            // Make request data
            var data = new Dictionary<string, object>()
            {
                { "prompt", prompt },
                { "max_tokens_to_sample", MaxTokensToSample },
                { "temperature", Temperature }
            };

            // Prepare API request
            using var streamRequest = new UnityWebRequest(Claude2APIUrl, "POST");
            streamRequest.SetRequestHeader("Content-Type", "application/json");
            var bodyRaw = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));
            streamRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            streamRequest.downloadHandler = new Claude2StreamDownloadHandler();
            ((Claude2StreamDownloadHandler)streamRequest.downloadHandler).DataCallbackFunc = (chunk) =>
            {
                // Add received data to stream buffer
                streamBuffer += chunk;
            };

            // Start API stream
            isResponseDone = false;
            streamBuffer = string.Empty;
            await streamRequest.SendWebRequest();
        }

        protected async UniTask ParseAnimatedVoiceAsync(CancellationToken token)
        {
            isParsing = true;

            try
            {
                var facePattern = @"\[face:(.+?)\]";
                var animPattern = @"\[anim:(.+?)\]";
                var splitIndex = 0;
                var isFirstAnimatedVoice = true;

                while (!token.IsCancellationRequested)
                {
                    // Split current buffer with the marks that represents the end of a sentence
                    var splittedBuffer = streamBuffer.Replace("。", "。|").Replace("、", "、|").Replace("！", "！|").Replace("？", "？|").Replace(". ", ". |").Replace(", ", ", |").Replace("\n", "").Split('|');

                    if (isResponseDone && splitIndex == splittedBuffer.Length)
                    {
                        // Exit while loop when stream response ends and all sentences has been processed
                        break;
                    }

                    if (splittedBuffer.Count() > splitIndex + 1 || isResponseDone)
                    {
                        // Process each splitted unprocessed sentence
                        foreach (var text in splittedBuffer.Skip(splitIndex).Take(isResponseDone ? splittedBuffer.Length - splitIndex : 1))
                        {
                            splitIndex += 1;
                            if (!string.IsNullOrEmpty(text.Trim()))
                            {
                                var avreq = new AnimatedVoiceRequest(startIdlingOnEnd: isFirstAnimatedVoice);
                                isFirstAnimatedVoice = false;
                                var textToSay = text;

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
                                    avreq.AddFace(face, duration: 7.0f);
                                    logMessage = $"[face:{face}]" + logMessage;
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

                                Debug.Log($"Assistant: {logMessage}");

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
                isParsing = false;
            }
        }

        protected async UniTask PerformAnimatedVoiceAsync(CancellationToken token)
        {
            var isFirstVoice = true;
            while (!token.IsCancellationRequested)
            {
                // Performance ends when streaming response and parsing ends and all response animated voices are done
                if (isResponseDone && !isParsing && responseAnimations.Count == 0) break;

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

        protected class Claude2StreamDownloadHandler : DownloadHandlerScript
        {
            public Action<string> DataCallbackFunc;

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || data.Length < 1) return false;

                var receivedData = System.Text.Encoding.UTF8.GetString(data, 0, dataLength);
                var matches = Regex.Matches(receivedData, @"event\{(.*?)\}");

                var resp = string.Empty;
                if (matches.Count > 0)
                {
                    foreach (Match match in matches)
                    {
                        var bytesJsonString = match.Groups[1].Value;
                        var b64string = JsonConvert.DeserializeObject<Dictionary<string, string>>("{" + bytesJsonString + "}")["bytes"];
                        var decodedBytes = Convert.FromBase64String(b64string);
                        var chunkJsonString = System.Text.Encoding.UTF8.GetString(decodedBytes);
                        var chunkJson = JsonConvert.DeserializeObject<Dictionary<string, object>>(chunkJsonString);
                        if (chunkJson.ContainsKey("completion"))
                        {
                            resp += chunkJson["completion"];
                        }
                    }
                }

                DataCallbackFunc?.Invoke(resp);

                return true;
            }
        }

        protected class Claude2Message
        {
            public string role { get; set; }
            public string content { get; set; }
        }
    }
}

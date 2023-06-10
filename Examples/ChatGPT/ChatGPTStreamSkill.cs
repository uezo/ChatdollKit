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
using ChatdollKit.Network;

namespace ChatdollKit.Examples.ChatGPT
{
    public class ChatGPTStreamSkill : SkillBase
    {
        [Header("API configuration")]
        public string ApiKey;
        public string Model = "gpt-3.5-turbo";
        public int MaxTokens = 2000;
        public float Temperature = 0.5f;

        [Header("Context configuration")]
        [TextArea(1, 6)]
        public string ChatCondition;
        [TextArea(1, 3)]
        public string User1stShot;
        [TextArea(1, 3)]
        public string Assistant1stShot;
        public int HistoryTurns = 10;

        protected List<Dictionary<string, string>> histories = new List<Dictionary<string, string>>();
        protected ChatdollHttp client = new ChatdollHttp(timeout: 20000);

        private bool isResponseDone = false;
        private string streamBuffer;
        private List<AnimatedVoiceRequest> responseAnimations = new List<AnimatedVoiceRequest>();

        private void Start()
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
            dialogController.OnRequestAsync = async (request, token) =>
            {
                modelController.StopIdling();
                modelController.Animate(processingAnimation);
                modelController.SetFace(processingFace);
            };
            dialogController.OnStartShowingResponseAsync = async (response, token) =>
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
                    // Clear history and put guide-rail histories when the state is newly created
                    histories.Clear();
                    if (!string.IsNullOrEmpty(User1stShot))
                    {
                        histories.Add(new Dictionary<string, string>() {
                            { "role", "user" },
                            { "content", User1stShot }
                        });
                    }
                    if (!string.IsNullOrEmpty(Assistant1stShot))
                    {
                        histories.Add(new Dictionary<string, string>() {
                            { "role", "assistant" },
                            { "content", Assistant1stShot }
                        });
                    }
                }

                var messages = new List<Dictionary<string, string>>();

                // Condition
                messages.Add(new Dictionary<string, string>() {
                    { "role", "system" },
                    { "content", ChatCondition.Replace("{now}", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")) }
                });

                // Histories
                messages.AddRange(histories.Skip(histories.Count - HistoryTurns * 2).ToList());

                // Input
                messages.Add(new Dictionary<string, string>() {
                    { "role", "user" },
                    { "content", request.Text }
                });

                // Make request data
                var data = new Dictionary<string, object>()
                {
                    { "model", Model },
                    { "max_tokens", MaxTokens },
                    { "temperature", Temperature },
                    { "messages", messages },
                    { "stream", true },
                };

                // Prepare API request
                var streamRequest = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST");
                streamRequest.SetRequestHeader("Authorization", "Bearer " + ApiKey);
                streamRequest.SetRequestHeader("Content-Type", "application/json");
                var bodyRaw = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));
                streamRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                streamRequest.downloadHandler = new ChatGPTStreamDownloadHandler();
                ((ChatGPTStreamDownloadHandler)streamRequest.downloadHandler).DataCallbackFunc = (chunk) =>
                {
                    // Add received data to stream buffer
                    streamBuffer += chunk;
                };

                // Start API stream
                isResponseDone = false;
                streamBuffer = string.Empty;
                var apiStreamTask = streamRequest.SendWebRequest();

                // Start parsing and performing AnimatedVoice from buffer
                responseAnimations.Clear();
                var parseTask = ParseAnimatedVoiceAsync(token);
                var performTask = PerformAnimatedVoiceAsync(token);

                // Make response
                var response = new Response(request.Id);
                response.Payloads = new Dictionary<string, object>()
                {
                    { "ApiStreamTask", apiStreamTask },
                    { "ParseTask", parseTask },
                    { "PerformTask", performTask },
                    { "UserMessage", messages.Last() }
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
            var apiStreamTask = (UnityWebRequestAsyncOperation)payloads["ApiStreamTask"];
            var parseTask = (UniTask)payloads["ParseTask"];
            var performTask = (UniTask)payloads["PerformTask"];
            var userMessage = (Dictionary<string, string>)payloads["UserMessage"];

            // Wait API stream ends
            await apiStreamTask;
            isResponseDone = true;

            // Wait parsing and performance
            await parseTask;
            await performTask;

            // Update histories
            histories.Add(userMessage);
            histories.Add(new Dictionary<string, string>() {
                { "role", "assistant" },
                { "content", streamBuffer }
            });
        }

        private async UniTask ParseAnimatedVoiceAsync(CancellationToken token)
        {
            var pattern = @"\[face:(.+?)\]";
            var splitIndex = 0;

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

        private async UniTask PerformAnimatedVoiceAsync(CancellationToken token)
        {
            var isFirstVoice = true;
            while (!token.IsCancellationRequested)
            {
                // Performance ends when streaming response ends and all response animated voices are done
                if (isResponseDone && responseAnimations.Count == 0) break;

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

        public class ChatGPTStreamDownloadHandler : DownloadHandlerScript
        {
            public Action<string> DataCallbackFunc;

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || data.Length < 1) return false;

                var receivedData = System.Text.Encoding.UTF8.GetString(data, 0, dataLength);

                var resp = string.Empty;
                foreach (var d in receivedData.Split("data:"))
                {
                    if (!string.IsNullOrEmpty(d) && d.Trim() != "[DONE]")
                    {
                        // Parse JSON and add content data to resp
                        var j = JsonConvert.DeserializeObject<ChatGPTStreamResponse>(d);
                        resp += j.choices[0].delta.GetValueOrDefault("content");
                    }
                }
                DataCallbackFunc?.Invoke(resp);

                return true;
            }
        }

        public class ChatGPTStreamResponse
        {
            public string id { get; set; }
            public List<StreamChoice> choices { get; set; }
        }

        public class StreamChoice
        {
            public Dictionary<string, string> delta { get; set; }
        }
    }
}

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

        protected List<ChatGPTMessage> histories = new List<ChatGPTMessage>();
        protected ChatdollHttp client = new ChatdollHttp(timeout: 20000);

        private bool isResponseDone = false;
        private string streamBuffer;

        protected enum ResponseType
        {
            None, Content, FunctionCalling
        }
        protected ResponseType responseType = ResponseType.None;
        protected Delta firstDelta;

        public List<ChatGPTFunction> ChatGPTFunctions = new List<ChatGPTFunction>();

        private List<AnimatedVoiceRequest> responseAnimations = new List<AnimatedVoiceRequest>();

        private void Start()
        {
            // Functions
            var functions = new ExampleFunctions();
            var weatherFunction = new ChatGPTFunction("weather", "指定された地点の天気予報を調べます", functions.GetWeatherAsync);
            weatherFunction.AddProperty("location", new Dictionary<string, object>()
            {
                { "type", "string" }
            });
            ChatGPTFunctions.Add(weatherFunction);

            var balanceFunction = new ChatGPTFunction("get_balance", "銀行の残高を調べます", functions.GetBalanceAsync);
            balanceFunction.AddProperty("bank_name", new Dictionary<string, object>()
            {
                { "type", "string" }
            });
            ChatGPTFunctions.Add(balanceFunction);

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
                    // Clear history and put guide-rail histories when the state is newly created
                    histories.Clear();
                    if (!string.IsNullOrEmpty(User1stShot))
                    {
                        histories.Add(new ChatGPTMessage("user", User1stShot));
                    }
                    if (!string.IsNullOrEmpty(Assistant1stShot))
                    {
                        histories.Add(new ChatGPTMessage("assistant", Assistant1stShot));
                    }
                }

                var messages = new List<ChatGPTMessage>();

                // Condition
                messages.Add(new ChatGPTMessage(
                    "system",
                    ChatCondition.Replace("{now}", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")))
                );

                // Histories
                messages.AddRange(histories.Skip(histories.Count - HistoryTurns * 2).ToList());

                // Input
                messages.Add(new ChatGPTMessage("user", request.Text));

                // Start API stream
                var apiStreamTask = ChatCompletionAsync(messages);

                // Start parsing and performing AnimatedVoice from buffer
                responseAnimations.Clear();

                // Wait for response type(content / function calling) set
                while (responseType == ResponseType.None)
                {
                    await UniTask.Delay(10, cancellationToken: token);
                }

                // Function calling
                if (responseType == ResponseType.FunctionCalling)
                {
                    var chatGptFunc = ChatGPTFunctions.Where(f => f.name == firstDelta.function_call.name).First();
                    Debug.Log($"Invoke function: {chatGptFunc.name}");

                    // TODO: Waiting AnimatedVoice

                    await apiStreamTask;
                    isResponseDone = true;

                    var responseForRequest = await chatGptFunc.func.Invoke(streamBuffer, token);

                    var function_call_message = new ChatGPTMessage("assistant", function_call: new Dictionary<string, object>() {
                        { "name", chatGptFunc.name },
                        { "arguments", streamBuffer }
                    });

                    // Update histories after function finishes successfully
                    histories.Add(messages.Last());
                    histories.Add(function_call_message);

                    // Add messages
                    messages.Add(function_call_message);
                    messages.Add(new ChatGPTMessage("user", responseForRequest));

                    // Call ChatCompletion to get human-friendly response
                    apiStreamTask = ChatCompletionAsync(messages, false);
                }

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
            var apiStreamTask = (UniTask)payloads["ApiStreamTask"];
            var parseTask = (UniTask)payloads["ParseTask"];
            var performTask = (UniTask)payloads["PerformTask"];
            var userMessage = (ChatGPTMessage)payloads["UserMessage"];

            // Wait API stream ends
            await apiStreamTask;
            isResponseDone = true;

            // Wait parsing and performance
            await parseTask;
            await performTask;

            // Update histories
            histories.Add(userMessage);
            histories.Add(new ChatGPTMessage("assistant", streamBuffer));
        }

        protected async UniTask ChatCompletionAsync(List<ChatGPTMessage> messages, bool useFunctions = true)
        {
            // Make request data
            var data = new Dictionary<string, object>()
                {
                    { "model", Model },
                    { "max_tokens", MaxTokens },
                    { "temperature", Temperature },
                    { "messages", messages },
                    { "stream", true },
                };
            if (useFunctions && ChatGPTFunctions.Count > 0)
            {
                data.Add("functions", ChatGPTFunctions);
            }

            // Prepare API request
            using var streamRequest = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST");
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
            ((ChatGPTStreamDownloadHandler)streamRequest.downloadHandler).SetFirstDelta = (delta) =>
            {
                firstDelta = delta;
                responseType = delta.function_call != null ? ResponseType.FunctionCalling : ResponseType.Content;
            };

            // Start API stream
            isResponseDone = false;
            streamBuffer = string.Empty;
            responseType = ResponseType.None;
            firstDelta = null;
            await streamRequest.SendWebRequest();
        }

        protected async UniTask ParseAnimatedVoiceAsync(CancellationToken token)
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

        protected async UniTask PerformAnimatedVoiceAsync(CancellationToken token)
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

        protected class ChatGPTStreamDownloadHandler : DownloadHandlerScript
        {
            public Action<string> DataCallbackFunc;
            public Action<Delta> SetFirstDelta;
            private bool isDeltaSet = false;

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
                        var delta = j.choices[0].delta;
                        if (!isDeltaSet)
                        {
                            SetFirstDelta(delta);
                            isDeltaSet = true;
                        }
                        if (delta.function_call == null)
                        {
                            resp += delta.content;
                        }
                        else
                        {
                            resp += delta.function_call.arguments;
                        }
                    }
                }
                DataCallbackFunc?.Invoke(resp);

                return true;
            }
        }

        protected class ChatGPTMessage
        {
            public string role { get; set; }
            public string content { get; set; }
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public Dictionary<string, object> function_call { get; set; }
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string name { get; set; }

            public ChatGPTMessage(string role, string content = null, Dictionary<string, object> function_call = null, string name = null)
            {
                this.role = role;
                this.content = content;
                this.function_call = function_call;
                this.name = name;
            }
        }

        protected class ChatGPTStreamResponse
        {
            public string id { get; set; }
            public List<StreamChoice> choices { get; set; }
        }

        protected class StreamChoice
        {
            public Delta delta { get; set; }
        }

        protected class Delta
        {
            public string content { get; set; }
            public FunctionCall function_call { get; set; }
        }

        protected class FunctionCall
        {
            public string name { get; set; }
            public string arguments { get; set; }
        }

        public class ChatGPTFunction
        {
            public string name { get; set; }
            public string description { get; set; }
            public ChatGPTFunctionParameters parameters;
            [JsonIgnore]
            public Func<string, CancellationToken, UniTask<string>> func;

            public ChatGPTFunction(string name, string description, Func<string, CancellationToken, UniTask<string>> func)
            {
                this.name = name;
                this.description = description;
                parameters = new ChatGPTFunctionParameters();
                this.func = func;
            }

            public void AddProperty(string key, Dictionary<string, object> value)
            {
                parameters.properties.Add(key, value);
            }
        }

        public class ChatGPTFunctionParameters
        {
            public string type { get; set; }
            public Dictionary<string, Dictionary<string, object>> properties;

            public ChatGPTFunctionParameters()
            {
                type = "object";
                properties = new Dictionary<string, Dictionary<string, object>>();
            }
        }
    }
}

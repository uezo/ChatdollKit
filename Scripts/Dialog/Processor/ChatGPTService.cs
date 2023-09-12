using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using ChatdollKit.Network;

namespace ChatdollKit.Dialog.Processor
{
    public class ChatGPTService : MonoBehaviour
    {
        [Header("API configuration")]
        public string ApiKey;
        public string Model = "gpt-3.5-turbo-0613";
        public int MaxTokens = 0;
        public float Temperature = 0.5f;

        [Header("Context configuration")]
        [TextArea(1, 6)]
        public string SystemMessageContent;
        public int HistoryTurns = 10;

        protected ChatdollHttp client = new ChatdollHttp(timeout: 20000);

        public bool IsResponseDone { get; protected set; } = false;
        public string StreamBuffer { get; protected set; }

        public enum ResponseType
        {
            None, Content, FunctionCalling
        }
        protected ResponseType responseType = ResponseType.None;
        protected Delta firstDelta;

        protected List<ChatGPTFunction> chatGPTFunctions = new List<ChatGPTFunction>();

        public virtual async UniTask ChatCompletionAsync(List<ChatGPTMessage> messages, bool useFunctions = true)
        {
            // Make request data
            var data = new Dictionary<string, object>()
            {
                { "model", Model },
                { "temperature", Temperature },
                { "messages", messages },
                { "stream", true },
            };
            if (MaxTokens > 0)
            {
                data.Add("max_tokens", MaxTokens);
            }
            if (useFunctions && chatGPTFunctions.Count > 0)
            {
                data.Add("functions", chatGPTFunctions);
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
                StreamBuffer += chunk;
            };
            ((ChatGPTStreamDownloadHandler)streamRequest.downloadHandler).SetFirstDelta = (delta) =>
            {
                firstDelta = delta;
                responseType = delta.function_call != null ? ResponseType.FunctionCalling : ResponseType.Content;
            };

            // Start API stream
            IsResponseDone = false;
            StreamBuffer = string.Empty;
            responseType = ResponseType.None;
            firstDelta = null;
            await streamRequest.SendWebRequest();
            IsResponseDone = true;
        }

        public async UniTask<string> WaitForFunctionName (CancellationToken token)
        {
            while (responseType == ResponseType.None)
            {
                await UniTask.Delay(10, cancellationToken: token);
            }

            if (responseType == ResponseType.FunctionCalling)
            {
                return firstDelta.function_call.name;
            }
            else
            {
                return string.Empty;
            }
        }

        public ChatGPTFunction GetFunction(string name)
        {
            return chatGPTFunctions.Where(f => f.name == name).First();
        }

        public void AddFunction(ChatGPTFunction function)
        {
            chatGPTFunctions.Add(function);
        }

        public List<ChatGPTMessage> GetHistories(State state)
        {
            List<ChatGPTMessage> histories;

            if (state.Data.ContainsKey("ChatGPTHistories"))
            {
                if (state.Data["ChatGPTHistories"] is List<ChatGPTMessage>)
                {

                }
                else
                {
                    state.Data["ChatGPTHistories"] = JsonConvert.DeserializeObject<List<ChatGPTMessage>>(
                        JsonConvert.SerializeObject(state.Data["ChatGPTHistories"])
                    );
                }
                histories = (List<ChatGPTMessage>)state.Data["ChatGPTHistories"];
            }
            else
            {
                histories = new List<ChatGPTMessage>();
            }

            return histories.Skip(histories.Count - HistoryTurns * 2).ToList();
        }

        public void AddHistory(State state, ChatGPTMessage message)
        {
            var histories = GetHistories(state);
            histories.Add(message);
            state.Data["ChatGPTHistories"] = histories;
        }

        // Internal classes
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
    }

    public class ChatGPTMessage
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

    public class ChatGPTFunction
    {
        public string name { get; set; }
        public string description { get; set; }
        public ChatGPTFunctionParameters parameters;

        public ChatGPTFunction(string name, string description)
        {
            this.name = name;
            this.description = description;
            parameters = new ChatGPTFunctionParameters();
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.LLM.ChatGPT
{
    public class ChatGPTService : LLMServiceBase
    {
        public string HistoryKey = "ChatGPTHistories";
        public string CustomParameterKey = "ChatGPTParameters";
        public string CustomHeaderKey = "ChatGPTHeaders";

        [Header("API configuration")]
        public string ApiKey;
        public string Model = "gpt-3.5-turbo";
        public string ChatCompletionUrl;
        public bool IsAzure;
        public int MaxTokens = 0;
        public float Temperature = 0.5f;
        public float FrequencyPenalty = 0.0f;
        public bool Logprobs = false;  // Not available on gpt-4v
        public int TopLogprobs = 0;    // Set true to Logprobs to use TopLogprobs
        public float PresencePenalty = 0.0f;
        public List<string> Stop;

        [Header("Network configuration")]
        [SerializeField]
        protected int responseTimeoutSec = 30;
        [SerializeField]
        protected float noDataResponseTimeoutSec = 5.0f;

        protected JsonSerializerSettings messageSerializationSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All
        };

        public override ILLMMessage CreateMessageAfterFunction(string role = null, string content = null, Dictionary<string, object> function_call = null, string name = null, Dictionary<string, object> arguments = null)
        {
            if (role == "user")
            {
                return new ChatGPTUserMessage(content);
            }
            else
            {
                return new ChatGPTFunctionMessage(content, name);
            }
        }

        protected List<ILLMMessage> GetHistoriesFromStateData(Dictionary<string, object> stateData, int count)
        {
            var messages = new List<ILLMMessage>();

            // Add histories to state if not exists
            if (!stateData.ContainsKey(HistoryKey) || stateData[HistoryKey] == null)
            {
                stateData[HistoryKey] = new JArray();
                return messages;
            }

            // Get JToken array from state
            var serializedMessagesAll = (JArray)stateData[HistoryKey];
            var serializedMessages = serializedMessagesAll.Skip(serializedMessagesAll.Count - count * 2).ToList();
            for (var i = 0; i < serializedMessages.Count; i++)
            {
                // JToken -> string -> Restore object
                messages.Add(JsonConvert.DeserializeObject<ILLMMessage>(serializedMessages[i].ToString(), messageSerializationSettings));
            }

            return messages;
        }

#pragma warning disable CS1998
        public override async UniTask AddHistoriesAsync(ILLMSession llmSession, object dataStore, CancellationToken token = default)
        {
            // Prepare state store
            var serializedMessages = (JArray)((Dictionary<string, object>)dataStore)[HistoryKey];

            // Add user message
            var serializedUserMessage = JsonConvert.SerializeObject(llmSession.Contexts.Last(), messageSerializationSettings);
            serializedMessages.Add(serializedUserMessage);

            // Add assistant message
            if (llmSession.ResponseType == ResponseType.FunctionCalling)
            {
                var functionCallMessage = new ChatGPTAssistantMessage(function_call: new Dictionary<string, object>() {
                    { "name", llmSession.FunctionName },
                    { "arguments", llmSession.StreamBuffer }
                });
                serializedMessages.Add(JsonConvert.SerializeObject(functionCallMessage, messageSerializationSettings));

                // Add also to contexts for using this message in this turn
                llmSession.Contexts.Add(functionCallMessage);
            }
            else
            {
                var assistantMessage = new ChatGPTAssistantMessage(llmSession.StreamBuffer);
                serializedMessages.Add(JsonConvert.SerializeObject(assistantMessage, messageSerializationSettings));
            }
        }

        public override async UniTask<List<ILLMMessage>> MakePromptAsync(string userId, string inputText, Dictionary<string, object> payloads, CancellationToken token = default)
        {
            var messages = new List<ILLMMessage>();

            // System
            if (!string.IsNullOrEmpty(SystemMessageContent))
            {
                messages.Add(new ChatGPTSystemMessage(SystemMessageContent));
            }

            // Histories
            var histories = GetHistoriesFromStateData((Dictionary<string, object>)payloads["StateData"], HistoryTurns);
            messages.AddRange(histories);

            // User (current input)
            if (((Dictionary<string, object>)payloads["RequestPayloads"]).ContainsKey("imageBytes"))
            {
                // Message with image as binary
                var imageBytes = (byte[])((Dictionary<string, object>)payloads["RequestPayloads"])["imageBytes"];
                messages.Add(new ChatGPTUserMessage(inputText, "data:image/jpeg;base64," + Convert.ToBase64String(imageBytes)));
            }
            else if (((Dictionary<string, object>)payloads["RequestPayloads"]).ContainsKey("imageUrl"))
            {
                // Message with image as url
                var imageUrl = (string)((Dictionary<string, object>)payloads["RequestPayloads"])["imageUrl"];
                messages.Add(new ChatGPTUserMessage(inputText, imageUrl));
            }
            else
            {
                // Text message
                messages.Add(new ChatGPTUserMessage(inputText));
            }

            return messages;
        }
#pragma warning restore CS1998

        public override async UniTask<ILLMSession> GenerateContentAsync(List<ILLMMessage> messages, Dictionary<string, object> payloads, bool useFunctions = true, int retryCounter = 1, CancellationToken token = default)
        {
            // Custom parameters and headers
            var stateData = (Dictionary<string, object>)payloads["StateData"];
            var customParameters = stateData.ContainsKey(CustomParameterKey) ? (Dictionary<string, string>)stateData[CustomParameterKey] : new Dictionary<string, string>();
            var customHeaders = stateData.ContainsKey(CustomHeaderKey) ? (Dictionary<string, string>)stateData[CustomHeaderKey] : new Dictionary<string, string>();

            // Start streaming session
            var chatGPTSession = new ChatGPTSession();
            chatGPTSession.Contexts = messages;
            chatGPTSession.StreamingTask = StartStreamingAsync(chatGPTSession, customParameters, customHeaders, useFunctions, token);
            chatGPTSession.FunctionName = await WaitForFunctionName(chatGPTSession, token);

            // Retry
            if (chatGPTSession.ResponseType == ResponseType.Timeout)
            {
                if (retryCounter > 0)
                {
                    Debug.LogWarning($"ChatGPT timeouts with no response data. Retrying ...");
                    chatGPTSession = (ChatGPTSession)await GenerateContentAsync(messages, payloads, useFunctions, retryCounter - 1, token);
                }
                else
                {
                    Debug.LogError($"ChatGPT timeouts with no response data.");
                    chatGPTSession.ResponseType = ResponseType.Error;
                    chatGPTSession.StreamBuffer = ErrorMessageContent;
                }
            }

            return chatGPTSession;
        }

        public virtual async UniTask StartStreamingAsync(ChatGPTSession chatGPTSession, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            // Make request data
            var data = new Dictionary<string, object>()
            {
                { "model", Model },
                { "temperature", Temperature },
                { "messages", chatGPTSession.Contexts },
                { "frequency_penalty", FrequencyPenalty },
                { "presence_penalty", PresencePenalty },
                { "stream", true },
            };

            if (MaxTokens > 0)
            {
                data.Add("max_tokens", MaxTokens);
            }
            if (useFunctions && llmTools.Count > 0 && !Model.ToLower().Contains("vision"))
            {
                data.Add("functions", llmTools);
            }
            if (Logprobs == true)
            {
                data.Add("logprobs", true);
                data.Add("top_logprobs", TopLogprobs);
            }
            if (Stop != null && Stop.Count > 0)
            {
                data.Add("stop", Stop);
            }
            foreach (var p in customParameters)
            {
                data[p.Key] = p.Value;
            }

            // Prepare API request
            using var streamRequest = new UnityWebRequest(
                string.IsNullOrEmpty(ChatCompletionUrl) ? "https://api.openai.com/v1/chat/completions" : ChatCompletionUrl,
                "POST"
            );
            streamRequest.timeout = responseTimeoutSec;

            if (IsAzure)
            {
                streamRequest.SetRequestHeader("api-key", ApiKey);
            }
            else
            {
                streamRequest.SetRequestHeader("Authorization", "Bearer " + ApiKey);
            }
            streamRequest.SetRequestHeader("Content-Type", "application/json");
            foreach (var h in customHeaders)
            {
                streamRequest.SetRequestHeader(h.Key, h.Value);
            }

            if (DebugMode)
            {
                Debug.Log($"Request to ChatGPT: {JsonConvert.SerializeObject(data)}");
            }
            var bodyRaw = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));

            // Request and response handlers
            streamRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);

            var downloadHandler = new ChatGPTStreamDownloadHandler();
            downloadHandler.DebugMode = DebugMode;
            downloadHandler.SetReceivedChunk = (chunk) =>
            {
                // Add received data to stream buffer
                chatGPTSession.StreamBuffer += chunk;
            };
            downloadHandler.SetFirstDelta = (delta) =>
            {
                chatGPTSession.FirstDelta = delta;
                chatGPTSession.ResponseType = delta.function_call != null ? ResponseType.FunctionCalling : ResponseType.Content;
            };
            streamRequest.downloadHandler = downloadHandler;

            // Start API stream
            _ = streamRequest.SendWebRequest();

            // Preprocessing response
            var noDataResponseTimeoutsAt = DateTime.Now.AddMilliseconds(noDataResponseTimeoutSec * 1000);
            while (true)
            {
                // Success
                if (streamRequest.result == UnityWebRequest.Result.Success)
                {
                    break;
                }

                // Timeout with no response data
                else if (streamRequest.downloadedBytes == 0 && DateTime.Now > noDataResponseTimeoutsAt)
                {
                    streamRequest.Abort();
                    chatGPTSession.ResponseType = ResponseType.Timeout;
                    break;
                }

                // Other errors
                else if (streamRequest.isDone)
                {
                    Debug.LogError($"ChatGPT ends with error ({streamRequest.result}): {streamRequest.error}");
                    chatGPTSession.ResponseType = ResponseType.Error;
                    break;
                }

                // Cancel
                else if (token.IsCancellationRequested)
                {
                    Debug.Log("Preprocessing response from ChatGPT canceled.");
                    chatGPTSession.ResponseType = ResponseType.Error;
                    streamRequest.Abort();
                    break;
                }

                await UniTask.Delay(10);
            }

            chatGPTSession.IsResponseDone = true;

            if (DebugMode)
            {
                Debug.Log($"Response from ChatGPT: {JsonConvert.SerializeObject(chatGPTSession.StreamBuffer)}");
            }
        }

        public async UniTask<string> WaitForFunctionName(ChatGPTSession chatGPTSession, CancellationToken token)
        {
            // Wait for response type is set
            while (chatGPTSession.ResponseType == ResponseType.None && !token.IsCancellationRequested)
            {
                await UniTask.Delay(10, cancellationToken: token);
            }

            if (chatGPTSession.ResponseType == ResponseType.FunctionCalling)
            {
                if (DebugMode)
                {
                    Debug.Log($"Function Calling response from ChatGPT: {chatGPTSession.FirstDelta.function_call.name}");
                }
                return chatGPTSession.FirstDelta.function_call.name;
            }
            else if (chatGPTSession.ResponseType == ResponseType.Error)
            {
                if (DebugMode)
                {
                    Debug.Log($"Error response");
                }
                return string.Empty;
            }
            else
            {
                if (DebugMode)
                {
                    Debug.Log($"Content response from ChatGPT");
                }
                return string.Empty;
            }
        }

        // Internal classes
        protected class ChatGPTStreamDownloadHandler : DownloadHandlerScript
        {
            public Action<string> SetReceivedChunk;
            public Action<Delta> SetFirstDelta;
            public bool DebugMode = false;
            private bool isDeltaSet = false;

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || data.Length < 1) return false;

                var receivedData = System.Text.Encoding.UTF8.GetString(data, 0, dataLength);
                if (DebugMode)
                {
                    Debug.Log($"Chunk from ChatGPT: {receivedData}");
                }

                var resp = string.Empty;
                foreach (var d in receivedData.Split("data:"))
                {
                    if (!string.IsNullOrEmpty(d) && d.Trim() != "[DONE]")
                    {
                        // Parse JSON and add content data to resp
                        ChatGPTStreamResponse j = null;
                        try
                        {
                            j = JsonConvert.DeserializeObject<ChatGPTStreamResponse>(d);
                        }
                        catch (Exception)
                        {
                            Debug.LogError($"Deserialize error: {d}");
                            continue;
                        }

                        // Azure OpenAI returns empty choices first response. (returns prompt_filter_results)
                        try
                        {
                            if (j.choices.Count == 0) continue;
                        }
                        catch (Exception)
                        {
                            Debug.LogError($"Empty choices error: {JsonConvert.SerializeObject(j)}");
                            continue;
                        }

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
                SetReceivedChunk(resp);

                return true;
            }
        }
    }

    public class ChatGPTStreamResponse
    {
        public string id { get; set; }
        public List<StreamChoice> choices { get; set; }
    }

    public class StreamChoice
    {
        public Delta delta { get; set; }
    }

    public class Delta
    {
        public string content { get; set; }
        public FunctionCall function_call { get; set; }
    }

    public class FunctionCall
    {
        public string name { get; set; }
        public string arguments { get; set; }
    }

    public class ChatGPTSession : LLMSession
    {
        public Delta FirstDelta { get; set; }

        public ChatGPTSession() : base()
        {
            FirstDelta = null;
        }
    }

    public class ChatGPTSystemMessage : ILLMMessage
    {
        public string role { get; } = "system";
        public string content { get; set; }

        public ChatGPTSystemMessage(string content = null)
        {
            this.content = content;
        }
    }

    public class ChatGPTUserMessage : ILLMMessage
    {
        public string role { get; } = "user";
        public List<IContentPart> content { get; set; }

        [JsonConstructor]
        public ChatGPTUserMessage(string role, List<IContentPart> content)
        {
            this.role = role;
            this.content = content;
        }

        public ChatGPTUserMessage(List<IContentPart> content = null)
        {
            this.content = content;
        }

        public ChatGPTUserMessage(string text)
        {
            content = new List<IContentPart>();
            content.Add(new TextContentPart(text));
        }

        public ChatGPTUserMessage(string text, string imageUrl)
        {
            content = new List<IContentPart>();
            if (!string.IsNullOrEmpty(text))
            {
                content.Add(new TextContentPart(text));
            }
            if (!string.IsNullOrEmpty(imageUrl))
            {
                content.Add(new ImageUrlContentPart(imageUrl));
            }
        }
    }

    public class ChatGPTAssistantMessage : ILLMMessage
    {
        public string role { get; } = "assistant";
        public string content { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> function_call { get; set; }

        [JsonConstructor]
        public ChatGPTAssistantMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }

        public ChatGPTAssistantMessage(string content = null, Dictionary<string, object> function_call = null)
        {
            this.content = content;
            this.function_call = function_call;
        }
    }

    public class ChatGPTFunctionMessage : ILLMMessage
    {
        public string role { get; } = "function";
        public string content { get; set; }
        public string name { get; set; }

        [JsonConstructor]
        public ChatGPTFunctionMessage(string role, string content, string name = null)
        {
            this.role = role;
            this.content = content;
            this.name = name;
        }

        public ChatGPTFunctionMessage(string content = null, string name = null)
        {
            this.content = content;
            this.name = name;
        }
    }

    public interface IContentPart
    {
        string type { get; }
    }

    public class TextContentPart : IContentPart
    {
        public string type { get; } = "text";
        public string text { get; set; }

        public TextContentPart(string text)
        {
            this.text = text;
        }
    }

    public class ImageUrlContentPart : IContentPart
    {
        public string type { get; } = "image_url";
        public string image_url { get; set; }

        public ImageUrlContentPart(string image_url)
        {
            this.image_url = image_url;
        }
    }

    public class ChatGPTFunction : LLMTool
    {
        public ChatGPTFunction(string name, string description) : base(name, description)
        {
            this.name = name;
            this.description = description;
            parameters = new ChatGPTFunctionParameters();
        }
    }

    public class ChatGPTFunctionParameters : LLMToolParameters
    {

    }
}

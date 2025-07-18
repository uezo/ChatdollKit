using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace ChatdollKit.LLM.ChatGPT
{
    public class ChatGPTService : LLMServiceBase
    {
        [Header("API configuration")]
        public string ApiKey;
        public string Model = "gpt-4o-mini";
        public string ChatCompletionUrl;
        public bool IsAzure;
        public bool IsOpenAICompatibleAPI;
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

        public override ILLMMessage CreateMessageAfterFunction(string role = null, string content = null, ILLMSession llmSession = null, Dictionary<string, object> arguments = null)
        {
            if (role == "user")
            {
                return new ChatGPTUserMessage(content);
            }
            else
            {
                return new ChatGPTFunctionMessage(content, ((ChatGPTSession)llmSession).ToolCallId);
            }
        }

        protected override void UpdateContext(LLMSession llmSession)
        {
            // User message
            var lastUserMessage = llmSession.Contexts.Last();
            if (lastUserMessage is ChatGPTUserMessage chatGPTUserMessage)
            {
                // Remove non-text content to keep context light
                chatGPTUserMessage.content.RemoveAll(part => !(part is TextContentPart));
            }
            context.Add(lastUserMessage);

            // Assistant message
            if (llmSession.ResponseType == ResponseType.FunctionCalling)
            {
                var functionCallMessage = new ChatGPTAssistantMessage(tool_calls: new List<Dictionary<string, object>>() {
                    new Dictionary<string, object>()
                    {
                        { "id", ((ChatGPTSession)llmSession).ToolCallId },
                        { "type", "function" },
                        { "function", new Dictionary<string, string>() {
                            { "name", llmSession.FunctionName },
                            { "arguments", llmSession.StreamBuffer }
                        }},
                    }
                });
                context.Add(functionCallMessage);

                // Add also to contexts for using this message in this turn
                llmSession.Contexts.Add(functionCallMessage);
            }
            else
            {
                var assistantMessage = new ChatGPTAssistantMessage(llmSession.StreamBuffer);
                context.Add(assistantMessage);
            }

            contextUpdatedAt = Time.time;
        }

#pragma warning disable CS1998
        public override async UniTask<List<ILLMMessage>> MakePromptAsync(string userId, string inputText, Dictionary<string, object> payloads, CancellationToken token = default)
        {
            var messages = new List<ILLMMessage>();

            // System
            if (!string.IsNullOrEmpty(SystemMessageContent))
            {
                messages.Add(new ChatGPTSystemMessage(SystemMessageContent));
            }

            // Histories
            messages.AddRange(GetContext(historyTurns * 2));

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
            var requestPayloads = (Dictionary<string, object>)payloads["RequestPayloads"];
            var customParameters = requestPayloads.ContainsKey(CustomParameterKey) ? (Dictionary<string, string>)requestPayloads[CustomParameterKey] : new Dictionary<string, string>();
            var customHeaders = requestPayloads.ContainsKey(CustomHeaderKey) ? (Dictionary<string, string>)requestPayloads[CustomHeaderKey] : new Dictionary<string, string>();

            // Start streaming session
            var chatGPTSession = new ChatGPTSession();
            chatGPTSession.Contexts = messages;
            chatGPTSession.ContextId = contextId;
            chatGPTSession.StreamingTask = StartStreamingAsync(chatGPTSession, customParameters, customHeaders, useFunctions, token);
            await WaitForFunctionInfo(chatGPTSession, token);

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
            chatGPTSession.CurrentStreamBuffer = string.Empty;

            // Make request data
            var data = new Dictionary<string, object>()
            {
                { "model", Model },
                { "temperature", Temperature },
                { "messages", chatGPTSession.Contexts },
                { "stream", true },
            };

            if (!IsOpenAICompatibleAPI)
            {
                data["frequency_penalty"] = FrequencyPenalty;
                data["presence_penalty"] = PresencePenalty;
            }
            if (MaxTokens > 0)
            {
                data.Add("max_tokens", MaxTokens);
            }
            if (useFunctions && Tools.Count > 0 && !Model.ToLower().Contains("vision"))
            {
                var tools = new List<Dictionary<string, object>>();
                foreach (var tool in Tools)
                {
                    tools.Add(new Dictionary<string, object>()
                    {
                        { "type", "function" },
                        { "function", tool }
                    });
                }
                data.Add("tools", tools);
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
                chatGPTSession.CurrentStreamBuffer += chunk;
                chatGPTSession.StreamBuffer += chunk;
            };
            downloadHandler.SetFirstDelta = (delta) =>
            {
                chatGPTSession.FirstDelta = delta;
                if (delta.tool_calls != null)
                {
                    chatGPTSession.ToolCallId = delta.tool_calls[0].id;
                    chatGPTSession.FunctionName = delta.tool_calls[0].function.name;
                    chatGPTSession.ResponseType = ResponseType.FunctionCalling;
                }
                else
                {
                    chatGPTSession.ResponseType = ResponseType.Content;
                }
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

            // Update context
            if (chatGPTSession.ResponseType != ResponseType.Error && chatGPTSession.ResponseType != ResponseType.Timeout)
            {
                UpdateContext(chatGPTSession);
            }
            else
            {
                Debug.LogWarning($"Messages are not added to context for response type is not success: {chatGPTSession.ResponseType}");
            }

            // Ends with error
            if (chatGPTSession.ResponseType == ResponseType.Error)
            {
                throw new Exception($"ChatGPT ends with error ({streamRequest.result}): {streamRequest.error}");
            }

            // Process tags
            var extractedTags = ExtractTags(chatGPTSession.CurrentStreamBuffer);
            if (extractedTags.Count > 0 && HandleExtractedTags != null)
            {
                HandleExtractedTags(extractedTags, chatGPTSession);
            }

            if (CaptureImage != null && extractedTags.ContainsKey("vision") && chatGPTSession.IsVisionAvailable)
            {
                // Prevent infinit loop
                chatGPTSession.IsVisionAvailable = false;

                // Get image
                var imageSource = extractedTags["vision"];
                var imageBytes = await CaptureImage(imageSource);

                // Make contexts
                if (imageBytes != null)
                {
                    chatGPTSession.Contexts.Add(new ChatGPTAssistantMessage(chatGPTSession.StreamBuffer));
                    // Image -> Text to get the better accuracy
                    chatGPTSession.Contexts.Add(new ChatGPTUserMessage(new List<IContentPart>() {
                        new ImageUrlContentPart("data:image/jpeg;base64," + Convert.ToBase64String(imageBytes)),
                        new TextContentPart($"This is the image you captured. (source: {imageSource})")
                    }));
                }
                else
                {
                    chatGPTSession.Contexts.Add(new ChatGPTUserMessage("Please inform the user that an error occurred while capturing the image."));
                }

                // Call recursively with image
                await StartStreamingAsync(chatGPTSession, customParameters, customHeaders, useFunctions, token);
            }
            else
            {
                chatGPTSession.IsResponseDone = true;

                if (DebugMode)
                {
                    Debug.Log($"Response from ChatGPT: {JsonConvert.SerializeObject(chatGPTSession.StreamBuffer)}");
                }
            }
        }

        public async UniTask WaitForFunctionInfo(ChatGPTSession chatGPTSession, CancellationToken token)
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
                    Debug.Log($"Function Calling response from ChatGPT: {chatGPTSession.FirstDelta.tool_calls[0].function.name}");
                }
            }
            else if (chatGPTSession.ResponseType == ResponseType.Error)
            {
                if (DebugMode)
                {
                    Debug.Log($"Error response");
                }
            }
            else
            {
                if (DebugMode)
                {
                    Debug.Log($"Content response from ChatGPT");
                }
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
                            if (j == null) continue;
                            if (j.choices.Count == 0) continue;
                        }
                        catch (Exception)
                        {
                            Debug.LogError($"Empty choices error: {JsonConvert.SerializeObject(j)}");
                            continue;
                        }

                        var delta = j.choices[0].delta;
                        if (delta != null)
                        {
                            if (!isDeltaSet)
                            {
                                SetFirstDelta(delta);
                                isDeltaSet = true;
                            }
                            if (delta.tool_calls == null)
                            {
                                resp += delta.content;
                            }
                            else if (delta.tool_calls.Count > 0)
                            {
                                resp += delta.tool_calls[0].function.arguments;
                            }
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
        public List<ToolCall> tool_calls { get; set; }
    }

    public class ToolCall
    {
        public string id { get; set; }
        public FunctionCall function { get; set; }
    }

    public class FunctionCall
    {
        public string name { get; set; }
        public string arguments { get; set; }
    }

    public class ChatGPTSession : LLMSession
    {
        public Delta FirstDelta { get; set; }
        public string ToolCallId { get; set; }

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
        public List<Dictionary<string, object>> tool_calls { get; set; }

        [JsonConstructor]
        public ChatGPTAssistantMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }

        public ChatGPTAssistantMessage(string content = null, List<Dictionary<string, object>> tool_calls = null)
        {
            this.content = content;
            this.tool_calls = tool_calls;
        }
    }

    public class ChatGPTFunctionMessage : ILLMMessage
    {
        public string role { get; } = "tool";
        public string content { get; set; }
        public string tool_call_id { get; set; }

        [JsonConstructor]
        public ChatGPTFunctionMessage(string role, string content, string tool_call_id = null)
        {
            this.role = role;
            this.content = content;
            this.tool_call_id = tool_call_id;
        }

        public ChatGPTFunctionMessage(string content = null, string tool_call_id = null)
        {
            this.content = content;
            this.tool_call_id = tool_call_id;
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
        public Dictionary<string, string> image_url { get; set; }

        public ImageUrlContentPart(string image_url)
        {
            this.image_url = new Dictionary<string, string>() { { "url", image_url } };
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

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
        public string ReasoningEffort;
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
            var assistantMessage = new ChatGPTAssistantMessage();
            if (!string.IsNullOrEmpty(llmSession.StreamBuffer))
            {
                assistantMessage.content = llmSession.StreamBuffer;
            }
            if (!string.IsNullOrEmpty(((ChatGPTSession)llmSession).ToolCallId))
            {
                assistantMessage.tool_calls = new List<Dictionary<string, object>>()
                {
                    new Dictionary<string, object>()
                    {
                        { "id", ((ChatGPTSession)llmSession).ToolCallId },
                        { "type", "function" },
                        { "function", new Dictionary<string, string>() {
                            { "name", llmSession.FunctionName },
                            { "arguments", llmSession.StreamBuffer }
                        }},
                    }
                };
                // Add also to llmSession.Contexts to create tool_call execution response
                llmSession.Contexts.Add(assistantMessage);
            }
            context.Add(assistantMessage);

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
            for (var i = messages.Count - 1; i >= 0; i--)
            {
                var message = messages[i] as ChatGPTAssistantMessage;
                if (message != null && message.tool_calls != null)
                {
                    // Valid sequence: tool_calls -> tool_result
                    if (i + 1 < messages.Count)
                    {
                        var nextMessage = messages[i + 1] as ChatGPTFunctionMessage;
                        if (nextMessage == null)
                        {
                            messages.RemoveAt(i);
                            continue;
                        }
                    }
                    else
                    {
                        messages.RemoveAt(i);
                        continue;
                    }
                }
            }

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
            await WaitForResponseType(chatGPTSession, token);

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
            if (!string.IsNullOrEmpty(ReasoningEffort))
            {
                data["reasoning_effort"] = ReasoningEffort;
                
            }
            if (!IsOpenAICompatibleAPI)
            {
                data["frequency_penalty"] = FrequencyPenalty;
                data["presence_penalty"] = PresencePenalty;
            }
            if (MaxTokens > 0)
            {
                data.Add("max_tokens", MaxTokens);
            }
            if (Tools.Count > 0)
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
                // Mask tools if useFunctions = false. Don't remove tools to keep cache hit and to prevent hallucination
                if (!useFunctions)
                {
                    data.Add("tool_choice", "none");
                }
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
                chatGPTSession.ResponseType = ResponseType.Content;
                // Add received data to stream buffer
                chatGPTSession.CurrentStreamBuffer += chunk;
                chatGPTSession.StreamBuffer += chunk;
            };
            downloadHandler.SetToolCallInfo = (id, name, arguments) =>
            {
                chatGPTSession.ResponseType = ResponseType.Content; // Set to exit WaitForResponseType
                if (!string.IsNullOrEmpty(id))
                {
                    chatGPTSession.ToolCallId = id;
                    chatGPTSession.FunctionName = name;
                    chatGPTSession.FunctionArguments = string.Empty;
                }
                if (!string.IsNullOrEmpty(arguments))
                {
                    chatGPTSession.FunctionArguments += arguments;
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

            if (!string.IsNullOrEmpty(chatGPTSession.ToolCallId))
            {
                foreach (var tool in gameObject.GetComponents<ITool>())
                {
                    var toolSpec = tool.GetToolSpec();
                    if (toolSpec.name == chatGPTSession.FunctionName)
                    {
                        Debug.Log($"Execute tool: {toolSpec.name}({chatGPTSession.FunctionArguments})");
                        // Execute tool
                        var toolResponse = await tool.ExecuteAsync(chatGPTSession.FunctionArguments, token);
                        chatGPTSession.Contexts.Add(new ChatGPTFunctionMessage(toolResponse.Body, chatGPTSession.ToolCallId));
                        // Reset tool call info to prevent infinite loop
                        chatGPTSession.ToolCallId = null;
                        chatGPTSession.FunctionName = null;
                        chatGPTSession.FunctionArguments = null;
                        // Call recursively with tool response
                        await StartStreamingAsync(chatGPTSession, customParameters, customHeaders, false, token);
                    }
                }
            }
            else if (CaptureImage != null && extractedTags.ContainsKey("vision") && chatGPTSession.IsVisionAvailable)
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

        private async UniTask WaitForResponseType(ChatGPTSession chatGPTSession, CancellationToken token)
        {
            // Wait for response type is set
            while (chatGPTSession.ResponseType == ResponseType.None && !token.IsCancellationRequested)
            {
                await UniTask.Delay(10, cancellationToken: token);
            }

            if (chatGPTSession.ResponseType == ResponseType.Error)
            {
                if (DebugMode)
                {
                    Debug.Log($"Error response");
                }
            }
        }

        // Internal classes
        protected class ChatGPTStreamDownloadHandler : DownloadHandlerScript
        {
            public Action<string> SetReceivedChunk;
            public Action<string, string, string> SetToolCallInfo;
            public bool DebugMode = false;

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || data.Length < 1) return false;

                var receivedData = System.Text.Encoding.UTF8.GetString(data, 0, dataLength);
                if (DebugMode)
                {
                    Debug.Log($"Chunk from ChatGPT: {receivedData}");
                }

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
                        if (delta == null) continue;

                        if (delta.tool_calls == null)
                        {
                            SetReceivedChunk(delta.content);
                        }
                        else if (delta.tool_calls.Count > 0)
                        {
                            SetToolCallInfo(
                                delta.tool_calls[0].id,
                                delta.tool_calls[0].function.name,
                                delta.tool_calls[0].function.arguments
                            );
                        }
                    }
                }

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
        public string ToolCallId { get; set; }
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace ChatdollKit.LLM.Claude
{
    public class ClaudeService : LLMServiceBase
    {
        [Header("API configuration")]
        public string ApiKey;
        public string Model = "claude-3-haiku-20240307";
        public string CreateMessageUrl;
        public int MaxTokens = 500;
        public float Temperature = 0.5f;
        //public float TopP = 1.0f; // You should either alter temperature or top_p, but not both.
        public int TopK = 0;
        public List<string> StopSequences;

        [Header("Network configuration")]
        [SerializeField]
        protected int responseTimeoutSec = 30;
        [SerializeField]
        protected float noDataResponseTimeoutSec = 5.0f;

        protected override void UpdateContext(LLMSession llmSession)
        {
            // User message
            var lastUserMessage = llmSession.Contexts.Last() as ClaudeMessage;
            // Remove non-text content to keep context light
            lastUserMessage.content.RemoveAll(c => c.type == "image");
            context.Add(lastUserMessage);

            // Assistant message
            var assistantMessage = new ClaudeMessage("assistant");
            if (!string.IsNullOrEmpty(llmSession.StreamBuffer))
            {
                assistantMessage.content.Add(new ClaudeContent(llmSession.StreamBuffer));
            }
            if (!string.IsNullOrEmpty(((ClaudeSession)llmSession).ToolUseId))
            {
                assistantMessage.content.Add(new ClaudeContent()
                {
                    type = "tool_use",
                    id = ((ClaudeSession)llmSession).ToolUseId,
                    name = llmSession.FunctionName,
                    input = JsonConvert.DeserializeObject<Dictionary<string, object>>(llmSession.FunctionArguments)
                });
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

            // System - Claude takes system message outside of message parameter

            // Histories
            messages.AddRange(GetContext(historyTurns * 2));
            for (var i = messages.Count - 1; i >= 0; i--)
            {
                var message = messages[i] as ClaudeMessage;
                if (message.content.Any(c => c.type == "tool_use"))
                {
                    // Valid sequence: tool_use -> tool_result
                    if (i + 1 < messages.Count)
                    {
                        var nextMessage = messages[i + 1] as ClaudeMessage;
                        if (!nextMessage.content.Any(c => c.type == "tool_result"))
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
                // Message with image
                var imageBytes = (byte[])((Dictionary<string, object>)payloads["RequestPayloads"])["imageBytes"];
                messages.Add(new ClaudeMessage("user", inputText, "image/jpeg", imageBytes));
            }
            else
            {
                // Text message
                messages.Add(new ClaudeMessage("user", inputText));
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
            var claudeSession = new ClaudeSession();
            claudeSession.Contexts = messages;
            claudeSession.ContextId = contextId;
            claudeSession.StreamingTask = StartStreamingAsync(claudeSession, customParameters, customHeaders, useFunctions, token);
            await WaitForResponseType(claudeSession, token);

            // Retry
            if (claudeSession.ResponseType == ResponseType.Timeout)
            {
                if (retryCounter > 0)
                {
                    Debug.LogWarning($"Claude timeouts with no response data. Retrying ...");
                    claudeSession = (ClaudeSession)await GenerateContentAsync(messages, payloads, useFunctions, retryCounter - 1, token);
                }
                else
                {
                    Debug.LogError($"Claude timeouts with no response data.");
                    claudeSession.ResponseType = ResponseType.Error;
                    claudeSession.StreamBuffer = ErrorMessageContent;
                }
            }

            return claudeSession;
        }

        public virtual async UniTask StartStreamingAsync(ClaudeSession claudeSession, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            claudeSession.CurrentStreamBuffer = string.Empty;

            // Make request data
            var data = new Dictionary<string, object>()
            {
                { "model", Model },
                { "messages", claudeSession.Contexts },
                { "system", SystemMessageContent },
                { "max_tokens", MaxTokens },
                { "stop_sequences", StopSequences },
                { "temperature", Temperature },
                { "stream", true },
            };

            if (Tools.Count > 0) // tools must be included when tool_result
            {
                var claudeTools = new List<ClaudeTool>();
                foreach (var tool in Tools)
                {
                    claudeTools.Add(new ClaudeTool(tool));
                }
                data.Add("tools", claudeTools);
                // Mask tools if useFunctions = false. Don't remove tools to prevent hallucination
                if (!useFunctions)
                {
                    data.Add("tool_choice", new Dictionary<string, string>(){ {"type", "none"} });
                }
            }

            if (TopK > 0)
            {
                data.Add("top_k", TopK);
            }
            foreach (var p in customParameters)
            {
                data[p.Key] = p.Value;
            }

            // Prepare API request
            using var streamRequest = new UnityWebRequest(
                string.IsNullOrEmpty(CreateMessageUrl) ? $"https://api.anthropic.com/v1/messages" : CreateMessageUrl,
                "POST"
            );
            streamRequest.timeout = responseTimeoutSec;
            streamRequest.SetRequestHeader("anthropic-version", "2023-06-01");
            streamRequest.SetRequestHeader("Content-Type", "application/json");
            streamRequest.SetRequestHeader("x-api-key", ApiKey);
            foreach (var h in customHeaders)
            {
                streamRequest.SetRequestHeader(h.Key, h.Value);
            }

            if (DebugMode)
            {
                Debug.Log($"Request to Claude: {JsonConvert.SerializeObject(data)}");
            }
            var bodyRaw = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));

            // Request and response handlers
            streamRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            var downloadHandler = new ClaudeStreamDownloadHandler();
            downloadHandler.DebugMode = DebugMode;
            downloadHandler.SetReceivedChunk = (chunk) =>
            {
                claudeSession.ResponseType = ResponseType.Content;
                claudeSession.CurrentStreamBuffer += chunk;
                claudeSession.StreamBuffer += chunk;
            };
            downloadHandler.SetToolCallInfo = (id, name, arguments) =>
            {
                claudeSession.ResponseType = ResponseType.Content;  // Set to exit WaitForResponseType
                if (!string.IsNullOrEmpty(id))
                {
                    claudeSession.ToolUseId = id;
                    claudeSession.FunctionName = name;
                    claudeSession.FunctionArguments = string.Empty;
                }
                if (!string.IsNullOrEmpty(arguments))
                {
                    claudeSession.FunctionArguments += arguments;
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
                    claudeSession.ResponseType = ResponseType.Timeout;
                    break;
                }

                // Other errors
                else if (streamRequest.isDone)
                {
                    Debug.LogError($"Claude ends with error ({streamRequest.result}): {streamRequest.error}");
                    claudeSession.ResponseType = ResponseType.Error;
                    break;
                }

                // Cancel
                else if (token.IsCancellationRequested)
                {
                    Debug.Log("Preprocessing response from Claude canceled.");
                    claudeSession.ResponseType = ResponseType.Error;
                    streamRequest.Abort();
                    break;
                }

                await UniTask.Delay(10);
            }

            // Update context
            if (claudeSession.ResponseType != ResponseType.Error && claudeSession.ResponseType != ResponseType.Timeout)
            {
                UpdateContext(claudeSession);
            }
            else
            {
                Debug.LogWarning($"Messages are not added to context for response type is not success: {claudeSession.ResponseType}");
            }

            // Ends with error
            if (claudeSession.ResponseType == ResponseType.Error)
            {
                throw new Exception($"Claude ends with error ({streamRequest.result}): {streamRequest.error}");
            }

            // Process tags
            var extractedTags = ExtractTags(claudeSession.CurrentStreamBuffer);
            if (extractedTags.Count > 0 && HandleExtractedTags != null)
            {
                HandleExtractedTags(extractedTags, claudeSession);
            }

            if (!string.IsNullOrEmpty(claudeSession.ToolUseId))
            {
                foreach (var tool in gameObject.GetComponents<ITool>())
                {
                    var toolSpec = tool.GetToolSpec();
                    if (toolSpec.name == claudeSession.FunctionName)
                    {
                        Debug.Log($"Execute tool: {toolSpec.name}({claudeSession.FunctionArguments})");
                        // Execute tool
                        var toolResponse = await tool.ExecuteAsync(claudeSession.FunctionArguments, token);
                        claudeSession.Contexts.Add(new ClaudeMessage("user", tool_use_id: claudeSession.ToolUseId, tool_use_content: toolResponse.Body));
                        // Reset tool call info to prevent infinite loop
                        claudeSession.ToolUseId = null;
                        claudeSession.FunctionName = null;
                        claudeSession.FunctionArguments = null;
                        // Call recursively with tool response
                        await StartStreamingAsync(claudeSession, customParameters, customHeaders, false, token);
                    }
                }
            }
            else if (CaptureImage != null && extractedTags.ContainsKey("vision") && claudeSession.IsVisionAvailable)
            {
                // Prevent infinit loop
                claudeSession.IsVisionAvailable = false;

                // Get image
                var imageSource = extractedTags["vision"];
                var imageBytes = await CaptureImage(imageSource);

                // Make contexts
                if (imageBytes != null)
                {
                    claudeSession.Contexts.Add(new ClaudeMessage("assistant", claudeSession.StreamBuffer));
                    // Image -> Text get the better accuracy
                    var userMessageWithVision = new ClaudeMessage("user", mediaType: "image/jpeg", data: imageBytes);
                    userMessageWithVision.content.Add(new ClaudeContent($"This is the image you captured. (source: {imageSource})"));
                    claudeSession.Contexts.Add(userMessageWithVision);
                }
                else
                {
                    claudeSession.Contexts.Add(new ClaudeMessage("user", "Please inform the user that an error occurred while capturing the image."));
                }

                // Call recursively with image
                await StartStreamingAsync(claudeSession, customParameters, customHeaders, useFunctions, token);
            }
            else
            {
                claudeSession.IsResponseDone = true;

                if (DebugMode)
                {
                    Debug.Log($"Response from Claude: {JsonConvert.SerializeObject(claudeSession.StreamBuffer)}");
                }
            }
        }

        protected async UniTask WaitForResponseType(ClaudeSession claudeSession, CancellationToken token)
        {
            // Wait for response type is set
            while (claudeSession.ResponseType == ResponseType.None && !token.IsCancellationRequested)
            {
                await UniTask.Delay(10, cancellationToken: token);
            }
        }

        protected class ClaudeStreamDownloadHandler : DownloadHandlerScript
        {
            public Action<string> SetReceivedChunk;
            public Action<string, string, string> SetToolCallInfo;
            public Action<ResponseType> SetResponseType;
            public bool DebugMode = false;
            private string contentBlockType = string.Empty;

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || data.Length < 1) return false;

                var receivedData = System.Text.Encoding.UTF8.GetString(data, 0, dataLength);
                if (DebugMode)
                {
                    Debug.Log($"Chunk from Claude: {receivedData}");
                }

                foreach (string line in receivedData.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("data:"))
                    {
                        var d = line.Substring("data:".Length).Trim();

                        // Parse JSON and add content data to resp
                        ClaudeStreamResponse csr = null;
                        try
                        {
                            csr = JsonConvert.DeserializeObject<ClaudeStreamResponse>(d);
                        }
                        catch (Exception)
                        {
                            Debug.LogError($"Deserialize error: {d}");
                            continue;
                        }

                        if (csr.type == "content_block_start")
                        {
                            contentBlockType = csr.content_block.type;
                            if (contentBlockType == "tool_use")
                            {
                                SetToolCallInfo(csr.content_block.id, csr.content_block.name, null);
                            }
                            continue;
                        }
                        else if (csr.type == "content_block_delta")
                        {
                            if (csr.delta.type == "input_json_delta")
                            {
                                SetToolCallInfo(null, null, csr.delta.partial_json);
                            }
                            else
                            {
                                SetReceivedChunk(csr.delta.text);
                            }
                        }
                    }
                }

                return true;
            }
        }
    }

    public class ClaudeSession : LLMSession
    {
        public string ToolUseId { get; set; }
    }

    public class ClaudeStreamResponse
    {
        public string type { get; set; }
        public CLaudeContentBlock content_block { get; set; }
        public ClaudeDelta delta { get; set; }
    }

    public class CLaudeContentBlock
    {
        public string type { get; set; }
        public string id { get; set; }
        public string name { get; set; }
    }

    public class ClaudeDelta
    {
        public string type { get; set; }
        public string text { get; set; }
        public string partial_json { get; set; }
    }

    public class ClaudeImageSource
    {
        public string type { get; } = "base64";
        public string media_type { get; set; }
        public string data { get; set; }
    }

    public class ClaudeContent
    {
        public string type { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string text { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ClaudeImageSource source { get; set; }

        // ToolUse Response
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string id { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string name { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> input { get; set; }

        // ToolUse Result Request
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string tool_use_id { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string content { get; set; }

        public ClaudeContent() { }

        public ClaudeContent(string text)
        {
            type = "text";
            this.text = text;
        }

        public ClaudeContent(string mediaType, byte[] imageBytes)
        {
            type = "image";
            source = new ClaudeImageSource();
            source.media_type = mediaType;
            source.data = Convert.ToBase64String(imageBytes);
        }

        public ClaudeContent(string tool_use_id, string content)
        {
            type = "tool_result";
            this.tool_use_id = tool_use_id;
            this.content = content;
        }
    }

    public class ClaudeMessage : ILLMMessage
    {
        public string role { get; set; }
        public List<ClaudeContent> content { get; set; }

        public ClaudeMessage(string role, string text = null, string mediaType = null, byte[] data = null, string tool_use_id = null, string tool_use_content = null)
        {
            this.role = role;
            content = new List<ClaudeContent>();

            if (!string.IsNullOrEmpty(text))
            {
                content.Add(new ClaudeContent(text));
            }

            if (!string.IsNullOrEmpty(mediaType) && data != null)
            {
                content.Add(new ClaudeContent(mediaType, data));
            }

            if (!string.IsNullOrEmpty(tool_use_id) && !string.IsNullOrEmpty(tool_use_content))
            {
                content.Add(new ClaudeContent(tool_use_id, tool_use_content));
            }
        }
    }

    public class ClaudeTool : ILLMTool
    {
        public string name { get; set; }
        public string description { get; set; }
        [JsonIgnore]
        public ILLMToolParameters parameters {
            get
            {
                return input_schema;
            }
            set
            {
                input_schema = value;
            }
        }
        public ILLMToolParameters input_schema { get; set; }

        public ClaudeTool(string name, string description)
        {
            this.name = name;
            this.description = description;
            input_schema = new LLMToolParameters();
        }

        public ClaudeTool(ILLMTool llmTool)
        {
            name = llmTool.name;
            description = llmTool.description;
            parameters = llmTool.parameters;
        }

        public void AddProperty(string key, Dictionary<string, object> value)
        {
            parameters.properties.Add(key, value);
        }
    }
}

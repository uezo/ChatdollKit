using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.LLM.CommandR
{
    public class CommandRService : LLMServiceBase
    {
        [Header("API configuration")]
        public string ApiKey;
        public string Model = "command-r-08-2024";
        public string CreateMessageUrl;
        public int MaxTokens = 500;
        public int MaxInputTokens = 0;
        public float Temperature = 0.3f;
        public int K = 0;
        public float P = 0.75f;
        // public int Seed;
        public List<string> StopSequences;
        public float FrequencyPenalty = 0.0f;
        public float PresencePenalty = 0.0f;

        [Header("Network configuration")]
        [SerializeField]
        protected int responseTimeoutSec = 30;
        [SerializeField]
        protected float noDataResponseTimeoutSec = 5.0f;

        public override ILLMMessage CreateMessageAfterFunction(string role = null, string content = null, ILLMSession llmSession = null, Dictionary<string, object> arguments = null)
        {
            if (role == "user")
            {
                return new CommandRMessage("USER", content);
            }
            else
            {
                var result = new CommandRToolResult()
                {
                    call = new CommandRToolCall()
                    {
                        name = llmSession.FunctionName,
                        parameters = JsonConvert.DeserializeObject<Dictionary<string, object>>(llmSession.StreamBuffer)
                    },
                    outputs = new List<Dictionary<string, object>>()
                };
                foreach (var kv in JsonConvert.DeserializeObject<Dictionary<string, object>>(content))
                {
                    result.outputs.Add(new Dictionary<string, object>() { { kv.Key, kv.Value } } );
                }

                var message = new CommandRMessage("USER");
                message.tool_results = new List<CommandRToolResult>() { result };
                return message;
            }
        }

        protected override void UpdateContext(LLMSession llmSession)
        {
            context = ((CommandRSession)llmSession).ChatHistories.Cast<ILLMMessage>().ToList();
            contextUpdatedAt = Time.time;
        }

#pragma warning disable CS1998
        public override async UniTask<List<ILLMMessage>> MakePromptAsync(string userId, string inputText, Dictionary<string, object> payloads, CancellationToken token = default)
        {
            var messages = new List<ILLMMessage>();

            // System
            if (!string.IsNullOrEmpty(SystemMessageContent))
            {
                messages.Add(new CommandRMessage("SYSTEM", SystemMessageContent));
            }

            // Histories
            messages.AddRange(GetContext(historyTurns));

            // Text message (This message will be removed before sending request)
            messages.Add(new CommandRMessage("USER", inputText));

            return messages;
        }
#pragma warning restore CS1998

        public override async UniTask<ILLMSession> GenerateContentAsync(List<ILLMMessage> messages, Dictionary<string, object> payloads, bool useFunctions = true, int retryCounter = 1, CancellationToken token = default)
        {
            // Custom parameters and headers
            var requestPayloads = (Dictionary<string, object>)payloads["RequestPayloads"];
            var customParameters = requestPayloads.ContainsKey(CustomParameterKey) ? (Dictionary<string, string>)requestPayloads[CustomParameterKey] : new Dictionary<string, string>();
            var customHeaders = requestPayloads.ContainsKey(CustomHeaderKey) ? (Dictionary<string, string>)requestPayloads[CustomHeaderKey] : new Dictionary<string, string>();

            // Split input text / tool result from messages
            var userMessage = (CommandRMessage)messages.Last();
            var inputText = userMessage.message;
            var toolResults = userMessage.tool_results;
            messages.RemoveAt(messages.Count - 1);

            // Start streaming session
            var commandRSession = new CommandRSession();
            commandRSession.Contexts = messages;
            commandRSession.StreamingTask = StartStreamingAsync(inputText, toolResults, commandRSession, customParameters, customHeaders, useFunctions, token);
            await WaitForResponseType(commandRSession, token);

            // Retry
            if (commandRSession.ResponseType == ResponseType.Timeout)
            {
                if (retryCounter > 0)
                {
                    Debug.LogWarning($"Command R timeouts with no response data. Retrying ...");
                    commandRSession = (CommandRSession)await GenerateContentAsync(messages, payloads, useFunctions, retryCounter - 1, token);
                }
                else
                {
                    Debug.LogError($"Command R timeouts with no response data.");
                    commandRSession.ResponseType = ResponseType.Error;
                    commandRSession.StreamBuffer = ErrorMessageContent;
                }
            }

            return commandRSession;
        }

        public virtual async UniTask StartStreamingAsync(string inputText, List<CommandRToolResult> toolResults, CommandRSession commandRSession, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            commandRSession.CurrentStreamBuffer = string.Empty;

            // Make request data
            var data = new Dictionary<string, object>()
            {
                { "stream", true },
                { "model", Model },
                { "temperature", Temperature },
                { "k", K },
                { "p", P },
                { "frequency_penalty", FrequencyPenalty },
                { "presence_penalty", PresencePenalty },
                { "force_single_step", true },  // To avoid tool-calls-chunk without tool name. But I'm not sure.
            };

            if (!string.IsNullOrEmpty(inputText))
            {
                data["message"] = inputText;
            }
            if (commandRSession.Contexts.Count > 0)
            {
                data["chat_history"] = commandRSession.Contexts;
            }
            if (MaxTokens > 0)
            {
                data["max_tokens"] = MaxTokens;
            }
            if (MaxInputTokens > 0)
            {
                data["max_input_tokens"] = MaxInputTokens;
            }
            if (StopSequences.Count > 0)
            {
                data["stop_sequences"] = StopSequences;
            }
            if (toolResults != null && toolResults.Count > 0)
            {
                data["tool_results"] = toolResults;
            }

            if (Tools.Count > 0)
            {
                var commandRTools = new List<CommandRTool>();
                foreach (var tool in Tools)
                {
                    commandRTools.Add(new CommandRTool(tool));
                }
                data.Add("tools", commandRTools);
            }

            foreach (var p in customParameters)
            {
                data[p.Key] = p.Value;
            }

            // Prepare API request
            using var streamRequest = new UnityWebRequest(
                string.IsNullOrEmpty(CreateMessageUrl) ? $"https://api.cohere.com/v1/chat" : CreateMessageUrl,
                "POST"
            );
            streamRequest.timeout = responseTimeoutSec;
            streamRequest.SetRequestHeader("Accept", "application/json");
            streamRequest.SetRequestHeader("Content-Type", "application/json");
            streamRequest.SetRequestHeader("Authorization", $"Bearer {ApiKey}");
            foreach (var h in customHeaders)
            {
                streamRequest.SetRequestHeader(h.Key, h.Value);
            }

            if (DebugMode)
            {
                Debug.Log($"Request to Command R: {JsonConvert.SerializeObject(data)}");
            }
            var bodyRaw = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));

            // Request and response handlers
            streamRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            var downloadHandler = new CommandRStreamDownloadHandler();
            downloadHandler.DebugMode = DebugMode;
            downloadHandler.SetReceivedChunk = (chunk) =>
            {
                commandRSession.CurrentStreamBuffer += chunk;
                commandRSession.StreamBuffer += chunk;
            };
            downloadHandler.SetToolCallInfo = (name) =>
            {
                commandRSession.FunctionName = name;
            };
            downloadHandler.SetResponseType = (responseType) =>
            {
                commandRSession.ResponseType = responseType;
            };
            downloadHandler.SetHistories = (chatHistories) =>
            {
                commandRSession.ChatHistories = chatHistories;
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
                    commandRSession.ResponseType = ResponseType.Timeout;
                    break;
                }

                // Other errors
                else if (streamRequest.isDone)
                {
                    Debug.LogError($"Command R ends with error ({streamRequest.result}): {streamRequest.error}");
                    commandRSession.ResponseType = ResponseType.Error;
                    break;
                }

                // Cancel
                else if (token.IsCancellationRequested)
                {
                    Debug.Log("Preprocessing response from Command R canceled.");
                    commandRSession.ResponseType = ResponseType.Error;
                    streamRequest.Abort();
                    break;
                }

                await UniTask.Delay(10);
            }

            // Update histories
            if (commandRSession.ResponseType != ResponseType.Error && commandRSession.ResponseType != ResponseType.Timeout)
            {
                UpdateContext(commandRSession);
            }
            else
            {
                Debug.LogWarning($"Messages are not added to histories for response type is not success: {commandRSession.ResponseType}");
            }

            // Ends with error
            if (commandRSession.ResponseType == ResponseType.Error)
            {
                throw new Exception($"command R ends with error ({streamRequest.result}): {streamRequest.error}");
            }

            commandRSession.IsResponseDone = true;

            if (DebugMode)
            {
                Debug.Log($"Response from Command R: {JsonConvert.SerializeObject(commandRSession.StreamBuffer)}");
            }
        }

        protected async UniTask WaitForResponseType(CommandRSession commandRSession, CancellationToken token)
        {
            // Wait for response type is set
            while (commandRSession.ResponseType == ResponseType.None && !token.IsCancellationRequested)
            {
                await UniTask.Delay(10, cancellationToken: token);
            }
        }

        protected class CommandRStreamDownloadHandler : DownloadHandlerScript
        {
            public Action<string> SetReceivedChunk;
            public Action<string> SetToolCallInfo;
            public Action<ResponseType> SetResponseType;
            public Action<List<CommandRMessage>> SetHistories;
            private bool isResponseTypeSet = false;
            public bool DebugMode = false;

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || data.Length < 1) return false;

                var receivedData = System.Text.Encoding.UTF8.GetString(data, 0, dataLength);
                if (DebugMode)
                {
                    Debug.Log($"Chunk from Command R: {receivedData}");
                }

                var resp = string.Empty;
                foreach (string line in receivedData.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    // Parse JSON and add content data to resp
                    CommandRStreamResponse csr = null;
                    try
                    {
                        csr = JsonConvert.DeserializeObject<CommandRStreamResponse>(line);
                    }
                    catch (Exception)
                    {
                        Debug.LogError($"Deserialize error: {line}");
                        continue;
                    }

                    if (csr.event_type == "stream-start")
                    {
                        continue;
                    }
                    if (csr.event_type == "text-generation")
                    {
                        if (!isResponseTypeSet)
                        {
                            SetResponseType(ResponseType.Content);
                            isResponseTypeSet = true;
                        }
                        resp += csr.text;
                    }
                    else if (csr.event_type == "tool-calls-chunk")
                    {
                        if (csr.tool_call_delta == null)
                        {
                            // Ignore non-tool-call chunks
                            continue;
                        }

                        if (!isResponseTypeSet)
                        {
                            if (string.IsNullOrEmpty(csr.tool_call_delta.name))
                            {
                                continue;
                            }

                            SetResponseType(ResponseType.FunctionCalling);
                            SetToolCallInfo(csr.tool_call_delta.name);
                            isResponseTypeSet = true;
                        }
                        if (csr.tool_call_delta.index != 0)
                        {
                            break;
                        }
                        resp += csr.tool_call_delta.parameters;
                    }
                    else if (csr.event_type == "stream-end")
                    {
                        SetHistories(csr.response.chat_history);
                        break;
                    }
                }

                SetReceivedChunk(resp);

                return true;
            }
        }
    }

    public class CommandRSession : LLMSession
    {
        public List<CommandRMessage> ChatHistories;
        public string PartialStreamEndChunk;    // Used for WebGL
    }

    public class CommandRStreamResponse
    {
        public string event_type { get; set; }
        public string generation_id { get; set; }
        public string text { get; set; }
        public CommandRToolCallDelta tool_call_delta { get; set; }
        public CommandRStreamResponseResponse response { get; set; }
    }

    public class CommandRStreamResponseResponse
    {
        public List<CommandRMessage> chat_history { get; set; }
    }

    public class CommandRToolCallDelta
    {
        public int index { get; set; }
        public string name { get; set; }
        public string parameters { get; set; }
    }

    public class CommandRMessage : ILLMMessage
    {
        public string role { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string message { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<CommandRToolCall> tool_calls { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<CommandRToolResult> tool_results { get; set; }

        [JsonConstructor]
        public CommandRMessage(string role, string message = null)
        {
            this.role = role;
            this.message = message;
        }
        public CommandRMessage(CommandRToolCall toolCall)
        {
            role = "CHATBOT";
            tool_calls = new List<CommandRToolCall>() { toolCall };
        }
    }

    public class CommandRToolResult
    {
        public CommandRToolCall call { get; set; }
        public List<Dictionary<string, object>> outputs { get; set; }
    }

    public class CommandRToolCall
    {
        public string name { get; set; }
        public Dictionary<string, object> parameters { get; set; }
    }

    public class CommandRTool
    {
        public string name { get; set; }
        public string description { get; set; }
        public Dictionary<string, CommandRToolParameterDifinition> parameterDefinitions { get; set; }

        public CommandRTool(ILLMTool tool)
        {
            name = tool.name;
            description = tool.description;
            if (tool.parameters.properties.Count > 0)
            {
                parameterDefinitions = new Dictionary<string, CommandRToolParameterDifinition>();
                foreach (var prop in tool.parameters.properties)
                {
                    parameterDefinitions[prop.Key] = new CommandRToolParameterDifinition();
                    parameterDefinitions[prop.Key].type = (string)prop.Value["type"];
                    if (prop.Value.ContainsKey("description"))
                    {
                        parameterDefinitions[prop.Key].description = (string)prop.Value["description"];
                    }
                }
            }
        }
    }

    public class CommandRToolParameterDifinition
    {
        public string type { get; set; }
        public string description { get; set; }
        public bool required { get; set; }
    }
}

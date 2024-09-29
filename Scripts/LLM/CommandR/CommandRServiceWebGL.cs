using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.LLM.CommandR
{
    public class CommandRServiceWebGL : CommandRService
    {
        public override bool IsEnabled
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return _IsEnabled;
#else
                return false;
#endif
            }
        }

#if UNITY_WEBGL
        [DllImport("__Internal")]
        protected static extern void StartCommandRMessageStreamJS(string targetObjectName, string sessionId, string url, string apiKey, string chatCompletionRequest);
        [DllImport("__Internal")]
        protected static extern void AbortCommandRMessageStreamJS();

        protected bool isChatCompletionJSDone { get; set; } = false;
        protected Dictionary<string, CommandRSession> sessions { get; set; } = new Dictionary<string, CommandRSession>();

        public override async UniTask StartStreamingAsync(string inputText, List<CommandRToolResult> toolResults, CommandRSession commandRSession, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            commandRSession.CurrentStreamBuffer = string.Empty;
            commandRSession.PartialStreamEndChunk = string.Empty;

            // Add session for callback
            var sessionId = Guid.NewGuid().ToString();
            sessions.Add(sessionId, commandRSession);

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

            if (llmTools.Count > 0)
            {
                var commandRTools = new List<CommandRTool>();
                foreach (var tool in llmTools)
                {
                    commandRTools.Add(new CommandRTool(tool));
                }
                data.Add("tools", commandRTools);
            }

            foreach (var p in customParameters)
            {
                data[p.Key] = p.Value;
            }

            // TODO: Support custom headers later...
            if (customHeaders.Count > 0)
            {
                Debug.LogWarning("Custom headers for Command R on WebGL is not supported for now.");
            }

            var serializedData = JsonConvert.SerializeObject(data);

            if (DebugMode)
            {
                Debug.Log($"Request to Command R: {serializedData}");
            }

            // Start API stream
            isChatCompletionJSDone = false;
            StartCommandRMessageStreamJS(
                gameObject.name,
                sessionId,
                string.IsNullOrEmpty(CreateMessageUrl) ? $"https://api.cohere.com/v1/chat" : CreateMessageUrl,
                ApiKey,
                serializedData
            );

            // Preprocessing response
            var noDataResponseTimeoutsAt = DateTime.Now.AddMilliseconds(noDataResponseTimeoutSec * 1000);
            while (true)
            {
                // Success
                if (!string.IsNullOrEmpty(commandRSession.StreamBuffer) && isChatCompletionJSDone)
                {
                    break;
                }

                // Timeout with no response data
                else if (string.IsNullOrEmpty(commandRSession.StreamBuffer) && DateTime.Now > noDataResponseTimeoutsAt)
                {
                    Debug.LogError($"Command R timeouts");
                    AbortCommandRMessageStreamJS();
                    commandRSession.ResponseType = ResponseType.Timeout;
                    sessions.Remove(sessionId);
                    break;
                }

                // Other errors
                else if (isChatCompletionJSDone)
                {
                    Debug.LogError($"Command R ends with error");
                    commandRSession.ResponseType = ResponseType.Error;
                    break;
                }

                // Cancel
                else if (token.IsCancellationRequested)
                {
                    Debug.Log("Preprocessing response from Command R canceled.");
                    commandRSession.ResponseType = ResponseType.Error;
                    AbortCommandRMessageStreamJS();
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
                throw new Exception($"command R ends with error");
            }

            commandRSession.IsResponseDone = true;

            sessions.Remove(sessionId);

            if (DebugMode)
            {
                Debug.Log($"Response from Command R: {JsonConvert.SerializeObject(commandRSession.StreamBuffer)}");
            }
        }

        public void SetCommandRMessageStreamChunk(string chunkStringWithSessionId)
        {
            var splittedChunk = chunkStringWithSessionId.Split("::");
            var sessionId = splittedChunk[0];
            var chunkString = splittedChunk[1];

            if (string.IsNullOrEmpty(chunkString))
            {
                Debug.Log("Chunk is null or empty. Set true to isChatCompletionJSDone.");
                isChatCompletionJSDone = true;
                return;
            }

            if (DebugMode)
            {
                Debug.Log($"Chunk from Command R: {chunkString}");
            }

            if (!sessions.ContainsKey(sessionId))
            {
                Debug.LogWarning($"Session not found. Set true to isChatCompletionJSDone.: {sessionId}");
                isChatCompletionJSDone = true;
                return;
            }

            var commandRSession = sessions[sessionId];

            var resp = string.Empty;
            var isDone = false;
            foreach (string line in chunkString.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                // Parse JSON and add content data to resp
                CommandRStreamResponse csr = null;
                var lineToParse = line;
                try
                {
                    if (!string.IsNullOrEmpty(commandRSession.PartialStreamEndChunk))
                    {
                        Debug.LogWarning("Append line to partialStreamEndChunk");
                        lineToParse = commandRSession.PartialStreamEndChunk + lineToParse;
                    }
                    csr = JsonConvert.DeserializeObject<CommandRStreamResponse>(lineToParse);
                }
                catch (Exception)
                {
                    Debug.LogError($"Deserialize error: {lineToParse}");
                    if (line.Contains("stream-end"))
                    {
                        commandRSession.PartialStreamEndChunk = lineToParse;
                    }
                    continue;
                }

                if (csr.event_type == "stream-start")
                {
                    continue;
                }
                if (csr.event_type == "text-generation")
                {
                    if (commandRSession.ResponseType == ResponseType.None)
                    {
                        commandRSession.ResponseType = ResponseType.Content;
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

                    if (commandRSession.ResponseType == ResponseType.None)
                    {
                        if (string.IsNullOrEmpty(csr.tool_call_delta.name))
                        {
                            continue;
                        }

                        commandRSession.ResponseType = ResponseType.FunctionCalling;
                        commandRSession.FunctionName = csr.tool_call_delta.name;
                    }
                    if (csr.tool_call_delta.index != 0)
                    {
                        break;
                    }
                    resp += csr.tool_call_delta.parameters;
                }
                else if (csr.event_type == "stream-end")
                {
                    commandRSession.ChatHistories = csr.response.chat_history;
                    isDone = true;
                    break;
                }
            }

            commandRSession.CurrentStreamBuffer += resp;
            commandRSession.StreamBuffer += resp;

            if (isDone)
            {
                isChatCompletionJSDone = true;
            }
        }
#endif
    }
}

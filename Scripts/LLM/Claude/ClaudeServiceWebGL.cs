using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.LLM.Claude
{
    public class ClaudeServiceWebGL : ClaudeService
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
        protected static extern void StartClaudeMessageStreamJS(string targetObjectName, string sessionId, string url, string apiKey, string chatCompletionRequest);
        [DllImport("__Internal")]
        protected static extern void AbortClaudeMessageStreamJS();

        protected bool isChatCompletionJSDone { get; set; } = false;
        protected Dictionary<string, ClaudeSession> sessions { get; set; } = new Dictionary<string, ClaudeSession>();

        public override async UniTask StartStreamingAsync(ClaudeSession claudeSession, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            claudeSession.CurrentStreamBuffer = string.Empty;

            // Store session with id to receive streaming data from JavaScript
            var sessionId = Guid.NewGuid().ToString();
            sessions.Add(sessionId, claudeSession);

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

            // TODO: Support custom headers later...
            if (customHeaders.Count > 0)
            {
                Debug.LogWarning("Custom headers for Claude on WebGL is not supported for now.");
            }

            var serializedData = JsonConvert.SerializeObject(data);

            if (DebugMode)
            {
                Debug.Log($"Request to Claude: {serializedData}");
            }

            // Start API stream
            isChatCompletionJSDone = false;
            StartClaudeMessageStreamJS(
                gameObject.name,
                sessionId,
                string.IsNullOrEmpty(CreateMessageUrl) ? $"https://api.anthropic.com/v1/messages" : CreateMessageUrl,
                ApiKey,
                serializedData
            );

            // Preprocessing response
            var noDataResponseTimeoutsAt = DateTime.Now.AddMilliseconds(noDataResponseTimeoutSec * 1000);
            while (true)
            {
                // Success
                if ((!string.IsNullOrEmpty(claudeSession.StreamBuffer) || !string.IsNullOrEmpty(claudeSession.FunctionName)) && isChatCompletionJSDone)
                {
                    break;
                }

                // Timeout with no response data
                else if (string.IsNullOrEmpty(claudeSession.StreamBuffer) && DateTime.Now > noDataResponseTimeoutsAt)
                {
                    Debug.LogError($"Claude timeouts");
                    AbortClaudeMessageStreamJS();
                    claudeSession.ResponseType = ResponseType.Timeout;
                    sessions.Remove(sessionId);
                    break;
                }

                // Other errors
                else if (isChatCompletionJSDone)
                {
                    Debug.LogError($"Claude ends with error");
                    claudeSession.ResponseType = ResponseType.Error;
                    break;
                }

                // Cancel
                else if (token.IsCancellationRequested)
                {
                    Debug.Log("Preprocessing response from Claude canceled.");
                    claudeSession.ResponseType = ResponseType.Error;
                    AbortClaudeMessageStreamJS();
                    break;
                }

                await UniTask.Delay(10);
            }

            // Update histories
            if (claudeSession.ResponseType != ResponseType.Error && claudeSession.ResponseType != ResponseType.Timeout)
            {
                UpdateContext(claudeSession);
            }
            else
            {
                Debug.LogWarning($"Messages are not added to histories for response type is not success: {claudeSession.ResponseType}");
            }

            // Ends with error
            if (claudeSession.ResponseType == ResponseType.Error)
            {
                throw new Exception($"Claude ends with error");
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
                        await StartStreamingAsync(claudeSession, customParameters, customHeaders, useFunctions, token);
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
                await StartStreamingAsync(claudeSession, customParameters, customHeaders, false, token);
            }
            else
            {
                claudeSession.IsResponseDone = true;

                sessions.Remove(sessionId);

                if (DebugMode)
                {
                    Debug.Log($"Response from Claude: {JsonConvert.SerializeObject(claudeSession.StreamBuffer)}");
                }
            }
        }

        public void SetClaudeMessageStreamChunk(string chunkStringWithSessionId)
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
                Debug.Log($"Chunk from Claude: {chunkString}");
            }

            if (!sessions.ContainsKey(sessionId))
            {
                Debug.LogWarning($"Session not found. Set true to isChatCompletionJSDone.: {sessionId}");
                isChatCompletionJSDone = true;
                return;
            }

            var claudeSession = sessions[sessionId];

            var isDone = false;
            foreach (string line in chunkString.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
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
                        claudeSession.ResponseType = ResponseType.Content;
                        if (csr.content_block.type == "tool_use")
                        {
                            if (!string.IsNullOrEmpty(csr.content_block.id))
                            {
                                claudeSession.ToolUseId = csr.content_block.id;
                                claudeSession.FunctionName = csr.content_block.name;
                                claudeSession.FunctionArguments = string.Empty;
                            }
                        }
                    }
                    else if (csr.type == "content_block_delta")
                    {
                        if (csr.delta.type == "input_json_delta")
                        {
                            claudeSession.FunctionArguments += csr.delta.partial_json;
                        }
                        else
                        {
                            claudeSession.CurrentStreamBuffer += csr.delta.text;
                            claudeSession.StreamBuffer += csr.delta.text;
                        }
                    }
                    else if (csr.type == "message_stop")
                    {
                        Debug.Log("Chunk is data:{'type': 'message_stop'}. Set true to isChatCompletionJSDone.");
                        isDone = true;
                        break;
                    }
                }
            }

            if (isDone)
            {
                isChatCompletionJSDone = true;
            }
        }
#endif
    }
}

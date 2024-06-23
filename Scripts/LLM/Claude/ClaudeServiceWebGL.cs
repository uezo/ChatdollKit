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

        public override async UniTask StartStreamingAsync(ClaudeSession claudeSession, Dictionary<string, object> stateData, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            claudeSession.CurrentStreamBuffer = string.Empty;

            string sessionId;
            if (stateData.ContainsKey("chatGPTSessionId"))
            {
                // Use existing session id for callback
                sessionId = (string)stateData["chatGPTSessionId"];
            }
            else
            {
                // Add session for callback
                sessionId = Guid.NewGuid().ToString();
                sessions.Add(sessionId, claudeSession);
            }

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

            if (llmTools.Count > 0) // tools must be included when tool_result
            {
                var claudeTools = new List<ClaudeTool>();
                foreach (var tool in llmTools)
                {
                    claudeTools.Add(new ClaudeTool(tool));
                }
                data.Add("tools", claudeTools);
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
            if (customHeaders.Count >= 0)
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
                if (!string.IsNullOrEmpty(claudeSession.StreamBuffer) && isChatCompletionJSDone)
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

            // Remove non-text parts to keep context light
            var lastUserMessage = claudeSession.Contexts.Last() as ClaudeMessage;
            if (lastUserMessage != null)
            {
                lastUserMessage.content.RemoveAll(c => c.type == "image");
            }

            // Update histories
            if (claudeSession.ResponseType != ResponseType.Error && claudeSession.ResponseType != ResponseType.Timeout)
            {
                await AddHistoriesAsync(claudeSession, stateData, token);
            }
            else
            {
                Debug.LogWarning($"Messages are not added to histories for response type is not success: {claudeSession.ResponseType}");
            }

            var extractedTags = ExtractTags(claudeSession.CurrentStreamBuffer);

            if (CaptureImage != null && extractedTags.ContainsKey("vision") && claudeSession.IsVisionAvailable)
            {
                // Prevent infinit loop
                claudeSession.IsVisionAvailable = false;

                // Get image
                var imageBytes = await CaptureImage(extractedTags["vision"]);

                // Make contexts
                var lastUserContentText = lastUserMessage.content.Where(c => !string.IsNullOrEmpty(c.text)).First().text;
                if (imageBytes != null)
                {
                    claudeSession.Contexts.Add(new ClaudeMessage("assistant", claudeSession.StreamBuffer));
                    // Image -> Text get the better accuracy
                    var userMessageWithVision = new ClaudeMessage("user", mediaType: "image/jpeg", data: imageBytes);
                    userMessageWithVision.content.Add(new ClaudeContent(lastUserContentText));
                    claudeSession.Contexts.Add(userMessageWithVision);
                }
                else
                {
                    claudeSession.Contexts.Add(new ClaudeMessage("user", "Please inform the user that an error occurred while capturing the image."));
                }

                // Call recursively with image
                await StartStreamingAsync(claudeSession, stateData, customParameters, customHeaders, useFunctions, token);
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

            var resp = string.Empty;
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
                        if (csr.content_block.type == "tool_use")
                        {
                            claudeSession.ToolUseId = csr.content_block.id;
                            claudeSession.FunctionName = csr.content_block.name;
                            claudeSession.ResponseType = ResponseType.FunctionCalling;
                        }
                        else
                        {
                            claudeSession.ResponseType = ResponseType.Content;
                        }
                        continue;
                    }
                    else if (csr.type == "content_block_delta")
                    {
                        if (claudeSession.ResponseType == ResponseType.FunctionCalling)
                        {
                            resp += csr.delta.partial_json;
                        }
                        else
                        {
                            resp += csr.delta.text;
                        }
                    }
                    else if (csr.type == "content_block_stop")
                    {
                        // NOTE: Only the first content block is used
                        isDone = true;
                        break;
                    }
                }
            }

            claudeSession.CurrentStreamBuffer += resp;
            claudeSession.StreamBuffer += resp;

            if (isDone)
            {
                isChatCompletionJSDone = true;
            }
        }
#endif
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.LLM.AIAvatarKit
{
    public class AIAvatarKitServiceWebGL : AIAvatarKitService
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
        protected static extern void StartAIAvatarKitMessageStreamJS(string targetObjectName, string sessionId, string url, string chatCompletionRequest, string aakHeaders);
        [DllImport("__Internal")]
        protected static extern void AbortAIAvatarKitMessageStreamJS();

        protected bool isChatCompletionJSDone { get; set; } = false;
        protected Dictionary<string, AIAvatarKitSession> sessions { get; set; } = new Dictionary<string, AIAvatarKitSession>();

        public override async UniTask StartStreamingAsync(AIAvatarKitSession aakSession, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            aakSession.CurrentStreamBuffer = string.Empty;

            var aakRequest = (AIAvatarKitRequestMessage)aakSession.Contexts[0];

            // Store session with id to receive streaming data from JavaScript
            var sessionId = Guid.NewGuid().ToString();
            sessions.Add(sessionId, aakSession);

            // Make request data
            var data = new Dictionary<string, object>()
            {
                { "type", "start" },    // Always start
                { "session_id", AAKSessionId },
                { "user_id", User },
                { "text", aakRequest.Text },
            };

            if (!string.IsNullOrEmpty(aakRequest.ImageUrl))
            {
                data["files"] = new List<Dictionary<string, string>>()
                {
                    new Dictionary<string, string>()
                    {
                        { "type", "image" },
                        { "url", aakRequest.ImageUrl }
                    }
                };
            }

            if (SystemPromptParams != null && SystemPromptParams.Count > 0)
            {
                data["system_prompt_params"] = SystemPromptParams;
            }

            if (!string.IsNullOrEmpty(aakSession.ContextId))
            {
                data.Add("context_id", aakSession.ContextId);
            }

            foreach (var p in customParameters)
            {
                data[p.Key] = p.Value;
            }

            // TODO: Support custom headers later...
            if (customHeaders.Count > 0)
            {
                Debug.LogWarning("Custom headers for AIAvatarKit on WebGL is not supported for now.");
            }

            var authHeader = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(ApiKey))
            {
                authHeader["Authorization"] = $"Bearer {ApiKey}";
            }

            var serializedData = JsonConvert.SerializeObject(data);

            if (DebugMode)
            {
                Debug.Log($"Request to AIAvatarKit: {serializedData}");
            }

            // Start API stream
            isChatCompletionJSDone = false;
            StartAIAvatarKitMessageStreamJS(
                gameObject.name,
                sessionId,
                BaseUrl + "/chat",
                serializedData,
                JsonConvert.SerializeObject(authHeader)
            );

            // Preprocessing response
            var noDataResponseTimeoutsAt = DateTime.Now.AddMilliseconds(noDataResponseTimeoutSec * 1000);
            while (true)
            {
                // Success
                if (!string.IsNullOrEmpty(aakSession.StreamBuffer) && isChatCompletionJSDone)
                {
                    break;
                }

                // Timeout with no response data
                else if (string.IsNullOrEmpty(aakSession.StreamBuffer) && DateTime.Now > noDataResponseTimeoutsAt)
                {
                    Debug.LogError($"AIAvatarKit timeouts");
                    AbortAIAvatarKitMessageStreamJS();
                    aakSession.ResponseType = ResponseType.Timeout;
                    sessions.Remove(sessionId);
                    break;
                }

                // Other errors
                else if (isChatCompletionJSDone)
                {
                    Debug.LogError($"AIAvatarKit ends with error");
                    aakSession.ResponseType = ResponseType.Error;
                    break;
                }

                // Cancel
                else if (token.IsCancellationRequested)
                {
                    Debug.Log("Preprocessing response from AIAvatarKit canceled.");
                    aakSession.ResponseType = ResponseType.Error;
                    AbortAIAvatarKitMessageStreamJS();
                    break;
                }

                await UniTask.Delay(10);
            }

            // Update histories (put ConversationId to state)
            if (aakSession.ResponseType != ResponseType.Error && aakSession.ResponseType != ResponseType.Timeout)
            {
                UpdateContext(aakSession);
            }
            else
            {
                Debug.LogWarning($"Messages are not added to histories for response type is not success: {aakSession.ResponseType}");
            }

            // Ends with error
            if (aakSession.ResponseType == ResponseType.Error)
            {
                throw new Exception($"AIAvatarKit ends with error");
            }

            // Process tags
            var extractedTags = ExtractTags(aakSession.CurrentStreamBuffer);
            if (extractedTags.Count > 0 && HandleExtractedTags != null)
            {
                HandleExtractedTags(extractedTags, aakSession);
            }

            if (CaptureImage != null && extractedTags.ContainsKey("vision") && aakSession.IsVisionAvailable)
            {
                // Prevent infinit loop
                aakSession.IsVisionAvailable = false;

                // Get image
                var imageSource = extractedTags["vision"];
                var imageBytes = await CaptureImage(imageSource);

                // Make contexts
                if (imageBytes != null)
                {
                    // Upload image
                    var imageUrl = await UploadImage(imageBytes);
                    if (string.IsNullOrEmpty(imageUrl))
                    {
                        aakSession.Contexts = new List<ILLMMessage>() { new AIAvatarKitRequestMessage("Please inform the user that an error occurred while capturing the image.") };
                    }
                    else
                    {
                        aakRequest.ImageUrl = imageUrl;
                        aakRequest.Text = $"This is the image you captured. (source: {imageSource})";
                    }
                }
                else
                {
                    aakSession.Contexts = new List<ILLMMessage>() { new AIAvatarKitRequestMessage("Please inform the user that an error occurred while capturing the image.") };
                }

                // Call recursively with image
                await StartStreamingAsync(aakSession, customParameters, customHeaders, useFunctions, token);
            }
            else
            {
                aakSession.IsResponseDone = true;

                sessions.Remove(sessionId);

                if (DebugMode)
                {
                    Debug.Log($"Response from AIAvatarKit: {JsonConvert.SerializeObject(aakSession.StreamBuffer)}");
                }
            }
        }

        public void SetAIAvatarKitMessageStreamChunk(string chunkStringWithSessionId)
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
                Debug.Log($"Chunk from AIAvatarKit: {chunkString}");
            }

            if (!sessions.ContainsKey(sessionId))
            {
                Debug.LogWarning($"Session not found. Set true to isChatCompletionJSDone.: {sessionId}");
                isChatCompletionJSDone = true;
                return;
            }

            var aakSession = sessions[sessionId];

            var isDone = false;
            foreach (string line in chunkString.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("data:"))
                {
                    var d = line.Substring("data:".Length).Trim();

                    try
                    {
                        var asr = JsonConvert.DeserializeObject<AIAvatarKitStreamResponse>(d);
                        if (asr.type == "start")
                        {
                            aakSession.ContextId = asr.context_id;
                            continue;
                        }
                        else if (asr.type == "chunk")
                        {
                            var language = !string.IsNullOrEmpty(asr.language)
                                ? $"[lang:{asr.language}]"
                                : string.Empty;
                            // Add `\n` to flush stream buffer immediately
                            aakSession.CurrentStreamBuffer += (language + asr.text + "\n ");
                            aakSession.StreamBuffer += (language + asr.text + "\n ");
                            aakSession.ContextId = asr.context_id;
                            continue;
                        }
                        else if (asr.type == "tool_call")
                        {
                            if (HandleToolCall != null)
                            {
                                var toolCall = (asr.metadata["tool_call"] as JObject).ToObject<AIAvatarKitToolCall>();
                                HandleToolCall(toolCall);
                            }
                            continue;
                        }
                        else if (asr.type == "final" || asr.type == "vision")
                        {
                            isDone = true;
                            break;
                        }
                        else if (asr.type == "error")
                        {
                            Debug.LogError($"Error in AIAvatarKit: {d}");
                            aakSession.ResponseType = ResponseType.Error;
                            isDone = true;
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        Debug.LogError($"Deserialize error: {d}");
                        continue;
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

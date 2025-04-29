using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.LLM.Dify
{
    public class DifyServiceWebGL : DifyService
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
        protected static extern void StartDifyMessageStreamJS(string targetObjectName, string sessionId, string url, string apiKey, string user, string chatCompletionRequest);
        [DllImport("__Internal")]
        protected static extern void AbortDifyMessageStreamJS();

        protected bool isChatCompletionJSDone { get; set; } = false;
        protected Dictionary<string, DifySession> sessions { get; set; } = new Dictionary<string, DifySession>();

        public override async UniTask StartStreamingAsync(DifySession difySession, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            difySession.CurrentStreamBuffer = string.Empty;

            var difyRequest = (DifyRequestMessage)difySession.Contexts[0];

            // Store session with id to receive streaming data from JavaScript
            var sessionId = Guid.NewGuid().ToString();
            sessions.Add(sessionId, difySession);

            // Make request data
            var data = new Dictionary<string, object>()
            {
                { "inputs", Inputs ?? new Dictionary<string, object>() },
                { "query", difyRequest.query },
                { "response_mode", "streaming" },
                { "user", User },
                { "auto_generate_name", false },
            };

            if (!string.IsNullOrEmpty(difyRequest.uploaded_file_id))
            {
                data["files"] = new List<Dictionary<string, string>>()
                {
                    new Dictionary<string, string>()
                    {
                        { "type", "image" },
                        { "transfer_method", "local_file" },
                        { "upload_file_id", difyRequest.uploaded_file_id }
                    }
                };
            }

            if (!string.IsNullOrEmpty(difySession.ConversationId))
            {
                data.Add("conversation_id", difySession.ConversationId);
            }

            foreach (var p in customParameters)
            {
                data[p.Key] = p.Value;
            }

            // TODO: Support custom headers later...
            if (customHeaders.Count > 0)
            {
                Debug.LogWarning("Custom headers for Dify on WebGL is not supported for now.");
            }

            var serializedData = JsonConvert.SerializeObject(data);

            if (DebugMode)
            {
                Debug.Log($"Request to Dify: {serializedData}");
            }

            // Start API stream
            isChatCompletionJSDone = false;
            StartDifyMessageStreamJS(
                gameObject.name,
                sessionId,
                BaseUrl + "/chat-messages",
                ApiKey,
                User,
                serializedData
            );

            // Preprocessing response
            var noDataResponseTimeoutsAt = DateTime.Now.AddMilliseconds(noDataResponseTimeoutSec * 1000);
            while (true)
            {
                // Success
                if (!string.IsNullOrEmpty(difySession.StreamBuffer) && isChatCompletionJSDone)
                {
                    break;
                }

                // Timeout with no response data
                else if (string.IsNullOrEmpty(difySession.StreamBuffer) && DateTime.Now > noDataResponseTimeoutsAt)
                {
                    Debug.LogError($"Dify timeouts");
                    AbortDifyMessageStreamJS();
                    difySession.ResponseType = ResponseType.Timeout;
                    sessions.Remove(sessionId);
                    break;
                }

                // Other errors
                else if (isChatCompletionJSDone)
                {
                    Debug.LogError($"Dify ends with error");
                    difySession.ResponseType = ResponseType.Error;
                    break;
                }

                // Cancel
                else if (token.IsCancellationRequested)
                {
                    Debug.Log("Preprocessing response from Dify canceled.");
                    difySession.ResponseType = ResponseType.Error;
                    AbortDifyMessageStreamJS();
                    break;
                }

                await UniTask.Delay(10);
            }

            // Update histories (put ConversationId to state)
            if (difySession.ResponseType != ResponseType.Error && difySession.ResponseType != ResponseType.Timeout)
            {
                UpdateContext(difySession);
            }
            else
            {
                Debug.LogWarning($"Messages are not added to histories for response type is not success: {difySession.ResponseType}");
            }

            // Ends with error
            if (difySession.ResponseType == ResponseType.Error)
            {
                throw new Exception($"Dify ends with error");
            }

            // Process tags
            var extractedTags = ExtractTags(difySession.CurrentStreamBuffer);
            if (extractedTags.Count > 0 && HandleExtractedTags != null)
            {
                HandleExtractedTags(extractedTags, difySession);
            }

            if (CaptureImage != null && extractedTags.ContainsKey("vision") && difySession.IsVisionAvailable)
            {
                // Prevent infinit loop
                difySession.IsVisionAvailable = false;

                // Get image
                var imageSource = extractedTags["vision"];
                var imageBytes = await CaptureImage(imageSource);

                // Make contexts
                if (imageBytes != null)
                {
                    // Upload image
                    var uploadedImageId = await UploadImage(imageBytes);
                    if (string.IsNullOrEmpty(uploadedImageId))
                    {
                        difySession.Contexts = new List<ILLMMessage>() { new DifyRequestMessage("Please inform the user that an error occurred while capturing the image.") };
                    }
                    else
                    {
                        difyRequest.uploaded_file_id = uploadedImageId;
                        difyRequest.query = $"This is the image you captured. (source: {imageSource})";
                    }
                }
                else
                {
                    difySession.Contexts = new List<ILLMMessage>() { new DifyRequestMessage("Please inform the user that an error occurred while capturing the image.") };
                }

                // Call recursively with image
                await StartStreamingAsync(difySession, customParameters, customHeaders, useFunctions, token);
            }
            else
            {
                difySession.IsResponseDone = true;

                sessions.Remove(sessionId);

                if (DebugMode)
                {
                    Debug.Log($"Response from Dify: {JsonConvert.SerializeObject(difySession.StreamBuffer)}");
                }
            }
        }

        public void SetDifyMessageStreamChunk(string chunkStringWithSessionId)
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
                Debug.Log($"Chunk from Dify: {chunkString}");
            }

            if (!sessions.ContainsKey(sessionId))
            {
                Debug.LogWarning($"Session not found. Set true to isChatCompletionJSDone.: {sessionId}");
                isChatCompletionJSDone = true;
                return;
            }

            var difySession = sessions[sessionId];

            var isDone = false;
            foreach (string line in chunkString.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("data:"))
                {
                    var d = line.Substring("data:".Length).Trim();

                    try
                    {
                        var dsr = JsonConvert.DeserializeObject<DifyStreamResponse>(d);
                        if (dsr.@event == "message" || dsr.@event == "agent_message")
                        {
                            difySession.CurrentStreamBuffer += dsr.answer;
                            difySession.StreamBuffer += dsr.answer;
                            difySession.ConversationId = dsr.conversation_id;
                            continue;
                        }
                        else if (dsr.@event == "message_end")
                        {
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

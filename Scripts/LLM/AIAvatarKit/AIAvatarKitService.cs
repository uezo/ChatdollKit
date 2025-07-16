using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.LLM.AIAvatarKit
{
    public class AIAvatarKitService : LLMServiceBase
    {
        [Header("API configuration")]
        public string ApiKey;
        public string BaseUrl;
        public string User;
        public string AAKSessionId;
        public Dictionary<string, object> SystemPromptParams;

        [Header("Network configuration")]
        [SerializeField]
        protected int responseTimeoutSec = 30;
        [SerializeField]
        protected float noDataResponseTimeoutSec = 10.0f;

        public Func<byte[], UniTask<string>> UploadImageFunc;
        public Action<AIAvatarKitToolCall> HandleToolCall;

        public string GetContextId()
        {
            if (Time.time - contextUpdatedAt > contextTimeout)
            {
                ClearContext();
            }

            return contextId;
        }

        protected override void UpdateContext(LLMSession llmSession)
        {
            contextId = ((AIAvatarKitSession)llmSession).ContextId;

            contextUpdatedAt = Time.time;
        }

        public override void ClearContext()
        {
            contextId = string.Empty;
            contextUpdatedAt = Time.time;
        }

#pragma warning disable CS1998
        public override async UniTask<List<ILLMMessage>> MakePromptAsync(string userId, string inputText, Dictionary<string, object> payloads, CancellationToken token = default)
        {
            var messages = new List<ILLMMessage>();

            if (((Dictionary<string, object>)payloads["RequestPayloads"]).ContainsKey("imageBytes"))
            {
                // Message with image
                var imageBytes = (byte[])((Dictionary<string, object>)payloads["RequestPayloads"])["imageBytes"];
                var uploadedImageId = await UploadImage(imageBytes);
                messages.Add(new AIAvatarKitRequestMessage(inputText, uploadedImageId));
            }
            else
            {
                // Text message
                messages.Add(new AIAvatarKitRequestMessage(inputText));
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
            var aakSession = new AIAvatarKitSession();
            aakSession.Contexts = messages;
            aakSession.ProcessLastChunkImmediately = true;

            if (!string.IsNullOrEmpty(GetContextId()))
            {
                aakSession.ContextId = GetContextId();
            }

            // Set SessionId
            if (string.IsNullOrEmpty(AAKSessionId))
            {
                AAKSessionId = $"ChatdollKit-{Guid.NewGuid()}";
            }

            aakSession.StreamingTask = StartStreamingAsync(aakSession, customParameters, customHeaders, useFunctions, token);

            // Retry
            if (aakSession.ResponseType == ResponseType.Timeout)
            {
                if (retryCounter > 0)
                {
                    Debug.LogWarning($"AIAvatarKit timeouts with no response data. Retrying ...");
                    aakSession = (AIAvatarKitSession)await GenerateContentAsync(messages, payloads, useFunctions, retryCounter - 1, token);
                }
                else
                {
                    Debug.LogError($"AIAvatarKit timeouts with no response data.");
                    aakSession.ResponseType = ResponseType.Error;
                    aakSession.StreamBuffer = ErrorMessageContent;
                }
            }

            return aakSession;
        }

        public virtual async UniTask StartStreamingAsync(AIAvatarKitSession aakSession, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            aakSession.CurrentStreamBuffer = string.Empty;

            var aakRequest = (AIAvatarKitRequestMessage)aakSession.Contexts[0];

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

            // Prepare API request
            using var streamRequest = new UnityWebRequest(
                BaseUrl + "/chat",
                "POST"
            );
            streamRequest.timeout = responseTimeoutSec;
            streamRequest.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(ApiKey))
            {
                streamRequest.SetRequestHeader("Authorization", $"Bearer {ApiKey}");
            }
            foreach (var h in customHeaders)
            {
                streamRequest.SetRequestHeader(h.Key, h.Value);
            }

            if (DebugMode)
            {
                Debug.Log($"Request to AIAvatarKit: {JsonConvert.SerializeObject(data)}");
            }
            var bodyRaw = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));

            // Request and response handlers
            streamRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            var downloadHandler = new AIAvatarKitStreamDownloadHandler();
            downloadHandler.DebugMode = DebugMode;
            downloadHandler.SetReceivedChunk = (text, contextId, error) =>
            {
                aakSession.CurrentStreamBuffer += text;
                aakSession.StreamBuffer += text;
                if (!string.IsNullOrEmpty(contextId))
                {
                    aakSession.ContextId = contextId;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    aakSession.ResponseType = ResponseType.Error;
                    Debug.LogError(error);
                }
            };
            downloadHandler.HandleToolCall = HandleToolCall;
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
                    aakSession.ResponseType = ResponseType.Timeout;
                    break;
                }

                // Other errors
                else if (streamRequest.isDone)
                {
                    Debug.LogError($"AIAvatarKit ends with error ({streamRequest.result}): {streamRequest.error}");
                    aakSession.ResponseType = ResponseType.Error;
                    break;
                }

                // Cancel
                else if (token.IsCancellationRequested)
                {
                    Debug.Log("Preprocessing response from AIAvatarKit canceled.");
                    aakSession.ResponseType = ResponseType.Error;
                    streamRequest.Abort();
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
                throw new Exception($"AIAvatarKit ends with error ({streamRequest.result}): {streamRequest.error}");
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

                if (DebugMode)
                {
                    Debug.Log($"Response from AIAvatarKit: {JsonConvert.SerializeObject(aakSession.StreamBuffer)}");
                }
            }
        }

        protected virtual async UniTask<string> UploadImage(byte[] imageBytes)
        {
            try
            {
                if (UploadImageFunc != null)
                {
                    
                    return await UploadImageFunc(imageBytes);
                }
                else
                {
                    return "data:image/jpeg;base64," + Convert.ToBase64String(imageBytes);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error at UploadImage: {ex.Message}\n{ex.StackTrace}");
            }

            return null;
        }

        protected class AIAvatarKitStreamDownloadHandler : DownloadHandlerScript
        {
            public Action<string, string, string> SetReceivedChunk;
            public Action<AIAvatarKitToolCall> HandleToolCall;
            public bool DebugMode = false;

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || data.Length < 1) return false;

                var receivedData = System.Text.Encoding.UTF8.GetString(data, 0, dataLength);
                if (DebugMode)
                {
                    Debug.Log($"Chunk from AIAvatarKit: {receivedData}");
                }

                var resp = string.Empty;
                foreach (string line in receivedData.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("data:"))
                    {
                        var d = line.Substring("data:".Length).Trim();

                        try
                        {
                            if (DebugMode)
                            {
                                Debug.Log($"Deserialize JSON: {d}");
                            }
                            var asr = JsonConvert.DeserializeObject<AIAvatarKitStreamResponse>(d);
                            if (asr.type == "start")
                            {
                                SetReceivedChunk(string.Empty, asr.context_id, string.Empty);
                                continue;
                            }
                            else if (asr.type == "chunk")
                            {
                                var language = !string.IsNullOrEmpty(asr.language)
                                    ? $"[lang:{asr.language}]"
                                    : string.Empty;
                                // Add `\n ` to flush stream buffer immediately
                                SetReceivedChunk(language + asr.text + "\n ", asr.context_id, string.Empty);
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
                            else if (asr.type == "error")
                            {
                                SetReceivedChunk(string.Empty, asr.context_id, $"Error in AIAvatarKit: {d}");
                                break;
                            }
                        }
                        catch (JsonReaderException)
                        {
                            Debug.LogWarning($"Deserialize error: {d}");
                            continue;
                        }
                    }
                    else if (line.StartsWith("ping"))
                    {
                        Debug.Log("Just ping");
                    }
                }

                return true;
            }
        }
    }

    public class AIAvatarKitSession : LLMSession
    {

    }

    public class AIAvatarKitRequestMessage : ILLMMessage
    {
        public string Text { get; set; }
        public string ImageUrl { get; set; }

        public AIAvatarKitRequestMessage(string text, string imageUrl = null)
        {
            Text = text;
            ImageUrl = imageUrl;
        }
    }

    public class AIAvatarKitStreamResponse
    {
        public string type { get; set; }
        public string context_id { get; set; }
        public string text { get; set; }
        public string language { get; set; }
        public Dictionary<string, object> metadata { get; set; }
    }

    public class AIAvatarKitToolCallResult
    {
        public Dictionary<string, object> data { get; set; }
        public bool is_final { get; set; }
    }

    public class AIAvatarKitToolCall
    {
        public string name { get; set; }
        public object arguments { get; set; }
        public AIAvatarKitToolCallResult result { get; set; }
    }
}

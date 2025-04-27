using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace ChatdollKit.LLM.Dify
{
    public class DifyService : LLMServiceBase
    {
        public string ConversationIdKey { get; } = "DifyConversationId";

        [Header("API configuration")]
        public string ApiKey;
        public string BaseUrl;
        public string User;
        public Dictionary<string, object> Inputs;

        [Header("Network configuration")]
        [SerializeField]
        protected int responseTimeoutSec = 30;
        [SerializeField]
        protected float noDataResponseTimeoutSec = 10.0f;

        protected string currentConversationId;
        protected string conversationIdKey = "DifyConversationId";

        public string GetConversationId()
        {
            if (Time.time - contextUpdatedAt > contextTimeout)
            {
                ClearContext();
            }

            return currentConversationId;
        }

        protected override void UpdateContext(LLMSession llmSession)
        {
            currentConversationId = ((DifySession)llmSession).ConversationId;

            contextUpdatedAt = Time.time;
        }

        public override void ClearContext()
        {
            currentConversationId = string.Empty;
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
                messages.Add(new DifyRequestMessage(inputText, uploadedImageId));
            }
            else
            {
                // Text message
                messages.Add(new DifyRequestMessage(inputText));
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
            var difySession = new DifySession();
            difySession.Contexts = messages;

            if (requestPayloads.ContainsKey(ConversationIdKey) && !string.IsNullOrEmpty((string)requestPayloads[ConversationIdKey]))
            {
                difySession.ConversationId = requestPayloads[ConversationIdKey] as string;
            }
            else if (!string.IsNullOrEmpty(GetConversationId()))
            {
                difySession.ConversationId = GetConversationId();
            }

            // Set ConversationId to ContextId for external use, not base.contextId
            difySession.ContextId = difySession.ConversationId;

            difySession.StreamingTask = StartStreamingAsync(difySession, customParameters, customHeaders, useFunctions, token);

            // Retry
            if (difySession.ResponseType == ResponseType.Timeout)
            {
                if (retryCounter > 0)
                {
                    Debug.LogWarning($"Dify timeouts with no response data. Retrying ...");
                    difySession = (DifySession)await GenerateContentAsync(messages, payloads, useFunctions, retryCounter - 1, token);
                }
                else
                {
                    Debug.LogError($"Dify timeouts with no response data.");
                    difySession.ResponseType = ResponseType.Error;
                    difySession.StreamBuffer = ErrorMessageContent;
                }
            }

            return difySession;
        }

        public virtual async UniTask StartStreamingAsync(DifySession difySession, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            difySession.CurrentStreamBuffer = string.Empty;

            var difyRequest = (DifyRequestMessage)difySession.Contexts[0];

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

            // Prepare API request
            using var streamRequest = new UnityWebRequest(
                BaseUrl + "/chat-messages",
                "POST"
            );
            streamRequest.timeout = responseTimeoutSec;
            streamRequest.SetRequestHeader("Content-Type", "application/json");
            streamRequest.SetRequestHeader("Authorization", "Bearer " + ApiKey);
            foreach (var h in customHeaders)
            {
                streamRequest.SetRequestHeader(h.Key, h.Value);
            }

            if (DebugMode)
            {
                Debug.Log($"Request to Dify: {JsonConvert.SerializeObject(data)}");
            }
            var bodyRaw = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));

            // Request and response handlers
            streamRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            var downloadHandler = new DifyStreamDownloadHandler();
            downloadHandler.DebugMode = DebugMode;
            downloadHandler.SetReceivedChunk = (answer, convid) =>
            {
                difySession.CurrentStreamBuffer += answer;
                difySession.StreamBuffer += answer;
                if (!string.IsNullOrEmpty(convid))
                {
                    difySession.ConversationId = convid;
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
                    difySession.ResponseType = ResponseType.Timeout;
                    break;
                }

                // Other errors
                else if (streamRequest.isDone)
                {
                    Debug.LogError($"Dify ends with error ({streamRequest.result}): {streamRequest.error}");
                    difySession.ResponseType = ResponseType.Error;
                    break;
                }

                // Cancel
                else if (token.IsCancellationRequested)
                {
                    Debug.Log("Preprocessing response from Dify canceled.");
                    difySession.ResponseType = ResponseType.Error;
                    streamRequest.Abort();
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
                throw new Exception($"Dify ends with error ({streamRequest.result}): {streamRequest.error}");
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

                if (DebugMode)
                {
                    Debug.Log($"Response from Dify: {JsonConvert.SerializeObject(difySession.StreamBuffer)}");
                }
            }
        }

        protected virtual async UniTask<string> UploadImage(byte[] imageBytes)
        {
            try
            {
                var form = new WWWForm();
                form.AddField("user", User);
                form.AddBinaryData("file", imageBytes, "image.png", "image/png");

                var request = UnityWebRequest.Post(BaseUrl + "/files/upload", form);
                request.SetRequestHeader("Authorization", "Bearer " + ApiKey);

                await request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Error at UploadImage: {request.error}");
                    return null;
                }
                else
                {
                    var responseText = request.downloadHandler.text;
                    var responseJson = JsonConvert.DeserializeObject<DifyImageUploadResponse>(responseText);
                    string id = responseJson.id;
                    return id;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error at UploadImage: {ex.Message}\n{ex.StackTrace}");
            }

            return null;
        }

        protected class DifyStreamDownloadHandler : DownloadHandlerScript
        {
            public Action<string, string> SetReceivedChunk;
            public bool DebugMode = false;

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || data.Length < 1) return false;

                var receivedData = System.Text.Encoding.UTF8.GetString(data, 0, dataLength);
                if (DebugMode)
                {
                    Debug.Log($"Chunk from Dify: {receivedData}");
                }

                var resp = string.Empty;
                foreach (string line in receivedData.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("data:"))
                    {
                        var d = line.Substring("data:".Length).Trim();

                        try
                        {
                            var dsr = JsonConvert.DeserializeObject<DifyStreamResponse>(d);
                            if (dsr.@event == "message" || dsr.@event == "agent_message")
                            {
                                SetReceivedChunk(dsr.answer, dsr.conversation_id);
                                continue;
                            }
                        }
                        catch (Exception)
                        {
                            Debug.LogError($"Deserialize error: {d}");
                            continue;
                        }
                    }
                }

                return true;
            }
        }
    }

    public class DifySession : LLMSession
    {
        public string ConversationId { get; set; }
    }

    public class DifyRequestMessage : ILLMMessage
    {
        public string query { get; set; }
        public string uploaded_file_id { get; set; }

        public DifyRequestMessage(string query, string uploaded_file_id = null)
        {
            this.query = query;
            this.uploaded_file_id = uploaded_file_id;
        }
    }

    public class DifyStreamResponse
    {
        public string @event { get; set; }
        public string conversation_id { get; set; }
        public string answer { get; set; }
    }

    public class DifyImageUploadResponse
    {
        public string id;
    }
}

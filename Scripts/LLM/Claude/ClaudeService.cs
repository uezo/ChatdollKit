using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.LLM.Claude
{
    public class ClaudeService : LLMServiceBase
    {
        public string HistoryKey = "ClaudeHistories";
        public string CustomParameterKey = "ClaudeParameters";
        public string CustomHeaderKey = "ClaudeHeaders";

        [Header("API configuration")]
        public string ApiKey;
        public string Model = "claude-2.1";
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

        public override ILLMMessage CreateMessageAfterFunction(string role = null, string content = null, Dictionary<string, object> function_call = null, string name = null, Dictionary<string, object> arguments = null)
        {
            // Create human message for next request after function execution
            return new ClaudeMessage("user", "this is dummy. claude has no function calling for now");
        }

        protected List<ClaudeMessage> GetHistoriesFromStateData(Dictionary<string, object> stateData)
        {
            // Add histories to state if not exists
            if (!stateData.ContainsKey(HistoryKey) || stateData[HistoryKey] == null)
            {
                stateData[HistoryKey] = new List<ClaudeMessage>();
            }

            // Restore type from stored session data
            if (stateData[HistoryKey] is JContainer)
            {
                stateData[HistoryKey] = ((JContainer)stateData[HistoryKey]).ToObject<List<ClaudeMessage>>();
            }

            return (List<ClaudeMessage>)stateData[HistoryKey];
        }

#pragma warning disable CS1998
        public override async UniTask AddHistoriesAsync(ILLMSession llmSession, object dataStore, CancellationToken token = default)
        {
            var histories = GetHistoriesFromStateData((Dictionary<string, object>)dataStore);
            var userMessage = (ClaudeMessage)llmSession.Contexts.Last();
            histories.Add(userMessage);
            histories.Add(new ClaudeMessage("assistant", llmSession.StreamBuffer));
        }

        public override async UniTask<List<ILLMMessage>> MakePromptAsync(string userId, string inputText, Dictionary<string, object> payloads, CancellationToken token = default)
        {
            var messages = new List<ILLMMessage>();

            // System - Claude takes system message outside of message parameter

            // Histories
            var histories = GetHistoriesFromStateData((Dictionary<string, object>)payloads["StateData"]);
            messages.AddRange(histories.Skip(histories.Count - HistoryTurns * 2).ToList());

            if (((Dictionary<string, object>)payloads["RequestPayloads"]).ContainsKey("imageBytes"))
            {
                // Message with image
                var imageBytes = (byte[])((Dictionary<string, object>)payloads["RequestPayloads"])["imageBytes"];
                messages.Add(new ClaudeMessage("user", inputText, "image/jpeg", Convert.ToBase64String(imageBytes)));
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
            var stateData = (Dictionary<string, object>)payloads["StateData"];
            var customParameters = stateData.ContainsKey(CustomParameterKey) ? (Dictionary<string, string>)stateData[CustomParameterKey] : new Dictionary<string, string>();
            var customHeaders = stateData.ContainsKey(CustomHeaderKey) ? (Dictionary<string, string>)stateData[CustomHeaderKey] : new Dictionary<string, string>();

            // Start streaming session
            var claudeSession = new ClaudeSession();
            claudeSession.Contexts = messages;
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
            streamRequest.SetRequestHeader("anthropic-beta", "messages-2023-12-15");
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
                claudeSession.StreamBuffer += chunk;
            };
            downloadHandler.SetResponseType = (responseType) =>
            {
                claudeSession.ResponseType = responseType;
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

            claudeSession.IsResponseDone = true;

            if (DebugMode)
            {
                Debug.Log($"Response from Claude: {JsonConvert.SerializeObject(claudeSession.StreamBuffer)}");
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
            public Action<ResponseType> SetResponseType;
            public bool DebugMode = false;
            private bool isFirstDelta = true;

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || data.Length < 1) return false;

                var receivedData = System.Text.Encoding.UTF8.GetString(data, 0, dataLength);
                if (DebugMode)
                {
                    Debug.Log($"Chunk from Claude: {receivedData}");
                }

                var resp = string.Empty;
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

                        if (csr.delta == null || string.IsNullOrEmpty(csr.delta.text)) continue;

                        if (isFirstDelta)
                        {
                            SetResponseType(ResponseType.Content);
                            isFirstDelta = false;
                        }

                        resp += csr.delta.text;
                    }
                }

                SetReceivedChunk(resp);

                return true;
            }
        }
    }

    public class ClaudeSession : LLMSession
    {

    }

    public class ClaudeStreamResponse
    {
        public string type { get; set; }
        public ClaudeDelta delta { get; set; }
    }

    public class ClaudeDelta
    {
        public string text { get; set; }
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

        public ClaudeContent() { }

        public ClaudeContent(string text)
        {
            type = "text";
            this.text = text;
        }

        public ClaudeContent(string mediaType, string data)
        {
            type = "image";
            source = new ClaudeImageSource();
            source.media_type = mediaType;
            source.data = data;
        }
    }

    public class ClaudeMessage : ILLMMessage
    {
        public string role { get; set; }
        public List<ClaudeContent> content { get; set; }

        public ClaudeMessage(string role, string text = null, string mediaType = null, string data = null)
        {
            this.role = role;
            content = new List<ClaudeContent>();

            if (!string.IsNullOrEmpty(text))
            {
                content.Add(new ClaudeContent(text));
            }

            if (!string.IsNullOrEmpty(mediaType) && !string.IsNullOrEmpty(data))
            {
                content.Add(new ClaudeContent(mediaType, data));
            }
        }
    }
}

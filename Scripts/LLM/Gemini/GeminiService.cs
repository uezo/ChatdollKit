using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.LLM.Gemini
{
    public class GeminiService : LLMServiceBase
    {
        public string HistoryKey = "GeminiHistories";
        public string CustomParameterKey = "GeminiParameters";
        public string CustomHeaderKey = "GeminiHeaders";

        [Header("API configuration")]
        public string ApiKey;
        public string Model = "gemini-1.5-flash-latest";
        public string GenerateContentUrl;
        public int MaxOutputTokens = 0;
        public float Temperature = 0.5f;
        public float TopP = 1.0f;
        public int TopK = 0;
        public List<string> StopSequences;

        [Header("Network configuration")]
        [SerializeField]
        protected int responseTimeoutSec = 30;
        [SerializeField]
        protected float noDataResponseTimeoutSec = 10.0f;   // Some requests like multi-modal takes time longer

        public Func<string, UniTask<byte[]>> CaptureImage = null;

        public override ILLMMessage CreateMessageAfterFunction(string role = null, string content = null, ILLMSession llmSession = null, Dictionary<string, object> arguments = null)
        {
            return new GeminiMessage(
                string.IsNullOrEmpty(role) ? "function" : role,
                functionResponse: new GeminiFunctionResponse() {
                    name = llmSession.FunctionName,
                    response = JsonConvert.DeserializeObject<Dictionary<string, object>>(content)
                }
            );
        }

        protected List<GeminiMessage> GetHistoriesFromStateData(Dictionary<string, object> stateData)
        {
            // Add histories to state if not exists
            if (!stateData.ContainsKey(HistoryKey) || stateData[HistoryKey] == null)
            {
                stateData[HistoryKey] = new List<GeminiMessage>();
            }

            // Restore type from stored session data
            if (stateData[HistoryKey] is JContainer)
            {
                stateData[HistoryKey] = ((JContainer)stateData[HistoryKey]).ToObject<List<GeminiMessage>>();
            }

            return (List<GeminiMessage>)stateData[HistoryKey];
        }

#pragma warning disable CS1998
        public async UniTask AddHistoriesAsync(ILLMSession llmSession, Dictionary<string, object> dataStore, CancellationToken token = default)
        {
            var histories = GetHistoriesFromStateData(dataStore);
            var userMessage = (GeminiMessage)llmSession.Contexts.Last();
            userMessage.parts.RemoveAll(p => p.inlineData != null);
            userMessage.parts.RemoveAll(p => p.fileData != null);
            histories.Add(userMessage);

            if (llmSession.ResponseType == ResponseType.FunctionCalling)
            {
                var functionCallMessage = new GeminiMessage("model", functionCall: new GeminiFunctionCall() {
                    name = llmSession.FunctionName,
                    args = JsonConvert.DeserializeObject<Dictionary<string, object>>(llmSession.StreamBuffer)
                });
                histories.Add(functionCallMessage);

                // Add also to contexts for using this message in this turn
                llmSession.Contexts.Add(functionCallMessage);
            }
            else
            {
                histories.Add(new GeminiMessage("model", llmSession.StreamBuffer));
            }
        }

        public override async UniTask<List<ILLMMessage>> MakePromptAsync(string userId, string inputText, Dictionary<string, object> payloads, CancellationToken token = default)
        {
            var messages = new List<ILLMMessage>();

            // System
            if (!string.IsNullOrEmpty(SystemMessageContent))
            {
                messages.Add(new GeminiMessage("user", SystemMessageContent));
                messages.Add(new GeminiMessage("model", "ok"));
            }

            // Histories
            var histories = GetHistoriesFromStateData((Dictionary<string, object>)payloads["StateData"]);
            messages.AddRange(histories.Skip(histories.Count - HistoryTurns * 2).ToList());

            // User (current input)
            if (((Dictionary<string, object>)payloads["RequestPayloads"]).ContainsKey("imageBytes"))
            {
                var imageBytes = (byte[])((Dictionary<string, object>)payloads["RequestPayloads"])["imageBytes"];
                messages.Add(new GeminiMessage("user", inputText, inlineData: new GeminiInlineData("image/jpeg", imageBytes)));
            }
            else
            {
                messages.Add(new GeminiMessage("user", inputText));
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
            var geminiSession = new GeminiSession();
            geminiSession.Contexts = messages;
            geminiSession.StreamingTask = StartStreamingAsync(geminiSession, stateData, customParameters, customHeaders, useFunctions, token);
            await WaitForResponseType(geminiSession, token);

            if (geminiSession.ResponseType == ResponseType.Timeout)
            {
                if (retryCounter > 0)
                {
                    Debug.LogWarning($"Gemini timeouts with no response data. Retrying ...");
                    geminiSession = (GeminiSession)await GenerateContentAsync(messages, payloads, useFunctions, retryCounter - 1, token);
                }
                else
                {
                    Debug.LogError($"Gemini timeouts with no response data.");
                    geminiSession.ResponseType = ResponseType.Error;
                    geminiSession.StreamBuffer = ErrorMessageContent;
                }
            }

            return geminiSession;
        }

        public virtual async UniTask StartStreamingAsync(GeminiSession geminiSession, Dictionary<string, object> stateData, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            // Clear current stream buffer here
            geminiSession.CurrentStreamBuffer = string.Empty;

            // GenerationConfig
            var generationConfig = new GeminiGenerationConfig()
            {
                temperature = Temperature,
                topP = TopP,
                topK = TopK,
                maxOutputTokens = MaxOutputTokens,
                stopSequences = StopSequences
            };

            // Make request data
            var data = new Dictionary<string, object>()
            {
                { "contents", geminiSession.Contexts },
                { "generationConfig", generationConfig }
            };
            foreach (var p in customParameters)
            {
                data[p.Key] = p.Value;
            }

            // Set tools. Multimodal model doesn't support function calling for now (2023.12.29)
            if (useFunctions && llmTools.Count > 0 && !Model.ToLower().Contains("vision"))
            {
                 data.Add("tools", new List<Dictionary<string, object>>(){
                     new Dictionary<string, object> {
                         { "function_declarations", llmTools }
                     }
                 });
            }

            // Prepare API request
            using var streamRequest = new UnityWebRequest(
                string.IsNullOrEmpty(GenerateContentUrl) ? $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:streamGenerateContent?key={ApiKey}" : GenerateContentUrl,
                "POST"
            );
            streamRequest.timeout = responseTimeoutSec;
            streamRequest.SetRequestHeader("Content-Type", "application/json");
            foreach (var h in customHeaders)
            {
                streamRequest.SetRequestHeader(h.Key, h.Value);
            }

            if (DebugMode)
            {
                Debug.Log($"Request to Gemini: {JsonConvert.SerializeObject(data)}");
            }
            var bodyRaw = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));

            // Request and response handlers
            streamRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            var downloadHandler = new GeminiStreamDownloadHandler();
            downloadHandler.DebugMode = DebugMode;
            downloadHandler.SetReceivedChunk = (chunk) =>
            {
                geminiSession.CurrentStreamBuffer += chunk;
                geminiSession.StreamBuffer += chunk;
            };
            downloadHandler.SetResponseType = (responseType, functionName) =>
            {
                geminiSession.ResponseType = responseType;
                if (!string.IsNullOrEmpty(functionName))
                {
                    geminiSession.FunctionName = functionName;
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
                    geminiSession.ResponseType = ResponseType.Timeout;
                    break;
                }

                // Other errors
                else if (streamRequest.isDone)
                {
                    Debug.LogError($"Gemini ends with error ({streamRequest.result}): {streamRequest.error}");
                    geminiSession.ResponseType = ResponseType.Error;
                    break;
                }

                // Cancel
                else if (token.IsCancellationRequested)
                {
                    Debug.Log("Preprocessing response from Gemini canceled.");
                    geminiSession.ResponseType = ResponseType.Error;
                    streamRequest.Abort();
                    break;
                }

                await UniTask.Delay(10);
            }

            // Remove non-text parts to keep context light
            var lastUserMessage = geminiSession.Contexts.Last() as GeminiMessage;
            if (lastUserMessage != null)
            {
                lastUserMessage.parts.RemoveAll(p => p.inlineData != null);
                lastUserMessage.parts.RemoveAll(p => p.fileData != null);
            }

            // Update histories
            if (geminiSession.ResponseType != ResponseType.Error && geminiSession.ResponseType != ResponseType.Timeout)
            {
                await AddHistoriesAsync(geminiSession, stateData, token);
            }
            else
            {
                Debug.LogWarning($"Messages are not added to histories for response type is not success: {geminiSession.ResponseType}");
            }

            var extractedTags = ExtractTags(geminiSession.CurrentStreamBuffer);

            if (CaptureImage != null && extractedTags.ContainsKey("vision") && geminiSession.IsVisionAvailable)
            {
                // Prevent infinit loop
                geminiSession.IsVisionAvailable = false;

                // Get image
                var imageBytes = await CaptureImage(extractedTags["vision"]);

                // Make contexts
                var lastUserContentText = lastUserMessage.parts.Where(p => !string.IsNullOrEmpty(p.text)).First().text;
                if (imageBytes != null)
                {
                    geminiSession.Contexts.Add(new GeminiMessage("model", geminiSession.StreamBuffer));
                    // Image -> Text to get the better accuracy
                    var userMessageWithVision = new GeminiMessage("user", inlineData: new GeminiInlineData("image/jpeg", imageBytes));
                    userMessageWithVision.parts.Add(new GeminiPart(text: lastUserContentText));
                    geminiSession.Contexts.Add(userMessageWithVision);
                }
                else
                {
                    geminiSession.Contexts.Add(new GeminiMessage("user", "Please inform the user that an error occurred while capturing the image."));
                }

                // Call recursively with image
                await StartStreamingAsync(geminiSession, stateData, customParameters, customHeaders, useFunctions, token);
            }
            else
            {
                geminiSession.IsResponseDone = true;

                if (DebugMode)
                {
                    Debug.Log($"Response from Gemini: {JsonConvert.SerializeObject(geminiSession.StreamBuffer)}");
                }
            }
        }

        protected async UniTask WaitForResponseType(GeminiSession geminiSession, CancellationToken token)
        {
            // Wait for response type is set
            while (geminiSession.ResponseType == ResponseType.None && !token.IsCancellationRequested)
            {
                await UniTask.Delay(10, cancellationToken: token);
            }
        }

        // Internal classes
        protected class GeminiStreamDownloadHandler : DownloadHandlerScript
        {
            public Action<string> SetReceivedChunk;
            public Action<ResponseType, string> SetResponseType;
            public bool DebugMode = false;
            private string receivedData = string.Empty;
            private ResponseType responseType = ResponseType.None;

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || data.Length < 1) return false;

                try
                {
                    receivedData += System.Text.Encoding.UTF8.GetString(data, 0, dataLength).Replace("\n", "").Trim();
                    if (DebugMode)
                    {
                        Debug.Log($"Chunk from Gemini: {receivedData}");
                    }

                    if (receivedData.StartsWith("[") || receivedData.StartsWith(","))
                    {
                        // Remove "[" or "," to parse as JSON
                        receivedData = receivedData.Substring(1);

                        // Remove trailing "]" or "," to parse as JSON
                        if (receivedData.EndsWith("]") || receivedData.EndsWith(","))
                        {
                            receivedData = receivedData.Substring(0, receivedData.Length - 1);
                        }

                        var streamResponses = JsonConvert.DeserializeObject<List<GeminiStreamResponse>>("[" + receivedData + "]");

                        if (streamResponses.Count == 0)
                        {
                            return true;
                        }
                        else if (streamResponses.Count > 1)
                        {
                            Debug.Log($"Multiple JSON: {streamResponses.Count}");
                        }

                        foreach (var streamResponse in streamResponses)
                        {
                            if (streamResponse.candidates[0].content.parts[0].functionCall != null)
                            {
                                if (responseType == ResponseType.None)
                                {
                                    responseType = ResponseType.FunctionCalling;
                                    SetResponseType(responseType, streamResponse.candidates[0].content.parts[0].functionCall.name);
                                }
                                SetReceivedChunk(JsonConvert.SerializeObject(streamResponse.candidates[0].content.parts[0].functionCall.args));
                            }
                            else
                            {
                                if (responseType == ResponseType.None)
                                {
                                    responseType = ResponseType.Content;
                                    SetResponseType(responseType, string.Empty);
                                }
                                SetReceivedChunk(streamResponse.candidates[0].content.parts[0].text);
                            }
                        }
                    }

                    // Clear local buffer for next turn
                    receivedData = string.Empty;
                }
                catch (Exception ex)
                {
                    // Do not clear receivedData to be processed with the next chunk
                    Debug.LogWarning($"Error at processing streaming: {receivedData}\n{ex}\n{ex.StackTrace}");
                }

                return true;
            }
        }
    }

    public class GeminiSession : LLMSession
    {

    }

    public class GeminiStreamResponse
    {
        public List<GeminiCandidate> candidates { get; set; }
    }

    public class GeminiCandidate
    {
        public GeminiMessage content { get; set; }
    }

    public class GeminiMessage : ILLMMessage
    {
        public string role { get; set; }
        public List<GeminiPart> parts { get; set; }

        public GeminiMessage(string role = null, string text = null, GeminiFileData fileData = null, GeminiInlineData inlineData = null, GeminiFunctionCall functionCall = null, GeminiFunctionResponse functionResponse = null)
        {
            this.role = role;
            parts = new List<GeminiPart>();

            if (text != null)
            {
                parts.Add(new GeminiPart(text: text));
            }
            if (fileData != null)
            {
                parts.Add(new GeminiPart(fileData: fileData));
            }
            if (inlineData != null)
            {
                parts.Add(new GeminiPart(inlineData: inlineData));
            }
            if (functionCall != null)
            {
                parts.Add(new GeminiPart(functionCall: functionCall));
            }
            if (functionResponse != null)
            {
                parts.Add(new GeminiPart(functionResponse: functionResponse));
            }
        }
    }

    public class GeminiPart
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string text { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public GeminiFileData fileData { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public GeminiInlineData inlineData { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public GeminiFunctionCall functionCall { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public GeminiFunctionResponse functionResponse { get; set; }

        public GeminiPart(string text = null, GeminiFileData fileData = null, GeminiInlineData inlineData = null, GeminiFunctionCall functionCall = null, GeminiFunctionResponse functionResponse = null)
        {
            this.text = text;
            this.fileData = fileData;
            this.inlineData = inlineData;
            this.functionCall = functionCall;
            this.functionResponse = functionResponse;
        }
    }

    public class GeminiFileData
    {
        public string mimeType { get; set; }
        public string fileUri { get; set; }

        public GeminiFileData(string mimeType, string fileUri)
        {
            this.mimeType = mimeType;
            this.fileUri = fileUri;
        }
    }

    public class GeminiInlineData
    {
        public string mimeType { get; set; }
        public string data { get; set; }

        [JsonConstructor]
        public GeminiInlineData(string mimeType, string data)
        {
            this.mimeType = mimeType;
            this.data = data;
        }

        public GeminiInlineData(string mimeType, byte[] imageBytes)
        {
            this.mimeType = mimeType;
            data = Convert.ToBase64String(imageBytes);
        }
    }

    public class GeminiFunctionCall
    {
        public string name { get; set; }
        public Dictionary<string, object> args { get; set; }
    }

    public class GeminiFunctionResponse
    {
        public string name { get; set; }
        public Dictionary<string, object> response { get; set; }
    }

    // Configuration
    public class GeminiGenerationConfig
    {
        public float temperature { get; set; }
        public float topP { get; set; }
        public int topK { get; set; }
        public bool ShouldSerializetopK()
        {
            return topK > 0;
        }
        public int candidateCount { get; set; } = 1;
        public int maxOutputTokens { get; set; }
        public bool ShouldSerializemaxOutputTokens()
        {
            return maxOutputTokens > 0;
        }
        public List<string> stopSequences { get; set; }
        public bool ShouldSerializestopSequences()
        {
            return stopSequences != null && stopSequences.Count > 0;
        }
    }

    // Tool
    public class GeminiFunction : LLMTool
    {
        public GeminiFunction(string name, string description) : base(name, description)
        {
            this.name = name;
            this.description = description;
            parameters = new GeminiFunctionParameters();
        }
    }

    public class GeminiFunctionParameters : LLMToolParameters
    {

    }
}

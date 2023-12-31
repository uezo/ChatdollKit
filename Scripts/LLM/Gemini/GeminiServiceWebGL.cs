using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.LLM.Gemini
{
    public class GeminiServiceWebGL : GeminiService
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
        protected static extern void StartGeminiMessageStreamJS(string targetObjectName, string sessionId, string url, string apiKey, string chatCompletionRequest);
        [DllImport("__Internal")]
        protected static extern void AbortGeminiMessageStreamJS();

        protected bool isChatCompletionJSDone { get; set; } = false;
        protected Dictionary<string, GeminiSession> sessions { get; set; } = new Dictionary<string, GeminiSession>();

        public override async UniTask StartStreamingAsync(GeminiSession geminiSession, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            // Add session for callback
            var sessionId = Guid.NewGuid().ToString();
            sessions.Add(sessionId, geminiSession);

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

            // TODO: Support custom headers later...
            if (customHeaders.Count >= 0)
            {
                Debug.LogWarning("Custom headers for Gemini on WebGL is not supported for now.");
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

            // Start API stream
            isChatCompletionJSDone = false;
            StartGeminiMessageStreamJS(
                gameObject.name,
                sessionId,
                string.IsNullOrEmpty(GenerateContentUrl) ? $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:streamGenerateContent" : GenerateContentUrl,
                ApiKey,
                JsonConvert.SerializeObject(data)
            );

            // Preprocessing response
            var noDataResponseTimeoutsAt = DateTime.Now.AddMilliseconds(noDataResponseTimeoutSec * 1000);
            while (true)
            {
                // Success
                if (!string.IsNullOrEmpty(geminiSession.StreamBuffer) && isChatCompletionJSDone)
                {
                    break;
                }

                // Timeout with no response data
                else if (string.IsNullOrEmpty(geminiSession.StreamBuffer) && DateTime.Now > noDataResponseTimeoutsAt)
                {
                    AbortGeminiMessageStreamJS();
                    geminiSession.ResponseType = ResponseType.Timeout;
                    sessions.Remove(sessionId);
                    break;
                }

                // Other errors
                else if (isChatCompletionJSDone)
                {
                    Debug.LogError($"ChatGPT ends with error");
                    geminiSession.ResponseType = ResponseType.Error;
                    break;
                }

                // Cancel
                else if (token.IsCancellationRequested)
                {
                    Debug.Log("Preprocessing response from ChatGPT canceled.");
                    geminiSession.ResponseType = ResponseType.Error;
                    AbortGeminiMessageStreamJS();
                    break;
                }

                await UniTask.Delay(10);
            }

            geminiSession.IsResponseDone = true;

            sessions.Remove(sessionId);

            if (DebugMode)
            {
                Debug.Log($"Response from Gemini: {JsonConvert.SerializeObject(geminiSession.StreamBuffer)}");
            }
        }

        public void SetGeminiMessageStreamChunk(string chunkStringWithSessionId)
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
                Debug.Log($"Chunk from Gemini: {chunkString}");
            }

            if (!sessions.ContainsKey(sessionId))
            {
                Debug.LogWarning($"Session not found. Set true to isChatCompletionJSDone.: {sessionId}");
                isChatCompletionJSDone = true;
                return;
            }

            var geminiSession = sessions[sessionId];

            // TODO: Local buffer for a chunk data is not deserializable

            var isDone = false;
            if (chunkString.StartsWith("[") || chunkString.StartsWith(","))
            {
                // Remove "[" or "," to parse as JSON
                chunkString = chunkString.Substring(1);

                // Remove trailing "]" to parse as JSON
                if (chunkString.EndsWith("]"))
                {
                    chunkString = chunkString.Substring(0, chunkString.Length - 1);
                    isDone = true;
                }

                var streamResponse = JsonConvert.DeserializeObject<GeminiStreamResponse>(chunkString);
                if (streamResponse == null) return;

                if (streamResponse.candidates[0].content.parts[0].functionCall != null)
                {
                    if (geminiSession.ResponseType == ResponseType.None)
                    {
                        geminiSession.ResponseType = ResponseType.FunctionCalling;
                        if (string.IsNullOrEmpty(geminiSession.FunctionName))
                        {
                            geminiSession.FunctionName = streamResponse.candidates[0].content.parts[0].functionCall.name;
                        }
                    }
                    geminiSession.StreamBuffer += JsonConvert.SerializeObject(streamResponse.candidates[0].content.parts[0].functionCall.args);
                }
                else
                {
                    if (geminiSession.ResponseType == ResponseType.None)
                    {
                        geminiSession.ResponseType = ResponseType.Content;
                    }
                    geminiSession.StreamBuffer += streamResponse.candidates[0].content.parts[0].text;
                }
            }
            else if (chunkString.EndsWith("]"))
            {
                isDone = true;
            }

            if (isDone)
            {
                isChatCompletionJSDone = true;
            }
        }
#endif
    }
}

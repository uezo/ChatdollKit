using System;
using System.Collections.Generic;
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
            // Add session for callback
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

            // Start API stream
            isChatCompletionJSDone = false;
            StartClaudeMessageStreamJS(
                gameObject.name,
                sessionId,
                string.IsNullOrEmpty(CreateMessageUrl) ? $"https://api.anthropic.com/v1/messages" : CreateMessageUrl,
                ApiKey,
                JsonConvert.SerializeObject(data)
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
                    AbortClaudeMessageStreamJS();
                    claudeSession.ResponseType = ResponseType.Timeout;
                    sessions.Remove(sessionId);
                    break;
                }

                // Other errors
                else if (isChatCompletionJSDone)
                {
                    Debug.LogError($"ChatGPT ends with error");
                    claudeSession.ResponseType = ResponseType.Error;
                    break;
                }

                // Cancel
                else if (token.IsCancellationRequested)
                {
                    Debug.Log("Preprocessing response from ChatGPT canceled.");
                    claudeSession.ResponseType = ResponseType.Error;
                    AbortClaudeMessageStreamJS();
                    break;
                }

                await UniTask.Delay(10);
            }

            claudeSession.IsResponseDone = true;

            sessions.Remove(sessionId);

            if (DebugMode)
            {
                Debug.Log($"Response from Claude: {JsonConvert.SerializeObject(claudeSession.StreamBuffer)}");
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

                    if (csr.type == "message_stop")
                    {
                        isDone = true;
                        break;
                    }

                    if (csr.delta == null || string.IsNullOrEmpty(csr.delta.text)) continue;

                    if (claudeSession.ResponseType == ResponseType.None)
                    {
                        claudeSession.ResponseType = ResponseType.Content;
                    }

                    resp += csr.delta.text;
                }
            }

            claudeSession.StreamBuffer += resp;

            if (isDone)
            {
                isChatCompletionJSDone = true;
            }
        }
#endif
    }
}

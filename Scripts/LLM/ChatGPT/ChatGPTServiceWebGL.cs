using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.LLM.ChatGPT
{
    public class ChatGPTServiceWebGL : ChatGPTService
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
        protected static extern void ChatCompletionJS(string targetObjectName, string sessionId, string url, string apiKey, string chatCompletionRequest);
        [DllImport("__Internal")]
        protected static extern void AbortChatCompletionJS();

        protected bool isChatCompletionJSDone { get; set; } = false;
        protected Dictionary<string, ChatGPTSession> sessions { get; set; } = new Dictionary<string, ChatGPTSession>();

        public override async UniTask StartStreamingAsync(ChatGPTSession chatGPTSession, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            // Add session for callback
            var sessionId = Guid.NewGuid().ToString();
            sessions.Add(sessionId, chatGPTSession);

            // Make request data
            var data = new Dictionary<string, object>()
            {
                { "model", Model },
                { "temperature", Temperature },
                { "messages", chatGPTSession.Contexts },
                { "frequency_penalty", FrequencyPenalty },
                { "presence_penalty", PresencePenalty },
                { "stream", true },
            };
            if (MaxTokens > 0)
            {
                data.Add("max_tokens", MaxTokens);
            }
            if (useFunctions && llmTools.Count > 0)
            {
                data.Add("functions", llmTools);
            }
            if (Logprobs == true)
            {
                data.Add("logprobs", true);
                data.Add("top_logprobs", TopLogprobs);
            }
            if (Stop != null && Stop.Count > 0)
            {
                data.Add("stop", Stop);
            }
            foreach (var p in customParameters)
            {
                data[p.Key] = p.Value;
            }

            // TODO: Support custom headers later...
            if (customHeaders.Count >= 0)
            {
                Debug.LogWarning("Custom headers for ChatGPT on WebGL is not supported for now.");
            }

            // Start API stream
            isChatCompletionJSDone = false;
            ChatCompletionJS(
                gameObject.name,
                sessionId,
                string.IsNullOrEmpty(ChatCompletionUrl) ? "https://api.openai.com/v1/chat/completions" : ChatCompletionUrl,
                ApiKey,
                JsonConvert.SerializeObject(data)
            );

            // Preprocessing response
            var noDataResponseTimeoutsAt = DateTime.Now.AddMilliseconds(noDataResponseTimeoutSec * 1000);
            while (true)
            {
                // Success
                if (!string.IsNullOrEmpty(chatGPTSession.StreamBuffer) && isChatCompletionJSDone)
                {
                    break;
                }

                // Timeout with no response data
                else if (string.IsNullOrEmpty(chatGPTSession.StreamBuffer) && DateTime.Now > noDataResponseTimeoutsAt)
                {
                    AbortChatCompletionJS();
                    chatGPTSession.ResponseType = ResponseType.Timeout;
                    sessions.Remove(sessionId);
                    break;
                }

                // Other errors
                else if (isChatCompletionJSDone)
                {
                    Debug.LogError($"ChatGPT ends with error");
                    chatGPTSession.ResponseType = ResponseType.Error;
                    break;
                }

                // Cancel
                else if (token.IsCancellationRequested)
                {
                    Debug.Log("Preprocessing response from ChatGPT canceled.");
                    chatGPTSession.ResponseType = ResponseType.Error;
                    AbortChatCompletionJS();
                    break;
                }

                await UniTask.Delay(10);
            }

            chatGPTSession.IsResponseDone = true;

            sessions.Remove(sessionId);

            if (DebugMode)
            {
                Debug.Log($"Response from ChatGPT: {JsonConvert.SerializeObject(chatGPTSession.StreamBuffer)}");
            }
        }

        public void SetChatCompletionStreamChunk(string chunkStringWithSessionId)
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

            if (!sessions.ContainsKey(sessionId))
            {
                Debug.LogWarning($"Session not found. Set true to isChatCompletionJSDone.: {sessionId}");
                isChatCompletionJSDone = true;
                return;
            }

            var chatGPTSession = sessions[sessionId];

            var isDeltaSet = false;
            var temp = string.Empty;
            var isDone = false;
            foreach (var d in chunkString.Split("data:"))
            {

                if (!string.IsNullOrEmpty(d))
                {
                    if (d.Trim() != "[DONE]")
                    {
                        // Parse JSON and add content data to resp
                        ChatGPTStreamResponse j = null;
                        try
                        {
                            j = JsonConvert.DeserializeObject<ChatGPTStreamResponse>(d);
                        }
                        catch (Exception)
                        {
                            Debug.LogError($"Deserialize error: {d}");
                            continue;
                        }

                        // Azure OpenAI returns empty choices first response. (returns prompt_filter_results)
                        try
                        {
                            if (j.choices.Count == 0) continue;
                        }
                        catch (Exception)
                        {
                            Debug.LogError($"Empty choices error: {JsonConvert.SerializeObject(j)}");
                            continue;
                        }

                        var delta = j.choices[0].delta;
                        if (!isDeltaSet)
                        {
                            chatGPTSession.FirstDelta = delta;
                            chatGPTSession.ResponseType = delta.function_call != null ? ResponseType.FunctionCalling : ResponseType.Content;
                            isDeltaSet = true;
                        }
                        if (delta.function_call == null)
                        {
                            temp += delta.content;
                        }
                        else
                        {
                            temp += delta.function_call.arguments;
                        }
                    }
                    else
                    {
                        Debug.Log("Chunk is data:[DONE]. Set true to isChatCompletionJSDone.");
                        isDone = true;
                        break;
                    }
                }
            }

            chatGPTSession.StreamBuffer += temp;

            if (isDone)
            {
                isChatCompletionJSDone = true;
            }
        }
#endif
    }
}

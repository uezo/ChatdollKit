using System;
using System.Collections.Generic;
using System.Linq;
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

        public override async UniTask StartStreamingAsync(ChatGPTSession chatGPTSession, Dictionary<string, object> stateData, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            chatGPTSession.CurrentStreamBuffer = string.Empty;

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
                sessions.Add(sessionId, chatGPTSession);
            }

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
                var tools = new List<Dictionary<string, object>>();
                foreach (var tool in llmTools)
                {
                    tools.Add(new Dictionary<string, object>()
                    {
                        { "type", "function" },
                        { "function", tool }
                    });
                }
                data.Add("tools", tools);
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

            var serializedData = JsonConvert.SerializeObject(data);

            if (DebugMode)
            {
                Debug.Log($"Request to ChatGPT: {serializedData}");
            }

            // Start API stream
            isChatCompletionJSDone = false;
            ChatCompletionJS(
                gameObject.name,
                sessionId,
                string.IsNullOrEmpty(ChatCompletionUrl) ? "https://api.openai.com/v1/chat/completions" : ChatCompletionUrl,
                ApiKey,
                serializedData
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
                    Debug.LogError($"ChatGPT timeouts");
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

            // Remove non-text content to keep context light
            var lastUserMessage = chatGPTSession.Contexts.Last() as ChatGPTUserMessage;
            if (lastUserMessage != null)
            {
                lastUserMessage.content.RemoveAll(part => !(part is TextContentPart));
            }

            // Update histories
            if (chatGPTSession.ResponseType != ResponseType.Error && chatGPTSession.ResponseType != ResponseType.Timeout)
            {
                await AddHistoriesAsync(chatGPTSession, stateData, token);
            }
            else
            {
                Debug.LogWarning($"Messages are not added to histories for response type is not success: {chatGPTSession.ResponseType}");
            }

            var extractedTags = ExtractTags(chatGPTSession.CurrentStreamBuffer);

            if (CaptureImage != null && extractedTags.ContainsKey("vision") && chatGPTSession.IsVisionAvailable)
            {
                // Prevent infinit loop
                chatGPTSession.IsVisionAvailable = false;

                // Get image
                var imageBytes = await CaptureImage(extractedTags["vision"]);

                // Make contexts
                var lastUserContentText = ((TextContentPart)lastUserMessage.content[0]).text;
                if (imageBytes != null)
                {
                    chatGPTSession.Contexts.Add(new ChatGPTAssistantMessage(chatGPTSession.StreamBuffer));
                    // Image -> Text to get the better accuracy
                    chatGPTSession.Contexts.Add(new ChatGPTUserMessage(new List<IContentPart>() {
                        new ImageUrlContentPart("data:image/jpeg;base64," + Convert.ToBase64String(imageBytes)),
                        new TextContentPart(lastUserContentText)
                    }));
                }
                else
                {
                    chatGPTSession.Contexts.Add(new ChatGPTUserMessage("Please inform the user that an error occurred while capturing the image."));
                }

                // Call recursively with image
                await StartStreamingAsync(chatGPTSession, stateData, customParameters, customHeaders, useFunctions, token);
            }
            else
            {
                chatGPTSession.IsResponseDone = true;

                sessions.Remove(sessionId);

                if (DebugMode)
                {
                    Debug.Log($"Response from ChatGPT: {JsonConvert.SerializeObject(chatGPTSession.StreamBuffer)}");
                }
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

            if (DebugMode)
            {
                Debug.Log($"Chunk from ChatGPT: {chunkString}");
            }

            if (!sessions.ContainsKey(sessionId))
            {
                Debug.LogWarning($"Session not found. Set true to isChatCompletionJSDone.: {sessionId}");
                isChatCompletionJSDone = true;
                return;
            }

            var chatGPTSession = sessions[sessionId];

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
                        if (delta != null)
                        {
                            if (chatGPTSession.FirstDelta == null)
                            {
                                chatGPTSession.FirstDelta = delta;

                                if (delta.tool_calls != null)
                                {
                                    chatGPTSession.ToolCallId = chatGPTSession.FirstDelta.tool_calls[0].id;
                                    chatGPTSession.FunctionName = chatGPTSession.FirstDelta.tool_calls[0].function.name;
                                    chatGPTSession.ResponseType = ResponseType.FunctionCalling;
                                }
                                else
                                {
                                    chatGPTSession.ResponseType = ResponseType.Content;
                                }
                            }
                            if (delta.tool_calls == null)
                            {
                                temp += delta.content;
                            }
                            else
                            {
                                temp += delta.tool_calls[0].function.arguments;
                            }
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

            chatGPTSession.CurrentStreamBuffer += temp;
            chatGPTSession.StreamBuffer += temp;

            if (isDone)
            {
                isChatCompletionJSDone = true;
            }
        }
#endif
    }
}

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

        public override async UniTask StartStreamingAsync(ChatGPTSession chatGPTSession, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            chatGPTSession.CurrentStreamBuffer = string.Empty;

            // Store session with id to receive streaming data from JavaScript
            var sessionId = Guid.NewGuid().ToString();
            sessions.Add(sessionId, chatGPTSession);

            // Make request data
            var data = new Dictionary<string, object>()
            {
                { "model", Model },
                { "temperature", Temperature },
                { "messages", chatGPTSession.Contexts },
                { "stream", true },
            };
            if (!string.IsNullOrEmpty(ReasoningEffort))
            {
                data["reasoning_effort"] = ReasoningEffort;
                
            }
            if (!IsOpenAICompatibleAPI)
            {
                data["frequency_penalty"] = FrequencyPenalty;
                data["presence_penalty"] = PresencePenalty;
            }
            if (MaxTokens > 0)
            {
                data.Add("max_tokens", MaxTokens);
            }
            if (Tools.Count > 0)
            {
                var tools = new List<Dictionary<string, object>>();
                foreach (var tool in Tools)
                {
                    tools.Add(new Dictionary<string, object>()
                    {
                        { "type", "function" },
                        { "function", tool }
                    });
                }
                data.Add("tools", tools);
                // Mask tools if useFunctions = false. Don't remove tools to keep cache hit and to prevent hallucination
                if (!useFunctions)
                {
                    data.Add("tool_choice", "none");
                }
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
            if (customHeaders.Count > 0)
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
                if ((!string.IsNullOrEmpty(chatGPTSession.StreamBuffer) || !string.IsNullOrEmpty(chatGPTSession.FunctionName)) && isChatCompletionJSDone)
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

            // Update histories
            if (chatGPTSession.ResponseType != ResponseType.Error && chatGPTSession.ResponseType != ResponseType.Timeout)
            {
                UpdateContext(chatGPTSession);
            }
            else
            {
                Debug.LogWarning($"Messages are not added to histories for response type is not success: {chatGPTSession.ResponseType}");
            }

            // Ends with error
            if (chatGPTSession.ResponseType == ResponseType.Error)
            {
                throw new Exception($"ChatGPT ends with error");
            }

            // Process tags
            var extractedTags = ExtractTags(chatGPTSession.CurrentStreamBuffer);
            if (extractedTags.Count > 0 && HandleExtractedTags != null)
            {
                HandleExtractedTags(extractedTags, chatGPTSession);
            }

            if (!string.IsNullOrEmpty(chatGPTSession.ToolCallId))
            {
                foreach (var tool in gameObject.GetComponents<ITool>())
                {
                    var toolSpec = tool.GetToolSpec();
                    if (toolSpec.name == chatGPTSession.FunctionName)
                    {
                        Debug.Log($"Execute tool: {toolSpec.name}({chatGPTSession.FunctionArguments})");
                        // Execute tool
                        var toolResponse = await tool.ExecuteAsync(chatGPTSession.FunctionArguments, token);
                        chatGPTSession.Contexts.Add(new ChatGPTFunctionMessage(toolResponse.Body, chatGPTSession.ToolCallId));
                        // Reset tool call info to prevent infinite loop
                        chatGPTSession.ToolCallId = null;
                        chatGPTSession.FunctionName = null;
                        chatGPTSession.FunctionArguments = null;
                        // Call recursively with tool response
                        await StartStreamingAsync(chatGPTSession, customParameters, customHeaders, false, token);
                    }
                }
            }
            else if (CaptureImage != null && extractedTags.ContainsKey("vision") && chatGPTSession.IsVisionAvailable)
            {
                // Prevent infinit loop
                chatGPTSession.IsVisionAvailable = false;

                // Get image
                var imageSource = extractedTags["vision"];
                var imageBytes = await CaptureImage(imageSource);

                // Make contexts
                if (imageBytes != null)
                {
                    chatGPTSession.Contexts.Add(new ChatGPTAssistantMessage(chatGPTSession.StreamBuffer));
                    // Image -> Text to get the better accuracy
                    chatGPTSession.Contexts.Add(new ChatGPTUserMessage(new List<IContentPart>() {
                        new ImageUrlContentPart("data:image/jpeg;base64," + Convert.ToBase64String(imageBytes)),
                        new TextContentPart($"This is the image you captured. (source: {imageSource})")
                    }));
                }
                else
                {
                    chatGPTSession.Contexts.Add(new ChatGPTUserMessage("Please inform the user that an error occurred while capturing the image."));
                }

                // Call recursively with image
                await StartStreamingAsync(chatGPTSession, customParameters, customHeaders, useFunctions, token);
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

                        if (delta == null) continue;

                        chatGPTSession.ResponseType = ResponseType.Content;
                        if (delta.tool_calls == null)
                        {
                            chatGPTSession.CurrentStreamBuffer += delta.content;
                            chatGPTSession.StreamBuffer += delta.content;
                        }
                        else if (delta.tool_calls.Count > 0)
                        {
                            if (!string.IsNullOrEmpty(delta.tool_calls[0].id))
                            {
                                chatGPTSession.ToolCallId = delta.tool_calls[0].id;
                                chatGPTSession.FunctionName = delta.tool_calls[0].function.name;
                                chatGPTSession.FunctionArguments = string.Empty;
                            }
                            if (!string.IsNullOrEmpty(delta.tool_calls[0].function.arguments))
                            {
                                chatGPTSession.FunctionArguments += delta.tool_calls[0].function.arguments;
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

            if (isDone)
            {
                isChatCompletionJSDone = true;
            }
        }
#endif
    }
}

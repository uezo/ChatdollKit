using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog.Processor
{
    public class ChatGPTServiceWebGL : ChatGPTService
    {
        [DllImport("__Internal")]
        private static extern void ChatCompletionJS(string targetObjectName, string url, string apiKey, string chatCompletionRequest);

        public int TimeoutMilliseconds = 60000;

        public override async UniTask ChatCompletionAsync(List<ChatGPTMessage> messages, bool useFunctions = true)
        {
            // Make request data
            var data = new Dictionary<string, object>()
            {
                { "model", Model },
                { "temperature", Temperature },
                { "messages", messages },
                { "stream", true },
            };
            if (MaxTokens > 0)
            {
                data.Add("max_tokens", MaxTokens);
            }
            if (useFunctions && chatGPTFunctions.Count > 0)
            {
                data.Add("functions", chatGPTFunctions);
            }

            // Start API stream
            IsResponseDone = false;
            StreamBuffer = string.Empty;
            responseType = ResponseType.None;
            firstDelta = null;
            ChatCompletionJS(gameObject.name, ChatCompletionUrl, ApiKey, JsonConvert.SerializeObject(data));

            var startAt = System.DateTime.UtcNow.Ticks;
            while (!IsResponseDone)
            {
                await UniTask.Delay(20);
                if (System.DateTime.UtcNow.Ticks - startAt > TimeoutMilliseconds * 10000)   // 10000tick / ms
                {
                    // Timeout
                    Debug.LogWarning($"Response timeout: {(System.DateTime.UtcNow.Ticks - startAt) / 10000}ms");
                    IsResponseDone = true;
                    break;
                }
            }
        }

        public void SetChatCompletionStreamChunk(string chunkString)
        {
            if (string.IsNullOrEmpty(chunkString))
            {
                Debug.Log("Chunk is null or empty. Set true to IsResponseDone.");
                IsResponseDone = true;
                return;
            }

            var isDeltaSet = false;
            var temp = string.Empty;
            foreach (var d in chunkString.Split("data:"))
            {
                if (!string.IsNullOrEmpty(d))
                {
                    if (d.Trim() != "[DONE]")
                    {
                        var j = JsonConvert.DeserializeObject<ChatGPTStreamResponse>(d);
                        var delta = j.choices[0].delta;
                        if (!isDeltaSet)
                        {
                            firstDelta = delta;
                            responseType = delta.function_call != null ? ResponseType.FunctionCalling : ResponseType.Content;
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
                        Debug.Log("Chunk is data:[DONE]. Set true to IsResponseDone.");
                        IsResponseDone = true;
                        return;
                    }
                }
            }

            StreamBuffer += temp;
        }
    }
}

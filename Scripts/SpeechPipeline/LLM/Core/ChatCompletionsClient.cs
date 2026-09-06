// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.LLM
{
    public sealed class ChatCompletionsClient : HttpLlmServiceBase
    {
        public ChatCompletionsClient(LlmServiceOptions options, HttpClient httpClient = null) : base(options, httpClient) { }
        protected override bool UsesResponsesApi => false;

        protected override async UniTask<LlmProviderResult> GenerateCoreAsync(LlmRequest request, LlmServiceOptions options,
            Func<LlmResponse, UniTask> emit, CancellationToken token)
        {
            var body = LlmRequestBuilder.BuildChatRequest(request, options);
            var text = new StringBuilder();
            var calls = new SortedDictionary<int, LlmToolCall>();
            var result = new LlmProviderResult();
            var ended = false;
            var error = await StreamRequestAsync("chat/completions", body, options, async data =>
            {
                if (data == "[DONE]") { ended = true; return false; }
                var evt = ParseEvent(data);
                if (evt["error"] != null) throw new LlmServiceException(LlmErrorParser.Parse(evt));
                result.ResponseId = LlmErrorParser.Value(evt["id"]) ?? result.ResponseId;
                if (evt["usage"] is JObject usage) result.Usage = (JObject)usage.DeepClone();
                var choices = evt["choices"] as JArray;
                if (choices == null || choices.Count == 0) return true;
                var choice = choices[0] as JObject;
                var reason = LlmErrorParser.Value(choice?["finish_reason"]);
                if (reason == "length" || reason == "content_filter")
                    throw LlmErrorParser.Failure(reason, "LLM response was incomplete.");
                var delta = choice?["delta"] as JObject;
                if (delta == null) return true;
                var content = LlmErrorParser.Value(delta["content"]);
                if (!string.IsNullOrEmpty(content))
                {
                    text.Append(content);
                    await emit(new LlmResponse { ContextId = request.ContextId, Text = content });
                }
                if (delta["tool_calls"] is JArray deltas)
                    foreach (var item in deltas)
                    {
                        var index = item["index"]?.Type == JTokenType.Integer ? (int)item["index"] : 0;
                        if (!calls.TryGetValue(index, out var call))
                            calls[index] = call = new LlmToolCall { Index = index, Arguments = string.Empty };
                        call.Id = LlmErrorParser.Value(item["id"]) ?? call.Id;
                        if (item["function"] is JObject function)
                        {
                            call.Name = (call.Name ?? string.Empty) + LlmErrorParser.Value(function["name"]);
                            call.Arguments += LlmErrorParser.Value(function["arguments"]);
                        }
                    }
                return true;
            }, token);
            if (error != null) throw new LlmServiceException(error);
            if (!ended) throw LlmErrorParser.Failure("unexpected_eof", "LLM stream ended before its completion marker.");
            result.ToolCalls = new List<LlmToolCall>(calls.Values);
            var assistant = new JObject { ["role"] = "assistant", ["content"] = text.Length == 0 ? JValue.CreateNull() : new JValue(text.ToString()) };
            if (calls.Count > 0)
            {
                var outputCalls = new JArray();
                foreach (var call in calls.Values)
                    outputCalls.Add(new JObject
                    {
                        ["id"] = call.Id, ["type"] = "function",
                        ["function"] = new JObject { ["name"] = call.Name, ["arguments"] = call.Arguments }
                    });
                assistant["tool_calls"] = outputCalls;
            }
            result.OutputItems = new JArray(assistant);
            return result;
        }
    }
}

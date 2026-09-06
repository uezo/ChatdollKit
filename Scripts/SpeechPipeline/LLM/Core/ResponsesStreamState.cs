using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.LLM
{
    /// <summary>Shared Responses HTTP/WebSocket event assembly; only completed responses produce a continuation ID.</summary>
    internal sealed class ResponsesStreamState
    {
        private readonly string contextId;
        private readonly Func<LlmResponse, UniTask> emit;
        private readonly SortedDictionary<int, LlmToolCall> toolCalls = new SortedDictionary<int, LlmToolCall>();
        internal bool IsCompleted { get; private set; }
        internal LlmProviderResult Result { get; } = new LlmProviderResult();

        internal ResponsesStreamState(string contextId, Func<LlmResponse, UniTask> emit)
        { this.contextId = contextId; this.emit = emit; }

        internal async UniTask ProcessEventAsync(JObject evt)
        {
            if (IsCompleted) return;
            var type = LlmErrorParser.Value(evt["type"]);
            switch (type)
            {
                case "response.output_text.delta":
                    var delta = LlmErrorParser.Value(evt["delta"]);
                    if (delta != null) await emit(new LlmResponse { ContextId = contextId, Text = delta });
                    break;
                case "response.output_item.added":
                    AddToolItem(evt["item"] as JObject, Index(evt), false);
                    break;
                case "response.output_item.done":
                    AddToolItem(evt["item"] as JObject, Index(evt), true);
                    break;
                case "response.function_call_arguments.delta":
                    var tool = GetTool(Index(evt));
                    tool.Arguments = (tool.Arguments ?? string.Empty) + LlmErrorParser.Value(evt["delta"]);
                    break;
                case "response.function_call_arguments.done":
                    var completedTool = GetTool(Index(evt));
                    completedTool.Arguments = LlmErrorParser.Value(evt["arguments"]) ?? completedTool.Arguments;
                    completedTool.Name = LlmErrorParser.Value(evt["name"]) ?? completedTool.Name;
                    break;
                case "response.completed":
                    var response = evt["response"] as JObject;
                    var id = LlmErrorParser.Value(response?["id"]);
                    if (string.IsNullOrEmpty(id)) throw LlmErrorParser.Failure("invalid_response", "Completed response did not include an ID.");
                    Result.ResponseId = id;
                    Result.OutputItems = (JArray)(response?["output"] as JArray)?.DeepClone();
                    Result.Usage = (JObject)(response?["usage"] as JObject)?.DeepClone();
                    if (Result.OutputItems != null)
                        for (var i = 0; i < Result.OutputItems.Count; i++) AddToolItem(Result.OutputItems[i] as JObject, i, true);
                    Result.ToolCalls = new List<LlmToolCall>(toolCalls.Values);
                    IsCompleted = true;
                    break;
                case "response.failed":
                    throw new LlmServiceException(LlmErrorParser.Parse(evt["response"] as JObject));
                case "response.incomplete":
                    var reason = LlmErrorParser.Value(evt["response"]?["incomplete_details"]?["reason"]);
                    throw LlmErrorParser.Failure(reason ?? "incomplete_response", "LLM response was incomplete.");
                case "error":
                    throw new LlmServiceException(LlmErrorParser.Parse(evt));
            }
        }

        private static int Index(JObject evt) => evt["output_index"]?.Type == JTokenType.Integer ? (int)evt["output_index"] : 0;

        private LlmToolCall GetTool(int index)
        {
            if (!toolCalls.TryGetValue(index, out var call))
                toolCalls[index] = call = new LlmToolCall { Index = index, Arguments = string.Empty };
            return call;
        }

        private void AddToolItem(JObject item, int index, bool final)
        {
            if ((string)item?["type"] != "function_call") return;
            var tool = GetTool(index);
            tool.Id = LlmErrorParser.Value(item["call_id"]) ?? tool.Id;
            tool.Name = LlmErrorParser.Value(item["name"]) ?? tool.Name;
            // Done/completed items replace, rather than append to, accumulated argument deltas.
            var arguments = LlmErrorParser.Value(item["arguments"]);
            if (arguments != null && (final || !string.IsNullOrEmpty(arguments) || string.IsNullOrEmpty(tool.Arguments)))
                tool.Arguments = arguments;
        }
    }
}

// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.LLM
{
    internal static class LlmRequestBuilder
    {
        internal static JObject BuildChatRequest(LlmRequest request, LlmServiceOptions options)
        {
            var messages = new JArray();
            if (!string.IsNullOrEmpty(options.SystemPrompt))
                messages.Add(new JObject { ["role"] = "system", ["content"] = options.SystemPrompt });
            Append(messages, options.InitialMessages);
            // Upstream omits leading partial-history assistant/tool messages until the first user turn.
            var foundUser = false;
            foreach (var item in request.History ?? new JArray())
            {
                foundUser |= (string)(item as JObject)?["role"] == "user";
                if (foundUser) messages.Add(item.DeepClone());
            }
            Append(messages, GetCurrentInput(request, false));
            var body = new JObject { ["model"] = options.Model, ["messages"] = messages, ["stream"] = true };
            if (options.ReasoningEffort != null) body["reasoning_effort"] = options.ReasoningEffort;
            if (options.MaxOutputTokens.HasValue) body["max_completion_tokens"] = options.MaxOutputTokens.Value;
            Complete(body, request, options, false);
            return body;
        }

        internal static JObject BuildResponsesRequest(LlmRequest request, LlmServiceOptions options, bool recovery = false)
        {
            var input = new JArray();
            if (recovery || string.IsNullOrEmpty(request.PreviousResponseId))
            {
                Append(input, options.InitialMessages);
                Append(input, request.History);
            }
            Append(input, GetCurrentInput(request, true));
            var body = new JObject { ["model"] = options.Model, ["input"] = input, ["stream"] = true };
            if (!string.IsNullOrEmpty(options.SystemPrompt)) body["instructions"] = options.SystemPrompt;
            if (!recovery && !string.IsNullOrEmpty(request.PreviousResponseId)) body["previous_response_id"] = request.PreviousResponseId;
            if (options.ReasoningEffort != null) body["reasoning"] = new JObject { ["effort"] = options.ReasoningEffort };
            if (options.MaxOutputTokens.HasValue) body["max_output_tokens"] = options.MaxOutputTokens.Value;
            Complete(body, request, options, true);
            if (recovery) body.Remove("previous_response_id");
            return body;
        }

        // Reuse final edited parameters; Python does not invoke the edit hook again on recovery.
        internal static JObject BuildResponsesRecoveryRequest(JObject sent, LlmRequest request, LlmServiceOptions options)
        {
            var recovered = (JObject)sent.DeepClone();
            recovered.Remove("previous_response_id");
            var input = new JArray();
            Append(input, options.InitialMessages);
            Append(input, request.History);
            Append(input, GetCurrentInput(request, true));
            recovered["input"] = input;
            return recovered;
        }

        internal static bool CanRecoverPreviousResponse(JObject sent, LlmRequest request, LlmError error)
        {
            var current = GetCurrentInput(request, true);
            return sent.Property("previous_response_id") != null && current.Count > 0 &&
                (string)(current[current.Count - 1] as JObject)?["role"] == "user" && LlmErrorParser.IsPreviousResponseNotFound(error);
        }

        internal static JArray GetCurrentInput(LlmRequest request, bool responses)
        {
            if (request.Input != null) return (JArray)request.Input.DeepClone();
            var content = new JArray();
            foreach (var url in request.ImageUrls ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(url)) continue;
                content.Add(responses
                    ? new JObject { ["type"] = "input_image", ["image_url"] = url }
                    : new JObject { ["type"] = "image_url", ["image_url"] = new JObject { ["url"] = url } });
            }
            JToken value = request.Text == null ? JValue.CreateNull() : new JValue(request.Text);
            if (content.Count > 0)
            {
                if (!string.IsNullOrEmpty(request.Text))
                    content.Add(new JObject { ["type"] = responses ? "input_text" : "text", ["text"] = request.Text });
                value = content;
            }
            return new JArray(new JObject { ["role"] = "user", ["content"] = value });
        }

        private static void Complete(JObject body, LlmRequest request, LlmServiceOptions options, bool responses)
        {
            if (options.Temperature.HasValue) body["temperature"] = options.Temperature.Value;
            if (options.ToolDefinitions != null && options.ToolDefinitions.Count > 0)
            {
                var tools = (JArray)options.ToolDefinitions.DeepClone();
                if (responses)
                    for (var i = 0; i < tools.Count; i++)
                        if ((string)tools[i]["type"] == "function" && tools[i]["function"] is JObject function)
                        {
                            var converted = (JObject)function.DeepClone();
                            converted["type"] = "function";
                            tools[i] = converted;
                        }
                body["tools"] = tools;
            }
            Overwrite(body, options.ExtraBody);
            Overwrite(body, request.Parameters);
            options.EditRequestParameters?.Invoke(body, request);
            if (body["stream"]?.Type != JTokenType.Boolean || (bool)body["stream"] != true)
                throw new ArgumentException("Streaming LLM services require stream=true.");
        }

        private static void Overwrite(JObject target, JObject source)
        {
            if (source == null) return;
            foreach (var property in source.Properties()) target[property.Name] = property.Value.DeepClone();
        }

        private static void Append(JArray target, JArray source)
        {
            if (source == null) return;
            foreach (var item in source) target.Add(item.DeepClone());
        }
    }
}

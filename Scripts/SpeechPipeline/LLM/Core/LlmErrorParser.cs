using System;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.LLM
{
    internal static class LlmErrorParser
    {
        internal static LlmError Parse(JObject payload, int? statusCode = null)
        {
            var error = payload?["error"] as JObject ?? payload;
            return new LlmError
            {
                Code = Value(error?["code"]) ?? (statusCode.HasValue ? "http_" + statusCode.Value : "provider_error"),
                Message = Value(error?["message"]) ?? (statusCode.HasValue ? "LLM request returned HTTP " + statusCode.Value + "." : "LLM provider returned an error."),
                Parameter = Value(error?["param"]),
                Type = Value(error?["type"]),
                StatusCode = statusCode
            };
        }

        internal static bool IsPreviousResponseNotFound(LlmError error)
        {
            if (error == null) return false;
            var code = (error.Code ?? string.Empty).ToLowerInvariant();
            var param = (error.Parameter ?? string.Empty).ToLowerInvariant();
            var message = (error.Message ?? string.Empty).ToLowerInvariant();
            var notFound = code.Contains("not_found") || message.Contains("not found") || message.Contains("does not exist");
            var previous = param == "previous_response_id" || code.Contains("previous_response_id") ||
                message.Contains("previous_response_id") || message.Contains("previous response");
            return notFound && previous;
        }

        internal static LlmServiceException Failure(string code, string message) =>
            new LlmServiceException(new LlmError { Code = code, Message = message });

        internal static string Value(JToken value) => value?.Type == JTokenType.String ? (string)value : null;
    }
}

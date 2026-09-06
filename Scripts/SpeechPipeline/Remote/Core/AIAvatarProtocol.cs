using ChatdollKit.SpeechPipeline;
using System;
using ChatdollKit.SpeechPipeline.LLM;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.Remote
{
    internal static class AIAvatarProtocol
    {
        internal static JObject Start(AIAvatarSpeechPipelineOptions options, string remoteSessionId, string contextId) => new JObject
        {
            ["type"] = "start", ["session_id"] = remoteSessionId, ["user_id"] = options.UserId,
            ["context_id"] = contextId, ["metadata"] = options.Metadata?.DeepClone()
        };

        internal static JObject Invoke(SpeechPipelineRequest request, string remoteSessionId, string contextId)
        {
            var files = new JArray();
            foreach (var url in request.ImageUrls ?? Array.Empty<string>()) files.Add(new JObject { ["url"] = url });
            return new JObject
            {
                ["type"] = "invoke", ["session_id"] = remoteSessionId, ["user_id"] = request.UserId,
                ["context_id"] = request.ContextId ?? contextId, ["text"] = request.Text,
                ["audio_data"] = request.AudioData == null ? null : Convert.ToBase64String(request.AudioData),
                ["files"] = files, ["system_prompt_params"] = request.SystemPromptParameters?.DeepClone(),
                ["allow_merge"] = request.AllowMerge, ["wait_in_queue"] = request.WaitInQueue,
                ["metadata"] = request.Metadata?.DeepClone()
            };
        }

        internal static SpeechPipelineResponse Response(JObject message, string sessionId, string transactionId, int maxAudioBytes)
        {
            var type = (string)message["type"];
            SpeechPipelineResponseType responseType;
            switch (type)
            {
                case "accepted": responseType = SpeechPipelineResponseType.Accepted; break;
                case "start": responseType = SpeechPipelineResponseType.Start; break;
                case "chunk": responseType = SpeechPipelineResponseType.Chunk; break;
                case "tool_call": responseType = SpeechPipelineResponseType.ToolCall; break;
                case "final": case "vision": responseType = SpeechPipelineResponseType.Final; break;
                case "canceled": case "cancelled": responseType = SpeechPipelineResponseType.Canceled; break;
                case "error": responseType = SpeechPipelineResponseType.Error; break;
                case "stop": responseType = SpeechPipelineResponseType.Stop; break;
                default: throw new FormatException("Unsupported AIAvatarKit response event: " + type);
            }
            var metadata = message["metadata"] as JObject;
            metadata = (JObject)metadata?.DeepClone() ?? new JObject();
            foreach (var field in new[] { "control_tags", "avatar_control_request" })
                if (message[field] != null && message[field].Type != JTokenType.Null) metadata[field] = message[field].DeepClone();
            if (type == "vision") metadata["remote_event_type"] = type;
            if (metadata["pcm_format"] != null && metadata["pcm_format"].Type != JTokenType.Null)
                throw new NotSupportedException("This pipeline requires complete WAV responses. Set AIAvatarWebSocketServer.response_audio_chunk_size to 0.");

            byte[] audio = null;
            var encoded = (string)message["audio_data"];
            if (!string.IsNullOrEmpty(encoded))
            {
                if ((long)encoded.Length > ((long)maxAudioBytes + 2) / 3 * 4)
                    throw new FormatException("AIAvatarKit response audio exceeds MaxResponseAudioBytes.");
                audio = Convert.FromBase64String(encoded);
                if (audio.Length > maxAudioBytes) throw new FormatException("AIAvatarKit response audio exceeds MaxResponseAudioBytes.");
            }
            LlmToolCall tool = null;
            if (responseType == SpeechPipelineResponseType.ToolCall && metadata["tool_call"] is JObject call)
                tool = new LlmToolCall
                {
                    Id = (string)call["id"], Name = (string)call["name"],
                    Arguments = call["arguments"]?.Type == JTokenType.String
                        ? (string)call["arguments"] : call["arguments"]?.ToString(Formatting.None)
                };
            return new SpeechPipelineResponse
            {
                Type = responseType, SessionId = sessionId, TransactionId = transactionId,
                UserId = (string)message["user_id"], ContextId = (string)message["context_id"],
                Text = (string)message["text"], VoiceText = (string)message["voice_text"], Language = (string)message["language"],
                AudioData = audio, Metadata = metadata, StructuredContent = (JObject)(message["structured_content"] as JObject)?.DeepClone(), ToolCall = tool
            };
        }
    }
}

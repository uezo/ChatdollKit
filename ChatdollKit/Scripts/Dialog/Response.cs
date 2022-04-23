using System;
using System.Collections.Generic;
using ChatdollKit.Model;

namespace ChatdollKit.Dialog
{
    public class Response
    {
        public string Id { get; }
        public DateTime CreatedAt { get; }
        public List<AnimatedVoiceRequest> AnimatedVoiceRequests { get; set; }
        public AnimatedVoiceRequest AnimatedVoiceRequest
        {
            get
            {
                return AnimatedVoiceRequests[AnimatedVoiceRequests.Count - 1];
            }
            set
            {
                AnimatedVoiceRequests[AnimatedVoiceRequests.Count - 1] = value;
            }
        }
        public virtual object Payloads { get; set; }
        public bool EndTopic { get; set; } = true;
        public bool EndConversation { get; set; } = false;
        public RequestType NextTurnRequestType { get; set; } = RequestType.Voice;
        public string SkillName { get; set; }

        public Response(string id)
        {
            Id = id;
            CreatedAt = DateTime.UtcNow;
            AnimatedVoiceRequests = new List<AnimatedVoiceRequest>();
            AnimatedVoiceRequests.Add(new AnimatedVoiceRequest());
        }

        public void AddVoice(string name, float preGap = 0.0f, float postGap = 0.0f, string description = null, bool asNewFrame = false)
        {
            AnimatedVoiceRequest.AddVoice(name, preGap, postGap, description, asNewFrame);
        }

        public void AddVoiceWeb(string url, float preGap = 0.0f, float postGap = 0.0f, string name = null, string text = null, string description = null, bool asNewFrame = false)
        {
            AnimatedVoiceRequest.AddVoiceWeb(url, preGap, postGap, name, text, description, asNewFrame);
        }

        public void AddVoiceTTS(string text, float preGap = 0.0f, float postGap = 0.0f, string name = null, TTSConfiguration ttsConfig = null, string description = null, bool asNewFrame = false)
        {
            AnimatedVoiceRequest.AddVoiceTTS(text, preGap, postGap, name, ttsConfig, description, asNewFrame);
        }

        public void AddAnimation(string name, string layerName = null, float duration = 0.0f, float fadeLength = -1.0f, float weight = 1.0f, float preGap = 0.0f, string description = null, bool asNewFrame = false)
        {
            AnimatedVoiceRequest.AddAnimation(name, layerName ?? string.Empty, duration, fadeLength, weight, preGap, description, asNewFrame);
        }

        public void AddFace(string name, float duration = 0.0f, string description = null, bool asNewFrame = false)
        {
            AnimatedVoiceRequest.AddFace(name, duration, description);
        }
    }
}

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
        public string Text
        {
            get
            {
                if (AnimatedVoiceRequest.AnimatedVoices.Count > 0 && AnimatedVoiceRequest.AnimatedVoices[0].Voices.Count > 0)
                {
                    return AnimatedVoiceRequest.AnimatedVoices[0].Voices[0].Text;
                }
                else
                {
                    return null;
                }
            }
        }
        public virtual object Payloads { get; set; }
        public bool EndTopic { get; set; }
        public bool EndConversation { get; set; }
        public RequestType NextTurnRequestType { get; set; }
        public string SkillName { get; set; }

        public Response(string id, string text = null, bool endTopic = true, bool endConversation = false, RequestType nextTurnRequestType = RequestType.Voice, string skillName = null)
        {
            Id = id;
            CreatedAt = DateTime.UtcNow;
            AnimatedVoiceRequests = new List<AnimatedVoiceRequest>();
            AnimatedVoiceRequests.Add(new AnimatedVoiceRequest());
            EndTopic = endTopic;
            EndConversation = endConversation;
            NextTurnRequestType = nextTurnRequestType;
            SkillName = skillName;

            if (!string.IsNullOrEmpty(text))
            {
                AnimatedVoiceRequest.AddVoiceTTS(text);
            }
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

        public void AddAnimation(string paramKey, int paramValue, float duration = 0.0f, string layeredAnimation = null, string layeredAnimationLayer = null, bool asNewFrame = false)
        {
            AnimatedVoiceRequest.AddAnimation(paramKey, paramValue, duration, layeredAnimation, layeredAnimationLayer, asNewFrame);
        }

        public void AddFace(string name, float duration = 0.0f, string description = null, bool asNewFrame = false)
        {
            AnimatedVoiceRequest.AddFace(name, duration, description);
        }
    }
}

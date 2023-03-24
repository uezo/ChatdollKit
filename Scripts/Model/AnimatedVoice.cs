using System.Collections.Generic;

namespace ChatdollKit.Model
{
    // Animation with voice and face expression
    public class AnimatedVoice
    {
        public List<Voice> Voices { get; set; }
        public List<Animation> Animations { get; set; }
        public List<FaceExpression> Faces { get; set; }

        public AnimatedVoice(List<Voice> voices = null, List<Animation> animations = null, List<FaceExpression> faces = null)
        {
            Voices = voices ?? new List<Voice>();
            Animations = animations ?? new List<Animation>();
            Faces = faces ?? new List<FaceExpression>();
        }

        public void AddVoice(string name, float preGap = 0.0f, float postGap = 0.0f, string text = null, string url = null, TTSConfiguration ttsConfig = null, VoiceSource source = VoiceSource.Local, string description = null)
        {
            Voices.Add(new Voice(name, preGap, postGap, text, url, ttsConfig, source, false, description));
        }

        public void AddVoiceWeb(string url, float preGap = 0.0f, float postGap = 0.0f, string name = null, string text = null, bool useCache = true, string description = null)
        {
            Voices.Add(new Voice(name ?? string.Empty, preGap, postGap, text, url, null, VoiceSource.Web, useCache, description));
        }

        public void AddVoiceTTS(string text, float preGap = 0.0f, float postGap = 0.0f, string name = null, TTSConfiguration ttsConfig = null, bool useCache = true, string description = null)
        {
            Voices.Add(new Voice(name ?? string.Empty, preGap, postGap, text, string.Empty, ttsConfig, VoiceSource.TTS, useCache, description));
        }

        public void AddAnimation(string paramKey, int paramValue, float duration = 0.0f, string layeredAnimation = null, string layeredAnimationLayer = null)
        {
            Animations.Add(new Animation(paramKey, paramValue, duration, layeredAnimation, layeredAnimationLayer));
        }

        public void AddFace(string name, float duration = 0.0f, string description = null)
        {
            Faces.Add(new FaceExpression(name, duration, description));
        }
    }
}

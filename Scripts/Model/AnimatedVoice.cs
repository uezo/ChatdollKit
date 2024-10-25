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

        public void AddVoice(string text, float preGap = 0.0f, float postGap = 0.0f, TTSConfiguration ttsConfig = null, bool useCache = true, string description = null)
        {
            Voices.Add(new Voice(text, preGap, postGap, ttsConfig, useCache, description));
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

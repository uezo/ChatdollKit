using System.Collections.Generic;


namespace ChatdollKit.Model
{
    // Animation with voice and face expression
    public class AnimatedVoice
    {
        public List<Voice> Voices { get; set; }
        public Dictionary<string, List<Animation>> Animations { get; set; }
        public List<FaceExpression> Faces { get; set; }

        public AnimatedVoice(List<Voice> voices = null, Dictionary<string, List<Animation>> animations = null, List<FaceExpression> faces = null)
        {
            Voices = voices ?? new List<Voice>();
            Animations = animations ?? new Dictionary<string, List<Animation>>();
            Faces = faces ?? new List<FaceExpression>();
        }

        public void AddVoice(string name, float preGap = 0.0f, float postGap = 0.0f)
        {
            Voices.Add(new Voice(name, preGap, postGap));
        }

        public void AddAnimation(string name, string layerName = null, float duration = 0.0f, float fadeLength = -1.0f, float weight = 1.0f, float preGap = 0.0f)
        {
            if (!Animations.ContainsKey(layerName))
            {
                Animations.Add(layerName, new List<Animation>());
            }
            Animations[layerName].Add(new Animation(name, layerName, duration, fadeLength, weight, preGap));
        }

        public void AddFace(string name, float duration = 0.0f)
        {
            Faces.Add(new FaceExpression(name, duration));
        }
    }
}

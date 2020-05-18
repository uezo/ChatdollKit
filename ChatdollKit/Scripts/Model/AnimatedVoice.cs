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

        public void AddVoice(string name, float preGap = 0.0f, float postGap = 0.0f, string text = null, string url = null, Dictionary<string, string> ttsOptions = null, VoiceSource source = VoiceSource.Local)
        {
            Voices.Add(new Voice(name, preGap, postGap, text, url, ttsOptions, source));
        }

        public void AddVoiceWeb(string url, float preGap = 0.0f, float postGap = 0.0f, string name = null, string text = null)
        {
            Voices.Add(new Voice(name ?? string.Empty, preGap, postGap, text, url, null, VoiceSource.Web));
        }

        public void AddVoiceTTS(string text, float preGap = 0.0f, float postGap = 0.0f, string name = null, Dictionary<string, string> ttsOptions = null)
        {
            Voices.Add(new Voice(name ?? string.Empty, preGap, postGap, text, string.Empty, ttsOptions, VoiceSource.TTS));
        }

        public void AddAnimation(string name, string layerName = null, float duration = 0.0f, float fadeLength = -1.0f, float weight = 1.0f, float preGap = 0.0f, string description = null)
        {
            if (!Animations.ContainsKey(layerName))
            {
                Animations.Add(layerName, new List<Animation>());
            }
            Animations[layerName].Add(new Animation(name, layerName, duration, fadeLength, weight, preGap, description));
        }

        public void AddFace(string name, float duration = 0.0f, string description = null)
        {
            Faces.Add(new FaceExpression(name, duration, description));
        }
    }
}

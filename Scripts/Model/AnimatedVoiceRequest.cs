using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ChatdollKit.Model
{
    // Request for amination with voice and face expression
    public class AnimatedVoiceRequest
    {
        public List<AnimatedVoice> AnimatedVoices { get; set; }
        public bool StopIdlingOnStart { get; set; }
        public bool StartIdlingOnEnd { get; set; }

        [JsonConstructor]
        public AnimatedVoiceRequest(List<AnimatedVoice> animatedVoice = null, bool startIdlingOnEnd = true, bool stopIdlingOnStart = true)
        {
            AnimatedVoices = animatedVoice ?? new List<AnimatedVoice>();
            StartIdlingOnEnd = startIdlingOnEnd;
            StopIdlingOnStart = stopIdlingOnStart;
        }

        public void AddVoice(string name, float preGap = 0.0f, float postGap = 0.0f, string description = null, bool asNewFrame = false)
        {
            if (asNewFrame || AnimatedVoices.Count == 0)
            {
                CreateNewFrame();
            }
            AnimatedVoices.Last().AddVoice(name, preGap, postGap, description: description);
        }

        public void AddVoiceWeb(string url, float preGap = 0.0f, float postGap = 0.0f, string name = null, string text = null, string description = null, bool asNewFrame = false)
        {
            if (asNewFrame || AnimatedVoices.Count == 0)
            {
                CreateNewFrame();
            }
            AnimatedVoices.Last().AddVoiceWeb(url, preGap, postGap, name, text, description: description);
        }

        public void AddVoiceTTS(string text, float preGap = 0.0f, float postGap = 0.0f, string name = null, TTSConfiguration ttsConfig = null, string description = null, bool asNewFrame = false)
        {
            if (asNewFrame || AnimatedVoices.Count == 0)
            {
                CreateNewFrame();
            }
            AnimatedVoices.Last().AddVoiceTTS(text, preGap, postGap, name, ttsConfig, description: description);
        }

        public void AddAnimation(string paramKey, int paramValue, float duration = 0.0f, string layeredAnimation = null, string layeredAnimationLayer = null, bool asNewFrame = false)
        {
            if (asNewFrame || AnimatedVoices.Count == 0)
            {
                CreateNewFrame();
            }
            AnimatedVoices.Last().AddAnimation(paramKey, paramValue, duration, layeredAnimation, layeredAnimationLayer);
        }

        public void AddFace(string name, float duration = 0.0f, string description = null, bool asNewFrame = false)
        {
            if (asNewFrame || AnimatedVoices.Count == 0)
            {
                CreateNewFrame();
            }
            AnimatedVoices.Last().AddFace(name, duration, description);
        }

        public int CreateNewFrame()
        {
            AnimatedVoices.Add(new AnimatedVoice());
            return AnimatedVoices.Count;
        }
    }
}

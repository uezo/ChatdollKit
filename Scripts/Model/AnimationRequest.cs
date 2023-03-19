using System.Collections.Generic;
using System.Linq;


namespace ChatdollKit.Model
{
    public class AnimationRequest
    {
        public Dictionary<string, List<Animation>> Animations { get; set; }
        public bool StopIdlingOnStart { get; set; }
        public bool StartIdlingOnEnd { get; set; }
        public bool StopLayeredAnimations { get; set; }
        private string baseLayerName { get; set; }

        public AnimationRequest(Dictionary<string, List<Animation>> animations = null, bool startIdlingOnEnd = true, bool stopIdlingOnStart = true, bool stopLayeredAnimations = true, string baseLayerName = null)
        {
            Animations = animations ?? new Dictionary<string, List<Animation>>();
            StartIdlingOnEnd = startIdlingOnEnd;
            StopIdlingOnStart = stopIdlingOnStart;
            StopLayeredAnimations = stopLayeredAnimations;
            this.baseLayerName = baseLayerName ?? string.Empty;
        }

        public List<Animation> BaseLayerAnimations {
            get
            {
                if (Animations.ContainsKey(BaseLayerName))
                {
                    return Animations[BaseLayerName];
                }
                else if (Animations.Count > 0)
                {
                    return Animations[Animations.Keys.First()];
                }
                else
                {
                    return null;
                }
            }
        }

        public string BaseLayerName
        {
            get
            {
                if (baseLayerName != string.Empty)
                {
                    return baseLayerName;
                }
                else if (Animations.ContainsKey(string.Empty))
                {
                    return string.Empty;
                }
                else if (Animations.Count > 0)
                {
                    return Animations.Keys.First();
                }
                else
                {
                    return string.Empty;
                }
            }
        }

        public void AddAnimation(string name, float duration = 0.0f, float fadeLength = -1.0f, float weight = 1.0f, float preGap = 0.0f)
        {
            AddAnimation(name, BaseLayerName, duration, fadeLength, weight, preGap);
        }

        public void AddAnimation(string name, string layerName, float duration = 0.0f, float fadeLength = -1.0f, float weight = 1.0f, float preGap = 0.0f, string description = null)
        {
            if (!Animations.ContainsKey(layerName))
            {
                Animations.Add(layerName, new List<Animation>());
            }
            Animations[layerName].Add(new Animation(name, layerName, duration, fadeLength, weight, preGap, description));
        }
    }
}

using System;

namespace ChatdollKit.Model
{
    public class Animation
    {
        public string Id { get; private set; }
        public string ParameterKey { get; set; }
        public int ParameterValue { get; set; }
        public float Duration { get; set; }
        public string LayeredAnimationName { get; set; }
        public string LayeredAnimationLayerName { get; set; }

        public Animation(string parameterKey, int parameterValue, float duration, string layeredAnimationName = null, string layeredAnimationLayerName = null)
        {
            Id = Guid.NewGuid().ToString();
            ParameterKey = parameterKey;
            ParameterValue = parameterValue;
            Duration = duration;
            LayeredAnimationName = layeredAnimationName;
            LayeredAnimationLayerName = layeredAnimationLayerName;
        }
    }
}

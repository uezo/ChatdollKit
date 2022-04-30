namespace ChatdollKit.Model
{
    public class Animation
    {
        public string Name { get; set; }
        public string LayerName { get; set; }
        public float Duration { get; set; }
        public float FadeLength { get; set; }
        public float Weight { get; set; }
        public float PreGap { get; set; }
        public string Description { get; set; }

        public Animation(string name, string layerName, float duration, float fadeLength, float weight, float preGap, string description)
        {
            Name = name;
            LayerName = layerName;
            Duration = duration;
            FadeLength = fadeLength;
            Weight = weight;
            PreGap = preGap;
            Description = description;
        }

        public float Length
        {
            get
            {
                return (Duration + PreGap);
            }
        }
    }
}

namespace ChatdollKit.Model
{
    public enum VoiceSource
    {
        Local, Web, TTS
    }

    // Voice
    public class Voice
    {
        public string Name { get; set; }
        public float PreGap { get; set; }
        public float PostGap { get; set; }
        public string Text { get; set; }
        public string Url { get; set; }
        public VoiceSource Source { get; set; }

        public Voice(string name, float preGap, float postGap, string text, string url, VoiceSource source)
        {
            Name = name;
            PreGap = preGap;
            PostGap = postGap;
            Text = text;
            Url = url;
            Source = source;
        }
    }
}

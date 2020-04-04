using System.Collections.Generic;

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
        public Dictionary<string, string> TTSOptions { get; set; }
        public VoiceSource Source { get; set; }

        public Voice(string name, float preGap, float postGap, string text, string url, Dictionary<string, string> ttsOptions, VoiceSource source)
        {
            Name = name;
            PreGap = preGap;
            PostGap = postGap;
            Text = text;
            Url = url;
            TTSOptions = ttsOptions;
            Source = source;
        }

        public string GetTTSOption(string key)
        {
            if (TTSOptions != null && TTSOptions.ContainsKey(key))
            {
                return TTSOptions[key];
            }
            return null;
        }
    }
}

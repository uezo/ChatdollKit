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
        public TTSConfiguration TTSConfig { get; set; }
        public VoiceSource Source { get; set; }
        public bool UseCache { get; set; }
        public string Description { get; set; }
        public string CacheKey
        {
            get
            {
                return Source == VoiceSource.Web ? Url : Text;
            }
        }

        public Voice(string name, float preGap, float postGap, string text, string url, TTSConfiguration ttsConfig, VoiceSource source, bool useCache, string description)
        {
            Name = name;
            PreGap = preGap;
            PostGap = postGap;
            Text = text;
            Url = url;
            TTSConfig = ttsConfig;
            Source = source;
            UseCache = useCache;
            Description = description;
        }

        public object GetTTSParam(string key)
        {
            if (TTSConfig != null)
            {
                return TTSConfig.GetParam(key);
            }
            return null;
        }

        public string GetTTSFunctionName()
        {
            if (TTSConfig != null)
            {
                return TTSConfig.TTSFunctionName;
            }
            else
            {
                return string.Empty;
            }
        }
    }

    public class TTSConfiguration
    {
        public string TTSFunctionName { get; set; }
        public Dictionary<string, object> Params { get; }

        public TTSConfiguration()
        {
            Params = new Dictionary<string, object>();
        }

        public TTSConfiguration(string ttsFunctionName = null)
        {
            TTSFunctionName = ttsFunctionName ?? string.Empty;
            Params = new Dictionary<string, object>();
        }

        public object GetParam(string key)
        {
            if (Params.ContainsKey(key))
            {
                return Params[key];
            }
            else
            {
                return null;
            }
        }
    }
}

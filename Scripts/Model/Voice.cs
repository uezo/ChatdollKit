using System.Collections.Generic;

namespace ChatdollKit.Model
{
    // Voice
    public class Voice
    {
        public string Text { get; set; }
        public float PreGap { get; set; }
        public float PostGap { get; set; }
        public TTSConfiguration TTSConfig { get; set; }
        public bool UseCache { get; set; }
        public string Description { get; set; }
        public string CacheKey
        {
            get
            {
                if (GetTTSParam("style") != null)
                {
                    return $"[{GetTTSParam("style")}]{Text}";
                }
                else
                {
                    return Text;
                }
            }
        }

        public Voice(string text, float preGap, float postGap, TTSConfiguration ttsConfig, bool useCache, string description)
        {
            PreGap = preGap;
            PostGap = postGap;
            Text = text;
            TTSConfig = ttsConfig;
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

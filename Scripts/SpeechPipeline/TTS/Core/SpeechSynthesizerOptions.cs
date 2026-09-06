using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.SpeechPipeline.TTS
{
    public class SpeechSynthesizerOptions
    {
        public int? SampleRate { get; set; }
        [JsonIgnore] public double TimeoutSeconds { get; set; } = 10;
        public Dictionary<string, string> StyleMapper { get; set; } = new Dictionary<string, string>();
        [JsonIgnore] public string CacheDirectory { get; set; }
        [JsonIgnore] public string CacheExtension { get; set; } = "wav";
        [JsonIgnore] public ITtsPreprocessor[] Preprocessors { get; set; } = Array.Empty<ITtsPreprocessor>();
        [JsonIgnore] public ITtsPostprocessor[] Postprocessors { get; set; } = Array.Empty<ITtsPostprocessor>();

        public virtual SpeechSynthesizerOptions Copy()
        {
            var copy = (SpeechSynthesizerOptions)MemberwiseClone();
            copy.StyleMapper = StyleMapper == null ? new Dictionary<string, string>() : new Dictionary<string, string>(StyleMapper);
            copy.Preprocessors = Preprocessors == null ? Array.Empty<ITtsPreprocessor>() : (ITtsPreprocessor[])Preprocessors.Clone();
            copy.Postprocessors = Postprocessors == null ? Array.Empty<ITtsPostprocessor>() : (ITtsPostprocessor[])Postprocessors.Clone();
            return copy;
        }

        /// <summary>Used only as input to the cache-key hash. Never log this object: provider options can contain credentials.
        /// Custom option types must include every setting affecting synthesis.</summary>
        public virtual JObject GetCacheConfiguration() => JObject.FromObject(this);

        public virtual void Validate()
        {
            if (SampleRate.HasValue && (SampleRate.Value < 1 || SampleRate.Value > 768000))
                throw new ArgumentOutOfRangeException(nameof(SampleRate));
            if (double.IsNaN(TimeoutSeconds) || double.IsInfinity(TimeoutSeconds) || TimeoutSeconds <= 0 || TimeoutSeconds > int.MaxValue / 1000.0)
                throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds));
            if (string.IsNullOrEmpty(CacheExtension)) throw new ArgumentException("A cache extension is required.");
            foreach (var c in CacheExtension)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_' && c != '-')
                    throw new ArgumentException("Use an extension without dots or path separators.", nameof(CacheExtension));
            foreach (var item in StyleMapper ?? new Dictionary<string, string>())
                if (string.IsNullOrEmpty(item.Key) || item.Value == null) throw new ArgumentException("Style mappings require a nonempty key and nonnull value.");
            foreach (var item in Preprocessors ?? Array.Empty<ITtsPreprocessor>())
                if (item == null) throw new ArgumentException("Preprocessors cannot contain null.");
            foreach (var item in Postprocessors ?? Array.Empty<ITtsPostprocessor>())
                if (item == null) throw new ArgumentException("Postprocessors cannot contain null.");
        }
    }

    internal static class SpeechSynthesisValidation
    {
        internal static void ApiKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("An API key without line breaks is required.");
        }
        internal static void HttpUrl(string value, bool allowHttp = false, bool allowQuery = false)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && !(uri.Scheme == "http" && (allowHttp || uri.IsLoopback))) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || (!allowQuery && !string.IsNullOrEmpty(uri.Query)))
                throw new ArgumentException("Use an absolute HTTP(S) endpoint without user info or fragment; HTTPS is required except for explicitly supported local services.");
        }
    }
}

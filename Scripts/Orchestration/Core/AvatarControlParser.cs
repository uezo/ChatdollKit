using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using ChatdollKit.Avatar;
using Newtonsoft.Json.Linq;

namespace ChatdollKit.Orchestration
{
    /// <summary>Extracts presentation controls without rewriting display text or VoiceText.
    /// A server pipeline implementation can supply normalized control_tags or avatar_control_request in metadata.</summary>
    public static class AvatarControlParser
    {
        private static readonly Regex Tags = new Regex(@"\[(?<bracket>face|anim|animation):(?<value>[^\]\r\n]+)\]|<(?<xml>face|anim|animation)\b(?<attributes>[^<>]*)>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        private static readonly Regex Attributes = new Regex(@"\G\s*(?<key>[A-Za-z_][\w-]*)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)')",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

        public static IReadOnlyList<AvatarControl> Parse(string text, JObject metadata = null)
        {
            var controls = new List<AvatarControl>();
            if (metadata?["control_tags"] is JArray supplied)
            {
                foreach (var item in supplied)
                    if (item is JObject tag && tag["attributes"] is JObject attributes)
                        Add(controls, String(tag["name"]), String(attributes["name"]), attributes["duration"]);
                if (controls.Count > 0) return controls.AsReadOnly();
            }
            if (metadata?["avatar_control_request"] is JObject legacy)
            {
                Add(controls, "face", String(legacy["face_name"]), legacy["face_duration"]);
                Add(controls, "animation", String(legacy["animation_name"]), legacy["animation_duration"]);
                if (controls.Count > 0) return controls.AsReadOnly();
            }
            foreach (Match match in Tags.Matches(text ?? string.Empty))
            {
                if (match.Groups["bracket"].Success)
                {
                    Add(controls, match.Groups["bracket"].Value, match.Groups["value"].Value.Trim(), null);
                    continue;
                }
                var attributes = ParseAttributes(match.Groups["attributes"].Value);
                if (attributes != null)
                    Add(controls, match.Groups["xml"].Value, String(attributes["name"]), attributes["duration"]);
            }
            return controls.AsReadOnly();
        }

        private static JObject ParseAttributes(string source)
        {
            source = source.Trim();
            if (source.EndsWith("/", StringComparison.Ordinal)) source = source.Substring(0, source.Length - 1).TrimEnd();
            var attributes = new JObject();
            var position = 0;
            while (position < source.Length)
            {
                var match = Attributes.Match(source, position);
                if (!match.Success || match.Index != position) return null;
                var key = match.Groups["key"].Value.ToLowerInvariant();
                if (attributes.Property(key) != null) return null;
                attributes[key] = match.Groups["value"].Value;
                position = match.Index + match.Length;
                if (string.IsNullOrWhiteSpace(source.Substring(position))) break;
            }
            return attributes;
        }

        private static void Add(List<AvatarControl> controls, string tag, string name, JToken durationToken)
        {
            AvatarControlKind kind;
            if (string.Equals(tag, "face", StringComparison.OrdinalIgnoreCase)) kind = AvatarControlKind.Face;
            else if (string.Equals(tag, "anim", StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "animation", StringComparison.OrdinalIgnoreCase)) kind = AvatarControlKind.Animation;
            else return;
            if (string.IsNullOrWhiteSpace(name)) return;
            double? duration = null;
            if (durationToken != null && durationToken.Type != JTokenType.Null)
            {
                if (durationToken.Type != JTokenType.String && durationToken.Type != JTokenType.Integer && durationToken.Type != JTokenType.Float) return;
                if (!double.TryParse(durationToken.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                    double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > float.MaxValue) return;
                duration = value;
            }
            controls.Add(new AvatarControl(kind, name.Trim(), duration));
        }
        private static string String(JToken value) => value?.Type == JTokenType.String ? (string)value : null;
    }
}

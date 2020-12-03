using System.Collections.Generic;


namespace ChatdollKit.Model
{
    // Request for voice
    public class VoiceRequest
    {
        public List<Voice> Voices { get; set; }
        public bool DisableBlink { get; set; }

        public VoiceRequest(List<Voice> voices = null, bool disableBlink = true)
        {
            Voices = voices ?? new List<Voice>();
            DisableBlink = disableBlink;
        }

        public VoiceRequest(params string[] voiceNames) : this()
        {
            foreach (var v in voiceNames)
            {
                AddVoice(v);
            }
        }

        public void AddVoice(string name, float preGap = 0.0f, float postGap = 0.0f, string text = null, string url = null, TTSConfiguration ttsConfig = null, VoiceSource source = VoiceSource.Local, string description = null)
        {
            Voices.Add(new Voice(name, preGap, postGap, text ?? string.Empty, url ?? string.Empty, ttsConfig, source, false, description));
        }

        public void AddVoiceWeb(string url, float preGap = 0.0f, float postGap = 0.0f, string name = null, string text = null, bool useCache = true, string description = null)
        {
            Voices.Add(new Voice(name ?? string.Empty, preGap, postGap, text ?? string.Empty, url ?? string.Empty, null, VoiceSource.Web, useCache, description));
        }

        public void AddVoiceTTS(string text, float preGap = 0.0f, float postGap = 0.0f, string name = null, TTSConfiguration ttsConfig = null, bool useCache = true, string description = null)
        {
            Voices.Add(new Voice(name ?? string.Empty, preGap, postGap, text, string.Empty, ttsConfig, VoiceSource.TTS, useCache, description));
        }
    }
}

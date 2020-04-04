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

        public void AddVoice(string name, float preGap = 0.0f, float postGap = 0.0f, string text = null, string url = null, Dictionary<string, string> ttsOptions = null, VoiceSource source = VoiceSource.Local)
        {
            Voices.Add(new Voice(name, preGap, postGap, text, url, ttsOptions, source));
        }

        public void AddVoiceWeb(string url, float preGap = 0.0f, float postGap = 0.0f, string name = null, string text = null)
        {
            Voices.Add(new Voice(name ?? string.Empty, preGap, postGap, text, url, null, VoiceSource.Web));
        }

        public void AddVoiceTTS(string text, float preGap = 0.0f, float postGap = 0.0f, string name = null, Dictionary<string, string> ttsOptions = null)
        {
            Voices.Add(new Voice(name ?? string.Empty, preGap, postGap, text, string.Empty, ttsOptions, VoiceSource.TTS));
        }
    }
}

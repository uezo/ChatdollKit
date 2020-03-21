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

        public void AddVoice(string name, float preGap = 0.0f, float postGap = 0.0f)
        {
            Voices.Add(new Voice(name, preGap, postGap));
        }
    }
}

using System;
using System.Collections.Generic;

namespace ChatdollKit.Dialog
{
    public class DialogRequest
    {
        public string Id { get; set; }
        public string ClientId { get; set; }
        public WakeWord WakeWord { get; set; }
        public Dictionary<string, object> Payloads { get; set; } = new Dictionary<string, object>();
        public bool SkipPrompt { get; set; }
        public Dictionary<string, string> Tokens { get; } = new Dictionary<string, string>();

        public DialogRequest(string clientId, WakeWord wakeWord = null, bool skipPrompt = false, Dictionary<string, object> payloads = null)
        {
            Id = Guid.NewGuid().ToString();
            ClientId = clientId;
            WakeWord = wakeWord;
            SkipPrompt = skipPrompt;
            Payloads = payloads ?? Payloads;
        }

        public Request ToRequest()
        {
            if ((WakeWord != null && !string.IsNullOrEmpty(WakeWord.InlineRequestText)) || Payloads.Count > 0)
            {
                return new Request(WakeWord.RequestType)
                {
                    ClientId = ClientId,
                    Text = WakeWord != null ? WakeWord.InlineRequestText : null,
                    Payloads = Payloads,
                    Intent = WakeWord != null ? new Intent(WakeWord.Intent, WakeWord.IntentPriority) : null,
                    Tokens = Tokens
                };
            }

            return null;
        }
    }
}

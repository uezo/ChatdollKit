using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ChatdollKit.Model;

namespace ChatdollKit.Dialog
{
    public class Response
    {
        public string Id { get; }
        public DateTime Timestamp { get; }
        public string Text { get; set; }
        public AnimatedVoiceRequest AnimatedVoiceRequest { get; set; }
        public virtual object Payloads { get; set; }

        public Response(string id)
        {
            Id = id;
            Timestamp = DateTime.UtcNow;
            Text = string.Empty;
            AnimatedVoiceRequest = new AnimatedVoiceRequest();
        }
    }
}

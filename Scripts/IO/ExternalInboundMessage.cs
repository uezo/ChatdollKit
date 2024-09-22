using System.Collections.Generic;

namespace ChatdollKit.IO
{
    public class ExternalInboundMessage
    {
        public string Endpoint { get; set; }
        public string Operation { get; set; }
        public int Priority { get; set; }
        public string Text { get; set; }
        public Dictionary<string, object> Payloads { get; set; }
    }
}

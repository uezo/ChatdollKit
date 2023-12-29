using System;
using System.Collections.Generic;

namespace ChatdollKit.Dialog
{
    public enum RequestType
    {
        None, Voice, Camera, QRCode
    }

    public class Request
    {
        public string Id { get; }
        public RequestType Type { get; }
        public DateTime CreatedAt { get; }
        public string Text { get; set; }
        public Dictionary<string, object> Payloads { get; set; }
        public Intent Intent { get; set; }
        public Dictionary<string, object> Entities { get; set; }
        public bool IsCanceled { get; set; }
        public string ClientId { get; set; }
        public Dictionary<string, string> Tokens { get; set; } = new Dictionary<string, string>();

        public Request(RequestType type, string text = null, bool isCanceled = false)
        {
            Id = Guid.NewGuid().ToString();
            Type = type;
            CreatedAt = DateTime.UtcNow;
            Text = text ?? string.Empty;
            Payloads = new Dictionary<string, object>();
            Intent = null;
            Entities = new Dictionary<string, object>();
            IsCanceled = isCanceled;
        }

        public bool IsSet()
        {
            if (Type == RequestType.Voice)
            {
                return !string.IsNullOrEmpty(Text);
            }
            else
            {
                return Payloads.Count > 0;
            }
        }

        public bool HasIntent()
        {
            return Intent != null && !string.IsNullOrEmpty(Intent.Name);
        }
    }
}

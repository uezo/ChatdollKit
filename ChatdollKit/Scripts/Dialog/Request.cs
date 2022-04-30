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
        public User User { get; set; }
        public string Text { get; set; }
        public List<object> Payloads { get; set; }
        public Intent Intent { get; set; }
        public Dictionary<string, object> Entities { get; set; }
        public List<WordNode> Words { get; set; }
        public bool IsCanceled { get; set; }

        public Request(RequestType type)
        {
            Id = Guid.NewGuid().ToString();
            Type = type;
            CreatedAt = DateTime.UtcNow;
            User = null;
            Text = string.Empty;
            Payloads = new List<object>();
            Intent = null;
            Entities = new Dictionary<string, object>();
            IsCanceled = false;
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

        public void SetExtractionResult(IntentExtractionResult result)
        {
            Intent = result.Intent;
            Entities = result.Entities;
            Words = result.Words;
        }
    }
}

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
        public DateTime Timestamp { get; }
        public User User { get; set; }
        public string Text { get; set; }
        public object Payloads { get; set; }
        public Intent Intent { get; set; }
        public Dictionary<string, object> Entities { get; set; }
        public List<WordNode> Words { get; set; }
        public bool IsCanceled { get; set; }

        public Request(RequestType type)
        {
            Id = Guid.NewGuid().ToString();
            Type = type;
            Timestamp = DateTime.UtcNow;
            User = null;
            Text = string.Empty;
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
                return Payloads != null;
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

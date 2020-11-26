using System;
using System.Collections.Generic;


namespace ChatdollKit.Dialog
{
    public enum RequestType
    {
        Voice, Camera, QRCode
    }

    public enum Priority
    {
        Lowest = 0, Low = 25, Normal = 50, High = 75, Highest = 100
    }

    public class Request
    {
        public string Id { get; }
        public RequestType Type { get; }
        public DateTime Timestamp { get; }
        public User User { get; set; }
        public string Text { get; set; }
        public object Payloads { get; set; }
        public string Intent { get; set; }
        public Priority IntentPriority { get; set; }
        public Dictionary<string, object> Entities { get; set; }
        public List<WordNode> Words { get; set; }
        public bool IsAdhoc { get; set; }
        public bool IsCanceled { get; set; }

        public Request(RequestType type)
        {
            Id = Guid.NewGuid().ToString();
            Type = type;
            Timestamp = DateTime.UtcNow;
            User = null;
            Text = string.Empty;
            IntentPriority = Priority.Normal;
            Entities = new Dictionary<string, object>();
            IsAdhoc = false;
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
    }

    public class WordNode
    {
        public string Word { get; set; }
        public string Part { get; set; }
        public string PartDetail1 { get; set; }
        public string PartDetail2 { get; set; }
        public string PartDetail3 { get; set; }
        public string StemType { get; set; }
        public string StemForm { get; set; }
        public string OriginalForm { get; set; }
        public string Kana { get; set; }
        public string Pronunciation { get; set; }
    }

}

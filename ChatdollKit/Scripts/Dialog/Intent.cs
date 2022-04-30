using System.Collections.Generic;
using Newtonsoft.Json;

namespace ChatdollKit.Dialog
{
    public class IntentExtractionResult
    {
        public Intent Intent { get; set; }
        public Dictionary<string, object> Entities { get; set; }
        public List<WordNode> Words { get; set; }

        [JsonConstructor]
        public IntentExtractionResult(Intent intent = null, Dictionary<string, object> entities = null, List<WordNode> words = null)
        {
            Intent = intent;
            Entities = entities ?? new Dictionary<string, object>();
            Words = words ?? new List<WordNode>();
        }

        public IntentExtractionResult(string intentName, Priority intentPriority = Priority.Normal) : this(new Intent(intentName, intentPriority))
        {
        }
    }

    public class Intent
    {
        public string Name { get; set; }
        public Priority Priority { get; set; }
        public bool IsAdhoc { get; set; }

        public Intent(string name, Priority priority = Priority.Normal, bool isAdhoc = false)
        {
            Name = name;
            Priority = priority;
            IsAdhoc = isAdhoc;
        }
    }
}

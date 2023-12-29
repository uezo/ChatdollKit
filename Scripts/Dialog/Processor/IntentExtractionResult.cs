using System.Collections.Generic;

namespace ChatdollKit.Dialog.Processor
{
    public class IntentExtractionResult
    {
        public Intent Intent { get; set; }
        public Dictionary<string, object> Entities { get; set; }

        public IntentExtractionResult(Intent intent = null, Dictionary<string, object> entities = null)
        {
            Intent = intent;
            Entities = entities ?? new Dictionary<string, object>();
        }

        public IntentExtractionResult(string intentName, Priority intentPriority = Priority.Normal) : this(new Intent(intentName, intentPriority))
        {
        }
    }
}

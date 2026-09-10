namespace ChatdollKit.UI.MessageWindow
{
    public enum MessageSpeaker
    {
        User,
        Assistant,
        Status
    }

    /// <summary>A cumulative text snapshot. Reuse the ID when appending to a message.</summary>
    public sealed class MessageWindowMessage
    {
        public string MessageId { get; set; }
        public MessageSpeaker Speaker { get; set; }
        public string SpeakerName { get; set; }
        public string Text { get; set; }
        public bool Animate { get; set; } = true;
        /// <summary>Source-defined display priority. A shared window chooses the newest eligible message.</summary>
        public long Order { get; set; }
        /// <summary>Allows local auto-hide after typing finishes. Generation Final alone does not complete AI output.</summary>
        public bool IsComplete { get; set; } = true;
        public bool IsPartial { get; set; }
        public bool IsAwaitingRecognition { get; set; }

        public MessageWindowMessage Copy()
        {
            return new MessageWindowMessage
            {
                MessageId = MessageId,
                Speaker = Speaker,
                SpeakerName = SpeakerName,
                Text = Text,
                Animate = Animate,
                Order = Order,
                IsComplete = IsComplete,
                IsPartial = IsPartial,
                IsAwaitingRecognition = IsAwaitingRecognition
            };
        }
    }
}

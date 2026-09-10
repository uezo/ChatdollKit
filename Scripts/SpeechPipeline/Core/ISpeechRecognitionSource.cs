using System;

namespace ChatdollKit.SpeechPipeline
{
    /// <summary>Optional recognition notifications, independent of response generation and UI.</summary>
    public interface ISpeechRecognitionSource
    {
        /// <summary>Started may precede text. Partial text is a cumulative replacement.
        /// Terminal updates close the recognition ID.</summary>
        event Action<SpeechRecognitionUpdate> RecognitionUpdated;
    }
}

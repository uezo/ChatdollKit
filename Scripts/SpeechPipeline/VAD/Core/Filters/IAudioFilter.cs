namespace ChatdollKit.SpeechPipeline.VAD.Filters
{
    /// <summary>Transforms PCM16 audio before VAD, recording, and recognition.</summary>
    public interface IAudioFilter
    {
        // Lookahead filters may return an empty array while accumulating input.
        byte[] Process(byte[] pcm, string sessionId);
        void ResetSession(string sessionId);
    }
}

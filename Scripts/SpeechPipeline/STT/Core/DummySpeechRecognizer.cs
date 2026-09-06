using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.STT
{
    /// <summary>A recognizer for local examples and deterministic tests; never calls a service.</summary>
    public sealed class DummySpeechRecognizer : ISpeechRecognizer
    {
        private readonly Func<string, byte[], CancellationToken, UniTask<SpeechRecognitionResult>> handler;

        public string RecognizedText { get; set; }

        public DummySpeechRecognizer(
            string recognizedText = null,
            Func<string, byte[], CancellationToken, UniTask<SpeechRecognitionResult>> handler = null)
        {
            RecognizedText = recognizedText;
            this.handler = handler;
        }

        public UniTask<SpeechRecognitionResult> RecognizeAsync(
            string sessionId, byte[] audio, CancellationToken cancellationToken = default)
        {
            if (audio == null) throw new ArgumentNullException(nameof(audio));
            cancellationToken.ThrowIfCancellationRequested();
            return handler != null
                ? handler(sessionId, audio, cancellationToken)
                : UniTask.FromResult(new SpeechRecognitionResult { Text = RecognizedText });
        }
    }
}

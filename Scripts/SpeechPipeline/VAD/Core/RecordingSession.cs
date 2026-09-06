using System;
using System.Collections.Generic;

namespace ChatdollKit.SpeechPipeline.VAD
{
    public class RecordingSession
    {
        internal readonly object SyncRoot = new object();
        internal readonly List<byte> Buffer = new List<byte>();
        internal readonly Queue<byte[]> PrerollBuffer = new Queue<byte[]>();
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        internal readonly int PrerollBufferCount;
        public string SessionId { get; }
        public bool IsRecording { get; internal set; }
        public double RecordDuration { get; internal set; }
        public double SilenceDuration { get; internal set; }
        public string LastRecognizedText { get; internal set; }
        public bool RecordingStartedTriggered { get; internal set; }
        internal double? AmplitudeThreshold;

        public RecordingSession(string sessionId, int prerollBufferCount = 5)
        {
            SessionId = sessionId;
            PrerollBufferCount = prerollBufferCount;
        }

        /// <summary>Safe read access for callbacks and injected providers without re-entering the detector.</summary>
        public object GetData(string key)
        {
            lock (SyncRoot) return Data.TryGetValue(key, out var value) ? value : null;
        }

        internal void AppendPreroll(byte[] samples)
        {
            if (PrerollBufferCount == 0) return;
            PrerollBuffer.Enqueue(samples);
            while (PrerollBuffer.Count > PrerollBufferCount) PrerollBuffer.Dequeue();
        }

        protected internal virtual void Reset()
        {
            Buffer.Clear();
            IsRecording = false;
            RecordDuration = SilenceDuration = 0;
            LastRecognizedText = null;
            RecordingStartedTriggered = false;
        }
    }


}

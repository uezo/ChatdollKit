using UnityEngine;
using UnityEngine.Scripting;

namespace ChatdollKit.SpeechPipeline.VAD.Silero.WebGL
{
    /// <summary>Runtime-created recipient of Silero inference callbacks; no microphone access.</summary>
    [AddComponentMenu("")]
    [Preserve]
    public sealed class SileroVadWebGLBridge : MonoBehaviour
    {
        internal WebGLSileroVadModel Model;
        [Preserve] public void OnSileroReady(string unused) => Model?.OnReady();
        [Preserve] public void OnSileroResult(string json) => Model?.OnResult(json);
        [Preserve] public void OnSileroError(string json) => Model?.OnError(json);
        private void OnDestroy() { Model?.Dispose(); Model = null; }
    }
}

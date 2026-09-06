using UnityEngine;
using UnityEngine.Scripting;

namespace ChatdollKit.SpeechPipeline.Remote
{
    /// <summary>Runtime-only recipient of browser WebSocket callbacks.</summary>
    [AddComponentMenu("")]
    [Preserve]
    public sealed class AIAvatarWebSocketBridge : MonoBehaviour
    {
        internal WebGLAIAvatarConnection Connection;
        [Preserve] public void OnWebSocketOpen(string unused) => Connection?.OnOpen();
        [Preserve] public void OnWebSocketMessage(string message) => Connection?.OnMessage(message);
        [Preserve] public void OnWebSocketError(string unused) => Connection?.OnFailure("The browser WebSocket failed. Check the endpoint, authentication and browser network console.");
        [Preserve] public void OnWebSocketClose(string unused) => Connection?.OnClose();
        private void OnDestroy() { Connection?.Abort(); Connection = null; }
    }
}

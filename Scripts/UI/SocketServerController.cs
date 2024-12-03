using UnityEngine;
using UnityEngine.UI;
using ChatdollKit.Network;

namespace ChatdollKit.UI
{
    public class SocketServerController : MonoBehaviour
    {
        [SerializeField]
        private InputField serverPortInput;
        [SerializeField]
        private Text serverStatusText;

        private SocketServer socketServer;

        private void Start()
        {
            socketServer = gameObject.GetComponent<SocketServer>();
        }

#if !UNITY_WEBGL
        private void Update()
        {
            serverStatusText.text = socketServer.IsRunning ? $"Listening: {socketServer.Port}" : "Stopped";
        }

        public void OnServerToggleButton()
        {
            if (socketServer.IsRunning)
            {
                // Stop server
                socketServer.StopServer();
            }
            else
            {
                // Start server
                var portStr = serverPortInput.text;
                if (string.IsNullOrEmpty(portStr))
                {
                    portStr = "8888";
                }

                var port = int.Parse(portStr);
                if (port < 1024 || port > 65535)
                {
                    Debug.LogError("Port number should be in range 1024 - 65535");
                    return;
                }

                socketServer.StartServer(port);
            }
        }
#endif
    }
}

using System;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace ChatdollKit.Network
{
    public class SocketClient : MonoBehaviour
    {
        private TcpClient client;
        private NetworkStream stream;
        [SerializeField]
        private string address = "127.0.0.1";
        public string Address { get { return address; } }
        [SerializeField]
        private int port = 0;
        public int Port { get { return port; } }

        public bool IsConnected { get { return client != null && client.Connected; }}

        public void Connect(string address = null, int port = 0)
        {
            if (!string.IsNullOrEmpty(address))
            {
                this.address = address;
            }

            if (port > 0)
            {
                this.port = port;
            }

            try
            {
                client = new TcpClient(Address, Port);
                stream = client.GetStream();
                Debug.Log($"Connected to server: {Address}:{Port}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to connect to server: {ex.Message}");
            }
        }

        public void SendMessageToServer(string message)
        {
            if (client == null || !client.Connected)
            {
                Debug.LogError("Not connected to the server.");
                return;
            }

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                stream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to send message: {ex.Message}");
            }
        }

        void OnApplicationQuit()
        {
            Disconnect();
        }

        public void Disconnect()
        {
            if (stream != null) stream.Close();
            if (client != null) client.Close();
            Debug.Log("Disconnected from server.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using ChatdollKit.IO;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Network
{
    public class SocketServer : MonoBehaviour, IExternalInboundMessageHandler
    {
        public Func<ExternalInboundMessage, UniTask> OnDataReceived { get; set; }

#if !UNITY_WEBGL
        [SerializeField]
        private int port;
        public int Port { get { return port; } }
        [SerializeField]
        private bool autoStart = true;
        [SerializeField]
        private bool isDebug = false;

        private TcpListener server;
        private Thread serverThread;
        private Queue<ExternalInboundMessage> messageQueue = new Queue<ExternalInboundMessage>();
        private object queueLock = new object();
        public bool IsRunning { get; private set; }

        private void Start()
        {
            if (autoStart)
            {
                StartServer();
            }
        }

        private void Update()
        {
            lock (queueLock)
            {
                while (messageQueue.Count > 0)
                {
                    var message = messageQueue.Dequeue();
                    // Execute in main thread
                    OnDataReceived?.Invoke(message);
                }
            }
        }

        public void StartServer(int port = 0)
        {
            if (IsRunning || (serverThread != null && serverThread.IsAlive))
            {
                Debug.LogWarning("Server is already running or thread is alive.");
                return;
            }

            if (server != null)
            {
                try
                {
                    server.Stop();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Error stopping an existing server instance: {ex.Message}");
                }
                server = null;
            }

            if (port > 0)
            {
                this.port = port;
            }

            serverThread = new Thread(new ThreadStart(StartServerInThread));
            serverThread.IsBackground = true;
            serverThread.Start();
        }

        private void StartServerInThread()
        {
            server = new TcpListener(IPAddress.Any, port);
            server.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);  // Throw error if port is already in use
            server.Start();
            IsRunning = true;
            Debug.Log($"Server started on port {port}");

            try
            {
                while (IsRunning)
                {
                    // Wait for connection from client
                    var client = server.AcceptTcpClient();
                    if (isDebug)
                    {
                        Debug.Log($"Client connected.");
                    }

                    // Start thread for processing client data
                    var clientThread = new Thread(() => HandleClient(client));
                    clientThread.IsBackground = true;
                    clientThread.Start();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Server encountered an error: {ex.Message}");
            }
            finally
            {
                server.Stop();
                Debug.Log("Server stopped in thread.");
            }
        }

        public void StopServer()
        {
            IsRunning = false;

            if (server != null)
            {
                try
                {
                    server.Stop();
                    Debug.Log("Server stopped.");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error stopping the server: {ex.Message}");
                }
            }

            if (serverThread != null && serverThread.IsAlive)
            {
                try
                {
                    serverThread.Join(1000); // Wait for thread to stop
                    if (serverThread.IsAlive)
                    {
                        serverThread.Abort(); // Force stop if not stopped
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error stopping the server thread: {ex.Message}");
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.UTF8);

            try
            {
                // Receive data
                string message;
                while ((message = reader.ReadLine()) != null)
                {
                    if (isDebug)
                    {
                        Debug.Log($"Received from client: {message}");
                    }

                    try
                    {
                        var request = JsonConvert.DeserializeObject<ExternalInboundMessage>(message);
                        lock (queueLock)
                        {
                            // Just enqueue to process OnDataReceived in main thread
                            messageQueue.Enqueue(request);
                        }
                    }
                    catch (Exception jex)
                    {
                        Debug.LogError($"Error while parsing message: {jex.Message}: {message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error while reading from client: {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }

        private void OnApplicationQuit()
        {
            StopServer();
        }
#endif
    }
}

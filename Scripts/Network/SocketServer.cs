using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;

namespace ChatdollKit.Network
{
#if !UNITY_WEBGL
    public class SocketServer : MonoBehaviour
    {
        [SerializeField]
        private int port;
        [SerializeField]
        private bool isDebug = false;

        public Action<SocketServerRequest> OnDataReceived;

        private TcpListener server;
        private Thread serverThread;
        private Queue<SocketServerRequest> messageQueue = new Queue<SocketServerRequest>();
        private object queueLock = new object();
        public bool IsRunning { get; private set; }

        private void Start()
        {
            serverThread = new Thread(new ThreadStart(StartServer));
            serverThread.IsBackground = true;
            serverThread.Start();
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

        private void StartServer()
        {
            server = new TcpListener(IPAddress.Any, port);
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
                Debug.Log("Server stopped.");
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

                    SocketServerRequest request;
                    try
                    {
                        request = JsonConvert.DeserializeObject<SocketServerRequest>(message);
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
            IsRunning = false;
            if (server != null)
            {
                server.Stop();
            }
            if (serverThread != null && serverThread.IsAlive)
            {
                serverThread.Abort();
            }
        }
    }
#endif

    public class SocketServerRequest
    {
        public string Endpoint { get; set; }
        public string Operation { get; set; }
        public int Priority { get; set; }
        public string Text { get; set; }
        public Dictionary<string, object> Payloads { get; set; }
    }
}

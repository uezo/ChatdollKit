using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using ChatdollKit.Network;

namespace ChatdollKit.SpeechListener
{
    public class AIAvatarKitStreamSpeechListener : MonoBehaviour, ISpeechListener
    {
        public bool _IsEnabled = true;
        public virtual bool IsEnabled
        {
            get => _IsEnabled;
            set => _IsEnabled = value;
        }

        [Header("Voice Recorder Settings")]
        public bool AutoStart = true;
        public bool PrintResult = false;

        [Header("WebSocket Settings")]
        public string WebSocketUrl = "ws://localhost:8000/ws/stt";
        public int SamplesPerMessage = 512;
        public int TargetSampleRate = 16000;

        // Recognizer
        public Func<string, UniTask> OnRecognized { get; set; }
        public Action<string> OnPartialRecognized { get; set; }
        public Action OnVoiced { get; set; }
        public bool IsRecording { get; private set; }
        public bool IsVoiceDetected { get; private set; }
        // MicrophoneManager
        protected MicrophoneManager microphoneManager;

        // WebSocket
        private IWebSocketClient webSocketAdapter;
        private string sessionId;
        private bool isConnected;
        private float lastVoicedTime;

        [Header("UI Settings")]
        [SerializeField]
        private float voiceDetectedTimeout = 0.5f;

        // Sample buffer for batching
        private List<float> sampleBuffer = new List<float>();

        public void StartListening(bool stopBeforeStart = false)
        {
            _ = StartRecognizerAsync(stopBeforeStart);
        }

        public void StopListening()
        {
            _ = StopRecognizerAsync();
        }

        public void ResetSession()
        {
            _ = ResetSessionAsync();
        }

        private async UniTask ResetSessionAsync()
        {
            await StopRecognizerAsync();
            sessionId = null;
        }

        private void Start()
        {
            microphoneManager = GetComponent<MicrophoneManager>();

            if (AutoStart && IsEnabled)
            {
                StartListening();
            }
        }

        private void Update()
        {
            if (IsVoiceDetected && Time.time - lastVoicedTime > voiceDetectedTimeout)
            {
                IsVoiceDetected = false;
            }
        }

        protected void OnDestroy()
        {
            _ = StopRecognizerAsync();
        }

        private void OnSamplesReceived(float[] samples)
        {
            if (!IsRecording || !isConnected) return;

            // Add samples to buffer
            sampleBuffer.AddRange(samples);

            // Send when buffer has enough samples
            while (sampleBuffer.Count >= SamplesPerMessage)
            {
                var samplesToSend = new float[SamplesPerMessage];
                sampleBuffer.CopyTo(0, samplesToSend, 0, SamplesPerMessage);
                sampleBuffer.RemoveRange(0, SamplesPerMessage);

                _ = SendAudioDataAsync(samplesToSend);
            }
        }

        private async UniTask StartRecognizerAsync(bool stopBeforeStart)
        {
            if (IsRecording && stopBeforeStart)
            {
                await StopRecognizerAsync();
            }

            if (IsRecording) return;

            if (microphoneManager == null)
            {
                Debug.LogError("MicrophoneManager is not found");
                return;
            }

            // Generate session ID if not exists
            if (string.IsNullOrEmpty(sessionId))
            {
                sessionId = Guid.NewGuid().ToString();
            }

            // Connect WebSocket
            try
            {
                webSocketAdapter = new WebSocketClient();
                webSocketAdapter.OnMessage += HandleMessage;
                await webSocketAdapter.ConnectAsync(WebSocketUrl, CancellationToken.None);
                Debug.Log($"WebSocket connected to {WebSocketUrl}");

                // Send start message
                await SendJsonAsync(new { type = "start", session_id = sessionId });
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to connect WebSocket: {ex.Message}");
                return;
            }

            // Register to MicrophoneManager
            sampleBuffer.Clear();
            microphoneManager.OnSamplesReceived += OnSamplesReceived;
            IsRecording = true;

            Debug.Log("WebSocket Speech Listener started");
        }

        private async UniTask StopRecognizerAsync()
        {
            if (!IsRecording && webSocketAdapter == null) return;

            IsRecording = false;
            isConnected = false;

            // Unregister from MicrophoneManager
            if (microphoneManager != null)
            {
                microphoneManager.OnSamplesReceived -= OnSamplesReceived;
            }
            sampleBuffer.Clear();

            // Close WebSocket
            if (webSocketAdapter != null)
            {
                try
                {
                    if (webSocketAdapter.IsConnected)
                    {
                        // Send stop message before closing
                        await SendJsonAsync(new { type = "stop", session_id = sessionId });
                    }
                    await webSocketAdapter.CloseAsync();
                    webSocketAdapter.Dispose();
                    Debug.Log("WebSocket Speech Listener stopped");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Error closing WebSocket: {ex.Message}");
                }
                finally
                {
                    webSocketAdapter = null;
                }
            }
        }

        private async UniTask SendJsonAsync(object message)
        {
            if (webSocketAdapter == null || !webSocketAdapter.IsConnected) return;

            try
            {
                var json = JsonConvert.SerializeObject(message);
                await webSocketAdapter.SendTextAsync(json, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error sending message: {ex.Message}");
            }
        }

        private float[] Resample(float[] samples, int originalRate, int targetRate)
        {
            if (originalRate == targetRate) return samples;

            int dstLength = Mathf.CeilToInt(samples.Length * (targetRate / (float)originalRate));
            var dst = new float[dstLength];
            float ratio = samples.Length / (float)dstLength;

            for (int i = 0; i < dstLength; i++)
            {
                float srcIndex = i * ratio;
                int i0 = Mathf.FloorToInt(srcIndex);
                int i1 = Mathf.Min(i0 + 1, samples.Length - 1);
                float t = srcIndex - i0;
                dst[i] = Mathf.Lerp(samples[i0], samples[i1], t);
            }
            return dst;
        }

        private async UniTask SendAudioDataAsync(float[] samples)
        {
            if (webSocketAdapter == null || !webSocketAdapter.IsConnected || !isConnected) return;

            try
            {
                // Resample if target sample rate is specified
                var samplesToSend = samples;
                if (TargetSampleRate > 0 && microphoneManager.SampleRate != TargetSampleRate)
                {
                    samplesToSend = Resample(samples, microphoneManager.SampleRate, TargetSampleRate);
                }

                // Convert float samples to 16-bit PCM
                var pcmData = new byte[samplesToSend.Length * 2];
                for (var i = 0; i < samplesToSend.Length; i++)
                {
                    var sample = (short)(samplesToSend[i] * 32767f);
                    pcmData[i * 2] = (byte)(sample & 0xFF);
                    pcmData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
                }

                // Create JSON message
                var message = new
                {
                    type = "data",
                    session_id = sessionId,
                    audio_data = Convert.ToBase64String(pcmData)
                };

                var json = JsonConvert.SerializeObject(message);
                await webSocketAdapter.SendTextAsync(json, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error sending audio data: {ex.Message}");
            }
        }

        private void HandleMessage(string data)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<WebSocketMessage>(data);

                switch (msg.type)
                {
                    case "connected":
                        isConnected = true;
                        sessionId = msg.session_id;
                        Debug.Log($"Session connected: {sessionId}");
                        break;

                    case "partial":
                        if (PrintResult)
                        {
                            Debug.Log($"Partial: {msg.text}");
                        }
                        OnPartialRecognized?.Invoke(msg.text);
                        break;

                    case "final":
                        if (PrintResult)
                        {
                            Debug.Log($"Final: {msg.text}");
                        }
                        if (!string.IsNullOrEmpty(msg.text))
                        {
                            OnRecognized?.Invoke(msg.text);
                        }
                        break;

                    case "voiced":
                        IsVoiceDetected = true;
                        lastVoicedTime = Time.time;
                        OnVoiced?.Invoke();
                        break;

                    case "error":
                        Debug.LogError($"Server error: {msg.metadata?.error ?? "Unknown error"}");
                        break;

                    default:
                        Debug.Log($"Unknown message type: {msg.type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error parsing message: {ex.Message}");
            }
        }

        public void ChangeSessionConfig(float silenceDurationThreshold = float.MinValue, float minRecordingDuration = float.MinValue, float maxRecordingDuration = float.MinValue)
        {
            Debug.LogWarning("Session configuration for AIAvatarKitStreamSpeechListener is managed on the server side.");
        }

        [Serializable]
        private class WebSocketMessage
        {
            public string type;
            public string session_id;
            public string text;
            public MessageMetadata metadata;
        }

        [Serializable]
        private class MessageMetadata
        {
            public string error;
        }
    }
}

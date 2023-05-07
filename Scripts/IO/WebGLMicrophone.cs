using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ChatdollKit.IO
{
    public class WebGLMicrophone : MonoBehaviour
    {
#if UNITY_WEBGL
        [DllImport("__Internal")]
        private static extern void InitWebGLMicrophone(string targetObjectName);
        [DllImport("__Internal")]
        private static extern void StartWebGLMicrophone();
        [DllImport("__Internal")]
        private static extern void EndWebGLMicrophone();
        [DllImport("__Internal")]
        private static extern int IsWebGLMicrophoneRecording();

        public static readonly string[] devices = new string[0];
        private static int FIXED_FREQUENCY = 44100;

        private AudioClip microphoneClip;
        private float[] capturedData;
        private bool loop;
        private int currentPosition;

        // Singleton
        private static WebGLMicrophone instance;
        public static WebGLMicrophone Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<WebGLMicrophone>();
                    if (instance == null)
                    {
                        Debug.LogError("WebGLMicrophone not found. Attach WebGLMicrophone to the GameObject you like.");
                    }
                }
                return instance;
            }
        }

        private void Awake()
        {
#if !UNITY_EDITOR
            InitWebGLMicrophone(gameObject.name);
#endif
        }

        public static AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency)
        {
            if (!IsRecording(deviceName))
            {
                Instance.loop = loop;
                Instance.currentPosition = 0;
                var channels = 1;   // デバイスから取得できるのであればするけど・・・
                Instance.capturedData = new float[FIXED_FREQUENCY * lengthSec * channels];
                if (Instance.microphoneClip != null)
                {
                    Destroy(Instance.microphoneClip);
                }
                Instance.microphoneClip = AudioClip.Create("WebGL Microphone", FIXED_FREQUENCY * lengthSec, channels, FIXED_FREQUENCY, false);

                StartWebGLMicrophone();
            }

            return Instance.microphoneClip;
        }


        public static void End(string deviceName)
        {
            if (IsRecording(deviceName))
            {
                EndWebGLMicrophone();
            }
        }

        public static bool IsRecording(string deviceName)
        {
            return IsWebGLMicrophoneRecording() == 1;
        }

        public static void GetDeviceCaps(string deviceName, out int minFreq, out int maxfreq)
        {
            minFreq = FIXED_FREQUENCY;
            maxfreq = FIXED_FREQUENCY;
        }

        public static int GetPosition(string deviceName)
        {
            return Instance.currentPosition;
        }

        private void SetSamplingData(string samplingDataString)
        {
            var samplingData = samplingDataString.Split(',').Select(s => Convert.ToSingle(s)).ToArray();


            for (int i = 0; i < samplingData.Length; i++)
            {
                capturedData[currentPosition] = samplingData[i];
                currentPosition++;
                if (currentPosition == capturedData.Length)
                {
                    if (loop)
                    {
                        currentPosition = 0;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (microphoneClip != null)
            {
                microphoneClip.SetData(capturedData, 0);
            }
        }
#endif
    }
}

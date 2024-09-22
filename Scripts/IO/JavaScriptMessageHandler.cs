using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace ChatdollKit.IO
{
    public class JavaScriptMessageHandler : MonoBehaviour, IExternalInboundMessageHandler
    {
        public Func<ExternalInboundMessage, UniTask> OnDataReceived { get; set; }

#if UNITY_WEBGL
        [DllImport("__Internal")]
        private static extern void InitJSMessageHandler(string targetObjectName, string targetFunctionName);

        [SerializeField]
        private bool captureKeyboardInput = true;
        [SerializeField]
        private bool isDebug;

        public void Start()
        {
#if !UNITY_EDITOR
            if (captureKeyboardInput)
            {
                WebGLInput.captureAllKeyboardInput = false;
            }

            InitJSMessageHandler(gameObject.name, "HandleMessageFromJavaScript");
#endif
        }

        public void HandleMessageFromJavaScript(string message)
        {
            try
            {
                if (isDebug)
                {
                    Debug.Log($"Received from JavaScript: {message}");
                }

                var jsMessage = JsonConvert.DeserializeObject<ExternalInboundMessage>(message);
                OnDataReceived?.Invoke(jsMessage);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error at HandleMessageFromJavaScript: {ex.Message}");
            }
        }
#endif
    }
}

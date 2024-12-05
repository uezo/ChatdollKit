using System.Text.RegularExpressions;
using UnityEngine;
using Newtonsoft.Json;
using ChatdollKit.Model;
using ChatdollKit.Dialog;
using ChatdollKit.IO;
using ChatdollKit.LLM;
using ChatdollKit.Network;

namespace ChatdollKit.Demo
{
    public class MainAITuber : MonoBehaviour
    {
        // ChatdollKit components
        private ModelController modelController;
        private DialogPriorityManager dialogPriorityManager;
        private DialogProcessor dialogProcessor;
        private LLMContentProcessor llmContentProcessor;
        private SocketClient socketClient;

        [SerializeField]
        private AITuberMessageHandler aiTuberMessageHandler;

        [SerializeField]
        private AGIARegistry.AnimationCollection animationCollectionKey = AGIARegistry.AnimationCollection.AGIAFree;

        [SerializeField]
        private bool autoPilot = false;
        [TextArea(1, 6)]
        public string AutoPilotRequestText;

        [SerializeField]
        private GameObject licensePanel;

        private void Start()
        {
            // Get ChatdollKit components
            modelController = gameObject.GetComponent<ModelController>();
            dialogPriorityManager = gameObject.GetComponent<DialogPriorityManager>();
            dialogProcessor = gameObject.GetComponent<DialogProcessor>();
            llmContentProcessor = gameObject.GetComponent<LLMContentProcessor>();
            socketClient = gameObject.GetComponent<SocketClient>();

            // Animations used in conversation
            modelController.RegisterAnimations(AGIARegistry.GetAnimations(animationCollectionKey));

#pragma warning disable CS1998
            dialogProcessor.OnRequestRecievedAsync += async (text, payloads, token) =>
            {
                Debug.Log($"<<LISTENER>> {text}");
            };

            dialogProcessor.OnResponseShownAsync += async (text, payloads, llmSession, token) =>
            {
                Debug.Log($"<<AITUBER>> {llmSession.StreamBuffer}");

                if (socketClient.IsConnected)
                {
                    var pattarn = $@"<{llmContentProcessor.ThinkTag}>.*?</{llmContentProcessor.ThinkTag}>";
                    var messageToAIAvatar = "$" + Regex.Replace(llmSession.StreamBuffer, pattarn, string.Empty, RegexOptions.Singleline);
                    socketClient.SendMessageToServer(JsonConvert.SerializeObject(new ExternalInboundMessage(){
                        Endpoint = "dialog", Operation = "process", Text = messageToAIAvatar, Priority = 10
                    }));
                }
            };

            // Add handler for auto pilot
            aiTuberMessageHandler.AddHandler("dialog", "auto_pilot", async (message) => {
                if (message.Payloads == null) return;

                if (message.Payloads.ContainsKey("is_on"))
                {
                    autoPilot = (bool)message.Payloads["is_on"];
                }
                if (message.Payloads.ContainsKey("auto_pilot_request"))
                {
                    AutoPilotRequestText = (string)message.Payloads["auto_pilot_request"];
                }
            });
#pragma warning restore CS1998
        }

        private void Update()
        {
            if (dialogProcessor.Status == DialogProcessor.DialogStatus.Idling)
            {
                if (autoPilot && !dialogPriorityManager.HasRequest())
                {
                    // Continue talking automatically
                    dialogPriorityManager.SetRequest(AutoPilotRequestText);
                }
            }
        }

        public void OnLicenseButton()
        {
            licensePanel.SetActive(!licensePanel.activeSelf);
        }
    }
}

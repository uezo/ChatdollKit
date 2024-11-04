using UnityEngine;
using ChatdollKit.Model;
using ChatdollKit.Dialog;
using ChatdollKit.Network;
using ChatdollKit.IO;
using ChatdollKit.LLM;

namespace ChatdollKit.Demo
{
    public class MainAITuber : MonoBehaviour
    {
        // ChatdollKit components
        private ModelController modelController;
        private ModelRequestBroker modelRequestBroker;
        private DialogPriorityManager dialogPriorityManager;
        private DialogProcessor dialogProcessor;

        [SerializeField]
        private AGIARegistry.AnimationCollection animationCollectionKey = AGIARegistry.AnimationCollection.AGIAFree;

        [SerializeField]
        private bool autoPilot = false;
        public string AutoPilotRequestText;

        private void Start()
        {
            // Get ChatdollKit components
            modelController = gameObject.GetComponent<ModelController>();
            modelRequestBroker = gameObject.GetComponent<ModelRequestBroker>();
            dialogPriorityManager = gameObject.GetComponent<DialogPriorityManager>();
            dialogProcessor = gameObject.GetComponent<DialogProcessor>();

#pragma warning disable CS1998
            // Access from external program
            gameObject.GetComponent<SocketServer>().OnDataReceived = async (message) =>
            {
                HandleExternalMessage(message, "SocketServer");
            };
#pragma warning restore CS1998

            // Animations used in conversation
            modelController.RegisterAnimations(AGIARegistry.GetAnimations(animationCollectionKey));
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

        private void HandleExternalMessage(ExternalInboundMessage message, string source)
        {
            // Assign actions based on the request's Endpoint and Operation
            if (message.Endpoint == "dialog")
            {
                if (message.Operation == "process")
                {
                    dialogPriorityManager.SetRequest(message.Text, message.Payloads, message.Priority);
                }
                else if (message.Operation == "clear")
                {
                    dialogPriorityManager.ClearDialogRequestQueue(message.Priority);
                }
                else if (message.Operation == "auto_on")
                {
                    autoPilot = true;
                }
                else if (message.Operation == "auto_off")
                {
                    autoPilot = false;
                }
                else if (message.Operation == "clear_context")
                {
                    dialogProcessor.ClearContext();
                }
            }
            else if (message.Endpoint == "model")
            {
                modelRequestBroker.SetRequest(message.Text);
            }
            else if (message.Endpoint == "config")
            {
                if (message.Payloads.ContainsKey("system_prompt"))
                {
                    ((LLMServiceBase)dialogProcessor.LLMService).SystemMessageContent = (string)message.Payloads["system_prompt"];
                }
                if (message.Payloads.ContainsKey("autopilot_request"))
                {
                    AutoPilotRequestText = (string)message.Payloads["autopilot_request"];
                }
            }
        }
    }
}

using UnityEngine;
using ChatdollKit.Model;
using ChatdollKit.Dialog;

namespace ChatdollKit.Demo
{
    public class MainAITuber : MonoBehaviour
    {
        // ChatdollKit components
        private ModelController modelController;
        private DialogPriorityManager dialogPriorityManager;
        private DialogProcessor dialogProcessor;

        [SerializeField]
        private AITuberMessageHandler aiTuberMessageHandler;

        [SerializeField]
        private AGIARegistry.AnimationCollection animationCollectionKey = AGIARegistry.AnimationCollection.AGIAFree;

        [SerializeField]
        private bool autoPilot = false;
        [TextArea(1, 6)]
        public string AutoPilotRequestText;

        private void Start()
        {
            // Get ChatdollKit components
            modelController = gameObject.GetComponent<ModelController>();
            dialogPriorityManager = gameObject.GetComponent<DialogPriorityManager>();
            dialogProcessor = gameObject.GetComponent<DialogProcessor>();

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
            };

            // Add handler for auto pilot
            aiTuberMessageHandler.AddHandler("dialog", "auto_pilot", async (message) => {
                autoPilot = (bool)message.Payloads["is_on"];
                Debug.LogWarning($"auto_pilog: {autoPilot}");
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
    }
}

using UnityEngine;
using ChatdollKit.Model;
using ChatdollKit.Dialog;
using ChatdollKit.Network;
using ChatdollKit.IO;

namespace ChatdollKit.Demo
{
    public class MainAITuber : MonoBehaviour
    {
        // ChatdollKit components
        private ModelController modelController;
        private ModelRequestBroker modelRequestBroker;
        private DialogPriorityManager dialogPriorityManager;

        [SerializeField]
        private AGIARegistry.AnimationCollection animationCollectionKey = AGIARegistry.AnimationCollection.AGIAFree;

        [SerializeField]
        private CommentContainer commentContainer;

        private void Start()
        {
            // Get ChatdollKit components
            modelController = gameObject.GetComponent<ModelController>();
            modelRequestBroker = gameObject.GetComponent<ModelRequestBroker>();
            dialogPriorityManager = gameObject.GetComponent<DialogPriorityManager>();

            // Show comment
#pragma warning disable CS1998
            modelController.OnSayStart = async (voice, token) =>
            {
                commentContainer.AddCharacterComment(voice.Text);
            };

            // Access from external program
            gameObject.GetComponent<SocketServer>().OnDataReceived = async (message) =>
            {
                HandleExternalMessage(message, "SocketServer");
            };

            // Animations used in conversation
            modelController.RegisterAnimations(AGIARegistry.GetAnimations(animationCollectionKey));
        }
#pragma warning restore CS1998

        private void HandleExternalMessage(ExternalInboundMessage message, string source)
        {
            // Assign actions based on the request's Endpoint and Operation
            if (message.Endpoint == "dialog")
            {
                if (message.Operation == "start")
                {
                    commentContainer.ResetCharacterComment();
                    dialogPriorityManager.SetRequest(message.Text, message.Payloads, message.Priority);
                }
                else if (message.Operation == "clear")
                {
                    dialogPriorityManager.ClearDialogRequestQueue(message.Priority);
                }
            }
            else if (message.Endpoint == "model")
            {
                commentContainer.ResetCharacterComment();
                modelRequestBroker.SetRequest(message.Text);
            }
            else if (message.Endpoint == "comment")
            {
                var userName = message.Payloads != null && message.Payloads.ContainsKey("userName") ? (string)message.Payloads["userName"] : null;
                commentContainer.AddUserComment(message.Text, userName);
            }
        }
    }
}

// Socker Client example: https://gist.github.com/uezo/9e56a828bb5ea0387f90cc07f82b4c15

using System;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.IO;
using ChatdollKit.Model;
using ChatdollKit.Network;

namespace ChatdollKit.Demo
{
    public class Main08Remote : MonoBehaviour
    {
        // ChatdollKit components
        private ModelController modelController;
        private ModelRequestBroker modelRequestBroker;
        private DialogProcessor dialogProcessor;
        private DialogPriorityManager dialogPriorityManager;
        private SimpleCamera simpleCamera;

        [SerializeField]
        private AGIARegistry.AnimationCollection animationCollectionKey = AGIARegistry.AnimationCollection.AGIAFree;
        [SerializeField]
        private bool ListRegisteredAnimationsOnStart = false;

        private void Start()
        {
            // Get ChatdollKit components
            modelController = gameObject.GetComponent<ModelController>();
            modelRequestBroker = gameObject.GetComponent<ModelRequestBroker>();
            dialogProcessor = gameObject.GetComponent<DialogProcessor>();
            dialogPriorityManager = gameObject.GetComponent<DialogPriorityManager>();

            // Image capture for vision
            if (simpleCamera == null)
            {
                simpleCamera = FindObjectOfType<SimpleCamera>();
                if (simpleCamera == null)
                {
                    Debug.LogWarning("SimpleCamera is not found in this scene.");
                }
                else
                {
                    dialogProcessor.LLMServiceExtensions.CaptureImage = async (source) =>
                    {
                        try
                        {
                            return await simpleCamera.CaptureImageAsync();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Error at CaptureImage: {ex.Message}\n{ex.StackTrace}");
                        }
                        return null;
                    };
                }
            }

            // Register animations
            modelController.RegisterAnimations(AGIARegistry.GetAnimations(animationCollectionKey));
            if (ListRegisteredAnimationsOnStart)
            {
                var animationsList = modelController.ListRegisteredAnimations();
                Debug.Log($"=== Registered Animations ===\n{animationsList}");
            }

            // Animation and face expression for idling
            modelController.AddIdleAnimation("generic", 10.0f);
            modelController.AddIdleAnimation("calm_hands_on_back", 5.0f);

            // Configure message handler for remote control
#pragma warning disable CS1998
#if UNITY_WEBGL && !UNITY_EDITOR
            gameObject.GetComponent<JavaScriptMessageHandler>().OnDataReceived = async (message) =>
            {
                HandleExternalMessage(message, "JavaScript");
            };
#else
            gameObject.GetComponent<SocketServer>().OnDataReceived = async (message) =>
            {
                HandleExternalMessage(message, "SocketServer");
            };
#endif
#pragma warning restore CS1998
        }

        private void HandleExternalMessage(ExternalInboundMessage message, string source)
        {
            // Assign actions based on the request's Endpoint and Operation
            if (message.Endpoint == "dialog")
            {
                if (message.Operation == "start")
                {
                    if (source == "JavaScript")
                    {
                        dialogPriorityManager.SetRequest(message.Text, message.Payloads, 0);
                    }
                    else
                    {
                        dialogPriorityManager.SetRequest(message.Text, message.Payloads, message.Priority);
                    }
                }
                else if (message.Operation == "clear")
                {
                    dialogPriorityManager.ClearDialogRequestQueue(message.Priority);
                }
            }
            else if (message.Endpoint == "model")
            {
                modelRequestBroker.SetRequest(message.Text);
            }            
        }
    }
}

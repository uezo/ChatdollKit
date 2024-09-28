// Socker Client example: https://gist.github.com/uezo/9e56a828bb5ea0387f90cc07f82b4c15

using System;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.IO;
using ChatdollKit.Model;
using ChatdollKit.Network;
using ChatdollKit.LLM;
using ChatdollKit.LLM.ChatGPT;

namespace ChatdollKit.Demo
{
    public class Main08Remote : MonoBehaviour
    {
        // ChatdollKit components
        private ModelController modelController;
        private ModelRequestBroker modelRequestBroker;
        private DialogPriorityManager dialogPriorityManager;
        private SimpleCamera simpleCamera;

        private void Start()
        {
            // Get ChatdollKit components
            modelController = gameObject.GetComponent<ModelController>();
            modelRequestBroker = gameObject.GetComponent<ModelRequestBroker>();
            dialogPriorityManager = gameObject.GetComponent<DialogPriorityManager>();

            // Image capture for ChatGPT vision
            if (simpleCamera == null)
            {
                simpleCamera = FindObjectOfType<SimpleCamera>();
                if (simpleCamera == null)
                {
                    Debug.LogWarning("SimpleCamera is not found in this scene.");
                }
            }
            gameObject.GetComponent<ChatGPTService>().CaptureImage = CaptureImageAsync;

            // Animation and face expression for idling
            modelController.AddIdleAnimation(new Model.Animation("BaseParam", 6, 5f));
            modelController.AddIdleAnimation(new Model.Animation("BaseParam", 2, 5f));

            // Animations used in conversation and remote control
            modelController.RegisterAnimation("angry_hands_on_waist", new Model.Animation("BaseParam", 0, 3.0f));
            modelController.RegisterAnimation("brave_hand_on_chest", new Model.Animation("BaseParam", 1, 3.0f));
            modelController.RegisterAnimation("calm_hands_on_back", new Model.Animation("BaseParam", 2, 3.0f));
            modelController.RegisterAnimation("concern_right_hand_front", new Model.Animation("BaseParam", 3, 3.0f));
            modelController.RegisterAnimation("energetic_right_fist_up", new Model.Animation("BaseParam", 4, 3.0f));
            modelController.RegisterAnimation("energetic_right_hand_piece", new Model.Animation("BaseParam", 5, 3.0f));
            modelController.RegisterAnimation("pitiable_right_hand_on_back_head", new Model.Animation("BaseParam", 7, 3.0f));
            modelController.RegisterAnimation("surprise_hands_open_front", new Model.Animation("BaseParam", 8, 3.0f));
            modelController.RegisterAnimation("walking", new Model.Animation("BaseParam", 9, 3.0f));
            modelController.RegisterAnimation("waving_arm", new Model.Animation("BaseParam", 10, 3.0f));
            modelController.RegisterAnimation("look_away", new Model.Animation("BaseParam", 6, 3.0f, "AGIA_Layer_look_away_01", "Additive Layer"));
            modelController.RegisterAnimation("nodding_once", new Model.Animation("BaseParam", 6, 3.0f, "AGIA_Layer_nodding_once_01", "Additive Layer"));
            modelController.RegisterAnimation("swinging_body", new Model.Animation("BaseParam", 6, 3.0f, "AGIA_Layer_swinging_body_01", "Additive Layer"));

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

        private async UniTask<byte[]> CaptureImageAsync(string source)
        {
            if (simpleCamera != null)
            {
                try
                {
                    return await simpleCamera.CaptureImageAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error at CaptureImageAsync: {ex.Message}\n{ex.StackTrace}");
                }
            }

            return null;
        }
    }
}

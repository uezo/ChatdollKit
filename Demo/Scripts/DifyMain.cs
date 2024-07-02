using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.IO;
using ChatdollKit.Model;
using ChatdollKit.LLM;
using ChatdollKit.UI;
using ChatdollKit.LLM.Dify;
using ChatdollKit.Extension.Dify;

namespace ChatdollKit.Demo
{
    public class DifyMain : MonoBehaviour
    {
        [Header("Dify Settings")]
        public string ApiKey;
        public string BaseUrl;
        public string User;
        public AudioType AudioType = AudioType.MPEG;

        // ChatdollKit components
        private ModelController modelController;
        private DialogController dialogController;

        // Input UI
        [Header("UI components")]
        [SerializeField]
        private InputUI inputUI;
        [SerializeField]
        private SimpleCamera simpleCamera;

        void Start()
        {
            // Configure Dify components
            gameObject.GetComponent<DifyWakeWordListener>().ApiKey = ApiKey;
            gameObject.GetComponent<DifyWakeWordListener>().BaseUrl = BaseUrl;
            gameObject.GetComponent<DifyVoiceRequestProvider>().ApiKey = ApiKey;
            gameObject.GetComponent<DifyVoiceRequestProvider>().BaseUrl = BaseUrl;
            gameObject.GetComponent<DifyTTSLoader>().ApiKey = ApiKey;
            gameObject.GetComponent<DifyTTSLoader>().BaseUrl = BaseUrl;
            gameObject.GetComponent<DifyTTSLoader>().User = User;
            gameObject.GetComponent<DifyTTSLoader>().AudioType = AudioType;
            gameObject.GetComponent<DifyService>().ApiKey = ApiKey;
            gameObject.GetComponent<DifyService>().BaseUrl = BaseUrl;
            gameObject.GetComponent<DifyService>().User = User;

            // Get ChatdollKit components
            modelController = gameObject.GetComponent<ModelController>();
            dialogController = gameObject.GetComponent<DialogController>();

            // Image capture for ChatGPT vision
            gameObject.GetComponent<DifyService>().CaptureImage = CaptureImageAsync;

            // Animation and face expression for idling
            modelController.AddIdleAnimation(new Model.Animation("BaseParam", 6, 5f));

            // Animation and face expression for processing
            var processingAnimation = new List<Model.Animation>();
            processingAnimation.Add(new Model.Animation("BaseParam", 3, 0.3f));
            processingAnimation.Add(new Model.Animation("BaseParam", 3, 20.0f, "AGIA_Layer_nodding_once_01", "Additive Layer"));
            var processingFace = new List<FaceExpression>();
            processingFace.Add(new FaceExpression("Blink", 3.0f));

            var neutralFaceRequest = new List<FaceExpression>();
            neutralFaceRequest.Add(new FaceExpression("Neutral"));

#pragma warning disable CS1998
            dialogController.OnRequestAsync = async (request, token) =>
            {
                if (request.Type == RequestType.Voice)
                {
                    var imageBytes = inputUI.GetImageBytes() ?? simpleCamera.GetStillImage();
                    if (imageBytes != null)
                    {
                        request.Payloads["imageBytes"] = imageBytes;
                        inputUI.ClearImage();
                        simpleCamera.ClearStillImage();
                    }
                }

                modelController.StopIdling();
                modelController.Animate(processingAnimation);
                modelController.SetFace(processingFace);
            };
            dialogController.OnStartShowingResponseAsync = async (response, token) =>
            {
                modelController.SetFace(neutralFaceRequest);
            };
#pragma warning restore CS1998

            // Animations used in conversation
            foreach (var llmContentSkill in gameObject.GetComponents<LLMContentSkill>())
            {
                if (llmContentSkill.GetType() == typeof(LLMContentSkill))
                {
                    llmContentSkill.RegisterAnimation("angry_hands_on_waist", new Model.Animation("BaseParam", 0, 3.0f));
                    llmContentSkill.RegisterAnimation("brave_hand_on_chest", new Model.Animation("BaseParam", 1, 3.0f));
                    llmContentSkill.RegisterAnimation("calm_hands_on_back", new Model.Animation("BaseParam", 2, 3.0f));
                    llmContentSkill.RegisterAnimation("concern_right_hand_front", new Model.Animation("BaseParam", 3, 3.0f));
                    llmContentSkill.RegisterAnimation("energetic_right_fist_up", new Model.Animation("BaseParam", 4, 3.0f));
                    llmContentSkill.RegisterAnimation("energetic_right_hand_piece", new Model.Animation("BaseParam", 5, 3.0f));
                    llmContentSkill.RegisterAnimation("pitiable_right_hand_on_back_head", new Model.Animation("BaseParam", 7, 3.0f));
                    llmContentSkill.RegisterAnimation("surprise_hands_open_front", new Model.Animation("BaseParam", 8, 3.0f));
                    llmContentSkill.RegisterAnimation("walking", new Model.Animation("BaseParam", 9, 3.0f));
                    llmContentSkill.RegisterAnimation("waving_arm", new Model.Animation("BaseParam", 10, 3.0f));
                    llmContentSkill.RegisterAnimation("look_away", new Model.Animation("BaseParam", 6, 3.0f, "AGIA_Layer_look_away_01", "Additive Layer"));
                    llmContentSkill.RegisterAnimation("nodding_once", new Model.Animation("BaseParam", 6, 3.0f, "AGIA_Layer_nodding_once_01", "Additive Layer"));
                    llmContentSkill.RegisterAnimation("swinging_body", new Model.Animation("BaseParam", 6, 3.0f, "AGIA_Layer_swinging_body_01", "Additive Layer"));
                    break;
                }
            }

            // Animation and face expression for start up
            var animationOnStart = new List<Model.Animation>();
            animationOnStart.Add(new Model.Animation("BaseParam", 6, 0.5f));
            animationOnStart.Add(new Model.Animation("BaseParam", 10, 3.0f));
            modelController.Animate(animationOnStart);

            var faceOnStart = new List<FaceExpression>();
            faceOnStart.Add(new FaceExpression("Joy", 3.0f));
            modelController.SetFace(faceOnStart);
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

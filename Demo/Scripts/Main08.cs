using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.IO;
using ChatdollKit.LLM;
using ChatdollKit.LLM.ChatGPT;
using ChatdollKit.Model;
// using ChatdollKit.SpeechListener;
// using ChatdollKit.SpeechSynthesizer;

namespace ChatdollKit.Demo
{
    public class Main08 : MonoBehaviour
    {
        // ChatdollKit components
        private ModelController modelController;
        private SimpleCamera simpleCamera;

        [SerializeField]
        private AGIARegistry.AnimationCollection animationCollectionKey = AGIARegistry.AnimationCollection.AGIAFree;
        [SerializeField]
        private bool ListRegisteredAnimationsOnStart = false;

        private void Start()
        {
            // Get ChatdollKit components
            modelController = gameObject.GetComponent<ModelController>();

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

            // Animation and face expression for processing
            var processingAnimation = new List<Model.Animation>();
            processingAnimation.Add(modelController.GetRegisteredAnimation("concern_right_hand_front", 0.3f));
            processingAnimation.Add(modelController.GetRegisteredAnimation("concern_right_hand_front", 20.0f, "AGIA_Layer_nodding_once_01", "Additive Layer"));
            var processingFace = new List<FaceExpression>();
            processingFace.Add(new FaceExpression("Blink", 3.0f));
            gameObject.GetComponent<AIAvatar>().AddProcessingPresentaion(processingAnimation, processingFace);

            // Animation and face expression for start up
            var animationOnStart = new List<Model.Animation>();
            animationOnStart.Add(modelController.GetRegisteredAnimation("generic", 0.5f));
            animationOnStart.Add(modelController.GetRegisteredAnimation("waving_arm", 3.0f));

            modelController.Animate(animationOnStart);

            var faceOnStart = new List<FaceExpression>();
            faceOnStart.Add(new FaceExpression("Joy", 3.0f));
            modelController.SetFace(faceOnStart);

            // User defined tag example: Dynamic multi language switching
            // Insert following instruction to ChatGPTService.
            // ----
            // If you want change current language, insert language tag like [language:en-US].
            //
            // Example:
            // [language:en-US]From now on, let's talk in English.
            // ----
            // var chatGPTService = gameObject.GetComponent<ChatGPTService>();
            // chatGPTService.HandleExtractedTags = (tags, session) =>
            // {
            //     if (tags.ContainsKey("language"))
            //     {
            //         var language = tags["language"].Contains("-") ? tags["language"].Split('-')[0] : tags["language"];
            //         if (language != "ja")
            //         {
            //             var openAISpeechSynthesizer = gameObject.GetComponent<OpenAISpeechSynthesizer>();
            //             modelController.SpeechSynthesizerFunc = openAISpeechSynthesizer.GetAudioClipAsync;
            //         }
            //         else
            //         {
            //             var voicevoxSpeechSynthesizer = gameObject.GetComponent<VoicevoxSpeechSynthesizer>();
            //             modelController.SpeechSynthesizerFunc = voicevoxSpeechSynthesizer.GetAudioClipAsync;
            //         }
            //         var openAIListener = gameObject.GetComponent<OpenAISpeechListener>();
            //         openAIListener.Language = language;
            //         Debug.Log($"Set language to {language}");
            //     }
            // };
        }

        private void Update()
        {
            // Advanced usage:
            // Uncomment the following lines to start a conversation in idle mode, with any word longer than 3 characters instead of the wake word.

            // if (aiAvatar.Mode == AIAvatar.AvatarMode.Idle)
            // {
            //     aiAvatar.WakeLength = 3;
            // }
            // else if (aiAvatar.Mode == AIAvatar.AvatarMode.Sleep)
            // {
            //     aiAvatar.WakeLength = 0;
            // }
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

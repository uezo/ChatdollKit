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
        private AIAvatar aiAvatar;
        private ModelController modelController;
        private SimpleCamera simpleCamera;

        private void Start()
        {
            // Get ChatdollKit components
            aiAvatar = gameObject.GetComponent<AIAvatar>();
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

            // Animation and face expression for idling
            modelController.AddIdleAnimation(new Model.Animation("BaseParam", 6, 5f));
            modelController.AddIdleAnimation(new Model.Animation("BaseParam", 2, 5f));

            // // Animation and face expression for processing
            // var processingAnimation = new List<Model.Animation>();
            // processingAnimation.Add(new Model.Animation("BaseParam", 3, 0.3f));
            // processingAnimation.Add(new Model.Animation("BaseParam", 3, 20.0f, "AGIA_Layer_nodding_once_01", "Additive Layer"));
            // var processingFace = new List<FaceExpression>();
            // processingFace.Add(new FaceExpression("Blink", 3.0f));
            // GetComponent<Orchestrator>().AddProcessingPresentaion(processingAnimation, processingFace);

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

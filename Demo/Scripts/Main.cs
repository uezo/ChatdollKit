using System.Collections.Generic;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.Model;
using ChatdollKit.LLM;

namespace ChatdollKit.Demo
{
    public class Main : MonoBehaviour
    {
        private ModelController modelController;
        private DialogController dialogController;

        //private int secondsToSleep = 60;
        //private DateTime sleepStartAt;

        void Start()
        {
            // Adjust frame rate to improve performance (Optional)
            Application.targetFrameRate = 60;

            modelController = gameObject.GetComponent<ModelController>();
            dialogController = gameObject.GetComponent<DialogController>();

            // Animation and face expression for idling
            modelController.AddIdleAnimation(new Model.Animation("BaseParam", 6, 5f));

            //// Add idle animations with `mode` if you want to have extra idling modes
            //modelController.AddIdleAnimation(new Model.Animation("BaseParam", 101, 5f), mode: "sleep");
            //modelController.AddIdleFace("sleep", "Blink");
            //sleepStartAt = DateTime.UtcNow.AddSeconds(secondsToSleep);

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
            //animationOnStart.Add(new Model.Animation("BaseParam", 101, 20.0f));
            modelController.Animate(animationOnStart);

            var faceOnStart = new List<FaceExpression>();
            faceOnStart.Add(new FaceExpression("Joy", 3.0f));
            modelController.SetFace(faceOnStart);
        }

        //private void Update()
        //{
        //    // Example for switching idling mode

        //    if (dialogController.IsChatting)
        //    {
        //        // Update the time to sleep when chatting
        //        sleepStartAt = DateTime.UtcNow.AddSeconds(secondsToSleep);
        //    }

        //    if (DateTime.UtcNow > sleepStartAt && modelController.IdlingMode != "sleep")
        //    {
        //        // Change mode to sleep after time to sleep
        //        _ = modelController.ChangeIdlingModeAsync("sleep");
        //    }
        //}
    }
}

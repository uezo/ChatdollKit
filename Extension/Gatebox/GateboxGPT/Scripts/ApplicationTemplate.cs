using System.Collections.Generic;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.Model;
using Animation = ChatdollKit.Model.Animation;

namespace ChatdollKit.Extension.Gatebox
{
    public class ApplicationTemplate : MonoBehaviour
    {
        private ModelController modelController;
        private DialogController dialogController;
        private GateboxApplication gatebox;

        private void Awake()
        {
            // ChatdollKit components
            modelController = GetComponent<ModelController>();
            dialogController = gameObject.GetComponent<DialogController>();
            gatebox = gameObject.GetComponent<GateboxApplication>();

            // Idling animations (random every 30 sec)
            // Free
            modelController.AddIdleAnimation(new Animation("BaseParam", 6, 30.0f), 2);
            modelController.AddIdleAnimation(new Animation("BaseParam", 2, 30.0f), 1);
            //// Paid
            //modelController.AddIdleAnimation(new Animation("BaseParam", 19, 30.0f), 2);
            //modelController.AddIdleAnimation(new Animation("BaseParam", 7, 30.0f), 1);
            //modelController.AddIdleAnimation(new Animation("BaseParam", 5, 30.0f), 1);
            //modelController.AddIdleAnimation(new Animation("BaseParam", 4, 30.0f), 1);
        }

        private void Start()
        {
            // Set actions
            gatebox.OnGateboxButtonDown = OnGateboxButtonDown;
            gatebox.OnGateboxButtonUp = OnGateboxButtonUp;
            gatebox.OnGateboxButtonSingleTap = OnGateboxButtonSingleTap;
            gatebox.OnGateboxButtonDoubleTap = OnGateboxButtonDoubleTap;
            gatebox.OnGateboxButtonLong = OnGateboxButtonLong;
            gatebox.OnAmbientSensorUpdated = OnAmbientSensorUpdated;


            // Application startup animations and face expression
            var animationOnStart = new List<Animation>();
            // Free
            animationOnStart.Add(new Animation("BaseParam", 6, 0.5f));
            animationOnStart.Add(new Animation("BaseParam", 10, 3.0f));
            //// Paid
            //animationOnStart.Add(new Animation("BaseParam", 19, 0.5f));
            //animationOnStart.Add(new Animation("BaseParam", 37, 3.0f));
            modelController.Animate(animationOnStart);
            var faceOnStart = new List<FaceExpression>();
            faceOnStart.Add(new FaceExpression("Joy", 3.0f));
            modelController.SetFace(faceOnStart);

            // Processing animations and face expression
            var processingAnimation = new List<Animation>();
            processingAnimation.Add(new Animation("BaseParam", 3, 0.3f));
            processingAnimation.Add(new Animation("BaseParam", 3, 20.0f, "AGIA_Layer_nodding_once_01", "Additive Layer"));
            //// Paid
            //processingAnimation.Add(new Animation("BaseParam", 28, 0.3f));
            //processingAnimation.Add(new Animation("BaseParam", 28, 20.0f, "AGIA_Layer_nod_twice_01", "Additive Layer"));

            var processingFace = new List<FaceExpression>();
            processingFace.Add(new FaceExpression("Blink", 3.0f));
            dialogController.OnRequestAsync = async (request, token) =>
            {
                modelController.StopIdling();
                modelController.Animate(processingAnimation);
                modelController.SetFace(processingFace);
            };

            // Reset face before showing response
            var neutralFaceRequest = new List<FaceExpression>();
            neutralFaceRequest.Add(new FaceExpression("Neutral"));
            dialogController.OnStartShowingResponseAsync = async (response, token) =>
            {
                modelController.SetFace(neutralFaceRequest);
            };
        }

        // On button down
        private void OnGateboxButtonDown()
        {

        }

        // On button up
        private void OnGateboxButtonUp()
        {

        }

        // Shorter than 1000msec from Down to Up
        private void OnGateboxButtonSingleTap()
        {
            _ = dialogController.StartDialogAsync();
        }

        // Single tap twice in 1000msec
        private void OnGateboxButtonDoubleTap()
        {
            dialogController.StopDialog();
        }

        // Being invoked every frames while button pressed
        private void OnGateboxButtonLong()
        {
            gameObject.transform.Rotate(0f, 1.0f, 0f);
        }

        // Ambient sensor
        private void OnAmbientSensorUpdated(float temperature, float humidity)
        {

        }
    }
}


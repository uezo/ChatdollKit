using System;
using UnityEngine;
using com.gateboxlab.gateboxsdk;

namespace ChatdollKit.Extension.Gatebox
{
    [RequireComponent(typeof(GateboxSDK))]
    public class GateboxApplication : ChatdollApplication
    {
        // LED Colors for each status
        [Header("LED Colors for each status")]
        public Color DefaultColor = Color.cyan;
        public Color ListeningColor = Color.green;
        public Color ChattingColor = Color.white;
        public Color ErrorColor = Color.red;

        // Gatebox Button actions
        public Action OnGateboxButtonDown;
        public Action OnGateboxButtonUp;
        public Action OnGateboxButtonSingleTap; // shorter than 1000msec from Down to Up
        public Action OnGateboxButtonDoubleTap; // single tap twice in 1000msec
        public Action OnGateboxButtonLongStart; // press for over 1000msec
        public Action OnGateboxButtonLongEnd;   // release after LongStart longer than 3000msec
        public Action OnGateboxButtonLongCancel;    // release after LongStart within 3000msec
        protected bool IsGateboxButtonLong;
        public Action OnGateboxButtonLong;

        // Human Sensor
        public Action OnHumanSensorLeftOn;
        public Action OnHumanSensorLeftOff;
        public Action OnHumanSensorRightOn;
        public Action OnHumanSensorRightOff;
        protected bool IsHumanSensorLeftOn;
        protected bool IsHumanSensorRightOn;
        protected bool IsHumanSensorOn;
        [Header("Human Sensor Sensitivity")]
        public float HumanSensorOnOffset = 3.0f;
        public float HumanSensorOffOffset = 10.0f;
        public int HumanSensorDetectionTimeoutFrames = 50;
        protected int HumanSensorTimeoutCounter = 0;
        protected DateTime HumanSensorLastDetectedAt;
        protected DateTime HumanSensorStartDetectingAt;

        // Temperature and humidity
        public Action<float, float> OnAmbientSensorUpdated;

        protected override void Awake()
        {
            base.Awake();

            // Default handlers for Gatebox Button
            OnGateboxButtonLongStart = () => { IsGateboxButtonLong = true; };
            OnGateboxButtonLongEnd = () => { IsGateboxButtonLong = false; };
            OnGateboxButtonLongCancel = () => { IsGateboxButtonLong = false; };
            OnGateboxButtonLong = () =>
            {
                gameObject.transform.Rotate(0f, 1.0f, 0f);
            };
            OnGateboxButtonDoubleTap = () =>
            {
                if (dialogController.IsChatting)
                {
                    dialogController.StopDialog();
                }
                else
                {
#pragma warning disable CS4014
                    dialogController.StartDialogAsync(new Dialog.DialogRequest(GetUserId()));
#pragma warning restore CS4014
                }
            };

            // Default handlers for Human Sensors
            OnHumanSensorLeftOn = () => { IsHumanSensorLeftOn = true; };
            OnHumanSensorLeftOff = () => { IsHumanSensorLeftOn = false; };
            OnHumanSensorRightOn = () => { IsHumanSensorRightOn = true; };
            OnHumanSensorRightOff = () => { IsHumanSensorRightOn = false; };
        }

        protected virtual void Start()
        {
            // Register sensor callbacks
            GateboxButton.RegisterListener(gameObject.name, "OnGateboxButton");
            HumanSensor.RegisterListener(gameObject.name, "OnHumanSensor");
            AmbientSensor.RegisterListener(gameObject.name, "OnAmbientSensor");
        }

        protected virtual void Update()
        {
            // Stage and Status LED
            SetLEDColors();

            // Gatebox Button
            if (IsGateboxButtonLong)
            {
                OnGateboxButtonLong?.Invoke();
            }

            // Set Human Sensor Status
            if (IsHumanSensorLeftOn || IsHumanSensorRightOn)
            {
                HumanSensorLastDetectedAt = DateTime.Now;
                if (!IsHumanSensorOn)
                {
                    HumanSensorStartDetectingAt = HumanSensorTimeoutCounter == 0 ? DateTime.Now : HumanSensorStartDetectingAt;
                    IsHumanSensorOn = (HumanSensorLastDetectedAt - HumanSensorStartDetectingAt).TotalSeconds > HumanSensorOnOffset;
                }
                HumanSensorTimeoutCounter = HumanSensorDetectionTimeoutFrames;
            }
            else
            {
                if (IsHumanSensorOn)
                {
                    IsHumanSensorOn = !((DateTime.Now - HumanSensorLastDetectedAt).TotalSeconds > HumanSensorOffOffset);
                }
                else
                {
                    if (HumanSensorTimeoutCounter > 0)
                    {
                        HumanSensorTimeoutCounter--;
                    }
                }
            }
        }

        protected override string GetUserId()
        {
            var gateboxCustomerId = GateboxDevices.GetCustomerID();
            return !string.IsNullOrEmpty(gateboxCustomerId) ? gateboxCustomerId : base.GetUserId();
        }

        protected virtual void SetLEDColors()
        {
            // Set LEDs Color for each status
            if (dialogController.IsError)
            {
                StageLED.SetColor(ErrorColor); StatusLED.SetColor(ErrorColor);
            }
            else if (voiceRequestProvider.IsListening)
            {
                StageLED.SetColor(ListeningColor); StatusLED.SetColor(ListeningColor);
            }
            else if (dialogController.IsChatting)
            {
                StageLED.SetColor(ChattingColor); StatusLED.SetColor(ChattingColor);
            }
            else
            {
                StageLED.SetColor(DefaultColor); StatusLED.SetColor(DefaultColor);
            }
        }

        // Gatebox Button Callback
        protected virtual void OnGateboxButton(string message)
        {
            var result = JsonUtility.FromJson<GateboxButton.Result>(message);

            if (result.keycode == "DOWN")
            {
                OnGateboxButtonDown?.Invoke();
            }
            else if (result.keycode == "UP")
            {
                OnGateboxButtonUp?.Invoke();
            }
            else if (result.keycode == "SINGLE_TAP")
            {
                OnGateboxButtonSingleTap?.Invoke();
            }
            else if (result.keycode == "DOUBLE_TAP")
            {
                OnGateboxButtonDoubleTap?.Invoke();
            }
            else if (result.keycode == "LONG_START")
            {
                OnGateboxButtonLongStart?.Invoke();
            }
            else if (result.keycode == "LONG_END")
            {
                OnGateboxButtonLongEnd?.Invoke();
            }
            else if (result.keycode == "LONG_CANCEL")
            {
                OnGateboxButtonLongCancel?.Invoke();
            }
        }

        // Human Sensor Callback
        protected virtual void OnHumanSensor(string message)
        {
            var result = JsonUtility.FromJson<HumanSensor.Result>(message);

            if (result.left == "true")
            {
                OnHumanSensorLeftOn?.Invoke();
            }
            else
            {
                OnHumanSensorLeftOff?.Invoke();
            }
            if (result.right == "true")
            {
                OnHumanSensorRightOn?.Invoke();
            }
            else
            {
                OnHumanSensorRightOff?.Invoke();
            }
        }

        // Ambient Sensor Callback
        protected virtual void OnAmbientSensor(string message)
        {
            var result = JsonUtility.FromJson<AmbientSensor.Result>(message);

            float.TryParse(result.temperature, out float temperature);
            float.TryParse(result.humidity, out float humidity);
            OnAmbientSensorUpdated?.Invoke(temperature, humidity);
        }
    }
}

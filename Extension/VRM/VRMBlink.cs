using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using VRM;
using ChatdollKit.Model;

namespace ChatdollKit.Extension.VRM
{
    public class VRMBlink : MonoBehaviour, IBlink
    {
        private VRMBlendShapeProxy blendShapeProxy;
        private BlendShapeKey blinkBlendShapeKey;

        [SerializeField] private float minBlinkIntervalToClose = 3.0f;
        [SerializeField] private float maxBlinkIntervalToClose = 5.0f;
        [SerializeField] private float minBlinkIntervalToOpen = 0.05f;
        [SerializeField] private float maxBlinkIntervalToOpen = 0.1f;
        [SerializeField] private float blinkTransitionToClose = 0.01f;
        [SerializeField] private float blinkTransitionToOpen = 0.02f;
        private float blinkIntervalToClose;
        private float blinkIntervalToOpen;
        private float blinkWeight = 0.0f;
        private float blinkVelocity = 0.0f;
        public bool IsBlinkEnabled { get; private set; } = false;
        private Action blinkAction;
        private CancellationTokenSource blinkTokenSource;

        private void Awake()
        {
            Setup(gameObject.GetComponent<ModelController>().AvatarModel);
        }

        public void Setup(GameObject avatarObject)
        {
            blendShapeProxy = avatarObject.GetComponent<VRMBlendShapeProxy>();
            blinkBlendShapeKey = BlendShapeKey.CreateFromPreset(BlendShapePreset.Blink);
            blinkTokenSource = new CancellationTokenSource();
        }

        private void Start()
        {
            _ = StartBlinkAsync(true);
        }

        private void LateUpdate()
        {
            blinkAction?.Invoke();
        }

        private void OnDestroy()
        {
            blinkTokenSource?.Cancel();
        }

        // Initialize and start blink
        public async UniTask StartBlinkAsync(bool startNew = false)
        {
            // Return with doing nothing when already blinking
            if (IsBlinkEnabled && startNew == false)
            {
                return;
            }

            // Initialize
            blinkWeight = 0f;
            blinkVelocity = 0f;
            blinkAction = null;

            // Open the eyes
            blendShapeProxy.ImmediatelySetValue(blinkBlendShapeKey, 0);


            // Enable blink
            IsBlinkEnabled = true;

            if (!startNew)
            {
                return;
            }

            // Start new blink loop
            while (true)
            {
                if (blinkTokenSource.Token.IsCancellationRequested)
                {
                    break;
                }

                // Close eyes
                blinkIntervalToClose = UnityEngine.Random.Range(minBlinkIntervalToClose, maxBlinkIntervalToClose);
                await UniTask.Delay((int)(blinkIntervalToClose * 1000));
                blinkAction = CloseEyesOnUpdate;
                // Open eyes
                blinkIntervalToOpen = UnityEngine.Random.Range(minBlinkIntervalToOpen, maxBlinkIntervalToOpen);
                await UniTask.Delay((int)(blinkIntervalToOpen * 1000));
                blinkAction = OpenEyesOnUpdate;
            }
        }

        // Stop blink
        public void StopBlink()
        {
            IsBlinkEnabled = false;
            blendShapeProxy.ImmediatelySetValue(blinkBlendShapeKey, 0);
        }

        // Action for closing eyes called on every updates
        private void CloseEyesOnUpdate()
        {
            if (!IsBlinkEnabled)
            {
                return;
            }
            blinkWeight = Mathf.SmoothDamp(blinkWeight, 1, ref blinkVelocity, blinkTransitionToClose);
            blendShapeProxy.ImmediatelySetValue(blinkBlendShapeKey, blinkWeight);
        }

        // Action for opening eyes called on every updates
        private void OpenEyesOnUpdate()
        {
            if (!IsBlinkEnabled)
            {
                return;
            }
            blinkWeight = Mathf.SmoothDamp(blinkWeight, 0, ref blinkVelocity, blinkTransitionToOpen);
            blendShapeProxy.ImmediatelySetValue(blinkBlendShapeKey, blinkWeight);
        }
    }
}

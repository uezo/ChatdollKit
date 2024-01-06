using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Model
{
    public class Blink : MonoBehaviour, IBlink
    {
        [SerializeField] private SkinnedMeshRenderer skinnedMeshRenderer;

        [Header("Blink")]
        [SerializeField] private string blinkBlendShapeName;
        private int blinkShapeIndex;
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
            blinkTokenSource = new CancellationTokenSource();
        }

        private void Start()
        {
            if (string.IsNullOrEmpty(blinkBlendShapeName))
            {
                Debug.LogWarning("Blink is disabled because BlinkBlendShapeName is not defined");
            }
            else
            {
                _ = StartBlinkAsync(true);
            }
        }

        private void LateUpdate()
        {
            blinkAction?.Invoke();
        }

        private void OnDestroy()
        {
            blinkTokenSource?.Cancel();
        }

        // For setup
        public void Setup(GameObject avatarObject)
        {
            skinnedMeshRenderer = AvatarUtility.GetFacialSkinnedMeshRenderer(avatarObject);
            blinkBlendShapeName = GetBlinkTargetName(skinnedMeshRenderer);
            if (string.IsNullOrEmpty(blinkBlendShapeName))
            {
                Debug.LogWarning("BlendShape for blink not found.");
            }
        }

        public string GetBlinkShapeName()
        {
            return blinkBlendShapeName;
        }

        private static string GetBlinkTargetName(SkinnedMeshRenderer skinnedMeshRenderer)
        {
            var mesh = skinnedMeshRenderer.sharedMesh;
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                var shapeName = mesh.GetBlendShapeName(i);
                var shapeNameLower = shapeName.ToLower();
                if (!shapeNameLower.Contains("left") && !shapeNameLower.Contains("right"))
                {
                    if (shapeNameLower.Contains("blink") || (shapeNameLower.Contains("eye") && shapeNameLower.Contains("close")))
                    {
                        return shapeName;
                    }
                }
            }

            return string.Empty;
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
            blinkShapeIndex = skinnedMeshRenderer.sharedMesh.GetBlendShapeIndex(blinkBlendShapeName);
            blinkWeight = 0f;
            blinkVelocity = 0f;
            blinkAction = null;

            // Open the eyes
            skinnedMeshRenderer.SetBlendShapeWeight(blinkShapeIndex, 0);

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
            skinnedMeshRenderer.SetBlendShapeWeight(blinkShapeIndex, 0);
        }

        // Action for closing eyes called on every updates
        private void CloseEyesOnUpdate()
        {
            if (!IsBlinkEnabled)
            {
                return;
            }
            blinkWeight = Mathf.SmoothDamp(blinkWeight, 1, ref blinkVelocity, blinkTransitionToClose);
            skinnedMeshRenderer.SetBlendShapeWeight(blinkShapeIndex, blinkWeight * 100);
        }

        // Action for opening eyes called on every updates
        private void OpenEyesOnUpdate()
        {
            if (!IsBlinkEnabled)
            {
                return;
            }
            blinkWeight = Mathf.SmoothDamp(blinkWeight, 0, ref blinkVelocity, blinkTransitionToOpen);
            skinnedMeshRenderer.SetBlendShapeWeight(blinkShapeIndex, blinkWeight * 100);
        }
    }
}

using System;
using System.Linq;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Model
{
    public class Blink : MonoBehaviour, IBlink
    {
        [SerializeField] private SkinnedMeshRenderer skinnedMeshRenderer;

        [Header("Blink")]
        [Tooltip("Explicitly specify the BlendShape name for 'Blink'. If not set, it will be auto-detected.")]
        [SerializeField] private string blinkBlendShapeName;
        private static readonly string[] ExcludeBlinkKeywords = { "left", "right" };
        [Tooltip("Keywords used to auto-detect the BlendShape name for 'Blink' when not explicitly specified.")]
        [SerializeField] private string[] blinkKeywords = { "blink", "eye", "close" };
        private int blinkShapeIndex = -1;
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
        private bool isBlinkSetup { get; set; } = false;
        private Action blinkAction;
        private CancellationTokenSource blinkTokenSource;
        private bool blinkLoopAlreadyStarted = false;   // This doesn't back to false after once it turns to true

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
            isBlinkSetup = false;

            // Get facial skinned mesh renderer and blend shape name for blink
            if (skinnedMeshRenderer == null)
            {
                if (string.IsNullOrEmpty(blinkBlendShapeName))
                {
                    skinnedMeshRenderer = AvatarUtility.GetFacialSkinnedMeshRenderer(avatarObject);
                    if (skinnedMeshRenderer != null)
                    {
                        blinkBlendShapeName = GetBlinkTargetName(skinnedMeshRenderer);
                    }
                }
                else
                {
                    skinnedMeshRenderer = AvatarUtility.GetFacialSkinnedMeshRenderer(avatarObject, new[] { blinkBlendShapeName });
                }
            }
            else
            {
                if (string.IsNullOrEmpty(blinkBlendShapeName))
                {
                    blinkBlendShapeName = GetBlinkTargetName(skinnedMeshRenderer);
                }
            }

            if (skinnedMeshRenderer == null)
            {
                Debug.LogWarning("Facial SkinnedMeshRenderer not found.");
                return;
            }
            else if (string.IsNullOrEmpty(blinkBlendShapeName))
            {
                Debug.LogWarning("BlendShape for blink not found.");
                return;
            }

            blinkShapeIndex = skinnedMeshRenderer.sharedMesh.GetBlendShapeIndex(blinkBlendShapeName);
            if (blinkShapeIndex == -1)
            {
                Debug.LogWarning($"BlendShape for the name not found: {blinkBlendShapeName}");
                return;
            }

            isBlinkSetup = true;
            blinkTokenSource = new CancellationTokenSource();
        }

        public string GetBlinkShapeName()
        {
            return blinkBlendShapeName;
        }

        private string GetBlinkTargetName(SkinnedMeshRenderer skinnedMeshRenderer)
        {
            var mesh = skinnedMeshRenderer.sharedMesh;
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                var shapeName = mesh.GetBlendShapeName(i);
                var shapeNameLower = shapeName.ToLower();
                if (!ExcludeBlinkKeywords.Any(keyword => shapeNameLower.Contains(keyword)))
                {
                    if (blinkKeywords.Any(keyword => !string.IsNullOrEmpty(keyword) && shapeNameLower.Contains(keyword)))
                    {
                        return shapeName;
                    }
                }
            }

            return string.Empty;
        }

        // Initialize and start blink
        public async UniTask StartBlinkAsync()
        {
            // Return if not setup
            if (!isBlinkSetup)
            {
                return;
            }

            // Return with doing nothing when already blinking
            if (IsBlinkEnabled && blinkLoopAlreadyStarted)
            {
                return;
            }

            // Initialize
            blinkWeight = 0f;
            blinkVelocity = 0f;
            blinkAction = null;

            // Open the eyes
            skinnedMeshRenderer.SetBlendShapeWeight(blinkShapeIndex, 0);

            // Enable blink
            IsBlinkEnabled = true;

            if (blinkLoopAlreadyStarted) return;

            // Start new blink loop
            blinkLoopAlreadyStarted = true;
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

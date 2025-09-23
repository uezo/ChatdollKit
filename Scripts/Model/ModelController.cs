using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Model
{
    public class ModelController : MonoBehaviour
    {
        // Avator
        [Header("Avatar")]
        public GameObject AvatarModel;

        // Animation
        [Header("Animation")]
        [SerializeField]
        private float IdleAnimationDefaultDuration = 10.0f;
        [SerializeField]
        private string IdleAnimationKey = "BaseParam";
        [SerializeField]
        private int IdleAnimationValue;
        [SerializeField]
        private string layeredAnimationDefaultState = "Default";
        public float AnimationFadeLength = 0.5f;
        private Animator animator;
        private List<Animation> animationQueue = new List<Animation>();
        private float animationStartAt { get; set; }
        private Animation currentAnimation { get; set; }
        private Dictionary<string, List<Animation>> idleAnimations = new Dictionary<string, List<Animation>>();
        private Dictionary<string, List<int>> idleWeightedIndexes = new Dictionary<string, List<int>>();
        private Dictionary<string, FaceExpression> idleFaces = new Dictionary<string, FaceExpression>();
        public string IdlingMode { get; private set; } = "normal";
        public DateTime IdlingModeStartAt { get; private set; } = DateTime.UtcNow;
        private Func<Animation> GetAnimation;
        private Dictionary<string, Animation> registeredAnimations { get; set; } = new Dictionary<string, Animation>();

        // Speech
        public SpeechController SpeechController { get; set; }
        public Func<string, Dictionary<string, object>, CancellationToken, UniTask<AudioClip>> SpeechSynthesizerFunc
        {
            get { return SpeechController.SpeechSynthesizerFunc; }
            set { SpeechController.SpeechSynthesizerFunc = value; }
        }

        // Face
        public FaceController FaceController { get; set; }

        // Helpers
        private IBlink blinker { get; set; }
        private ILipSyncHelper lipSyncHelper;

        // History recorder for debug and test
        public ActionHistoryRecorder History;

        private void Awake()
        {
            GetAnimation = GetIdleAnimation;

            // Sub Controllers
            SpeechController = gameObject.GetComponent<SpeechController>();
            FaceController = gameObject.GetComponent<FaceController>();

            // LipSyncHelper
            lipSyncHelper = gameObject.GetComponent<ILipSyncHelper>();

            // Blinker
            blinker = gameObject.GetComponent<IBlink>();

            if (AvatarModel == null)
            {
                enabled = false;
            }
            else
            {
                ActivateAvatar();
            }
        }

        private void Start()
        {
            if (idleAnimations.Count == 0)
            {
                // Set idle animation if not registered
                Debug.LogWarning("Idle animations are not registered. Temprarily use dafault state as idle animation.");
                AddIdleAnimation(new Animation(
                    IdleAnimationKey, IdleAnimationValue, IdleAnimationDefaultDuration
                ));
            }

            // NOTE: Do not start idling here to prevent overwrite the animation that user invokes at Start() in other module.
            // Don't worry, idling will be started at UpdateAnimation() if user doesn't invoke their custom animation.
        }

        private void Update()
        {
            UpdateAnimation();
        }

        private void LateUpdate()
        {
            // Move to avatar position (because this game object includes AudioSource)
            gameObject.transform.position = AvatarModel.transform.position;
        }

#region Idling
        // Start idling
        public void StartIdling(bool resetStartTime = true)
        {
            // Only switch to idle if not already idling or if forced reset
            if (GetAnimation != GetIdleAnimation || resetStartTime)
            {
                GetAnimation = GetIdleAnimation;
                if (resetStartTime)
                {
                    animationStartAt = 0;
                }
            }
        }

        // Stop idling
        public void StopIdling()
        {
            GetAnimation = GetQueuedAnimation;
        }

        // Register idling animation
        public void AddIdleAnimation(Animation animation, int weight = 1, string mode = "normal")
        {
            if (!idleAnimations.ContainsKey(mode))
            {
                idleAnimations.Add(mode, new List<Animation>());
                idleWeightedIndexes.Add(mode, new List<int>());
            }

            idleAnimations[mode].Add(animation);

            // Set weight
            var index = idleAnimations[mode].Count - 1;
            for (var i = 0; i < weight; i++)
            {
                idleWeightedIndexes[mode].Add(index);
            }
        }

        public void AddIdleAnimation(string name, float duration, int weight = 1, string mode = "normal")
        {
            AddIdleAnimation(GetRegisteredAnimation(name, duration), weight, mode);
        }

        // Register idling face expression for mode
        public void AddIdleFace(string mode, string name)
        {
            idleFaces.Add(mode, new FaceExpression(name, float.MaxValue));
        }

        // Change idling mode
        public async UniTask ChangeIdlingModeAsync(string mode = "normal", Func<UniTask> onBeforeChange = null)
        {
            IdlingMode = mode;

            if (onBeforeChange != null)
            {
                await onBeforeChange();
            }

            if (idleFaces.ContainsKey(mode))
            {
                FaceController.SetFace(new List<FaceExpression>() { idleFaces[mode] });
            }
            else if (mode == "normal")
            {
                FaceController.SetFace(new List<FaceExpression>() { new FaceExpression("Neutral", 0.0f, string.Empty) });
            }
            IdlingModeStartAt = DateTime.UtcNow;

            StartIdling(true);
        }
#endregion

        // Speak with animation and face expression
        public async UniTask AnimatedSay(AnimatedVoiceRequest request, CancellationToken token)
        {
            // Speak, animate and express face sequentially
            foreach (var animatedVoice in request.AnimatedVoices)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                // Animation
                if (animatedVoice.Animations.Count > 0)
                {
                    Animate(animatedVoice.Animations);
                }

                // Face
                if (animatedVoice.Faces != null && animatedVoice.Faces.Count > 0)
                {
                    FaceController.SetFace(animatedVoice.Faces);
                }

                // Speech
                if (animatedVoice.Voices.Count > 0)
                {
                    // Wait for the requested voices end
                    await SpeechController.Say(animatedVoice.Voices, token);
                }
            }

            if (request.StartIdlingOnEnd && !token.IsCancellationRequested)
            {
                // Only reset to idle if we're not already idling
                // This prevents interrupting ongoing idle animations
                if (GetAnimation != GetIdleAnimation)
                {
                    // Wait for current animation to complete before starting idle
                    if (currentAnimation != null)
                    {
                        var remainingTime = currentAnimation.Duration - (Time.realtimeSinceStartup - animationStartAt);
                        if (remainingTime > 0)
                        {
                            await UniTask.Delay((int)(remainingTime * 1000), cancellationToken: token);
                        }
                    }
                    // Stop running animation not to keep the current state to the next turn (prompting)
                    // Do not start idling when cancel requested because the next animated voice request may include animations
                    StartIdling();
                }
                // If already idling, don't interrupt the current idle animation
            }
        }

        public AnimatedVoiceRequest ToAnimatedVoiceRequest(string inputText, string language = null)
        {
            var avreq = new AnimatedVoiceRequest();
            var preGap = 0f;
            var ttsConfig = new TTSConfiguration();
            if (!string.IsNullOrEmpty(language))
            {
                ttsConfig.Params["language"] = language;
            }

            var pattern = @"(\[.*?\])|([^[]+)";
            foreach (Match match in Regex.Matches(inputText, pattern))
            {
                var parsedText = match.Value.Trim();

                if (parsedText.StartsWith("[face:"))
                {
                    var face = parsedText.Substring(6, parsedText.Length - 7);
                    avreq.AddFace(face, duration: FaceController.DefaultFaceExpressionDuration);
                    ttsConfig.Params["style"] = face;
                }
                else if (parsedText.StartsWith("[anim:"))
                {
                    var anim = parsedText.Substring(6, parsedText.Length - 7);
                    if (IsAnimationRegistered(anim))
                    {
                        var a = GetRegisteredAnimation(anim);
                        avreq.AddAnimation(a.ParameterKey, a.ParameterValue, a.Duration, a.LayeredAnimationName, a.LayeredAnimationLayerName);
                    }
                    else
                    {
                        Debug.LogWarning($"Animation {anim} is not registered.");
                    }
                }
                else if (parsedText.StartsWith("[pause:"))
                {
                    var pauseValue = parsedText.Substring(7, parsedText.Length - 8);
                    if (float.TryParse(pauseValue, out float gap))
                    {
                        preGap = gap;
                    }
                }
                else if (parsedText.StartsWith("["))
                {
                    continue;
                }
                else
                {
                    avreq.AddVoice(parsedText, preGap, parsedText.EndsWith("。") ? 0 : 0.3f, ttsConfig: ttsConfig);
                    // Reset preGap. Do not reset ttsConfig to continue the style of voice.
                    preGap = 0f;
                }
            }

            return avreq;
        }

#region Animation
        // Set animations to queue
        public void Animate(List<Animation> animations)
        {
            ResetLayers();
            animationQueue.Clear();
            animationQueue = new List<Animation>(animations);
            animationStartAt = 0;
            GetAnimation = GetQueuedAnimation;
        }

        private void ResetLayers()
        {
            for (var i = 1; i < animator.layerCount; i++)
            {
                animator.CrossFadeInFixedTime(layeredAnimationDefaultState, AnimationFadeLength, i);
            }
        }

        private void UpdateAnimation()
        {
            // This method will be called every frames in `Update()`
            var animationToRun = GetAnimation();
            if (animationToRun == null)
            {
                // Start idling instead when animationToRun is null
                // `resetStartTime = false`: When idling, GetIdleAnimation() returns null if idle animation is not timeout.
                // So, StartIdling() will be called every frame while idle animation is not timeout.
                // If set 0 `animationStartAt` everytime StartIdling() called, idle animation changes every frame.
                // This `false` prevents reset `animationStartAt` to keep running idle animation.
                StartIdling(false);
                return;
            }

            if (currentAnimation == null || animationToRun.Id != currentAnimation.Id)
            {
                // Start new animation
                ResetLayers();
                animator.SetInteger(animationToRun.ParameterKey, animationToRun.ParameterValue);
                if (!string.IsNullOrEmpty(animationToRun.LayeredAnimationName))
                {
                    var layerIndex = animator.GetLayerIndex(animationToRun.LayeredAnimationLayerName);
                    animator.CrossFadeInFixedTime(animationToRun.LayeredAnimationName, AnimationFadeLength, layerIndex);
                }
                currentAnimation = animationToRun;
                animationStartAt = Time.realtimeSinceStartup;
            }
        }

        private Animation GetQueuedAnimation()
        {
            if (animationQueue.Count == 0) return default;

            if (animationStartAt > 0 && currentAnimation != null && Time.realtimeSinceStartup - animationStartAt > currentAnimation.Duration)
            {
                // Remove the first animation and reset animationStartAt when:
                // - Not right after the Animate() called (animationStartAt > 0)
                // - Animation is timeout (Time.realtimeSinceStartup - animationStartAt > currentAnimation.Duration)
                animationQueue.RemoveAt(0);
                animationStartAt = 0;

                // If queue is empty after removing, ensure smooth transition
                if (animationQueue.Count == 0)
                {
                    // Let the current animation finish naturally before transitioning
                    return default;
                }
            }

            if (animationQueue.Count == 0) return default;

            return animationQueue.First();
        }

        private Animation GetIdleAnimation()
        {
            if (currentAnimation == null || animationStartAt == 0 || Time.realtimeSinceStartup - animationStartAt > currentAnimation.Duration)
            {
                // Return random idle animation when:
                // - Animation is not running currently (currentAnimation == null)
                // - Right after StartIdling() called (animationStartAt == 0)
                // - Idle animation is timeout (Time.realtimeSinceStartup - animationStartAt > currentAnimation.Duration)
                var i = UnityEngine.Random.Range(0, idleWeightedIndexes[IdlingMode].Count);
                return idleAnimations[IdlingMode][idleWeightedIndexes[IdlingMode][i]];
            }
            else
            {
                return default;
            }
        }

        public void RegisterAnimations(Dictionary<string, Animation> animations)
        {
            foreach (var animation in animations)
            {
                RegisterAnimation(animation.Key, animation.Value);
            }
        }

        public void RegisterAnimation(string name, Animation animation)
        {
            registeredAnimations[name] = animation;
        }

        public Animation GetRegisteredAnimation(string name, float duration = 0.0f, string layeredAnimationName = null, string layeredAnimationLayerName = null)
        {
            return new Animation(
                registeredAnimations[name].ParameterKey,
                registeredAnimations[name].ParameterValue,
                duration == 0.0f ? registeredAnimations[name].Duration : duration,
                layeredAnimationName ?? registeredAnimations[name].LayeredAnimationName,
                layeredAnimationLayerName ?? registeredAnimations[name].LayeredAnimationLayerName
            );
        }

        public bool IsAnimationRegistered(string name)
        {
            return registeredAnimations.ContainsKey(name);
        }

        public string ListRegisteredAnimations(string itemTemplate = null)
        {
            var template = (itemTemplate == null ? "- {0}" : itemTemplate) + "\n";
            var list = "";
            foreach (var item in registeredAnimations.Keys)
            {
                list += string.Format(template, item);
            }
            return list;
        }
#endregion


#region Avatar
        public void ActivateAvatar(GameObject avatarObject = null, bool configureViseme = false)
        {
            if (avatarObject != null)
            {
                AvatarModel = avatarObject;
            }
            animator = AvatarModel.GetComponent<Animator>();

            // Blink (Blink at first because FaceExpression depends blink)
            blinker.Setup(AvatarModel);
            FaceController.Setup(AvatarModel);

            if (configureViseme)
            {
                lipSyncHelper.ConfigureViseme(AvatarModel);
            }

            _ = blinker.StartBlinkAsync();
            currentAnimation = null;  // Set null to newly start idling animation

            AvatarModel.SetActive(true);
            enabled = true;
        }

        public void DeactivateAvatar(Action unloadAction = null)
        {
            if (AvatarModel == null) return;

            enabled = false;
            blinker.StopBlink();

            AvatarModel.SetActive(false);
            unloadAction?.Invoke();
        }
#endregion
    }
}

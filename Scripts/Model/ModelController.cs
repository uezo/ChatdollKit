using System;
using System.Collections.Generic;
using System.Linq;
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

        // Audio
        [Header("Voice")]
        public AudioSource AudioSource;
        private Dictionary<string, AudioClip> voiceAudioClips = new Dictionary<string, AudioClip>();
        public Func<Voice, CancellationToken, UniTask<AudioClip>> VoiceDownloadFunc;
        public Func<Voice, CancellationToken, UniTask<AudioClip>> TextToSpeechFunc;
        public Action<string, CancellationToken> OnSayStart;
        public Action OnSayEnd;
        public Dictionary<string, Func<Voice, CancellationToken, UniTask<AudioClip>>> TextToSpeechFunctions = new Dictionary<string, Func<Voice, CancellationToken, UniTask<AudioClip>>>();
        public bool UsePrefetch = true;
        private ILipSyncHelper lipSyncHelper;

        // Animation
        [Header("Animation")]
        [SerializeField]
        private float IdleAnimationDefaultDuration = 10.0f;
        [SerializeField]
        private string IdleAnimationKey = "BaseParam";
        [SerializeField]
        private int IdleAnimationValue;
        public Func<CancellationToken, UniTask> IdleFunc;
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

        // Face Expression
        [Header("Face")]
        public SkinnedMeshRenderer SkinnedMeshRenderer;
        private IFaceExpressionProxy faceExpressionProxy;
        private List<FaceExpression> faceQueue = new List<FaceExpression>();
        private float faceStartAt { get; set; }
        private FaceExpression currentFace { get; set; }

        // History recorder for debug and test
        public ActionHistoryRecorder History;

        private void Awake()
        {
            animator = AvatarModel.gameObject.GetComponent<Animator>();
            GetAnimation = GetIdleAnimation;

            // Web and TTS voice loaders
            foreach (var loader in gameObject.GetComponents<WebVoiceLoaderBase>())
            {
                if (!loader.enabled)
                {
                    continue;
                }

                if (loader.Type == VoiceLoaderType.Web)
                {
                    VoiceDownloadFunc = loader.GetAudioClipAsync;
                }
                else if (loader.Type == VoiceLoaderType.TTS)
                {
                    RegisterTTSFunction(loader.Name, loader.GetAudioClipAsync, loader.IsDefault);
                }
            }

            // Get lipSyncHelper
            lipSyncHelper = gameObject.GetComponent<ILipSyncHelper>();

            // Face expression proxy
            faceExpressionProxy = gameObject.GetComponent<IFaceExpressionProxy>();
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
            UpdateFace();
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
            GetAnimation = GetIdleAnimation;
            if (resetStartTime)
            {
                animationStartAt = 0;
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
                SetFace(new List<FaceExpression>() { idleFaces[mode] });
            }
            else if (mode == "normal")
            {
                SetFace(new List<FaceExpression>() { new FaceExpression("Neutral", 0.0f, string.Empty) });
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
                    SetFace(animatedVoice.Faces);
                }

                // Speech
                if (animatedVoice.Voices.Count > 0)
                {
                    // Wait for the requested voices end
                    await Say(animatedVoice.Voices, token);
                }
            }

            if (request.StartIdlingOnEnd && !token.IsCancellationRequested)
            {
                // Stop running animation not to keep the current state to the next turn (prompting)
                // Do not start idling when cancel requested because the next animated voice request may include animations
                StartIdling();
            }
        }

#region Speech
        // Speak
        public async UniTask Say(List<Voice> voices, CancellationToken token)
        {
            // Stop speech
            StopSpeech();

            // Prefetch Web/TTS voice
            if (UsePrefetch)
            {
                PrefetchVoices(voices, token);
            }

            // Speak sequentially
            foreach (var v in voices)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                OnSayStart?.Invoke(v.Text, token);

                try
                {
                    if (v.Source == VoiceSource.Local)
                    {
                        if (voiceAudioClips.ContainsKey(v.Name))
                        {
                            // Wait for PreGap
                            await UniTask.Delay((int)(v.PreGap * 1000), cancellationToken: token);
                            // Play audio
                            History?.Add(v);
                            AudioSource.PlayOneShot(voiceAudioClips[v.Name]);
                        }
                        else
                        {
                            Debug.LogWarning($"Voice not found: {v.Name}");
                        }
                    }
                    else
                    {
                        // Download voice from web or TTS service
                        var downloadStartTime = Time.time;
                        AudioClip clip = null;
                        if (v.Source == VoiceSource.Web)
                        {
                            if (VoiceDownloadFunc != null)
                            {
                                clip = await VoiceDownloadFunc(v, token);
                            }
                            else
                            {
                                Debug.LogError("Voice download function not found");
                            }
                        }
                        else if (v.Source == VoiceSource.TTS)
                        {
                            var ttsFunc = GetTTSFunction(v.GetTTSFunctionName());
                            if (ttsFunc != null)
                            {
                                clip = await ttsFunc(v, token);
                            }
                            else
                            {
                                Debug.LogError($"TTS function not found: {v.GetTTSFunctionName()}");
                            }
                        }

                        if (clip != null)
                        {
                            // Wait for PreGap remains after download
                            var preGap = v.PreGap - (Time.time - downloadStartTime);
                            if (preGap > 0)
                            {
                                if (!token.IsCancellationRequested)
                                {
                                    try
                                    {
                                        await UniTask.Delay((int)(preGap * 1000), cancellationToken: token);
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        // OperationCanceledException raises
                                        Debug.Log("Task canceled in waiting PreGap");
                                    }
                                }
                            }
                            // Play audio
                            History?.Add(v);
                            AudioSource.PlayOneShot(clip);
                        }
                    }

                    // Wait while voice playing
                    while (AudioSource.isPlaying && !token.IsCancellationRequested)
                    {
                        await UniTask.Delay(1);
                    }

                    if (!token.IsCancellationRequested)
                    {
                        try
                        {
                            // Wait for PostGap
                            await UniTask.Delay((int)(v.PostGap * 1000), cancellationToken: token);
                        }
                        catch (OperationCanceledException)
                        {
                            Debug.Log("Task canceled in waiting PostGap");
                        }
                    }
                }

                catch (Exception ex)
                {
                    Debug.LogError($"Error at Say: {ex.Message}\n{ex.StackTrace}");
                }

                finally
                {
                    OnSayEnd?.Invoke();
                }
            }

            // Reset viseme
            lipSyncHelper?.ResetViseme();
        }

        // Start downloading voices from web/TTS
        private void PrefetchVoices(List<Voice> voices, CancellationToken token)
        {
            foreach (var voice in voices)
            {
                if (voice.Source == VoiceSource.Web)
                {
                    VoiceDownloadFunc?.Invoke(voice, token);
                }
                else if (voice.Source == VoiceSource.TTS)
                {
                    var ttsFunc = GetTTSFunction(voice.GetTTSFunctionName());
                    ttsFunc?.Invoke(voice, token);
                }
            }
        }

        // Stop speech
        public void StopSpeech()
        {
            History?.Add("Stop speech");
            AudioSource.Stop();
        }

        // Register voice with its name
        public void AddVoice(string name, AudioClip audioClip)
        {
            voiceAudioClips[ReplaceDakuten(name)] = audioClip;
        }

        // Get registered TTS Function by name
        private Func<Voice, CancellationToken, UniTask<AudioClip>> GetTTSFunction(string name)
        {
            if (!string.IsNullOrEmpty(name) && TextToSpeechFunctions.ContainsKey(name))
            {
                return TextToSpeechFunctions[name];
            }
            return TextToSpeechFunc;
        }

        // Register TTS Function with name
        public void RegisterTTSFunction(string name, Func<Voice, CancellationToken, UniTask<AudioClip>> func, bool asDefault = false)
        {
            TextToSpeechFunctions[name] = func;
            if (asDefault)
            {
                TextToSpeechFunc = func;
            }
        }

        // Replace Japanese Dakuten from resource files
        private string ReplaceDakuten(string value)
        {
            var ret = value;
            var dt = "゙";
            var hdt = "゚";

            if (value.Contains(dt))
            {
                // Hiragana
                ret = ret.Replace($"か{dt}", "が").Replace($"き{dt}", "ぎ").Replace($"く{dt}", "ぐ").Replace($"け{dt}", "げ").Replace($"こ{dt}", "ご");
                ret = ret.Replace($"さ{dt}", "ざ").Replace($"し{dt}", "じ").Replace($"す{dt}", "ず").Replace($"せ{dt}", "ぜ").Replace($"そ{dt}", "ぞ");
                ret = ret.Replace($"た{dt}", "だ").Replace($"ち{dt}", "ぢ").Replace($"つ{dt}", "づ").Replace($"て{dt}", "で").Replace($"と{dt}", "ど");
                ret = ret.Replace($"は{dt}", "ば").Replace($"ひ{dt}", "び").Replace($"ふ{dt}", "ぶ").Replace($"へ{dt}", "べ").Replace($"ほ{dt}", "ぼ");
                // Katakana
                ret = ret.Replace($"カ{dt}", "ガ").Replace($"キ{dt}", "ギ").Replace($"ク{dt}", "グ").Replace($"け{dt}", "げ").Replace($"こ{dt}", "ご");
                ret = ret.Replace($"サ{dt}", "ザ").Replace($"シ{dt}", "ジ").Replace($"ス{dt}", "ズ").Replace($"せ{dt}", "ぜ").Replace($"そ{dt}", "ぞ");
                ret = ret.Replace($"タ{dt}", "ダ").Replace($"チ{dt}", "ヂ").Replace($"ツ{dt}", "ヅ").Replace($"て{dt}", "で").Replace($"と{dt}", "ど");
                ret = ret.Replace($"ハ{dt}", "バ").Replace($"ヒ{dt}", "ビ").Replace($"フ{dt}", "ブ").Replace($"へ{dt}", "べ").Replace($"ほ{dt}", "ぼ");

            }
            if (value.Contains(hdt))
            {
                ret = ret.Replace($"は{hdt}", "ぱ").Replace($"ひ{hdt}", "ぴ").Replace($"ふ{hdt}", "ぷ").Replace($"へ{hdt}", "ぺ").Replace($"ほ{hdt}", "ぽ");
            }

            return ret;
        }
#endregion


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

            if (animationStartAt > 0 && Time.realtimeSinceStartup - animationStartAt > currentAnimation.Duration)
            {
                // Remove the first animation and reset animationStartAt when:
                // - Not right after the Animate() called (animationStartAt > 0)
                // - Animation is timeout (Time.realtimeSinceStartup - animationStartAt > currentAnimation.Duration)
                animationQueue.RemoveAt(0);
                animationStartAt = 0;
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
#endregion


#region Face Expression
        // Set face expressions
        public void SetFace(List<FaceExpression> faces)
        {
            faceQueue.Clear();
            faceQueue = new List<FaceExpression>(faces);    // Copy faces not to change original list
            faceStartAt = 0;
        }

        private void UpdateFace()
        {
            // This method will be called every frames in `Update()`
            var faceToSet = GetFaceExpression();
            if (faceToSet == null)
            {
                // Set neutral instead when faceToSet is null
                SetFace(new List<FaceExpression>() { new FaceExpression("Neutral", 0.0f, string.Empty) });
                return;
            }

            if (currentFace == null || faceToSet.Name != currentFace.Name || faceToSet.Duration != currentFace.Duration)
            {
                // Set new face
                faceExpressionProxy.SetExpressionSmoothly(faceToSet.Name, 1.0f);
                currentFace = faceToSet;
                faceStartAt = Time.realtimeSinceStartup;
            }
        }

        public FaceExpression GetFaceExpression()
        {
            if (faceQueue.Count == 0) return default;

            if (faceStartAt > 0 && Time.realtimeSinceStartup - faceStartAt > currentFace.Duration)
            {
                // Remove the first face after the duration passed
                faceQueue.RemoveAt(0);
                faceStartAt = 0;
            }

            if (faceQueue.Count == 0) return default;

            return faceQueue.First();
        }
#endregion

#region Avatar
        public void SetAvatar(GameObject avatarObject = null, bool activation = false)
        {
            var currentAvatarObject = animator.gameObject;
            var newAvatarObject = avatarObject == null ? AvatarModel : avatarObject;

            if (activation)
            {
                currentAvatarObject.SetActive(false);
            }

            // Animator
            animator = newAvatarObject.gameObject.GetComponent<Animator>();

            // Blink (Blink at first because FaceExpression depends blink)
            // TODO: Make the dependency simple
            GetComponent<IBlink>()?.Setup(newAvatarObject);

            // Face expression
            faceExpressionProxy.Setup(newAvatarObject);

            // LipSync
            lipSyncHelper.ConfigureViseme(newAvatarObject);

            // Start idling
            currentAnimation = null;  // Set null to newly start idling animation
            StartIdling(true);

            if (activation)
            {
                newAvatarObject.SetActive(true);
            }
        }
#endregion
    }
}

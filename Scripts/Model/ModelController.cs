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
        private Dictionary<string, AudioClip> voices = new Dictionary<string, AudioClip>();
        public Func<Voice, CancellationToken, UniTask<AudioClip>> VoiceDownloadFunc;
        public Func<Voice, CancellationToken, UniTask<AudioClip>> TextToSpeechFunc;
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
        private List<Animation> idleAnimations = new List<Animation>();
        private List<int> idleWeightedIndexes = new List<int>();
        private Func<Animation> GetAnimation;

        // Face Expression
        [Header("Face")]
        public SkinnedMeshRenderer SkinnedMeshRenderer;
        private IFaceExpressionProxy faceExpressionProxy;

        // History recorder for debug and test
        public ActionHistoryRecorder History;

        private void Awake()
        {
            animator = AvatarModel.gameObject.GetComponent<Animator>();

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
            // Start default animation
            if (idleAnimations.Count == 0)
            {
                // Set idle animation if not registered
                Debug.LogWarning("Idle animations are not registered. Temprarily use dafault state as idle animation.");
                AddIdleAnimation(new Animation(
                    IdleAnimationKey, IdleAnimationValue, IdleAnimationDefaultDuration
                ));
            }
            StartIdling();
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
        public void StartIdling()
        {
            GetAnimation = GetIdleAnimation;
        }

        // Stop idling
        public void StopIdling()
        {
            GetAnimation = GetQueuedAnimation;
        }

        // Register idling animation
        public void AddIdleAnimation(Animation animation, int weight = 1)
        {
            idleAnimations.Add(animation);

            // Set weight
            var index = idleAnimations.Count - 1;
            for (var i = 0; i < weight; i++)
            {
                idleWeightedIndexes.Add(index);
            }
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
                    _ = SetFace(new FaceRequest(animatedVoice.Faces), token);
                }

                // Speech
                if (animatedVoice.Voices.Count > 0)
                {
                    // Wait for the requested voices end
                    await Say(new VoiceRequest(animatedVoice.Voices), token);
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
        // Speak one phrase
        public async UniTask Say(string voiceName, float preGap = 0f, float postGap = 0f)
        {
            var request = new VoiceRequest(voiceName);
            request.Voices[0].PreGap = preGap;
            request.Voices[0].PostGap = postGap;
            await Say(request, new CancellationTokenSource().Token); // ノンキャンセラブル
        }

        // Speak
        public async UniTask Say(VoiceRequest request, CancellationToken token)
        {
            // Stop speech
            StopSpeech();

            // Prefetch Web/TTS voice
            if (UsePrefetch)
            {
                PrefetchVoices(request.Voices, token);
            }

            // Speak sequentially
            foreach (var v in request.Voices)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (v.Source == VoiceSource.Local)
                {
                    if (voices.ContainsKey(v.Name))
                    {
                        // Wait for PreGap
                        await UniTask.Delay((int)(v.PreGap * 1000), cancellationToken: token);
                        // Play audio
                        History?.Add(v);
                        AudioSource.PlayOneShot(voices[v.Name]);
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
            voices[ReplaceDakuten(name)] = audioClip;
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
                StartIdling();
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
                // Remove the first animation after the duration passed
                animationQueue.RemoveAt(0);
                animationStartAt = 0;
            }

            if (animationQueue.Count == 0) return default;

            return animationQueue.First();
        }

        private Animation GetIdleAnimation()
        {
            var i = UnityEngine.Random.Range(0, idleWeightedIndexes.Count);
            return idleAnimations[idleWeightedIndexes[i]];            
        }
#endregion


        #region Face Expression
        // Set face expressions
        public async UniTask SetFace(FaceRequest request, CancellationToken token)
        {
            foreach (var face in request.Faces)
            {
                faceExpressionProxy.SetExpressionSmoothly(face.Name, 1.0f);
                await UniTask.Delay((int)(face.Duration * 1000), cancellationToken: token);

                if (token.IsCancellationRequested) break;
            }
        }
#endregion
    }
}

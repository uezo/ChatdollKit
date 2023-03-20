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
        private Animator animator;
        [Header("Animation")]
        public float AnimationFadeLength = 0.5f;
        public float IdleAnimationDefaultDuration = 10.0f;
        private List<AnimatedVoiceRequest> idleAnimatedVoiceRequests = new List<AnimatedVoiceRequest>();
        private List<int> idleWeightedIndexes = new List<int>();
        public Func<CancellationToken, UniTask> IdleFunc;
        private CancellationTokenSource idleTokenSource;
        public string AnimatorDefaultState = "Default";

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
            if (idleAnimatedVoiceRequests.Count == 0)
            {
                // Set idle animation if not registered
                Debug.LogWarning("Idle animations are not registered. Temprarily use dafault state as idle animation.");
                AddIdleAnimation(AnimatorDefaultState);
            }
            _ = StartIdlingAsync();
        }

        private void LateUpdate()
        {
            // Move to avatar position (because this game object includes AudioSource)
            gameObject.transform.position = AvatarModel.transform.position;
        }

        private void OnDestroy()
        {
            idleTokenSource?.Cancel();
        }

#region Idling
        // Start idling
        public async UniTask StartIdlingAsync()
        {
            // Stop running idle loop
            StopIdling();

            // Create new default animation token
            idleTokenSource = new CancellationTokenSource();
            var token = idleTokenSource.Token;

            History?.Add("Start idling");
            if (IdleFunc == null)
            {
                while (!token.IsCancellationRequested)
                {
                    var i = UnityEngine.Random.Range(0, idleWeightedIndexes.Count);
                    await AnimatedSay(idleAnimatedVoiceRequests[idleWeightedIndexes[i]], token);
                }
            }
            else
            {
                await IdleFunc(token);
            }
        }

        // Stop idling
        public void StopIdling()
        {
            History?.Add("Stop idling");
            // Stop running default animation loop
            if (idleTokenSource != null)
            {
                idleTokenSource.Cancel();
                idleTokenSource.Dispose();
                idleTokenSource = null;
            }
        }

        // Shortcut to register idling animation
        public void AddIdleAnimation(string animationName, string faceName = null, float duration = 0.0f, float fadeLength = -1.0f, float blendWeight = 1.0f, float preGap = 0.0f, int weight = 1)
        {
            var animatedVoiceRequest = new AnimatedVoiceRequest();
            animatedVoiceRequest.AddAnimation(
                animationName,
                duration == 0.0f ? IdleAnimationDefaultDuration : duration,
                fadeLength, blendWeight, preGap, "idle", true);
            if (faceName != null)
            {
                animatedVoiceRequest.AddFace(faceName, description: "idle");
            }
            AddIdleAnimation(animatedVoiceRequest, weight);
        }

        // Register idling animation
        public void AddIdleAnimation(AnimatedVoiceRequest animatedVoiceRequest, int weight = 1)
        {
            // Add animated voice request
            animatedVoiceRequest.StartIdlingOnEnd = false;
            animatedVoiceRequest.StopIdlingOnStart = false;
            animatedVoiceRequest.StopLayeredAnimations = true;
            idleAnimatedVoiceRequests.Add(animatedVoiceRequest);

            // Set weight
            var index = idleAnimatedVoiceRequests.Count - 1;
            for (var i = 0; i < weight; i++)
            {
                idleWeightedIndexes.Add(index);
            }
        }
#endregion

        // Speak with animation and face expression
        public async UniTask AnimatedSay(AnimatedVoiceRequest request, CancellationToken token)
        {
            // Stop default animation loop
            if (request.StopIdlingOnStart && !token.IsCancellationRequested)
            {
                StopIdling();
            }

            // Speak, animate and express face sequentially
            foreach (var animatedVoice in request.AnimatedVoices)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                UniTask animationTask;
                if (animatedVoice.Animations != null && animatedVoice.Animations.Count > 0)
                {
                    animationTask = Animate(new AnimationRequest(animatedVoice.Animations, false, false, request.StopLayeredAnimations), token);
                }
                else
                {
                    animationTask = UniTask.Delay(1);   // Set empty task
                }
                
                if (animatedVoice.Faces != null && animatedVoice.Faces.Count > 0)
                {
                    _ = SetFace(new FaceRequest(animatedVoice.Faces), token);
                }

                if (animatedVoice.Voices.Count > 0)
                {
                    // Wait for the requested voices end
                    await Say(new VoiceRequest(animatedVoice.Voices), token);
                }
                else
                {
                    // Wait for the requested animations end
                    await animationTask;
                }
            }

            // Restart idling and reset face
            if (request.StartIdlingOnEnd && !token.IsCancellationRequested)
            {
                _ = StartIdlingAsync();
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
        // Do animations
        public async UniTask Animate(AnimationRequest request, CancellationToken token)
        {
            // Stop default animation loop
            if (request.StopIdlingOnStart && !token.IsCancellationRequested)
            {
                StopIdling();
            }

            // Animate sequentially
            var baseTask = PlayAnimations(request.BaseLayerAnimations, request.BaseLayerName, token);
            foreach (var anims in request.Animations)
            {
                if (anims.Key != request.BaseLayerName)
                {
                    _ = PlayAnimations(anims.Value, anims.Key, token);
                }
            }
            // Stop layered animations
            if (request.StopLayeredAnimations)
            {
                ResetLayers(request.Animations.Keys.ToList());
            }

            await baseTask;

            // Restart default animation
            if (request.StartIdlingOnEnd && request.BaseLayerAnimations.Count > 0 && request.BaseLayerAnimations.Last().Duration > 0)
            {
                _ = StartIdlingAsync();
            }
        }

        private async UniTask PlayAnimations(List<Animation> animations, string layerName, CancellationToken token)
        {
            var layerIndex = layerName == string.Empty ? 0 : animator.GetLayerIndex(layerName);
            foreach (var a in animations)
            {
                var histroyId = string.Empty;
                try
                {
                    if (a.PreGap > 0)
                    {
                        await UniTask.Delay((int)(a.PreGap * 1000), cancellationToken: token);
                    }
                    animator.SetLayerWeight(layerIndex, a.Weight);
                    histroyId = History?.Add(a);
                    animator.CrossFadeInFixedTime(a.Name, a.FadeLength < 0 ? AnimationFadeLength : a.FadeLength, layerIndex);
                    await UniTask.Delay((int)(a.Duration * 1000), cancellationToken: token);
                }
                catch (OperationCanceledException tcex)
                {
                    History?.Add(histroyId, "canceled");
                    Debug.Log($"Animation {a.Name} on {a.LayerName} canceled: {tcex.Message}");
                }
                catch (Exception ex)
                {
                    History?.Add(histroyId, "error");
                    Debug.LogError($"Error occured in playing animation: {ex.Message}\n{ex.StackTrace}");
                }
                finally
                {
                    animator.SetLayerWeight(layerIndex, 1.0f);
                }
            }
        }

        private void ResetLayers(List<string> excludeLayerNames)
        {
            for (var i = 0; i < animator.layerCount; i++)
            {
                if (i == 0 || excludeLayerNames.Contains(animator.GetLayerName(i)))
                {
                    continue;
                }
                else
                {
                    animator.CrossFadeInFixedTime(AnimatorDefaultState, AnimationFadeLength, i);
                }
            }
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
            }
        }
#endregion
    }
}

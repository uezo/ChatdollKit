using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;


namespace ChatdollKit.Model
{
    // Model controller
    public class ModelController : MonoBehaviour
    {
        // Audio
        [Header("Voice")]
        public AudioSource AudioSource;
        public OVRLipSyncContext LipSyncContext;
        private Dictionary<string, AudioClip> voices = new Dictionary<string, AudioClip>();
        public Func<Voice, Task<AudioClip>> VoiceDownloadFunc;
        public Func<Voice, Task<AudioClip>> TextToSpeechFunc;
        public Dictionary<string, Func<Voice, Task<AudioClip>>> TextToSpeechFunctions = new Dictionary<string, Func<Voice, Task<AudioClip>>>();
        public bool UsePrefetch = true;

        // Animation
        private Animator animator;
        [Header("Animation")]
        public float AnimationFadeLength = 0.5f;
        public float IdleAnimationDefaultDuration = 10.0f;
        private List<AnimationRequest> idleAnimationRequests = new List<AnimationRequest>();
        public Func<CancellationToken, Task> IdleFunc;
        private CancellationTokenSource idleTokenSource;
        public string AnimatorDefaultState = "Default";

        // Blink
        [Header("Blink")]
        public string BlinkBlendShapeName;
        private int blinkShapeIndex;
        public float MinBlinkIntervalToClose = 3.0f;
        public float MaxBlinkIntervalToClose = 5.0f;
        public float MinBlinkIntervalToOpen = 0.05f;
        public float MaxBlinkIntervalToOpen = 0.1f;
        public float BlinkTransitionToClose = 0.01f;
        public float BlinkTransitionToOpen = 0.02f;
        private float blinkIntervalToClose;
        private float blinkIntervalToOpen;
        private float blinkWeight = 0.0f;
        private float blinkVelocity = 0.0f;
        private bool isBlinkEnabled = false;
        private Action blinkAction;
        private CancellationTokenSource blinkTokenSource;

        // Face Expression
        [Header("Face")]
        public SkinnedMeshRenderer SkinnedMeshRenderer;
        public string FaceConfigurationFile;
        private Dictionary<string, FaceClip> faceClips = new Dictionary<string, FaceClip>();
        private FaceRequest DefaultFace;

        // History recorder for debug and test
        public ActionHistoryRecorder History;

        private void Awake()
        {
            animator = gameObject.GetComponent<Animator>();
            blinkTokenSource = new CancellationTokenSource();

            if (!string.IsNullOrEmpty(FaceConfigurationFile))
            {
                // Load at Await() to overwrite at Start()
                LoadFacesFromFile();
            }

            // Web and TTS voice loaders
            foreach (var loader in gameObject.GetComponents<IVoiceLoader>())
            {
                if (loader.Type == VoiceLoaderType.Web)
                {
                    VoiceDownloadFunc = loader.GetAudioClipAsync;
                }
                else if (loader.Type == VoiceLoaderType.TTS)
                {
                    RegisterTTSFunction(loader.Name, loader.GetAudioClipAsync, loader.IsDefault);
                }
            }
        }

        private void Start()
        {
            if (DefaultFace == null)
            {
                // Set default face if not registered
                Debug.LogWarning("Default face expression is not registered. Temprarily use zero-weigths-face as default.");
                AddFace("DefaultFace", new Dictionary<string, float>(), asDefault: true);
            }
            _ = SetDefaultFace();

            // Start default animation
            if (idleAnimationRequests.Count == 0)
            {
                // Set idle animation if not registered
                Debug.LogWarning("Idle animations are not registered. Temprarily use dafault state as idle animation.");
                AddIdleAnimation(AnimatorDefaultState);
            }
            _ = StartIdlingAsync();

            // Start blink
            if (string.IsNullOrEmpty(BlinkBlendShapeName))
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
            // Update blink status
            blinkAction?.Invoke();
        }

        private void OnDestroy()
        {
            blinkTokenSource?.Cancel();
            idleTokenSource?.Cancel();
        }

        // Start idling
        public async Task StartIdlingAsync()
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
                    var i = UnityEngine.Random.Range(0, idleAnimationRequests.Count);
                    var request = idleAnimationRequests[i];
                    request.DisableBlink = false;
                    request.StartIdlingOnEnd = false;
                    request.StopIdlingOnStart = false;
                    request.StopLayeredAnimations = true;
                    await Animate(request, token);
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

        // Shortcut method to register idling animation
        public void AddIdleAnimation(string name, float duration = 0.0f, float fadeLength = -1.0f, float weight = 1.0f, float preGap = 0.0f, bool addToLastRequest = false)
        {
            AddIdleAnimation(name, null, duration, fadeLength, weight, preGap, addToLastRequest);
        }

        public void AddIdleAnimation(string name, string layerName, float duration = 0.0f, float fadeLength = -1.0f, float weight = 1.0f, float preGap = 0.0f, bool addToLastRequest = false)
        {
            if (addToLastRequest)
            {
                var request = idleAnimationRequests.Last();
                request.AddAnimation(name, layerName ?? request.BaseLayerName, duration == 0.0f ? IdleAnimationDefaultDuration : duration, fadeLength, weight, preGap, "idle");
            }
            else
            {
                var request = new AnimationRequest();
                request.AddAnimation(name, layerName ?? request.BaseLayerName, duration == 0.0f ? IdleAnimationDefaultDuration : duration, fadeLength, weight, preGap, "idle");
                idleAnimationRequests.Add(request);
            }
        }

        // Speak with animation and face expression
        public async Task AnimatedSay(AnimatedVoiceRequest request, CancellationToken token)
        {
            // Stop blink
            if (request.DisableBlink && !token.IsCancellationRequested)
            {
                StopBlink();
            }

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
                    return;
                }
                if (animatedVoice.Animations != null && animatedVoice.Animations.Count > 0)
                {
                    _ = Animate(new AnimationRequest(animatedVoice.Animations, false, false, false, request.StopLayeredAnimations), token);
                }
                if (animatedVoice.Faces != null && animatedVoice.Faces.Count > 0)
                {
                    _ = SetFace(new FaceRequest(animatedVoice.Faces, false));
                }
                // Wait for the requested voices end
                await Say(new VoiceRequest(animatedVoice.Voices, false), token);
            }

            // Restart idling and reset face
            if (request.StartIdlingOnEnd && !token.IsCancellationRequested)
            {
                _ = StartIdlingAsync();
            }

            // Restore default face expression
            if (request.StartIdlingOnEnd)
            {
                _ = SetDefaultFace();
            }

            // Restart blink
            if (request.DisableBlink && !token.IsCancellationRequested)
            {
                _ = StartBlinkAsync();
            }
        }

        // AnimatedSay from JSON file
        public async Task AnimatedSay(string filePath, CancellationToken token)
        {
            AnimatedVoiceRequest animatedVoiceRequest;

            try
            {
                var jsonString = string.Empty;
                using (var reader = File.OpenText(filePath))
                {
                    jsonString = await reader.ReadToEndAsync();
                }

                animatedVoiceRequest = JsonConvert.DeserializeObject<AnimatedVoiceRequest>(jsonString);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in AnimatedSay from file {filePath}: {ex.Message}\n{ex.StackTrace}");
                return;
            }

            await AnimatedSay(animatedVoiceRequest, token);
        }

        // Speak one phrase
        public async Task Say(string voiceName, float preGap = 0f, float postGap = 0f)
        {
            var request = new VoiceRequest(voiceName);
            request.Voices[0].PreGap = preGap;
            request.Voices[0].PostGap = postGap;
            await Say(request, new CancellationTokenSource().Token); // ノンキャンセラブル
        }

        // Speak
        public async Task Say(VoiceRequest request, CancellationToken token)
        {
            // Stop speech
            StopSpeech();

            // Stop blink
            if (request.DisableBlink && !token.IsCancellationRequested)
            {
                StopBlink();
            }

            // Prefetch Web/TTS voice
            if (UsePrefetch)
            {
                PrefetchVoices(request.Voices);
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
                        await Task.Delay((int)(v.PreGap * 1000), token);
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
                            clip = await VoiceDownloadFunc(v);
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
                            clip = await ttsFunc(v);
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
                                    await Task.Delay((int)(preGap * 1000), token);
                                }
                                catch (TaskCanceledException)
                                {
                                    // TaskCanceledException raises
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
                    await Task.Delay(1);
                }

                if (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Wait for PostGap
                        await Task.Delay((int)(v.PostGap * 1000), token);
                    }
                    catch (TaskCanceledException)
                    {
                        Debug.Log("Task canceled in waiting PostGap");
                    }
                }
            }

            // Reset viseme
            LipSyncContext?.ResetContext();

            // Restart blink
            if (request.DisableBlink && !token.IsCancellationRequested)
            {
                _ = StartBlinkAsync();
            }
        }

        // Start downloading voices from web/TTS
        private void PrefetchVoices(List<Voice> voices)
        {
            foreach (var voice in voices)
            {
                if (voice.Source == VoiceSource.Web)
                {
                    VoiceDownloadFunc?.Invoke(voice);
                }
                else if (voice.Source == VoiceSource.TTS)
                {
                    var ttsFunc = GetTTSFunction(voice.GetTTSFunctionName());
                    ttsFunc?.Invoke(voice);
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
        public Func<Voice, Task<AudioClip>> GetTTSFunction(string name)
        {
            if (!string.IsNullOrEmpty(name) && TextToSpeechFunctions.ContainsKey(name))
            {
                return TextToSpeechFunctions[name];
            }
            return TextToSpeechFunc;
        }

        // Register TTS Function with name
        public void RegisterTTSFunction(string name, Func<Voice, Task<AudioClip>> func, bool asDefault = false)
        {
            TextToSpeechFunctions[name] = func;
            if (asDefault)
            {
                TextToSpeechFunc = func;
            }
        }

        // Replace Japanese Dakuten from resource files
        public string ReplaceDakuten(string value)
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

        // Do animations
        public async Task Animate(AnimationRequest request, CancellationToken token)
        {
            // Stop blink
            if (request.DisableBlink && !token.IsCancellationRequested)
            {
                StopBlink();
            }

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

            // Restart blink
            if (request.DisableBlink && !token.IsCancellationRequested)
            {
                _ = StartBlinkAsync();
            }
        }

        public async Task PlayAnimations(List<Animation> animations, string layerName, CancellationToken token)
        {
            var layerIndex = layerName == string.Empty ? 0 : animator.GetLayerIndex(layerName);
            foreach (var a in animations)
            {
                var histroyId = string.Empty;
                try
                {
                    if (a.PreGap > 0)
                    {
                        await Task.Delay((int)(a.PreGap * 1000), token);
                    }
                    animator.SetLayerWeight(layerIndex, a.Weight);
                    histroyId = History?.Add(a);
                    animator.CrossFadeInFixedTime(a.Name, a.FadeLength < 0 ? AnimationFadeLength : a.FadeLength, layerIndex);
                    await Task.Delay((int)(a.Duration * 1000), token);
                }
                catch (TaskCanceledException tcex)
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

        public void ResetLayers(List<string> excludeLayerNames)
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

        // Set default face expression
        public async Task SetDefaultFace()
        {
            History?.Add("Set default face");
            await SetFace(DefaultFace);
        }

        // Set face expression
        public async Task SetFace(string name, float duration = 0.0f, string description = null)
        {
            await SetFace(new FaceRequest(new List<FaceExpression>() { new FaceExpression(name, duration, description) }));
        }

        // Set face expressions
        public async Task SetFace(FaceRequest request)
        {
            foreach (var face in request.Faces)
            {
                History?.Add(face);
                if (faceClips.ContainsKey(face.Name))
                {
                    foreach (var blendShapeValue in faceClips[face.Name].Values)
                    {
                        // Apply weights
                        SkinnedMeshRenderer.SetBlendShapeWeight(blendShapeValue.Index, blendShapeValue.Weight);
                    }
                    await Task.Delay((int)(face.Duration * 1000));
                }
                else
                {
                    Debug.LogWarning($"FaceClip not exists: {face.Name}");
                }
            }

            // Reset default face expression
            if (request.DefaultOnEnd && request.Faces.Last().Duration > 0)
            {
                _ = SetDefaultFace();
            }
        }

        // Register FaceClip
        public void AddFace(FaceClip faceClip, bool asDefault = false)
        {
            faceClips[faceClip.Name] = faceClip;
            if (asDefault)
            {
                DefaultFace = new FaceRequest();
                DefaultFace.AddFace(faceClip.Name, 0.0f, "default face");
            }
        }

        // Register weights for face expression name
        public void AddFace(string name, Dictionary<string, float> weights, bool asDefault = false)
        {
            AddFace(new FaceClip(name, SkinnedMeshRenderer, weights), asDefault);
        }

        // Load faces from config file
        public void LoadFacesFromFile(string configFilePath = null)
        {
            var path = !string.IsNullOrEmpty(configFilePath) ? configFilePath : Application.dataPath + "/" + FaceConfigurationFile;
            if (!File.Exists(path))
            {
                Debug.LogWarning("Face configuration file does not exist: " + path);
                return;
            }

            foreach (var faceClip in JsonConvert.DeserializeObject<List<FaceClip>>(File.ReadAllText(path)))
            {
                var asDefault = faceClip.Name.ToLower() == "default" || faceClip.Name.ToLower() == "neutral";
                AddFace(faceClip, asDefault);
            }
        }

        // Initialize and start blink
        public async Task StartBlinkAsync(bool startNew = false)
        {
            // Return with doing nothing when already blinking
            if (isBlinkEnabled && startNew == false)
            {
                return;
            }

            // Initialize
            blinkShapeIndex = SkinnedMeshRenderer.sharedMesh.GetBlendShapeIndex(BlinkBlendShapeName);
            blinkWeight = 0f;
            blinkVelocity = 0f;
            blinkAction = null;

            // Open the eyes
            SkinnedMeshRenderer.SetBlendShapeWeight(blinkShapeIndex, 0);

            // Enable blink
            isBlinkEnabled = true;

            if (!startNew)
            {
                return;
            }

            // Start new blink loop
            History?.Add("Start blink");
            while (true)
            {
                if (blinkTokenSource.Token.IsCancellationRequested)
                {
                    break;
                }
                // Close eyes
                blinkIntervalToClose = UnityEngine.Random.Range(MinBlinkIntervalToClose, MaxBlinkIntervalToClose);
                await Task.Delay((int)(blinkIntervalToClose * 1000));
                blinkAction = CloseEyesOnUpdate;
                // Open eyes
                blinkIntervalToOpen = UnityEngine.Random.Range(MinBlinkIntervalToOpen, MaxBlinkIntervalToOpen);
                await Task.Delay((int)(blinkIntervalToOpen * 1000));
                blinkAction = OpenEyesOnUpdate;
            }
        }

        // Stop blink
        public void StopBlink()
        {
            History?.Add("Stop blink");
            isBlinkEnabled = false;
            SkinnedMeshRenderer.SetBlendShapeWeight(blinkShapeIndex, 0);
        }

        // Action for closing eyes called on every updates
        private void CloseEyesOnUpdate()
        {
            if (!isBlinkEnabled)
            {
                return;
            }
            blinkWeight = Mathf.SmoothDamp(blinkWeight, 1, ref blinkVelocity, BlinkTransitionToClose);
            SkinnedMeshRenderer.SetBlendShapeWeight(blinkShapeIndex, blinkWeight * 100);
        }

        // Action for opening eyes called on every updates
        private void OpenEyesOnUpdate()
        {
            if (!isBlinkEnabled)
            {
                return;
            }
            blinkWeight = Mathf.SmoothDamp(blinkWeight, 0, ref blinkVelocity, BlinkTransitionToOpen);
            SkinnedMeshRenderer.SetBlendShapeWeight(blinkShapeIndex, blinkWeight * 100);
        }
    }
}

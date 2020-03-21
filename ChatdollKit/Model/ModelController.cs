using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;


namespace ChatdollKit.Model
{
    // Model controller
    public class ModelController : MonoBehaviour
    {
        // Audio
        public AudioSource AudioSource;
        private Dictionary<string, AudioClip> voices = new Dictionary<string, AudioClip>();

        // Animation
        private Animator animator;
        public float AnimationDuration = 0.0f;
        public float AnimationFadeLength = 0.5f;
        public float IdleAnimationDefaultDuration = 10.0f;
        public List<AnimationRequest> IdleAnimationRequests;
        public Func<CancellationToken, Task> IdleFunc;
        private CancellationTokenSource idleTokenSource;
        public string DefaultLayeredAnimationName = "Default";

        // Face Expression
        public string DefaultFaceName = "Default";
        public SkinnedMeshRenderer skinnedMeshRenderer;
        private Dictionary<string, Dictionary<string, float>> faces = new Dictionary<string, Dictionary<string, float>>();

        // Blink
        public string BlinkBlendShapeName;
        private int blinkShapeIndex;
        public float MinBlinkIntervalToClose = 3.0f;
        public float MaxBlinkIntervalToClose = 5.0f;
        public float MinBlinkIntervalToOpen = 0.05f;
        public float MaxBlinkIntervalToOpen = 0.1f;
        public float BlinkTransitionToClose = 0.01f;
        public float BlinkTransitionToOpen = 0.02f;
        public bool BlinkOnCoroutine = false;
        private float blinkIntervalToClose;
        private float blinkIntervalToOpen;
        private float blinkWeight = 0.0f;
        private float blinkVelocity = 0.0f;
        private bool isBlinkEnabled = false;
        private Action blinkAction;
        private CancellationTokenSource blinkTokenSource;

        private void Awake()
        {
            // Get animator of this model
            animator = gameObject.GetComponent<Animator>();

            if (!BlinkOnCoroutine)
            {
                blinkTokenSource = new CancellationTokenSource();
            }
        }

        private void Start()
        {
            // Set default face expression
            if (!faces.ContainsKey(DefaultFaceName))
            {
                // Set zero for all shape keys when default is not registered
                faces.Add(DefaultFaceName, new Dictionary<string, float>());
            }
            _ = SetDefaultFace();

            // Start default animation
            _ = StartIdlingAsync();

            // Start blink
            StartBlink(true);
        }

        private void LateUpdate()
        {
            // Update blink status
            blinkAction?.Invoke();
        }

        private void OnDestroy()
        {
            if (!BlinkOnCoroutine)
            {
                blinkTokenSource.Cancel();
            }

            idleTokenSource.Cancel();
        }

        // Start idling
        public async Task StartIdlingAsync()
        {
            // Stop running idle loop
            StopIdling();

            // Create new default animation token
            idleTokenSource = new CancellationTokenSource();
            var token = idleTokenSource.Token;

            if (IdleFunc == null)
            {
                while (!token.IsCancellationRequested)
                {
                    var i = UnityEngine.Random.Range(0, IdleAnimationRequests.Count);
                    var request = IdleAnimationRequests[i];
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
            if (IdleAnimationRequests == null)
            {
                IdleAnimationRequests = new List<AnimationRequest>();
            }

            if (addToLastRequest)
            {
                var request = IdleAnimationRequests.Last();
                request.AddAnimation(name, layerName ?? request.BaseLayerName, duration == 0.0f ? IdleAnimationDefaultDuration : duration, fadeLength, weight, preGap);
            }
            else
            {
                var request = new AnimationRequest();
                request.AddAnimation(name, layerName ?? request.BaseLayerName, duration == 0.0f ? IdleAnimationDefaultDuration : duration, fadeLength, weight, preGap);
                IdleAnimationRequests.Add(request);
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
                StartBlink();
            }
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

            // Speak sequentially
            foreach (var v in request.Voices)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }
                await Task.Delay((int)(v.PreGap * 1000), token);
                if (voices.ContainsKey(v.Name))
                {
                    AudioSource.PlayOneShot(voices[v.Name]);
                }
                else
                {
                    Debug.LogWarning($"Voice not found: {v.Name}");
                }
                while (AudioSource.isPlaying && !token.IsCancellationRequested)
                {
                    await Task.Delay(1);
                }
                await Task.Delay((int)(v.PostGap * 1000), token);
            }

            // Restart blink
            if (request.DisableBlink && !token.IsCancellationRequested)
            {
                StartBlink();
            }
        }

        // Stop speech
        public void StopSpeech()
        {
            AudioSource.Stop();
        }

        // Register voice with its name
        public void AddVoice(string name, AudioClip audioClip)
        {
            voices[ReplaceDakuten(name)] = audioClip;
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
                StartBlink();
            }
        }

        public async Task PlayAnimations(List<Animation> animations, string layerName, CancellationToken token)
        {
            var layerIndex = layerName == string.Empty ? 0 : animator.GetLayerIndex(layerName);
            foreach (var a in animations)
            {
                try
                {
                    if (a.PreGap > 0)
                    {
                        await Task.Delay((int)(a.PreGap * 1000), token);
                    }
                    animator.SetLayerWeight(layerIndex, a.Weight);
                    animator.CrossFadeInFixedTime(a.Name, a.FadeLength < 0 ? AnimationFadeLength : a.FadeLength, layerIndex);
                    await Task.Delay((int)(a.Duration * 1000), token);
                }
                catch (TaskCanceledException tcex)
                {
                    Debug.Log($"Animation {a.Name} on {a.LayerName} canceled: {tcex.Message}");
                }
                catch (Exception ex)
                {
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
                    animator.CrossFadeInFixedTime(DefaultLayeredAnimationName, AnimationFadeLength, i);
                }
            }
        }

        // Set default face expression
        public async Task SetDefaultFace()
        {
            await SetFace(new FaceRequest(new List<FaceExpression>() { new FaceExpression(DefaultFaceName, 0.0f) }));
        }

        // Set a face expression
        public async Task SetFace(string name, float duration = 0f)
        {
            await SetFace(new FaceRequest(new List<FaceExpression>() { new FaceExpression(name, duration) }));
        }

        // Set face expressions
        public async Task SetFace(FaceRequest request)
        {
            foreach (var face in request.Faces)
            {
                for (var i = 0; i < skinnedMeshRenderer.sharedMesh.blendShapeCount; i++)
                {
                    // Apply weights
                    var weights = faces[face.Name];
                    var name = skinnedMeshRenderer.sharedMesh.GetBlendShapeName(i);
                    var weight = weights.Keys.Contains(name) ? weights[name] : 0f;
                    skinnedMeshRenderer.SetBlendShapeWeight(i, weight * 100);
                }
                await Task.Delay((int)(face.Duration * 1000));
            }

            // Reset default face expression
            if (request.DefaultOnEnd && request.Faces.Last().Duration > 0)
            {
                _ = SetDefaultFace();
            }
        }

        // Register face expression
        public void AddFace(string name, Dictionary<string, float> weights)
        {
            if (faces.ContainsKey(name))
            {
                faces[name] = weights;
            }
            else
            {
                faces.Add(name, weights);
            }
        }

        // Initialize and start blink
        private void StartBlink(bool startNew = false)
        {
            // Return with doing nothing when already blinking
            if (isBlinkEnabled && startNew == false)
            {
                return;
            }

            // Initialize
            blinkShapeIndex = skinnedMeshRenderer.sharedMesh.GetBlendShapeIndex(BlinkBlendShapeName);
            blinkWeight = 0f;
            blinkVelocity = 0f;
            blinkAction = null;

            // Open the eyes
            skinnedMeshRenderer.SetBlendShapeWeight(blinkShapeIndex, 0);

            // Enable blink
            isBlinkEnabled = true;

            if (!startNew)
            {
                return;
            }

            // Start
            if (BlinkOnCoroutine)
            {
                StartCoroutine(BlinkCoroutine(startNew));
            }
            else
            {
                _ = StartBlinkTask(startNew);
            }
        }

        // Blink loop in Task
        private async Task StartBlinkTask(bool startNew = false)
        {
            // Start new blink loop
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

        // Blink coroutine
        private IEnumerator BlinkCoroutine(bool startNew = false)
        {
            // Start new blink loop
            while (true)
            {
                // Close eyes
                blinkIntervalToClose = UnityEngine.Random.Range(MinBlinkIntervalToClose, MaxBlinkIntervalToClose);
                yield return new WaitForSeconds(blinkIntervalToClose);
                blinkAction = CloseEyesOnUpdate;
                // Open eyes
                blinkIntervalToOpen = UnityEngine.Random.Range(MinBlinkIntervalToOpen, MaxBlinkIntervalToOpen);
                yield return new WaitForSeconds(blinkIntervalToOpen);
                blinkAction = OpenEyesOnUpdate;
            }
        }

        // Stop blink
        private void StopBlink()
        {
            isBlinkEnabled = false;
            skinnedMeshRenderer.SetBlendShapeWeight(blinkShapeIndex, 0);
        }

        // Action for closing eyes called on every updates
        private void CloseEyesOnUpdate()
        {
            if (!isBlinkEnabled)
            {
                return;
            }
            blinkWeight = Mathf.SmoothDamp(blinkWeight, 1, ref blinkVelocity, BlinkTransitionToClose);
            skinnedMeshRenderer.SetBlendShapeWeight(blinkShapeIndex, blinkWeight * 100);
        }

        // Action for opening eyes called on every updates
        private void OpenEyesOnUpdate()
        {
            if (!isBlinkEnabled)
            {
                return;
            }
            blinkWeight = Mathf.SmoothDamp(blinkWeight, 0, ref blinkVelocity, BlinkTransitionToOpen);
            skinnedMeshRenderer.SetBlendShapeWeight(blinkShapeIndex, blinkWeight * 100);
        }
    }
}

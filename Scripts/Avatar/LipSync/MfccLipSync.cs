using ChatdollKit.Avatar;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChatdollKit.Avatar.LipSync
{


    /// <summary>MFCC lip sync for Avatar.SpeechController, with named mouth blend shapes.</summary>
    [DisallowMultipleComponent, AddComponentMenu("ChatdollKit/Avatar/MFCC Lip Sync")]
    public sealed class MfccLipSync : MonoBehaviour, ILipSync
    {
        public const string DefaultProfileResource = "ChatdollKit/Mfcc/default-female";
        [Tooltip("Leave empty to use the bundled female profile.")]
        public TextAsset Profile;
        public SkinnedMeshRenderer TargetRenderer;
        public VisemeMapping[] Mappings =
        {
            new VisemeMapping { Viseme = Viseme.A }, new VisemeMapping { Viseme = Viseme.I },
            new VisemeMapping { Viseme = Viseme.U }, new VisemeMapping { Viseme = Viseme.E },
            new VisemeMapping { Viseme = Viseme.O }
        };
        [Tooltip("Log10 RMS level at which the mouth begins to open.")]
        public float MinVolume = -2.5f;
        [Tooltip("Log10 RMS level at which the mouth is fully open.")]
        public float MaxVolume = -1.5f;
        [Min(0)] public float VolumeGain = 1;
        [Tooltip("Positive values move the mouth ahead of the audio.")]
        public float TimeOffsetSeconds;
        [Range(0, 0.3f)] public float Smoothness = 0.05f;
        [Tooltip("Blend all vowel scores. When off, use the strongest vowel.")]
        public bool UsePhonemeBlend;

        public LipSyncResult CurrentResult { get; private set; }

        private MfccLipSyncEngine engine;
        private TextAsset loadedProfile;
        private MfccProfile profileData;
        private WaveAudio audio;
        private Binding[] bindings = Array.Empty<Binding>();
        private BoundShape[] shapes = Array.Empty<BoundShape>();
        private float[] shapeWeights = Array.Empty<float>();
        private VisemeMapping[] cachedMappingsArray;
        private CachedMapping[] cachedMappings = Array.Empty<CachedMapping>();
        private SkinnedMeshRenderer cachedTargetRenderer;
        private readonly float[] targets = new float[5];
        private readonly float[] weights = new float[5];
        private readonly float[] velocities = new float[5];
        private bool mappingsDirty = true;

        private struct Binding
        {
            public int Vowel;
            public int ShapeSlot;
            public float MaxWeight;
        }

        private struct BoundShape
        {
            public SkinnedMeshRenderer Renderer;
            public Mesh Mesh;
            public int Index;
        }

        private struct CachedMapping
        {
            public VisemeMapping Mapping;
            public SkinnedMeshRenderer OverrideRenderer;
            public SkinnedMeshRenderer Renderer;
            public Mesh Mesh;
            public string BlendShape;
            public Viseme Viseme;
            public float MaxWeight;
        }

        private void Reset()
        {
            Profile = Resources.Load<TextAsset>(DefaultProfileResource);
            TargetRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
        }

        private void OnValidate() { engine = null; mappingsDirty = true; }
        private void OnDisable() => ResetViseme();
        private void OnDestroy() => ResetViseme();
        private void LateUpdate() => ApplyVisemes(Time.unscaledDeltaTime);

        public void BeginPlayback(WaveAudio decoded)
        {
            ResetViseme();
            if (!isActiveAndEnabled) return;
            if (decoded == null) throw new ArgumentNullException(nameof(decoded));
            try
            {
                EnsureEngine();
                // Validate the sample rate even when this utterance starts with silence.
                if (decoded.SampleRate < profileData.targetSampleRate)
                    throw new ArgumentException("MFCC lip sync requires an input sample rate at least as high as the profile sample rate.");
                RebuildMappings();
                audio = decoded;
            }
            catch (Exception error)
            {
                // A mouth configuration error should not prevent speech from playing.
                Debug.LogWarning("MFCC lip sync could not start: " + error.Message, this);
            }
        }

        public void UpdatePlayback(int samplePosition)
        {
            if (!isActiveAndEnabled || audio == null) return;
            try
            {
                EnsureEngine();
                engine.MinVolume = MinVolume;
                engine.MaxVolume = MaxVolume;
                engine.VolumeGain = VolumeGain;
                SetResult(engine.Process(audio.Samples, audio.Channels, audio.SampleRate, samplePosition,
                    timeOffsetSeconds: TimeOffsetSeconds));
            }
            catch (Exception error)
            {
                ResetViseme();
                Debug.LogWarning("MFCC lip sync stopped: " + error.Message, this);
            }
        }

        /// <summary>Set vowel targets without requiring AudioSource playback; also useful for presentation tests.</summary>
        public void SetResult(LipSyncResult result)
        {
            CurrentResult = result;
            for (var i = 0; i < targets.Length; i++)
            {
                var viseme = (Viseme)(i + 1);
                var value = UsePhonemeBlend ? result.GetWeight(viseme)
                    : (result.MainViseme == viseme ? result.MainVisemeWeight : 0);
                targets[i] = IsFinite(value) ? Mathf.Clamp01((float)value) : 0;
            }
        }

        /// <summary>Advance smoothing by an explicit duration and apply only mapped mouth shapes.</summary>
        public void ApplyVisemes(float deltaTime)
        {
            if (!isActiveAndEnabled) return;
            if (!IsFinite(deltaTime) || deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (MappingsChanged()) RebuildMappings();
            if (shapes.Length == 0) return;
            for (var i = 0; i < weights.Length; i++)
            {
                if (Smoothness <= 0) weights[i] = targets[i];
                else if (deltaTime > 0)
                    weights[i] = Mathf.SmoothDamp(weights[i], targets[i], ref velocities[i], Smoothness, Mathf.Infinity, deltaTime);
            }
            Array.Clear(shapeWeights, 0, shapeWeights.Length);
            foreach (var binding in bindings)
                shapeWeights[binding.ShapeSlot] += weights[binding.Vowel] * binding.MaxWeight;
            for (var i = 0; i < shapes.Length; i++)
            {
                var shape = shapes[i];
                if (ShapeExists(shape))
                    shape.Renderer.SetBlendShapeWeight(shape.Index, Mathf.Clamp(shapeWeights[i], 0, 100));
            }
        }

        public void ResetViseme()
        {
            audio = null;
            CurrentResult = default;
            Array.Clear(targets, 0, targets.Length);
            Array.Clear(weights, 0, weights.Length);
            Array.Clear(velocities, 0, velocities.Length);
            ClearBoundShapes();
        }

        /// <summary>Rebuild immediately after setup. Runtime mapping and renderer changes also rebind automatically.</summary>
        public void RebuildMappings()
        {
            ClearBoundShapes();
            cachedTargetRenderer = TargetRenderer;
            cachedMappingsArray = Mappings;
            var source = Mappings ?? Array.Empty<VisemeMapping>();
            cachedMappings = new CachedMapping[source.Length];
            var mapped = new List<Binding>();
            var boundShapes = new List<BoundShape>();
            for (var mappingIndex = 0; mappingIndex < source.Length; mappingIndex++)
            {
                var mapping = source[mappingIndex];
                if (mapping == null) continue;
                var renderer = GetRenderer(mapping);
                var mesh = renderer != null ? renderer.sharedMesh : null;
                cachedMappings[mappingIndex] = new CachedMapping
                {
                    Mapping = mapping, OverrideRenderer = mapping.Renderer, Renderer = renderer, Mesh = mesh,
                    BlendShape = mapping.BlendShape, Viseme = mapping.Viseme, MaxWeight = mapping.MaxWeight
                };
                if (mesh == null || mapping.Viseme < Viseme.A || mapping.Viseme > Viseme.O || string.IsNullOrEmpty(mapping.BlendShape)) continue;
                var index = mesh.GetBlendShapeIndex(mapping.BlendShape);
                if (index < 0) continue;
                var slot = -1;
                for (var i = 0; i < boundShapes.Count; i++)
                {
                    var shape = boundShapes[i];
                    if (shape.Renderer == renderer && shape.Mesh == mesh && shape.Index == index) { slot = i; break; }
                }
                if (slot < 0)
                {
                    slot = boundShapes.Count;
                    boundShapes.Add(new BoundShape { Renderer = renderer, Mesh = mesh, Index = index });
                }
                mapped.Add(new Binding { Vowel = (int)mapping.Viseme - 1, ShapeSlot = slot,
                    MaxWeight = IsFinite(mapping.MaxWeight) ? Mathf.Clamp(mapping.MaxWeight, 0, 100) : 0 });
            }
            bindings = mapped.ToArray();
            shapes = boundShapes.ToArray();
            shapeWeights = new float[shapes.Length];
            mappingsDirty = false;
        }

        private bool MappingsChanged()
        {
            if (mappingsDirty || !ReferenceEquals(cachedTargetRenderer, TargetRenderer) || !ReferenceEquals(cachedMappingsArray, Mappings)) return true;
            if (Mappings == null) return false;
            for (var i = 0; i < Mappings.Length; i++)
            {
                var mapping = Mappings[i];
                var cached = cachedMappings[i];
                if (!ReferenceEquals(cached.Mapping, mapping)) return true;
                if (mapping == null) continue;
                var renderer = GetRenderer(mapping);
                var mesh = renderer != null ? renderer.sharedMesh : null;
                if (!ReferenceEquals(cached.OverrideRenderer, mapping.Renderer) || !ReferenceEquals(cached.Renderer, renderer)
                    || !ReferenceEquals(cached.Mesh, mesh) || cached.BlendShape != mapping.BlendShape
                    || cached.Viseme != mapping.Viseme || !cached.MaxWeight.Equals(mapping.MaxWeight)) return true;
            }
            return false;
        }

        private SkinnedMeshRenderer GetRenderer(VisemeMapping mapping)
            // Empty serialized Unity references may have a managed wrapper after prefab loading.
            => mapping.Renderer != null ? mapping.Renderer : TargetRenderer;

        private static bool ShapeExists(BoundShape shape)
            => shape.Renderer != null && shape.Mesh != null && shape.Renderer.sharedMesh == shape.Mesh
                && shape.Index >= 0 && shape.Index < shape.Mesh.blendShapeCount;

        private void ClearBoundShapes()
        {
            // A changed mesh can reuse indices for unrelated expressions; never clear those.
            foreach (var shape in shapes)
                if (ShapeExists(shape)) shape.Renderer.SetBlendShapeWeight(shape.Index, 0);
        }

        private MfccProfile GetProfile()
        {
            var asset = Profile != null ? Profile : Resources.Load<TextAsset>(DefaultProfileResource);
            if (asset == null) throw new InvalidOperationException("Assign a profile JSON TextAsset.");
            loadedProfile = Profile;
            return JsonUtility.FromJson<MfccProfile>(asset.text);
        }

        private void EnsureEngine()
        {
            if (engine == null || loadedProfile != Profile)
            {
                profileData = GetProfile();
                engine = new MfccLipSyncEngine(profileData);
            }
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

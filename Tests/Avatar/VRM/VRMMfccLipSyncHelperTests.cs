using ChatdollKit.Avatar.LipSync;
#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using ChatdollKit.Avatar;
using ChatdollKit.Extension.VRM;
using ChatdollKit.Model;
using NUnit.Framework;
using UnityEngine;
using VRM;

namespace ChatdollKit.Tests.Avatar
{
    public class VRMMfccLipSyncHelperTests
    {
        private readonly List<Object> objects = new List<Object>();
        private MfccLipSync lip;
        private VRMMfccLipSyncHelper helper;

        [SetUp]
        public void SetUp()
        {
            var player = Track(new GameObject("MFCC player"));
            helper = player.AddComponent<VRMMfccLipSyncHelper>();
            lip = player.GetComponent<MfccLipSync>();
            lip.Smoothness = 0;
            lip.UsePhonemeBlend = true;
        }

        [TearDown]
        public void TearDown()
        {
            // Components release their cached mesh bindings before the meshes and clips are destroyed.
            foreach (var item in objects)
                if (item is GameObject && item != null) Object.DestroyImmediate(item);
            foreach (var item in objects)
                if (item != null) Object.DestroyImmediate(item);
            objects.Clear();
        }

        [Test]
        public void AddingOnlyVrmHelperResolvesSameObjectEngineAndUsesVrmBindingsWithoutGenericShapeNames()
        {
            var player = Track(new GameObject("Automatically configured VRM player"));
            var automaticHelper = player.AddComponent<VRMMfccLipSyncHelper>();
            var engine = player.GetComponent<MfccLipSync>();
            Assert.That(engine, Is.Not.Null, "RequireComponent must add the engine when only the helper is attached.");
            automaticHelper.BlendShapeNameForMouthA = "unrelated";
            automaticHelper.BlendShapeNameForMouthI = "unrelated";
            automaticHelper.BlendShapeNameForMouthU = "unrelated";
            automaticHelper.BlendShapeNameForMouthE = "unrelated";
            automaticHelper.BlendShapeNameForMouthO = "unrelated";
            var avatar = CreateAvatar(out var profile);
            var face = CreateRenderer(avatar, "Face", "mouth-a", "mouth-i", "unrelated");
            var teeth = CreateRenderer(avatar, "Teeth", "mouth-inner");
            profile.Clips.Add(CreateClip(BlendShapePreset.A, "Vowel A", Bind("Face", 0, 60), Bind("Teeth", 0, 25)));
            profile.Clips.Add(CreateClip(BlendShapePreset.I, "Vowel I", Bind("Face", 1, 45)));
            profile.Clips.Add(CreateClip(BlendShapePreset.U, "Vowel U", Bind("Face", 0, 20)));
            profile.Clips.Add(CreateClip(BlendShapePreset.E, "Vowel E", Bind("Teeth", 0, 10)));
            profile.Clips.Add(CreateClip(BlendShapePreset.O, "Vowel O", Bind("Face", 1, 30)));
            face.SetBlendShapeWeight(2, 37);

            automaticHelper.ConfigureViseme(avatar);
            engine.Smoothness = 0;
            engine.UsePhonemeBlend = true;
            engine.SetResult(new LipSyncResult(0.2, 0.1, 0.3, 0.25, 0.15, Viseme.U, 1, 0.1));
            engine.ApplyVisemes(0.02f);

            Assert.That(player.GetComponent<MfccLipSync>(), Is.SameAs(engine), "Setup must reuse the same-GameObject engine.");
            Assert.That(player.GetComponents<MfccLipSync>().Length, Is.EqualTo(1));
            Assert.That(engine.Mappings.Length, Is.EqualTo(6));
            Assert.That(face.GetBlendShapeWeight(0), Is.EqualTo(18).Within(1e-4));
            Assert.That(face.GetBlendShapeWeight(1), Is.EqualTo(9).Within(1e-4));
            Assert.That(teeth.GetBlendShapeWeight(0), Is.EqualTo(7.5).Within(1e-4));
            Assert.That(face.GetBlendShapeWeight(2), Is.EqualTo(37), "VRM preset metadata must ignore the generic helper's shape-name fields.");
        }

        [Test]
        public void VowelPresetKeepsEveryBindingRendererAndWeightAndHelperResetsOnlyMappedShapes()
        {
            var avatar = CreateAvatar(out var profile);
            var face = CreateRenderer(avatar, "Face", "smile", "mouth-a");
            var teeth = CreateRenderer(avatar, "Teeth", "mouth-inner", "unrelated");
            profile.Clips.Add(CreateClip(BlendShapePreset.A, "Different display name",
                Bind("Face", 1, 60), Bind("Teeth", 0, 25), Bind("Face", 1, 20)));
            face.SetBlendShapeWeight(0, 31);
            teeth.SetBlendShapeWeight(1, 47);

            ILipSyncHelper setupHelper = helper;
            setupHelper.ConfigureViseme(avatar);

            Assert.That(lip.Mappings.Length, Is.EqualTo(3));
            Assert.That(lip.Mappings[0].Renderer, Is.SameAs(face));
            Assert.That(lip.Mappings[0].BlendShape, Is.EqualTo("mouth-a"));
            Assert.That(lip.Mappings[0].MaxWeight, Is.EqualTo(60));
            Assert.That(lip.Mappings[1].Renderer, Is.SameAs(teeth));
            ApplyA(0.5);
            Assert.That(face.GetBlendShapeWeight(1), Is.EqualTo(40));
            Assert.That(teeth.GetBlendShapeWeight(0), Is.EqualTo(12.5));

            setupHelper.ResetViseme();
            Assert.That(face.GetBlendShapeWeight(1), Is.Zero);
            Assert.That(teeth.GetBlendShapeWeight(0), Is.Zero);
            Assert.That(face.GetBlendShapeWeight(0), Is.EqualTo(31));
            Assert.That(teeth.GetBlendShapeWeight(1), Is.EqualTo(47));
        }

        [Test]
        public void PresetsTakePriorityAndLegacyNamesOnlyIdentifyUnknownClips()
        {
            var avatar = CreateAvatar(out var profile);
            CreateRenderer(avatar, "Face", "a", "i", "u", "e", "o", "emotion");
            profile.Clips.Add(CreateClip(BlendShapePreset.Unknown, "A", Bind("Face", 5, 100)));
            profile.Clips.Add(CreateClip(BlendShapePreset.A, "I", Bind("Face", 0, 80)));
            profile.Clips.Add(CreateClip(BlendShapePreset.I, "vowel", Bind("Face", 1, 100)));
            profile.Clips.Add(CreateClip(BlendShapePreset.U, "vowel", Bind("Face", 2, 100)));
            profile.Clips.Add(CreateClip(BlendShapePreset.Unknown, "e", Bind("Face", 3, 75)));
            profile.Clips.Add(CreateClip(BlendShapePreset.O, "vowel", Bind("Face", 4, 100)));
            profile.Clips.Add(CreateClip(BlendShapePreset.Joy, "A", Bind("Face", 5, 100)));

            helper.ConfigureViseme(avatar);

            Assert.That(lip.Mappings.Length, Is.EqualTo(5));
            for (var i = 0; i < lip.Mappings.Length; i++)
            {
                Assert.That(lip.Mappings[i].Viseme, Is.EqualTo((Viseme)(i + 1)));
                Assert.That(lip.Mappings[i].BlendShape, Is.EqualTo(new[] { "a", "i", "u", "e", "o" }[i]));
            }
        }

        [Test]
        public void MissingAndInvalidBindingsNeverMapAnUnrelatedFirstShape()
        {
            var avatar = CreateAvatar(out var profile);
            var face = CreateRenderer(avatar, "Face", "smile", "mouth-a");
            var noMesh = Track(new GameObject("NoMesh"));
            noMesh.transform.SetParent(avatar.transform, false);
            noMesh.AddComponent<SkinnedMeshRenderer>();
            profile.Clips.Add(null);
            profile.Clips.Add(CreateClip(BlendShapePreset.I, "I", null));
            profile.Clips.Add(CreateClip(BlendShapePreset.A, "A",
                Bind("Missing", 0, 100), Bind("NoMesh", 0, 100), Bind("Face", -1, 100),
                Bind("Face", 2, 100), Bind("Face", 0, float.NaN), Bind("Face", 0, float.PositiveInfinity),
                Bind("Face", 1, 55)));
            face.SetBlendShapeWeight(0, 37);

            Assert.DoesNotThrow(() => helper.ConfigureViseme(avatar));

            Assert.That(lip.Mappings.Length, Is.EqualTo(1));
            ApplyA(1);
            Assert.That(face.GetBlendShapeWeight(1), Is.EqualTo(55));
            Assert.That(face.GetBlendShapeWeight(0), Is.EqualTo(37));
        }

        [Test]
        public void EmptyBindingPathResolvesTheAvatarRootAndWeightsAreClamped()
        {
            var avatar = CreateAvatar(out var profile);
            var renderer = CreateRenderer(avatar, null, "mouth-a", "mouth-i");
            profile.Clips.Add(CreateClip(BlendShapePreset.A, "A", Bind("", 0, 120)));
            profile.Clips.Add(CreateClip(BlendShapePreset.I, "I", Bind(null, 1, -10)));

            helper.ConfigureViseme(avatar);
            Assert.That(lip.Mappings[0].Renderer, Is.SameAs(renderer));
            Assert.That(lip.Mappings[0].MaxWeight, Is.EqualTo(100));
            Assert.That(lip.Mappings[1].MaxWeight, Is.Zero);
        }

        [Test]
        public void AvatarReplacementReleasesOldMappingsAndResolvesTheNewMeshIndices()
        {
            var first = CreateAvatar(out var firstProfile);
            var firstRenderer = CreateRenderer(first, "Face", "mouth-a", "smile");
            firstProfile.Clips.Add(CreateClip(BlendShapePreset.A, "A", Bind("Face", 0, 80)));
            var second = CreateAvatar(out var secondProfile);
            var secondRenderer = CreateRenderer(second, "DifferentFace", "smile", "new-mouth");
            secondProfile.Clips.Add(CreateClip(BlendShapePreset.A, "A", Bind("DifferentFace", 1, 45)));
            helper.ConfigureViseme(first);
            ApplyA(1);
            secondRenderer.SetBlendShapeWeight(0, 19);

            helper.ConfigureViseme(second);

            Assert.That(firstRenderer.GetBlendShapeWeight(0), Is.Zero);
            Assert.That(lip.Mappings.Length, Is.EqualTo(1));
            Assert.That(lip.Mappings[0].Renderer, Is.SameAs(secondRenderer));
            ApplyA(1);
            Assert.That(firstRenderer.GetBlendShapeWeight(0), Is.Zero);
            Assert.That(secondRenderer.GetBlendShapeWeight(1), Is.EqualTo(45));
            Assert.That(secondRenderer.GetBlendShapeWeight(0), Is.EqualTo(19));

            helper.ConfigureViseme(null);
            Assert.That(secondRenderer.GetBlendShapeWeight(1), Is.Zero);
            Assert.That(lip.Mappings, Is.Empty);
        }

        [Test]
        public void MissingProxyProfileOrClipsProducesNoMappings()
        {
            var avatar = Track(new GameObject("Unconfigured avatar"));
            Assert.DoesNotThrow(() => helper.ConfigureViseme(avatar));
            Assert.That(lip.Mappings, Is.Empty);
            var proxy = avatar.AddComponent<VRMBlendShapeProxy>();
            proxy.enabled = false;
            Assert.DoesNotThrow(() => helper.ConfigureViseme(avatar));
            Assert.That(lip.Mappings, Is.Empty);
            proxy.BlendShapeAvatar = Track(ScriptableObject.CreateInstance<BlendShapeAvatar>());
            proxy.BlendShapeAvatar.Clips = null;
            Assert.DoesNotThrow(() => helper.ConfigureViseme(avatar));
            Assert.That(lip.Mappings, Is.Empty);
        }

        private void ApplyA(double weight)
        {
            lip.SetResult(new LipSyncResult(weight, 0, 0, 0, 0, Viseme.A, weight, 0.1));
            lip.ApplyVisemes(0.02f);
        }

        private GameObject CreateAvatar(out BlendShapeAvatar profile)
        {
            var avatar = Track(new GameObject("VRM avatar"));
            var proxy = avatar.AddComponent<VRMBlendShapeProxy>();
            proxy.enabled = false;
            profile = Track(ScriptableObject.CreateInstance<BlendShapeAvatar>());
            proxy.BlendShapeAvatar = profile;
            return avatar;
        }

        private SkinnedMeshRenderer CreateRenderer(GameObject avatar, string path, params string[] shapes)
        {
            var meshObject = path == null ? avatar : Track(new GameObject(path));
            if (path != null) meshObject.transform.SetParent(avatar.transform, false);
            var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            var mesh = Track(new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            });
            foreach (var shape in shapes)
                mesh.AddBlendShapeFrame(shape, 100, new[] { Vector3.up, Vector3.up, Vector3.up }, null, null);
            renderer.sharedMesh = mesh;
            return renderer;
        }

        private BlendShapeClip CreateClip(BlendShapePreset preset, string name, params BlendShapeBinding[] bindings)
        {
            var clip = Track(ScriptableObject.CreateInstance<BlendShapeClip>());
            clip.Preset = preset;
            clip.BlendShapeName = name;
            clip.Values = bindings;
            return clip;
        }

        private static BlendShapeBinding Bind(string path, int index, float weight)
            => new BlendShapeBinding { RelativePath = path, Index = index, Weight = weight };

        private T Track<T>(T item) where T : Object { objects.Add(item); return item; }
    }
}
#endif

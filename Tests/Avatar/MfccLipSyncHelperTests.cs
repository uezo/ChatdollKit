using ChatdollKit.Avatar.LipSync;
#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using ChatdollKit.Avatar;
using ChatdollKit.Model;
using NUnit.Framework;
using UnityEngine;

namespace ChatdollKit.Tests.Avatar
{
    public class MfccLipSyncHelperTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<Mesh> meshes = new List<Mesh>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in objects) if (item != null) Object.DestroyImmediate(item);
            foreach (var mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh);
            objects.Clear();
            meshes.Clear();
        }

        [Test]
        public void ExactNameWinsOverPartialMatchesAndUniquePartialNameStillBinds()
        {
            var helper = CreateHelper();
            var engine = helper.GetComponent<MfccLipSync>();
            var avatar = CreateObject("Avatar");
            var renderer = AddRenderer(avatar, "Face", "prefix.vrc.v_aa", "vrc.v_aa", "prefix.vrc.v_ih", "smile");
            renderer.SetBlendShapeWeight(0, 17);
            renderer.SetBlendShapeWeight(3, 37);

            helper.ConfigureViseme(avatar);

            Assert.That(engine.Mappings.Length, Is.EqualTo(2));
            Assert.That(engine.Mappings[0].Viseme, Is.EqualTo(Viseme.A));
            Assert.That(engine.Mappings[0].BlendShape, Is.EqualTo("vrc.v_aa"));
            Assert.That(engine.Mappings[1].Viseme, Is.EqualTo(Viseme.I));
            Assert.That(engine.Mappings[1].BlendShape, Is.EqualTo("prefix.vrc.v_ih"));
            Apply(helper, new LipSyncResult(0.5, 0.25, 0, 0, 0, Viseme.A, 1, 0.1));
            Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(50));
            Assert.That(renderer.GetBlendShapeWeight(2), Is.EqualTo(25));
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(17));
            Assert.That(renderer.GetBlendShapeWeight(3), Is.EqualTo(37));
        }

        [Test]
        public void InactiveChildrenAndDifferentMeshesReceiveTheirOwnVowels()
        {
            var helper = CreateHelper();
            var engine = helper.GetComponent<MfccLipSync>();
            var avatar = CreateObject("Avatar");
            var first = AddRenderer(avatar, "Upper face", "vrc.v_aa", "smile");
            var second = AddRenderer(avatar, "Lower face", "smile", "vrc.v_ih");
            second.gameObject.SetActive(false);
            first.SetBlendShapeWeight(1, 31);
            second.SetBlendShapeWeight(0, 41);

            helper.ConfigureViseme(avatar);

            Assert.That(engine.Mappings.Length, Is.EqualTo(2));
            Assert.That(engine.Mappings[0].Renderer, Is.EqualTo(first));
            Assert.That(engine.Mappings[1].Renderer, Is.EqualTo(second));
            Apply(helper, new LipSyncResult(0.4, 0.25, 0, 0, 0, Viseme.A, 1, 0.1));
            Assert.That(first.GetBlendShapeWeight(0), Is.EqualTo(40));
            Assert.That(second.GetBlendShapeWeight(1), Is.EqualTo(25));
            Assert.That(first.GetBlendShapeWeight(1), Is.EqualTo(31));
            Assert.That(second.GetBlendShapeWeight(0), Is.EqualTo(41));
        }

        [Test]
        public void AmbiguousExactOrPartialNamesAndMissingVowelsDoNotBindIndexZero()
        {
            var helper = CreateHelper();
            var engine = helper.GetComponent<MfccLipSync>();
            helper.BlendShapeNameForMouthA = "mouth-a";
            helper.BlendShapeNameForMouthI = "mouth-i";
            var avatar = CreateObject("Avatar");
            var first = AddRenderer(avatar, "First face", "smile", "first-mouth-a", "mouth-i");
            var second = AddRenderer(avatar, "Second face", "smile", "second-mouth-a", "mouth-i");
            first.SetBlendShapeWeight(0, 27);
            second.SetBlendShapeWeight(0, 43);

            helper.ConfigureViseme(avatar);

            Assert.That(engine.Mappings, Is.Empty);
            Apply(helper, new LipSyncResult(1, 0, 0, 0, 0, Viseme.A, 1, 0.1));
            helper.ResetViseme();
            Assert.That(first.GetBlendShapeWeight(0), Is.EqualTo(27));
            Assert.That(second.GetBlendShapeWeight(0), Is.EqualTo(43));
            Assert.That(first.GetBlendShapeWeight(1), Is.Zero);
            Assert.That(second.GetBlendShapeWeight(2), Is.Zero);
        }

        [Test]
        public void ReconfigurationAndNullAvatarReleaseOnlyPreviouslyBoundMouths()
        {
            var helper = CreateHelper();
            var engine = helper.GetComponent<MfccLipSync>();
            var firstAvatar = CreateObject("First avatar");
            var first = AddRenderer(firstAvatar, "Face", "vrc.v_aa", "smile");
            var secondAvatar = CreateObject("Second avatar");
            var second = AddRenderer(secondAvatar, "Face", "smile", "vrc.v_aa");
            first.SetBlendShapeWeight(1, 37);
            second.SetBlendShapeWeight(0, 23);
            helper.ConfigureViseme(firstAvatar);
            Apply(helper, new LipSyncResult(1, 0, 0, 0, 0, Viseme.A, 1, 0.1));
            Assert.That(first.GetBlendShapeWeight(0), Is.EqualTo(100));

            helper.ConfigureViseme(secondAvatar);

            Assert.That(first.GetBlendShapeWeight(0), Is.Zero);
            Assert.That(first.GetBlendShapeWeight(1), Is.EqualTo(37));
            Assert.That(engine.Mappings.Length, Is.EqualTo(1), "Reconfiguring replaces mappings instead of appending them.");
            Apply(helper, new LipSyncResult(0.5, 0, 0, 0, 0, Viseme.A, 0.5, 0.1));
            Assert.That(second.GetBlendShapeWeight(1), Is.EqualTo(50));

            helper.ConfigureViseme(null);

            Assert.That(engine.Mappings, Is.Empty);
            Assert.That(engine.TargetRenderer, Is.Null);
            Assert.That(engine.CurrentResult.MainViseme, Is.EqualTo(Viseme.None));
            Assert.That(second.GetBlendShapeWeight(1), Is.Zero);
            Assert.That(second.GetBlendShapeWeight(0), Is.EqualTo(23));
            Apply(helper, new LipSyncResult(1, 0, 0, 0, 0, Viseme.A, 1, 0.1));
            Assert.That(second.GetBlendShapeWeight(1), Is.Zero);
        }

        [Test]
        public void SetupHelperIsSeparateFromTheSpeechPlaybackConsumer()
        {
            var helper = CreateHelper();
            var engine = helper.GetComponent<MfccLipSync>();
            Assert.That(helper, Is.InstanceOf<ILipSyncHelper>());
            Assert.That(helper, Is.Not.InstanceOf<ILipSync>());
            helper.ConfigureViseme(null);
            Assert.That(engine, Is.Not.Null, "Attaching the helper adds its same-GameObject engine.");
            Assert.That(helper.GetComponents<MfccLipSync>().Length, Is.EqualTo(1));
            Assert.That(engine, Is.InstanceOf<ILipSync>());
            Assert.That(engine, Is.Not.InstanceOf<ILipSyncHelper>());
        }

        private MfccLipSyncHelper CreateHelper() => CreateObject("MFCC setup host").AddComponent<MfccLipSyncHelper>();

        private GameObject CreateObject(string name)
        {
            var result = new GameObject(name);
            objects.Add(result);
            return result;
        }

        private SkinnedMeshRenderer AddRenderer(GameObject parent, string name, params string[] shapeNames)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            var renderer = child.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            meshes.Add(mesh);
            foreach (var shapeName in shapeNames)
                mesh.AddBlendShapeFrame(shapeName, 100, new[] { Vector3.up, Vector3.up, Vector3.up }, null, null);
            renderer.sharedMesh = mesh;
            return renderer;
        }

        private static void Apply(MfccLipSyncHelper helper, LipSyncResult result)
        {
            var engine = helper.GetComponent<MfccLipSync>();
            engine.Smoothness = 0;
            engine.UsePhonemeBlend = true;
            engine.SetResult(result);
            engine.ApplyVisemes(1f / 60);
        }
    }
}
#endif

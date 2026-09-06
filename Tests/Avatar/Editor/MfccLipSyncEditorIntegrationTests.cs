using ChatdollKit.Avatar.LipSync;
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ChatdollKit.Avatar;
using ChatdollKit.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Speech = ChatdollKit.Avatar.SpeechController;

namespace ChatdollKit.Tests.Avatar
{
    public class MfccLipSyncEditorIntegrationTests
    {
        private readonly List<Object> transientObjects = new List<Object>();
        private string assetFolder;

        [TearDown]
        public void TearDown()
        {
            foreach (var item in transientObjects)
                if (item is GameObject && item != null) Object.DestroyImmediate(item);
            foreach (var item in transientObjects)
                if (item != null && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item);
            transientObjects.Clear();
            if (assetFolder != null)
            {
                AssetDatabase.DeleteAsset(assetFolder);
                assetFolder = null;
            }
        }

        [Test]
        public void ModelControllerSetupUsesMfccHelperWithoutImplicitlyConnectingSpeech()
        {
            var player = Track(new GameObject("Model setup test"));
            var avatar = Track(new GameObject("Synthetic avatar"));
            var renderer = avatar.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = Track(CreateMesh("blink", "vrc.v_aa"));
            var model = player.AddComponent<ModelController>();
            model.AvatarModel = avatar;
            var helper = player.AddComponent<MfccLipSyncHelper>();
            var speech = player.AddComponent<Speech>();
            Assert.That(player.GetComponent<ILipSyncHelper>(), Is.SameAs(helper));
            Assert.That(player.GetComponents<MonoBehaviour>().Any(component =>
                component.GetType().FullName == "ChatdollKit.Model.uLipSyncHelper"), Is.False);

            // Invoke the real context menu without an assembly reference to Assembly-CSharp-Editor.
            var editorType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("FaceClipEditor", false))
                .FirstOrDefault(type => type != null);
            Assert.That(editorType, Is.Not.Null, "Load the production ModelControllerEditor in the test project.");
            var setup = editorType.GetMethod("Setup", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(setup, Is.Not.Null);
            Assert.DoesNotThrow(() => setup.Invoke(null, new object[] { new MenuCommand(model) }));

            var lip = player.GetComponent<MfccLipSync>();
            Assert.That(lip, Is.Not.Null);
            Assert.That(lip.Mappings.Length, Is.EqualTo(1));
            Assert.That(lip.Mappings[0].Viseme, Is.EqualTo(Viseme.A));
            Assert.That(lip.Mappings[0].Renderer, Is.SameAs(renderer));
            Assert.That(lip.Mappings[0].BlendShape, Is.EqualTo("vrc.v_aa"));
            Assert.That(speech.LipSyncEngine, Is.Null, "Avatar mapping setup must preserve the explicit speech opt-in.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PrefabRoundTripPreservesSpeechReferencesAndHelperResetWithRendererFallback(bool useLegacyEngineFieldName)
        {
            var folderName = "__MfccLipSyncEditorTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName);
            assetFolder = "Assets/" + folderName;
            var mesh = CreateMesh("blink", "mouth-a");
            AssetDatabase.CreateAsset(mesh, assetFolder + "/Mouth.asset");

            var source = Track(new GameObject("MFCC prefab"));
            var face = new GameObject("Face");
            face.transform.SetParent(source.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            var lip = source.AddComponent<MfccLipSync>();
            lip.TargetRenderer = renderer;
            lip.Mappings = new[] { new VisemeMapping { Viseme = Viseme.A, BlendShape = "mouth-a", MaxWeight = 80 } };
            lip.Smoothness = 0;
            lip.UsePhonemeBlend = true;
            var helper = source.AddComponent<MfccLipSyncHelper>();
            var speech = source.AddComponent<Speech>();
            speech.LipSyncEngine = lip;
            speech.LipSyncHelper = helper;
            var prefabPath = assetFolder + "/Mouth.prefab";
            Assert.That(PrefabUtility.SaveAsPrefabAsset(source, prefabPath), Is.Not.Null);
            AssetDatabase.SaveAssets();
            Object.DestroyImmediate(source);
            if (useLegacyEngineFieldName)
            {
                var yaml = File.ReadAllText(prefabPath);
                const string currentField = "\n  lipSyncEngineComponent:";
                StringAssert.Contains(currentField, yaml, "The saved prefab must contain the current serialized engine field.");
                File.WriteAllText(prefabPath, yaml.Replace(currentField, "\n  speechLipSyncComponent:"));
            }
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var instance = Track((GameObject)PrefabUtility.InstantiatePrefab(prefab));
            var restoredLip = instance.GetComponent<MfccLipSync>();
            var restoredRenderer = instance.transform.Find("Face").GetComponent<SkinnedMeshRenderer>();
            Assert.That(instance.GetComponent<Speech>().LipSyncEngine, Is.SameAs(restoredLip));
            Assert.That(instance.GetComponent<Speech>().LipSyncHelper, Is.SameAs(instance.GetComponent<MfccLipSyncHelper>()));
            Assert.That(restoredLip.TargetRenderer, Is.SameAs(restoredRenderer));
            Assert.That(restoredRenderer.sharedMesh, Is.SameAs(AssetDatabase.LoadAssetAtPath<Mesh>(assetFolder + "/Mouth.asset")));
            Assert.That(restoredLip.Mappings[0].Renderer == null, Is.True,
                "An unset serialized Unity reference must use TargetRenderer even when it has a managed fake-null wrapper.");
            restoredRenderer.SetBlendShapeWeight(0, 29);

            restoredLip.SetResult(new LipSyncResult(0.5, 0, 0, 0, 0, Viseme.A, 0.5, 0.1));
            restoredLip.ApplyVisemes(0.02f);

            Assert.That(restoredRenderer.GetBlendShapeWeight(1), Is.EqualTo(40),
                "Empty per-mapping renderer references must still bind the fallback renderer after deserialization.");
            Assert.That(restoredRenderer.GetBlendShapeWeight(0), Is.EqualTo(29));
            instance.GetComponent<MfccLipSyncHelper>().ResetViseme();
            Assert.That(restoredRenderer.GetBlendShapeWeight(1), Is.Zero);
            Assert.That(restoredRenderer.GetBlendShapeWeight(0), Is.EqualTo(29));
        }

        private static Mesh CreateMesh(params string[] shapes)
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            foreach (var shape in shapes)
                mesh.AddBlendShapeFrame(shape, 100, new[] { Vector3.up, Vector3.up, Vector3.up }, null, null);
            return mesh;
        }

        private T Track<T>(T item) where T : Object { transientObjects.Add(item); return item; }
    }
}
#endif

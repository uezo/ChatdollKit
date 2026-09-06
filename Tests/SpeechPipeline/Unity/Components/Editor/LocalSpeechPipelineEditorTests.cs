using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.VAD;
using System;
using System.Linq;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChatdollKit.Tests.SpeechPipeline.Unity
{
    public sealed class LocalSpeechPipelineEditorTests
    {
        private GameObject gameObject;
        private GameObject externalObject;
        private LocalSpeechPipeline pipeline;

        [SetUp]
        public void SetUp()
        {
            gameObject = new GameObject("Pipeline dropdown test");
            pipeline = gameObject.AddComponent<LocalSpeechPipeline>();
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            UnityEngine.Object.DestroyImmediate(externalObject);
            UnityEngine.Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void AutoShowsOnlyEnabledCandidateWithoutAssigningIt()
        {
            var disabled = gameObject.AddComponent<AzureSpeechRecognizer>();
            disabled.enabled = false;
            var enabled = gameObject.AddComponent<OpenAISpeechRecognizer>();
            var choices = Choices();

            Assert.That(choices[0].Component, Is.Null);
            Assert.That(choices[0].Label.text, Is.EqualTo("Auto (OpenAISpeechRecognizer)"));
            Assert.That(choices.Single(choice => choice.Component == disabled).Label.text, Does.Contain("disabled"));
            Assert.That(choices.Single(choice => choice.Component == enabled).Label.text, Does.Not.Contain("disabled"));
            Assert.That(pipeline.Stt, Is.Null);
        }

        [Test]
        public void IdenticalProvidersHaveUniqueNamesAndAutoShowsAmbiguity()
        {
            var first = gameObject.AddComponent<AzureSpeechRecognizer>();
            var second = gameObject.AddComponent<AzureSpeechRecognizer>();
            var third = gameObject.AddComponent<OpenAISpeechRecognizer>();
            var choices = Choices();

            Assert.That(choices[0].Label.text, Is.EqualTo("Auto (select one: 3 enabled)"));
            Assert.That(choices.Single(choice => choice.Component == first).Label.text, Is.EqualTo("AzureSpeechRecognizer #1"));
            Assert.That(choices.Single(choice => choice.Component == second).Label.text, Is.EqualTo("AzureSpeechRecognizer #2"));
            Assert.That(choices.Single(choice => choice.Component == third).Label.text, Is.EqualTo("OpenAISpeechRecognizer"));
        }

        [Test]
        public void ExternallyAssignedComponentRemainsAnExplicitChoice()
        {
            externalObject = new GameObject("External STT");
            var external = externalObject.AddComponent<AzureSpeechRecognizer>();
            gameObject.AddComponent<AzureSpeechRecognizer>();
            pipeline.Stt = external;
            var choices = Choices();
            var selected = choices.Single(choice => choice.Component == external);

            Assert.That(selected.Label.text, Is.EqualTo("AzureSpeechRecognizer @ External STT (external)"));
            Assert.That(choices.Count(choice => choice.Component == external), Is.EqualTo(1));
            Assert.That(pipeline.Stt, Is.SameAs(external));
        }

        [Test]
        public void SelectingSecondInstanceAndAutoSupportsUndoAndRedo()
        {
            gameObject.AddComponent<AzureSpeechRecognizer>();
            var second = gameObject.AddComponent<AzureSpeechRecognizer>();
            var choices = Choices();
            Select(choices.Single(choice => choice.Component == second));
            Assert.That(pipeline.Stt, Is.SameAs(second));

            Undo.PerformUndo();
            Assert.That(pipeline.Stt, Is.Null);
            Undo.PerformRedo();
            Assert.That(pipeline.Stt, Is.SameAs(second));

            Select(choices[0]);
            Assert.That(pipeline.Stt, Is.Null);
        }

        [Test]
        public void SelectingProviderCreatesPrefabPropertyOverride()
        {
            var prefabPath = "Assets/LocalPipelineDropdownTest-" + Guid.NewGuid().ToString("N") + ".prefab";
            GameObject instance = null;
            try
            {
                gameObject.AddComponent<AzureSpeechRecognizer>();
                gameObject.AddComponent<AzureSpeechRecognizer>();
                var prefab = PrefabUtility.SaveAsPrefabAsset(gameObject, prefabPath);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var instancePipeline = instance.GetComponent<LocalSpeechPipeline>();
                var second = instance.GetComponents<AzureSpeechRecognizer>()[1];
                var choices = LocalSpeechPipelineEditor.BuildChoices(instancePipeline, typeof(SpeechRecognizerComponent), null);
                var serialized = new SerializedObject(instancePipeline);
                LocalSpeechPipelineEditor.SelectChoice(serialized.FindProperty("Stt"), choices.Single(choice => choice.Component == second));
                serialized.ApplyModifiedProperties();

                Assert.That(instancePipeline.Stt, Is.SameAs(second));
                // Refresh the serialized view before reading Unity's computed prefab-override state.
                serialized.Update();
                Assert.That(serialized.FindProperty("Stt").prefabOverride, Is.True);
                Assert.That(PrefabUtility.GetPropertyModifications(instance)
                    .Any(modification => modification.propertyPath == "Stt"), Is.True);
                Assert.That(prefab.GetComponent<LocalSpeechPipeline>().Stt, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(prefabPath);
            }
        }

        [Test]
        public void OptionalVadAndRequiredSttExplainMissingCandidates()
        {
            var vad = LocalSpeechPipelineEditor.BuildChoices(pipeline, typeof(SpeechDetectorComponent), null, optional: true);
            Assert.That(vad[0].Label.text, Is.EqualTo("Auto (none; optional)"));
            Assert.That(Choices()[0].Label.text, Is.EqualTo("Auto (none found)"));
        }

        [Test]
        public void LocalPipelineUsesSpecializedEditorAndSharedLiveStatus()
        {
            var editor = UnityEditor.Editor.CreateEditor(pipeline);
            try
            {
                Assert.That(editor, Is.TypeOf<LocalSpeechPipelineEditor>());
                Assert.That(editor, Is.InstanceOf<LiveSpeechComponentEditor>());
            }
            finally { UnityEngine.Object.DestroyImmediate(editor); }
        }

        private LocalSpeechPipelineEditor.ComponentChoice[] Choices()
            => LocalSpeechPipelineEditor.BuildChoices(pipeline, typeof(SpeechRecognizerComponent), pipeline.Stt);

        private void Select(LocalSpeechPipelineEditor.ComponentChoice choice)
        {
            Undo.IncrementCurrentGroup();
            var serialized = new SerializedObject(pipeline);
            LocalSpeechPipelineEditor.SelectChoice(serialized.FindProperty("Stt"), choice);
            serialized.ApplyModifiedProperties();
        }
    }
}

using System;
using ChatdollKit.Avatar;
using ChatdollKit.IO;
using ChatdollKit.Orchestration;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.UI.ConversationControls;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ChatdollKit.Tests.Orchestration.Unity
{
    public class ConversationControlsPrefabTests
    {
        private const string Folder = "Assets/ChatdollKit/Prefabs/Runtime/Orchestration/";

        [TestCase("ConversationInput")]
        [TestCase("MicrophoneControl")]
        [TestCase("SpeakerControl")]
        [TestCase("UIControlHorizontal")]
        public void PrefabsHaveValidScriptsAndNoDanglingPersistentCallbacks(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                Assert.That(component, Is.Not.Null, name + " has a missing script.");
            foreach (var button in prefab.GetComponentsInChildren<Button>(true))
                for (var i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    Assert.That(button.onClick.GetPersistentTarget(i), Is.Not.Null, button.name + " has a missing callback target.");

            var input = prefab.GetComponentInChildren<ConversationInput>(true);
            if (input != null)
            {
                Assert.That(input.Input, Is.Not.Null);
                Assert.That(input.SendButton, Is.Not.Null);
                Assert.That(input.ClearImageButton, Is.Not.Null);
                Assert.That(input.ImagePreview, Is.Not.Null);
                Assert.That(input.StatusText, Is.Not.Null);
                Assert.That(input.Orchestrator, Is.Null);
                Assert.That(input.Input.onEndEdit.GetPersistentEventCount(), Is.Zero, "Losing focus must not submit.");
            }
            var microphone = prefab.GetComponentInChildren<MicrophoneControl>(true);
            if (microphone != null)
            {
                Assert.That(microphone.MuteButton, Is.Not.Null);
                Assert.That(microphone.LevelMeter, Is.Not.Null);
                Assert.That(microphone.Microphone, Is.Null);
                Assert.That(microphone.MuteButton.onClick.GetPersistentEventCount(), Is.Zero);
                Assert.That(microphone.LevelMeter.onValueChanged.GetPersistentEventCount(), Is.Zero);
            }
            var speaker = prefab.GetComponentInChildren<SpeakerControl>(true);
            if (speaker != null)
            {
                Assert.That(speaker.MuteButton, Is.Not.Null);
                Assert.That(speaker.SettingsButton, Is.Not.Null);
                Assert.That(speaker.VolumeSlider, Is.Not.Null);
                Assert.That(speaker.VolumePanel, Is.Not.Null);
                Assert.That(speaker.Speech, Is.Null);
                Assert.That(speaker.MuteButton.onClick.GetPersistentEventCount(), Is.Zero);
                Assert.That(speaker.VolumeSlider.onValueChanged.GetPersistentEventCount(), Is.Zero);
            }
        }

        [Test]
        public void HorizontalPrefabIsWiredInternallyAndUsesNewConversationComponents()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "UIControlHorizontal.prefab");
            Assert.That(prefab.GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<GraphicRaycaster>(), Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<UnityEngine.EventSystems.EventSystem>(true), Is.Null);
            var input = prefab.GetComponentInChildren<ConversationInput>(true);
            var camera = prefab.GetComponentInChildren<SimpleCamera>(true);
            var cameraButton = prefab.GetComponentInChildren<ChatdollKit.UI.CameraButton>(true);
            Assert.That(camera, Is.Not.Null);
            Assert.That(cameraButton, Is.Not.Null);
            Assert.That(input.Camera, Is.SameAs(camera));
            Assert.That(input.ImagePicker, Is.SameAs(prefab.GetComponentInChildren<ChatdollKit.UI.ImageButton>(true)));
            Assert.That(input.ImagePicker.gameObject.activeSelf, Is.True);
            Assert.That(new SerializedObject(cameraButton).FindProperty("simpleCamera").objectReferenceValue, Is.SameAs(camera));
            Assert.That(prefab.GetComponentInChildren<ChatdollKit.UI.UIControlContainer>(true), Is.Null);
            Assert.That(prefab.GetComponentInChildren<ChatdollKit.UI.MessageInput>(true), Is.Null);
            Assert.That(prefab.GetComponentInChildren<ChatdollKit.UI.MicrophoneButton>(true), Is.Null);
            Assert.That(prefab.GetComponentInChildren<ChatdollKit.UI.SpeakerButton>(true), Is.Null);
            Assert.That(prefab.GetComponentInChildren<ChatdollKit.AIAvatar>(true), Is.Null);
            Assert.That(prefab.GetComponentInChildren<ChatdollKit.Dialog.DialogProcessor>(true), Is.Null);
        }

        [Test]
        public void AssemblyDependenciesPreserveLegacyMembershipAndDoNotPointBackToUi()
        {
            const string assembly = "ChatdollKit.UI.ConversationControls";
            Assert.That(typeof(ConversationInput).Assembly.GetName().Name, Is.EqualTo(assembly));
            Assert.That(typeof(MicrophoneControl).Assembly.GetName().Name, Is.EqualTo(assembly));
            Assert.That(typeof(SpeakerControl).Assembly.GetName().Name, Is.EqualTo(assembly));
            Assert.That(typeof(ChatdollKit.UI.MessageInput).Assembly.GetName().Name, Is.EqualTo("ChatdollKit"));
            Assert.That(typeof(ChatdollKit.UI.MicrophoneButton).Assembly.GetName().Name, Is.EqualTo("ChatdollKit"));
            Assert.That(typeof(ChatdollKit.UI.SpeakerButton).Assembly.GetName().Name, Is.EqualTo("ChatdollKit"));
            foreach (var type in new[] { typeof(ChatdollOrchestrator), typeof(AvatarController), typeof(SpeechPipelineResponse) })
                Assert.That(Array.Exists(type.Assembly.GetReferencedAssemblies(), reference => reference.Name == assembly), Is.False);
        }

        [Test]
        public void LegacyPrefabsRetainTheirOriginalComponents()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ChatdollKit/Prefabs/Runtime/UIControlHorizontal.prefab");
            Assert.That(prefab.GetComponentInChildren<ChatdollKit.UI.UIControlContainer>(true), Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<ChatdollKit.UI.MessageInput>(true), Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<ChatdollKit.UI.MicrophoneButton>(true), Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<ChatdollKit.UI.SpeakerButton>(true), Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<ConversationInput>(true), Is.Null);
        }
    }
}

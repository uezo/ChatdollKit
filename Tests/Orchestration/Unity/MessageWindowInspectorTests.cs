using System;
using System.Linq;
using ChatdollKit.Orchestration;
using ChatdollKit.UI.MessageWindow.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Window = ChatdollKit.UI.MessageWindow.MessageWindow;

namespace ChatdollKit.Tests.Orchestration.Unity
{
    public class MessageWindowInspectorTests
    {
        private GameObject sourceRoot;
        private GameObject windowRoot;
        private GameObject secondWindowRoot;
        private string prefabPath;

        [SetUp]
        public void SetUp()
        {
            sourceRoot = new GameObject("Conversation publishers");
            windowRoot = new GameObject("Subscriber");
            windowRoot.AddComponent<Window>();
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            if (sourceRoot != null) Object.DestroyImmediate(sourceRoot);
            if (windowRoot != null) Object.DestroyImmediate(windowRoot);
            if (secondWindowRoot != null) Object.DestroyImmediate(secondWindowRoot);
            if (!string.IsNullOrEmpty(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
        }

        [Test]
        public void GameObjectDropFindsPublisherInsteadOfFirstUnrelatedComponent()
        {
            var unrelated = sourceRoot.AddComponent<MessageWindowInspectorNonSource>();
            var source = sourceRoot.AddComponent<MessageWindowInspectorSource>();
            Assert.That(MessageWindowEditor.FindSources(sourceRoot), Is.EqualTo(new[] { source }));
            Assert.That(MessageWindowEditor.FindSources(unrelated), Is.EqualTo(new[] { source }));
            Assert.That(windowRoot.GetComponent<Window>().Source, Is.Null, "Looking for sources must never edit a scene.");
        }

        [Test]
        public void MultiplePublishersRequireChoiceWhileExplicitComponentKeepsItsIdentity()
        {
            var first = sourceRoot.AddComponent<MessageWindowInspectorSource>();
            var second = sourceRoot.AddComponent<MessageWindowInspectorSource>();
            Assert.That(MessageWindowEditor.FindSources(sourceRoot), Is.EqualTo(new[] { first, second }));
            Assert.That(MessageWindowEditor.FindSources(second), Is.EqualTo(new[] { second }));
            Assert.That(MessageWindowEditor.FindSources(windowRoot), Is.Empty);
            Assert.That(MessageWindowEditor.FindSources(null), Is.Empty);
        }

        [Test]
        public void ExplicitRepairOfInvalidComponentIsSerializedAndUndoable()
        {
            var unrelated = sourceRoot.AddComponent<MessageWindowInspectorNonSource>();
            var source = sourceRoot.AddComponent<MessageWindowInspectorSource>();
            var window = windowRoot.GetComponent<Window>();
            window.Source = unrelated;
            var candidates = MessageWindowEditor.FindSources(window.Source);
            Assert.That(window.Source, Is.SameAs(unrelated), "Inspecting an old invalid assignment must not replace it.");
            Undo.IncrementCurrentGroup();
            Assert.That(MessageWindowEditor.AssignSource(new Object[] { window }, candidates[0]), Is.True);
            Assert.That(window.Source, Is.SameAs(source));
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(window.Source, Is.SameAs(unrelated));
        }

        [Test]
        public void ExplicitSelectionSupportsMultipleWindowsAndRejectsInvalidComponents()
        {
            secondWindowRoot = new GameObject("Second subscriber");
            var second = secondWindowRoot.AddComponent<Window>();
            var first = windowRoot.GetComponent<Window>();
            var source = sourceRoot.AddComponent<MessageWindowInspectorSource>();
            var invalid = sourceRoot.AddComponent<MessageWindowInspectorNonSource>();
            var windows = new Object[] { first, second };
            Assert.That(MessageWindowEditor.AssignSource(windows, source), Is.True);
            Assert.That(first.Source, Is.SameAs(source));
            Assert.That(second.Source, Is.SameAs(source));
            Assert.That(MessageWindowEditor.AssignSource(windows, invalid), Is.False);
            Assert.That(first.Source, Is.SameAs(source));
            Assert.That(second.Source, Is.SameAs(source));
            Assert.That(MessageWindowEditor.AssignSource(windows, null), Is.True);
            Assert.That(first.Source, Is.Null);
            Assert.That(second.Source, Is.Null);
        }

        [Test]
        public void ScenePublisherAssignmentCreatesPrefabOverrideButCannotBeWrittenIntoPrefabAsset()
        {
            prefabPath = AssetDatabase.GenerateUniqueAssetPath("Assets/MessageWindowInspectorTest.prefab");
            var prefab = PrefabUtility.SaveAsPrefabAsset(windowRoot, prefabPath);
            var source = sourceRoot.AddComponent<MessageWindowInspectorSource>();
            Assert.That(MessageWindowEditor.AssignSource(new Object[] { prefab.GetComponent<Window>() }, source), Is.False);
            Assert.That(prefab.GetComponent<Window>().Source, Is.Null);
            secondWindowRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var instanceWindow = secondWindowRoot.GetComponent<Window>();
            Assert.That(MessageWindowEditor.AssignSource(new Object[] { instanceWindow }, source), Is.True);
            Assert.That(instanceWindow.Source, Is.SameAs(source));
            Assert.That(PrefabUtility.GetPropertyModifications(secondWindowRoot)
                .Any(modification => modification.propertyPath == "Source" && modification.objectReference == source), Is.True);
            Assert.That(prefab.GetComponent<Window>().Source, Is.Null);
        }
    }

    public sealed class MessageWindowInspectorSource : MonoBehaviour, IConversationEventSource
    {
        public ConversationSnapshot CurrentConversation => new ConversationSnapshot();
        public event Action<ConversationEvent> ConversationEventReceived { add { } remove { } }
    }

    public sealed class MessageWindowInspectorNonSource : MonoBehaviour { }
}

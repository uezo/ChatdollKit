using ChatdollKit.Orchestration;
using NUnit.Framework;
using UnityEngine;

namespace ChatdollKit.Tests.Orchestration.Unity
{
    public class ChatdollOrchestratorOptionsSerializationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ExistingSavedSettingsKeepTheirMeaningAndRoundTripWithTheNewApi(bool savedMute)
        {
            var owner = new GameObject("Orchestrator options serialization test");
            var restoredOwner = new GameObject("Restored orchestrator options");
            try
            {
                var orchestrator = owner.AddComponent<ChatdollOrchestrator>();
                JsonUtility.FromJsonOverwrite("{\"Options\":{\"MuteMicrophoneDuringResponse\":"
                    + (savedMute ? "true" : "false")
                    + ",\"MaxPendingAudioFrames\":17,\"MaxPendingPresentations\":23}}", orchestrator);
                Assert.That(orchestrator.Options.AllowBargeIn, Is.EqualTo(!savedMute));
                Assert.That(orchestrator.Options.Copy().AllowBargeIn, Is.EqualTo(!savedMute));
                Assert.That(orchestrator.Options.MaxPendingAudioFrames, Is.EqualTo(17));
                Assert.That(orchestrator.Options.MaxPendingPresentations, Is.EqualTo(23));

                // Change through the new API, then save and restore through Unity's actual serializer.
                orchestrator.Options.AllowBargeIn = savedMute;
                var restored = restoredOwner.AddComponent<ChatdollOrchestrator>();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(orchestrator), restored);
                Assert.That(restored.Options.AllowBargeIn, Is.EqualTo(savedMute));
                Assert.That(restored.Options.MaxPendingAudioFrames, Is.EqualTo(17));
                Assert.That(restored.Options.MaxPendingPresentations, Is.EqualTo(23));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(restoredOwner);
            }
        }
    }
}

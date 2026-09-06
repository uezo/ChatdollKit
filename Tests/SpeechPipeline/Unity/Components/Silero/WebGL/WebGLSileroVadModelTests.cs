using System;
using System.IO;
using System.Reflection;
using System.Threading;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.VAD.Silero;
using ChatdollKit.SpeechPipeline.VAD.Silero.WebGL;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace ChatdollKit.Tests.SpeechPipeline.Unity.Silero.WebGL
{
    /// <summary>Exercises the Unity recipient of browser callbacks without starting JavaScript or loading a model.</summary>
    public sealed class WebGLSileroVadModelTests
    {
        private GameObject owner;
        private SileroVadWebGLBridge bridge;
        private WebGLSileroVadModel model;

        [SetUp]
        public void SetUp()
        {
            model = (WebGLSileroVadModel)Activator.CreateInstance(typeof(WebGLSileroVadModel), true);
            owner = new GameObject("Silero callback test");
            bridge = owner.AddComponent<SileroVadWebGLBridge>();
            SetField(bridge, "Model", model);
            SetField(model, "bridge", bridge);
            bridge.OnSileroReady("");
        }

        [TearDown]
        public void TearDown()
        {
            model?.Dispose();
            if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
        }

        [Test]
        public void OnlyTheMatchingRequestReceivesItsProbability()
        {
            var completion = BeginRequest(model, 2);
            bridge.OnSileroResult("{\"requestId\":1,\"probability\":0.9}");
            Assert.That(completion.Task.Status, Is.EqualTo(UniTaskStatus.Pending));
            bridge.OnSileroResult("{\"requestId\":2,\"probability\":0.75}");
            Assert.That(completion.Task.GetAwaiter().GetResult(), Is.EqualTo(0.75f));
        }

        [Test]
        public void ResetCancelsThePreviousRequestAndDiscardsItsLateResultAndError()
        {
            var previous = BeginRequest(model, 1);
            model.ResetStates();
            Assert.Throws<OperationCanceledException>(() => previous.Task.GetAwaiter().GetResult());

            var current = BeginRequest(model, 2);
            bridge.OnSileroResult("{\"requestId\":1,\"probability\":0.95}");
            bridge.OnSileroError("{\"requestId\":1,\"message\":\"old inference failed\"}");
            Assert.That(current.Task.Status, Is.EqualTo(UniTaskStatus.Pending));
            bridge.OnSileroResult("{\"requestId\":2,\"probability\":0.25}");
            Assert.That(current.Task.GetAwaiter().GetResult(), Is.EqualTo(0.25f));
        }

        [Test]
        public void ARequestErrorCompletesThatRequestAndAllowsAReset()
        {
            var completion = BeginRequest(model, 3);
            bridge.OnSileroError("{\"requestId\":3,\"message\":\"model failed\"}");
            var error = Assert.Throws<IOException>(() => completion.Task.GetAwaiter().GetResult());
            Assert.That(error.Message, Does.Contain("model failed"));
            Assert.DoesNotThrow(() => model.ResetStates());
        }

        [Test]
        public void AModelFailureFailsPendingWorkAndCannotBeReopenedByALateReadyCallback()
        {
            var completion = BeginRequest(model, 1);
            bridge.OnSileroError("{\"requestId\":0,\"message\":\"runtime failed\"}");
            Assert.Throws<IOException>(() => completion.Task.GetAwaiter().GetResult());
            bridge.OnSileroReady("");
            var error = Assert.Throws<IOException>(() => model.ResetStates());
            Assert.That(error.Message, Does.Contain("runtime failed"));
        }

        [TestCase("{\"requestId\":1,\"probability\":-0.1}")]
        [TestCase("{\"requestId\":1,\"probability\":1.1}")]
        [TestCase("not-json")]
        public void InvalidProbabilitiesOrMalformedMessagesFailPendingWork(string json)
        {
            var completion = BeginRequest(model, 1);
            bridge.OnSileroResult(json);
            Assert.Throws<IOException>(() => completion.Task.GetAwaiter().GetResult());
            Assert.Throws<IOException>(() => model.ResetStates());
        }

        [Test]
        public void DisposalCancelsWorkDetachesCallbacksAndRejectsFurtherUse()
        {
            var completion = BeginRequest(model, 1);
            model.Dispose();
            Assert.Throws<OperationCanceledException>(() => completion.Task.GetAwaiter().GetResult());
            Assert.That(owner == null, Is.True);
            Assert.DoesNotThrow(() => model.Dispose());
            Assert.DoesNotThrow(() => bridge.OnSileroReady(""));
            Assert.DoesNotThrow(() => bridge.OnSileroResult("{\"requestId\":1,\"probability\":1}"));
            Assert.DoesNotThrow(() => bridge.OnSileroError("{\"requestId\":0,\"message\":\"late\"}"));
            Assert.Throws<ObjectDisposedException>(() => model.ResetStates());
        }

        [Test]
        public void DestroyingTheCallbackRecipientDisposesTheModel()
        {
            var completion = BeginRequest(model, 1);
            // Edit Mode does not invoke OnDestroy for a plain component whose Awake has not run.
            typeof(SileroVadWebGLBridge).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(bridge, null);
            Assert.Throws<OperationCanceledException>(() => completion.Task.GetAwaiter().GetResult());
            Assert.Throws<ObjectDisposedException>(() => model.ResetStates());
        }

        [Test]
        public void ModelsWithTheSameRequestIdHaveIndependentCallbacks()
        {
            var other = (WebGLSileroVadModel)Activator.CreateInstance(typeof(WebGLSileroVadModel), true);
            var otherOwner = new GameObject("Independent Silero callback test");
            var otherBridge = otherOwner.AddComponent<SileroVadWebGLBridge>();
            SetField(otherBridge, "Model", other);
            SetField(other, "bridge", otherBridge);
            otherBridge.OnSileroReady("");
            try
            {
                var first = BeginRequest(model, 1);
                var second = BeginRequest(other, 1);
                bridge.OnSileroResult("{\"requestId\":1,\"probability\":0.1}");
                Assert.That(first.Task.GetAwaiter().GetResult(), Is.EqualTo(0.1f));
                Assert.That(second.Task.Status, Is.EqualTo(UniTaskStatus.Pending));
                otherBridge.OnSileroResult("{\"requestId\":1,\"probability\":0.9}");
                Assert.That(second.Task.GetAwaiter().GetResult(), Is.EqualTo(0.9f));
            }
            finally
            {
                other.Dispose();
                if (otherOwner != null) UnityEngine.Object.DestroyImmediate(otherOwner);
            }
        }

        [Test]
        public void InvalidOrCanceledInputDoesNotReplaceAPendingRequest()
        {
            Assert.Throws<ArgumentNullException>(() => model.PredictAsync(null, 16000).GetAwaiter().GetResult());
            Assert.Throws<ArgumentOutOfRangeException>(() => model.PredictAsync(new float[512], 44100).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => model.PredictAsync(new float[256], 16000).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => model.PredictAsync(new float[512], 8000).GetAwaiter().GetResult());

            var completion = BeginRequest(model, 1);
            Assert.Throws<InvalidOperationException>(() => model.PredictAsync(new float[512], 16000).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => model.PredictAsync(new float[512], 16000,
                new CancellationToken(true)).GetAwaiter().GetResult());
            Assert.That(completion.Task.Status, Is.EqualTo(UniTaskStatus.Pending));
            bridge.OnSileroResult("{\"requestId\":1,\"probability\":0.5}");
            Assert.That(completion.Task.GetAwaiter().GetResult(), Is.EqualTo(0.5f));
        }

        [Test]
        public void EditorCreationReportsTheRequiredPlatform()
        {
            Assert.Throws<PlatformNotSupportedException>(() => WebGLSileroVadModel.CreateAsync(
                "https://example.com/silero_vad.onnx", WebGLSileroVadModel.DefaultRuntimeScriptUrl).GetAwaiter().GetResult());
        }

        private static SpeechCompletionSource<float> BeginRequest(WebGLSileroVadModel target, int id)
        {
            var requestType = typeof(WebGLSileroVadModel).GetNestedType("Prediction", BindingFlags.NonPublic);
            var request = Activator.CreateInstance(requestType, true);
            SetField(request, "Id", id);
            SetField(target, "pending", request);
            return (SpeechCompletionSource<float>)requestType.GetField("Completion", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(request);
        }

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.VAD.Silero;
using ChatdollKit.SpeechPipeline.VAD.Silero.WebGL;
using ChatdollKit.SpeechPipeline.VAD;
using NUnit.Framework;
using UnityEngine;

namespace ChatdollKit.Tests.SpeechPipeline.Unity.Silero
{
    public class SileroSpeechDetectorTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async NUnitTask NativeProviderLoadsSuppliedModelProcessesPcmAndAppliesLiveSettings(bool stream)
        {
#if !CHATDOLLKIT_ONNXRUNTIME
            Assert.Ignore("Install com.github.asus4.onnxruntime to run the native provider integration test.");
#endif
            if (!File.Exists(Path.Combine(Application.streamingAssetsPath, "silero_vad.onnx")))
                Assert.Ignore("Supply StreamingAssets/silero_vad.onnx to run the native provider integration test.");
            var owner = new GameObject("Silero provider integration test");
            SpeechDetectorLease lease = null;
            var recognizer = new BorrowedRecognizer();
            try
            {
                SileroSpeechDetector component = stream
                    ? owner.AddComponent<SileroStreamSpeechDetector>()
                    : owner.AddComponent<SileroSpeechDetector>();
                if (stream) component.ModelFileName = Path.Combine(Application.streamingAssetsPath, "silero_vad.onnx");
                component.SpeechProbabilityThreshold = 1;
                component.UseVolumeThreshold = true;
                component.VolumeDbThreshold = -40;
                lease = await component.CreateDetectorAsync(recognizer, CancellationToken.None);
                Assert.That(lease.Detector, stream ? Is.TypeOf<SileroStreamSpeechDetectorEngine>() : Is.TypeOf<SileroSpeechDetectorEngine>());
                if (stream)
                    Assert.That(((SileroStreamSpeechDetectorEngine)lease.Detector).SpeechRecognizer, Is.SameAs(recognizer));
                var errors = new List<Exception>();
                lease.Detector.Error += errors.Add;
                Assert.That(await lease.Detector.ProcessSamplesAsync(new byte[1024]), Is.False);

                component.UseVolumeThreshold = false;
                component.Settings.SilenceDurationThreshold = 0.25f;
                if (stream) ((SileroStreamSpeechDetector)component).SegmentSilenceThreshold = 0.125f;
                await component.ApplyDetectorOptionsAsync(lease.Detector, component.BuildOptions(), CancellationToken.None);
                Assert.That(((SileroSpeechDetectorOptions)lease.Detector.GetOptions()).VolumeDbThreshold, Is.Null);
                Assert.That(lease.Detector.GetOptions().SilenceDurationThreshold, Is.EqualTo(0.25));
                if (stream)
                    Assert.That(((SileroStreamSpeechDetectorOptions)lease.Detector.GetOptions()).SegmentSilenceThreshold, Is.EqualTo(0.125));
                Assert.That(await lease.Detector.ProcessSamplesAsync(new byte[1024]), Is.False);
                Assert.That(errors, Is.Empty);
                await lease.DisposeAsync();
                await lease.DisposeAsync();
                Assert.That(recognizer.Disposals, Is.Zero, "The pipeline owns the selected STT provider.");
                await ExpectExceptionAsync<ObjectDisposedException>(async () => await lease.Detector.ProcessSamplesAsync(new byte[1024]));
            }
            finally
            {
                if (lease != null) await lease.DisposeAsync();
                recognizer.Dispose();
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void ProviderRestartKeyIncludesModelRuntimeIteratorModeAndPcmFormat()
        {
            var owner = new GameObject("Silero restart settings test");
            try
            {
                var component = owner.AddComponent<SileroSpeechDetector>();
                var original = component.GetRestartKey();
                component.ModelFileName = "alternate.onnx";
                Assert.That(component.GetRestartKey(), Is.Not.EqualTo(original));
                component.ModelFileName = "silero_vad.onnx";
                var originalRuntimeScript = component.WebGLRuntimeScriptUrl;
                Assert.That(originalRuntimeScript, Is.EqualTo(WebGLSileroVadModel.DefaultRuntimeScriptUrl));
                component.WebGLRuntimeScriptUrl = "onnxruntime/ort.wasm.min.js";
                Assert.That(component.GetRestartKey(), Is.Not.EqualTo(original));
                component.WebGLRuntimeScriptUrl = originalRuntimeScript;
                component.UseVadIterator = true;
                Assert.That(component.GetRestartKey(), Is.Not.EqualTo(original));
                component.UseVadIterator = false;
                component.Settings.SampleRate = 8000;
                Assert.That(component.GetRestartKey(), Is.Not.EqualTo(original));
                Assert.That(((SileroSpeechDetectorOptions)component.BuildOptions()).ChunkSize, Is.EqualTo(256));
                component.Settings.SampleRate = 16000;
                component.SpeechProbabilityThreshold = 0.8f;
                component.UseVolumeThreshold = true;
                Assert.That(component.GetRestartKey(), Is.EqualTo(original), "Threshold edits apply to the current detector.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public async NUnitTask NativeProviderRejectsRelativeTraversalBeforeReadingTheModel()
        {
#if !CHATDOLLKIT_ONNXRUNTIME
            Assert.Ignore("Install com.github.asus4.onnxruntime to exercise native model loading.");
#endif
            var owner = new GameObject("Silero model path test");
            try
            {
                var component = owner.AddComponent<SileroSpeechDetector>();
                component.ModelFileName = "../outside.onnx";
                await ExpectExceptionAsync<ArgumentException>(async () => await component.CreateDetectorAsync(null, CancellationToken.None));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void WebGLUrlsUseStreamingAssetsAndAcceptAnHttpsRuntime()
        {
            var expectedModelUrl = new Uri(Application.streamingAssetsPath.TrimEnd('/') + "/models/silero_vad.onnx").AbsoluteUri;
            var expectedRuntimeUrl = new Uri(Application.streamingAssetsPath.TrimEnd('/') + "/onnxruntime/ort.wasm.min.js").AbsoluteUri;
            Assert.That(ResolveUrl("ResolveModelUrl", "models/silero_vad.onnx", true), Is.EqualTo(expectedModelUrl));
            Assert.That(ResolveUrl("ResolveModelUrl", "models\\silero_vad.onnx", true), Is.EqualTo(expectedModelUrl));
            Assert.That(ResolveUrl("ResolveWebGLRuntimeScriptUrl", "onnxruntime/ort.wasm.min.js"), Is.EqualTo(expectedRuntimeUrl));
            Assert.That(ResolveUrl("ResolveWebGLRuntimeScriptUrl", WebGLSileroVadModel.DefaultRuntimeScriptUrl),
                Is.EqualTo(WebGLSileroVadModel.DefaultRuntimeScriptUrl));
        }

        [Test]
        public void NativeAbsoluteModelPathsRemainSupportedButWebGLRequiresStreamingAssets()
        {
            var path = Path.Combine(Application.temporaryCachePath, "silero_vad.onnx");
            Assert.That(ResolveUrl("ResolveModelUrl", path, false), Is.EqualTo(new Uri(path).AbsoluteUri));
            Assert.Throws<ArgumentException>(() => ResolveUrl("ResolveModelUrl", path, true));
        }

        [TestCase("")]
        [TestCase("  ")]
        [TestCase("../outside.onnx")]
        [TestCase("models\\..\\outside.onnx")]
        [TestCase("https://example.com/silero_vad.onnx")]
        public void InvalidRelativeModelPathsAreRejected(string path)
        {
            Assert.Throws<ArgumentException>(() => ResolveUrl("ResolveModelUrl", path, false));
            Assert.Throws<ArgumentException>(() => ResolveUrl("ResolveModelUrl", path, true));
        }

        [TestCase("")]
        [TestCase("  ")]
        [TestCase("../ort.wasm.min.js")]
        [TestCase("onnxruntime\\..\\ort.wasm.min.js")]
        [TestCase("http://example.com/ort.wasm.min.js")]
        [TestCase("//example.com/ort.wasm.min.js")]
        [TestCase("file:///tmp/ort.wasm.min.js")]
        [TestCase("javascript:alert(1)")]
        public void RuntimeScriptRejectsInvalidLocations(string path)
            => Assert.Throws<ArgumentException>(() => ResolveUrl("ResolveWebGLRuntimeScriptUrl", path));

#if !CHATDOLLKIT_ONNXRUNTIME
        [Test]
        public async NUnitTask NativeProviderWithoutOnnxReportsTheDependencyBeforeReadingTheModel()
        {
            var owner = new GameObject("Silero missing native runtime test");
            try
            {
                var component = owner.AddComponent<SileroSpeechDetector>();
                component.ModelFileName = "does-not-exist.onnx";
                var error = await ExpectExceptionAsync<NotSupportedException>(async () =>
                    await component.CreateDetectorAsync(null, CancellationToken.None));
                Assert.That(error.Message, Does.Contain("com.github.asus4.onnxruntime"));
                Assert.That(error.Message, Does.Contain("WebGL build"));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
#endif

        [Test]
        public void StreamProviderRejectsMissingRecognizerBeforeReadingTheModel()
        {
            var owner = new GameObject("Silero stream dependency test");
            try
            {
                var component = owner.AddComponent<SileroStreamSpeechDetector>();
                component.ModelFileName = "does-not-exist.onnx";
                Assert.Throws<ArgumentNullException>(() => component.CreateDetectorAsync(null, CancellationToken.None));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        private sealed class BorrowedRecognizer : ISpeechRecognizer, IDisposable
        {
            public int Disposals;
            public UniTask<SpeechRecognitionResult> RecognizeAsync(string sessionId, byte[] audio, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("Silent input should not request recognition.");
            public void Dispose() => Disposals++;
        }

        private static string ResolveUrl(string methodName, params object[] arguments)
        {
            var method = typeof(SileroSpeechDetector).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            try { return (string)method.Invoke(null, arguments); }
            catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
        }

        private static async UniTask<T> ExpectExceptionAsync<T>(Func<UniTask> operation) where T : Exception
        {
            try { await operation(); }
            catch (Exception error)
            {
                Assert.That(error, Is.InstanceOf<T>());
                return (T)error;
            }
            Assert.Fail("Expected " + typeof(T).Name);
            return null;
        }

    }
}

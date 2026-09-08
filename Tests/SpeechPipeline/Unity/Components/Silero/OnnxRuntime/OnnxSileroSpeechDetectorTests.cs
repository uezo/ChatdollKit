using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.VAD.Silero;
using ChatdollKit.Extension.SileroOnnxRuntime;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.VAD;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChatdollKit.Tests.SpeechPipeline.Unity.Silero.OnnxRuntime
{
    public class OnnxSileroSpeechDetectorTests
    {
        private const string DefaultRuntimeScriptUrl = "https://cdn.jsdelivr.net/npm/onnxruntime-web@1.21.0/dist/ort.wasm.min.js";
        [UnityTest]
        public IEnumerator NativeProviderProcesses16KhzPcmAndAppliesLiveSettings()
            => UniTask.ToCoroutine(() => NativeProviderLoadsSuppliedModelProcessesPcmAndAppliesLiveSettings(false, 16000));

        [UnityTest]
        public IEnumerator NativeStreamProviderProcesses16KhzPcmAndAppliesLiveSettings()
            => UniTask.ToCoroutine(() => NativeProviderLoadsSuppliedModelProcessesPcmAndAppliesLiveSettings(true, 16000));

        [UnityTest]
        public IEnumerator NativeProviderProcesses8KhzPcmAndAppliesLiveSettings()
            => UniTask.ToCoroutine(() => NativeProviderLoadsSuppliedModelProcessesPcmAndAppliesLiveSettings(false, 8000));

        [UnityTest]
        public IEnumerator NativeStreamProviderProcesses8KhzPcmAndAppliesLiveSettings()
            => UniTask.ToCoroutine(() => NativeProviderLoadsSuppliedModelProcessesPcmAndAppliesLiveSettings(true, 8000));

        private async UniTask NativeProviderLoadsSuppliedModelProcessesPcmAndAppliesLiveSettings(bool stream, int sampleRate)
        {
            var modelPath = RequireNativeModelPath();
            var owner = new GameObject("Silero provider integration test");
            SpeechDetectorLease lease = null;
            var recognizer = new BorrowedRecognizer();
            try
            {
                OnnxSileroSpeechDetector component = stream
                    ? owner.AddComponent<OnnxSileroStreamSpeechDetector>()
                    : owner.AddComponent<OnnxSileroSpeechDetector>();
                component.ModelFileName = modelPath;
                component.Settings.SampleRate = sampleRate;
                var chunkSize = sampleRate == 16000 ? 512 : 256;
                component.SpeechProbabilityThreshold = 1;
                component.UseVolumeThreshold = true;
                component.VolumeDbThreshold = -40;
                lease = await component.CreateDetectorAsync(recognizer, CancellationToken.None);
                Assert.That(lease.Detector, stream ? Is.TypeOf<SileroStreamSpeechDetectorEngine>() : Is.TypeOf<SileroSpeechDetectorEngine>());
                Assert.That(lease.Detector.SampleRate, Is.EqualTo(sampleRate));
                Assert.That(lease.Detector.Channels, Is.EqualTo(1));
                Assert.That(((SileroSpeechDetectorOptions)lease.Detector.GetOptions()).ChunkSize, Is.EqualTo(chunkSize));
                if (stream)
                    Assert.That(((SileroStreamSpeechDetectorEngine)lease.Detector).SpeechRecognizer, Is.SameAs(recognizer));
                var errors = new List<Exception>();
                lease.Detector.Error += errors.Add;
                await ExpectExceptionAsync<OperationCanceledException>(async () =>
                    await lease.Detector.ProcessSamplesAsync(new byte[chunkSize * 2], cancellationToken: new CancellationToken(true)));
                Assert.That(await lease.Detector.ProcessSamplesAsync(new byte[chunkSize * 2]), Is.False);

                component.UseVolumeThreshold = false;
                component.Settings.SilenceDurationThreshold = 0.25f;
                if (stream) ((OnnxSileroStreamSpeechDetector)component).SegmentSilenceThreshold = 0.125f;
                await component.ApplyDetectorOptionsAsync(lease.Detector, component.BuildOptions(), CancellationToken.None);
                Assert.That(((SileroSpeechDetectorOptions)lease.Detector.GetOptions()).VolumeDbThreshold, Is.Null);
                Assert.That(lease.Detector.GetOptions().SilenceDurationThreshold, Is.EqualTo(0.25));
                if (stream)
                    Assert.That(((SileroStreamSpeechDetectorOptions)lease.Detector.GetOptions()).SegmentSilenceThreshold, Is.EqualTo(0.125));
                Assert.That(await lease.Detector.ProcessSamplesAsync(new byte[chunkSize * 2]), Is.False);
                Assert.That(errors, Is.Empty);
                await lease.DisposeAsync();
                await lease.DisposeAsync();
                Assert.That(recognizer.Disposals, Is.Zero, "The pipeline owns the selected STT provider.");
                await ExpectExceptionAsync<ObjectDisposedException>(async () => await lease.Detector.ProcessSamplesAsync(new byte[chunkSize * 2]));
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
                var component = owner.AddComponent<OnnxSileroSpeechDetector>();
                var original = component.GetRestartKey();
                component.ModelFileName = "alternate.onnx";
                Assert.That(component.GetRestartKey(), Is.Not.EqualTo(original));
                component.ModelFileName = "silero_vad.onnx";
                var originalRuntimeScript = component.WebGLRuntimeScriptUrl;
                Assert.That(originalRuntimeScript, Is.EqualTo(DefaultRuntimeScriptUrl));
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

        [UnityTest]
        public IEnumerator NativeProviderRejectsRelativeTraversalBeforeReadingTheModel()
            => UniTask.ToCoroutine(NativeProviderRejectsRelativeTraversalBeforeReadingTheModelAsync);

        private async UniTask NativeProviderRejectsRelativeTraversalBeforeReadingTheModelAsync()
        {
#if !CHATDOLLKIT_ONNXRUNTIME
            Assert.Ignore("Install com.github.asus4.onnxruntime to exercise native model loading.");
#endif
            var owner = new GameObject("Silero model path test");
            try
            {
                var component = owner.AddComponent<OnnxSileroSpeechDetector>();
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
            Assert.That(ResolveUrl("ResolveWebGLRuntimeScriptUrl", DefaultRuntimeScriptUrl),
                Is.EqualTo(DefaultRuntimeScriptUrl));
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
        [UnityTest]
        public IEnumerator NativeProviderWithoutOnnxReportsTheDependencyBeforeReadingTheModel()
            => UniTask.ToCoroutine(() => NativeProviderWithoutOnnxReportsTheDependencyBeforeReadingTheModelAsync(false));

        [UnityTest]
        public IEnumerator NativeStreamProviderWithoutOnnxReportsTheDependencyBeforeReadingTheModel()
            => UniTask.ToCoroutine(() => NativeProviderWithoutOnnxReportsTheDependencyBeforeReadingTheModelAsync(true));

        private async UniTask NativeProviderWithoutOnnxReportsTheDependencyBeforeReadingTheModelAsync(bool stream)
        {
            var owner = new GameObject("Silero missing native runtime test");
            var recognizer = new BorrowedRecognizer();
            try
            {
                OnnxSileroSpeechDetector component = stream
                    ? owner.AddComponent<OnnxSileroStreamSpeechDetector>()
                    : owner.AddComponent<OnnxSileroSpeechDetector>();
                component.ModelFileName = "does-not-exist.onnx";
                var error = await ExpectExceptionAsync<NotSupportedException>(async () =>
                    await component.CreateDetectorAsync(recognizer, CancellationToken.None));
                Assert.That(recognizer.Disposals, Is.Zero);
                Assert.That(error.Message, Does.Contain("com.github.asus4.onnxruntime"));
                Assert.That(error.Message, Does.Contain("WebGL build"));
            }
            finally
            {
                recognizer.Dispose();
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }
#endif

        [Test]
        public void StreamProviderRejectsMissingRecognizerBeforeReadingTheModel()
        {
            var owner = new GameObject("Silero stream dependency test");
            try
            {
                var component = owner.AddComponent<OnnxSileroStreamSpeechDetector>();
                component.ModelFileName = "does-not-exist.onnx";
                Assert.Throws<ArgumentNullException>(() => component.CreateDetectorAsync(null, CancellationToken.None));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [TestCase(false, 8000, 256)]
        [TestCase(true, 8000, 256)]
        [TestCase(false, 16000, 512)]
        [TestCase(true, 16000, 512)]
        public void BothProvidersRemainSelectableByLocalSpeechPipelineAndExposeTheCorrectPcmFormat(bool stream, int sampleRate, int chunkSize)
        {
            var owner = new GameObject("ONNX Silero pipeline selection test");
            try
            {
                OnnxSileroSpeechDetector component = stream
                    ? owner.AddComponent<OnnxSileroStreamSpeechDetector>()
                    : owner.AddComponent<OnnxSileroSpeechDetector>();
                var pipeline = owner.AddComponent<LocalSpeechPipeline>();
                pipeline.Vad = component;
                component.Settings.SampleRate = sampleRate;
                var options = (SileroSpeechDetectorOptions)component.BuildOptions();
                Assert.That(pipeline.Vad, Is.SameAs(component));
                Assert.That(options.SampleRate, Is.EqualTo(sampleRate));
                Assert.That(options.ChunkSize, Is.EqualTo(chunkSize));
                Assert.That(options.Channels, Is.EqualTo(1));
                Assert.That(options, stream ? Is.TypeOf<SileroStreamSpeechDetectorOptions>() : Is.TypeOf<SileroSpeechDetectorOptions>());
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [UnityTest]
        public IEnumerator CanceledCreationDoesNotReadTheModelOrDisposeTheBorrowedRecognizer()
            => UniTask.ToCoroutine(() => CanceledCreationDoesNotReadTheModelOrDisposeTheBorrowedRecognizerAsync(false));

        [UnityTest]
        public IEnumerator CanceledStreamCreationDoesNotReadTheModelOrDisposeTheBorrowedRecognizer()
            => UniTask.ToCoroutine(() => CanceledCreationDoesNotReadTheModelOrDisposeTheBorrowedRecognizerAsync(true));

        private async UniTask CanceledCreationDoesNotReadTheModelOrDisposeTheBorrowedRecognizerAsync(bool stream)
        {
            var owner = new GameObject("ONNX Silero cancellation test");
            var recognizer = new BorrowedRecognizer();
            try
            {
                OnnxSileroSpeechDetector component = stream
                    ? owner.AddComponent<OnnxSileroStreamSpeechDetector>()
                    : owner.AddComponent<OnnxSileroSpeechDetector>();
                component.ModelFileName = "../must-not-be-read.onnx";
                await ExpectExceptionAsync<OperationCanceledException>(async () =>
                    await component.CreateDetectorAsync(recognizer, new CancellationToken(true)));
                Assert.That(recognizer.Disposals, Is.Zero);
            }
            finally
            {
                recognizer.Dispose();
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [UnityTest]
        public IEnumerator FailedDetectorConstructionDisposesTheLoadedModelButKeepsTheBorrowedRecognizer()
            => UniTask.ToCoroutine(FailedDetectorConstructionDisposesTheLoadedModelButKeepsTheBorrowedRecognizerAsync);

        private async UniTask FailedDetectorConstructionDisposesTheLoadedModelButKeepsTheBorrowedRecognizerAsync()
        {
            var modelPath = RequireNativeModelPath();
            var owner = new GameObject("ONNX Silero failed construction test");
            var recognizer = new BorrowedRecognizer();
            FailingOnnxSileroSpeechDetector component = null;
            try
            {
                component = owner.AddComponent<FailingOnnxSileroSpeechDetector>();
                component.ModelFileName = modelPath;
                var error = await ExpectExceptionAsync<InvalidOperationException>(async () =>
                    await component.CreateDetectorAsync(recognizer, CancellationToken.None));
                Assert.That(error.Message, Is.EqualTo(FailingOnnxSileroSpeechDetector.FailureMessage));
                Assert.That(component.CapturedModel, Is.Not.Null);
                await ExpectExceptionAsync<ObjectDisposedException>(async () =>
                    await component.CapturedModel.PredictAsync(new float[512], 16000));
                Assert.That(recognizer.Disposals, Is.Zero);
            }
            finally
            {
                component?.CapturedModel?.Dispose();
                recognizer.Dispose();
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static string RequireNativeModelPath()
        {
#if !CHATDOLLKIT_ONNXRUNTIME
            Assert.Ignore("Install com.github.asus4.onnxruntime to run the native model integration tests.");
#endif
            var path = Environment.GetEnvironmentVariable("CHATDOLLKIT_SILERO_MODEL_PATH");
            if (string.IsNullOrWhiteSpace(path))
                Assert.Ignore("Set CHATDOLLKIT_SILERO_MODEL_PATH to an original Silero ONNX model to run this integration test.");
            path = Path.GetFullPath(path);
            Assert.That(File.Exists(path), Is.True, "CHATDOLLKIT_SILERO_MODEL_PATH must point to an existing model file.");
            return path;
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
            var method = typeof(OnnxSileroSpeechDetector).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
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

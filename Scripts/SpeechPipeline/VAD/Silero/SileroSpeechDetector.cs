using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.VAD;
using ChatdollKit.SpeechPipeline.VAD.Silero.WebGL;
#if CHATDOLLKIT_ONNXRUNTIME && (!UNITY_WEBGL || UNITY_EDITOR)
using ChatdollKit.SpeechPipeline.VAD.Silero.Onnx;
#endif
using UnityEngine;
using UnityEngine.Networking;

namespace ChatdollKit.SpeechPipeline.VAD.Silero
{
    /// <summary>Inspector settings for a caller-owned Silero detector lease. Requires a supplied ONNX model.</summary>
    [AddComponentMenu("ChatdollKit/Speech Pipeline/VAD/Silero Speech Detector")]
    public class SileroSpeechDetector : SpeechDetectorComponent
    {
        [Tooltip("Path relative to StreamingAssets, or an absolute model file path on native platforms. Changing the model requires restarting the pipeline.")]
        public string ModelFileName = "silero_vad.onnx";
        [Tooltip("ONNX Runtime Web script HTTPS URL, or a path relative to StreamingAssets. Used by WebGL builds; place its matching WASM files beside the script when hosting locally.")]
        public string WebGLRuntimeScriptUrl = WebGLSileroVadModel.DefaultRuntimeScriptUrl;
        public SpeechDetectorSettings Settings = new SpeechDetectorSettings();
        [Range(0, 1)] public float SpeechProbabilityThreshold = 0.5f;
        [Tooltip("Changing iterator mode requires restarting the pipeline.")]
        public bool UseVadIterator;
        public bool UseVolumeThreshold;
        public float VolumeDbThreshold = -40;

        public override SpeechDetectorOptions BuildOptions()
        {
            var options = new SileroSpeechDetectorOptions();
            ApplySettings(options);
            return options;
        }

        protected void ApplySettings(SileroSpeechDetectorOptions options)
        {
            Settings.ApplyTo(options);
            options.ChunkSize = options.SampleRate == 8000 ? 256 : 512;
            options.SpeechProbabilityThreshold = SpeechProbabilityThreshold;
            options.UseVadIterator = UseVadIterator;
            options.VolumeDbThreshold = UseVolumeThreshold ? VolumeDbThreshold : (double?)null;
            options.Validate();
        }

        public override string GetRestartKey() => base.GetRestartKey() + "\n" + ModelFileName + "\n" + WebGLRuntimeScriptUrl;

        public override async UniTask<SpeechDetectorLease> CreateDetectorAsync(ISpeechRecognizer stt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = (SileroSpeechDetectorOptions)BuildOptions();
#if UNITY_WEBGL && !UNITY_EDITOR
            var model = await WebGLSileroVadModel.CreateAsync(ResolveModelUrl(ModelFileName, true),
                ResolveWebGLRuntimeScriptUrl(WebGLRuntimeScriptUrl), cancellationToken);
#elif CHATDOLLKIT_ONNXRUNTIME
            var model = await LoadNativeModelAsync(cancellationToken);
#else
            var model = await UniTask.FromException<ISileroVadModel>(new NotSupportedException(
                "Silero in the Unity Editor and native builds requires the com.github.asus4.onnxruntime package. " +
                "Install ONNX Runtime to use this detector here, or run a WebGL build to use ONNX Runtime Web."));
#endif
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new SpeechDetectorLease(CreateDetector(model, stt, options), model);
            }
            catch
            {
                model.Dispose();
                throw;
            }
        }

#if CHATDOLLKIT_ONNXRUNTIME && (!UNITY_WEBGL || UNITY_EDITOR)
        private async UniTask<ISileroVadModel> LoadNativeModelAsync(CancellationToken cancellationToken)
        {
            var modelFileName = ModelFileName;
            var uri = ResolveModelUrl(modelFileName, false);
            byte[] bytes;
            using (var request = UnityWebRequest.Get(uri))
            {
                _ = request.SendWebRequest();
                try
                {
                    while (!request.isDone)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await SpeechAsync.Yield();
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (request.result != UnityWebRequest.Result.Success)
                        throw new IOException($"Unable to load Silero model '{modelFileName}': {request.error}");
                    bytes = request.downloadHandler.data;
                }
                catch
                {
                    request.Abort();
                    throw;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new OnnxSileroVadModel(bytes);
        }
#endif

        private static string ResolveModelUrl(string modelFileName, bool forWebGL)
        {
            if (string.IsNullOrWhiteSpace(modelFileName)) throw new ArgumentException("ModelFileName must not be empty.");
            var path = modelFileName.Replace('\\', '/');
            if (Path.IsPathRooted(path))
            {
                if (forWebGL) throw new ArgumentException("WebGL ModelFileName must be a relative path inside StreamingAssets.");
                return new Uri(path).AbsoluteUri;
            }
            ValidateRelativePath(path, nameof(ModelFileName));
            return ResolveStreamingAssetUrl(path);
        }

        private static string ResolveWebGLRuntimeScriptUrl(string scriptUrl)
        {
            if (string.IsNullOrWhiteSpace(scriptUrl)) throw new ArgumentException("WebGLRuntimeScriptUrl must not be empty.");
            if (Uri.TryCreate(scriptUrl, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host)) return uri.AbsoluteUri;
                throw new ArgumentException("WebGLRuntimeScriptUrl must be an HTTPS URL or a relative path inside StreamingAssets.");
            }
            var path = scriptUrl.Replace('\\', '/');
            ValidateRelativePath(path, nameof(WebGLRuntimeScriptUrl));
            return ResolveStreamingAssetUrl(path);
        }

        private static void ValidateRelativePath(string path, string fieldName)
        {
            if (Path.IsPathRooted(path) || Uri.TryCreate(path, UriKind.Absolute, out _) ||
                Array.IndexOf(path.Split('/'), "..") >= 0)
                throw new ArgumentException(fieldName + " must be a relative path inside StreamingAssets.");
        }

        private static string ResolveStreamingAssetUrl(string path)
        {
            var root = Application.streamingAssetsPath.TrimEnd('/');
            if (root.Contains("://") || root.StartsWith("jar:", StringComparison.Ordinal))
            {
                var segments = path.Split('/');
                for (var i = 0; i < segments.Length; i++) segments[i] = Uri.EscapeDataString(segments[i]);
                return root + "/" + string.Join("/", segments);
            }
            return new Uri(root + "/" + path).AbsoluteUri;
        }

        protected virtual ISpeechDetector CreateDetector(ISileroVadModel model, ISpeechRecognizer stt, SileroSpeechDetectorOptions options)
            => new SileroSpeechDetectorEngine(model, options);
    }
}

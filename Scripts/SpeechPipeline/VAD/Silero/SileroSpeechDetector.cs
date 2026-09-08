using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.VAD;
using ChatdollKit.SpeechPipeline.VAD.Silero.Sentis;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.VAD.Silero
{
    /// <summary>Inspector settings for a caller-owned Silero detector using the bundled Sentis model.</summary>
    [AddComponentMenu("ChatdollKit/Speech Pipeline/VAD/Silero Speech Detector")]
    public class SileroSpeechDetector : SpeechDetectorComponent
    {
        private const string ModelResourcePath = "ChatdollKit/Silero/silero_vad_16k.sentis";

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
            if (options.SampleRate != 16000)
                throw new NotSupportedException("The bundled Silero Sentis model requires 16000 Hz mono PCM. Configure the pipeline input to use 16000 Hz.");
            options.ChunkSize = 512;
            options.SpeechProbabilityThreshold = SpeechProbabilityThreshold;
            options.UseVadIterator = UseVadIterator;
            options.VolumeDbThreshold = UseVolumeThreshold ? VolumeDbThreshold : (double?)null;
            options.Validate();
        }

        public override UniTask<SpeechDetectorLease> CreateDetectorAsync(ISpeechRecognizer stt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = (SileroSpeechDetectorOptions)BuildOptions();
            var asset = Resources.Load<TextAsset>(ModelResourcePath);
            if (asset == null)
                throw new FileNotFoundException("Bundled Silero Sentis model was not found in Resources: " + ModelResourcePath);
            cancellationToken.ThrowIfCancellationRequested();
            var model = new SentisSileroVadModel(asset.bytes);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.FromResult(new SpeechDetectorLease(CreateDetector(model, stt, options), model));
            }
            catch
            {
                model.Dispose();
                throw;
            }
        }

        protected virtual ISpeechDetector CreateDetector(ISileroVadModel model, ISpeechRecognizer stt, SileroSpeechDetectorOptions options)
            => new SileroSpeechDetectorEngine(model, options);
    }
}

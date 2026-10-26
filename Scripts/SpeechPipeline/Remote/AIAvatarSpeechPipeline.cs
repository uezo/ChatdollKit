using ChatdollKit.SpeechPipeline;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ChatdollKit.SpeechPipeline.Remote
{
    [DisallowMultipleComponent]
    [AddComponentMenu("ChatdollKit/Speech Pipeline/AIAvatar Speech Pipeline")]
    public sealed class AIAvatarSpeechPipeline : SpeechPipelineComponent
    {
        public string ServerUrl = "ws://localhost:48001/ws";
        public string ApiKey;
        [Tooltip("Leave empty to create a new session when connecting.")]
        public string SessionId;
        public string UserId;
        public string ContextId;
        [Tooltip("Must match the server's mono PCM16 input sample rate.")]
        [Min(1)] public int InputSampleRate = 16000;
        [Min(1)] public int SamplesPerMessage = 512;
        [Min(0.01f)] public float ConnectTimeoutSeconds = 10;
        [Min(0.01f)] public float RequestTimeoutSeconds = 120;
        [Tooltip("Log connection/configuration, server events and audio send summaries. Can be changed during Play Mode.")]
        public bool DebugLogEnabled;

        public Func<IAIAvatarConnection> ConnectionFactory { get; set; }
        public Action<AIAvatarSpeechPipelineOptions> ConfigureOptions { get; set; }
        public AIAvatarSpeechPipelineClient Pipeline { get; private set; }

        // Diagnostics can change without replacing the connection.
        public override string GetRestartKey()
        {
            var settings = JObject.Parse(JsonUtility.ToJson(this));
            settings.Remove(nameof(DebugLogEnabled));
            return settings.ToString(Formatting.None);
        }

        protected override void Update()
        {
            if (Pipeline != null) Pipeline.DebugLogEnabled = DebugLogEnabled;
            base.Update();
        }

        public override UniTask<SpeechPipelineLease> CreatePipelineAsync(CancellationToken cancellationToken) =>
            CreatePipelineAsync(null, cancellationToken);

        public override UniTask<SpeechPipelineLease> CreatePipelineAsync(bool allowBargeIn, CancellationToken cancellationToken) =>
            CreatePipelineAsync((bool?)allowBargeIn, cancellationToken);

        private async UniTask<SpeechPipelineLease> CreatePipelineAsync(bool? allowBargeIn, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAvailable();
            var sampleRate = InputSampleRate;
            var samplesPerMessage = SamplesPerMessage;
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(InputSampleRate));
            if (samplesPerMessage <= 0) throw new ArgumentOutOfRangeException(nameof(SamplesPerMessage));
            var stamp = CaptureSettingsStamp();
            var options = new AIAvatarSpeechPipelineOptions
            {
                Url = ServerUrl,
                ApiKey = ApiKey,
                SessionId = EmptyToNull(SessionId),
                UserId = EmptyToNull(UserId),
                ContextId = EmptyToNull(ContextId),
                SendSilenceWhenIdle = true,
                InputSampleRate = sampleRate,
                SamplesPerMessage = samplesPerMessage,
                ConnectTimeout = TimeSpan.FromSeconds(ConnectTimeoutSeconds),
                RequestTimeout = TimeSpan.FromSeconds(RequestTimeoutSeconds)
            };
            ConfigureOptions?.Invoke(options);
            // The idle stream must use exactly the same PCM format as the microphone lease.
            options.InputSampleRate = sampleRate;
            options.SamplesPerMessage = samplesPerMessage;
            var created = new AIAvatarSpeechPipelineClient(options, ConnectionFactory ?? CreateDefaultConnection)
            {
                DebugLogEnabled = DebugLogEnabled,
                DebugLog = message => Debug.Log(message, this)
            };
            SpeechPipelineLease lease = null;
            try
            {
                if (allowBargeIn.HasValue)
                    await created.SetAllowBargeInAsync(allowBargeIn.Value, cancellationToken);
                await created.ConnectAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                // Another startup may have acquired this component while connecting.
                EnsureAvailable();
                lease = new SpeechPipelineLease(created, sampleRate, samplesPerMessage);
                BindSettingsSnapshot(() => token =>
                {
                    token.ThrowIfCancellationRequested();
                    created.DebugLogEnabled = DebugLogEnabled;
                    return UniTask.CompletedTask;
                }, stamp, () =>
                {
                    if (ReferenceEquals(Pipeline, created)) Pipeline = null;
                });
                lease.TrackBinding(this);
                Pipeline = created;
                return lease;
            }
            catch (Exception failure)
            {
                try
                {
                    if (lease != null) await lease.DisposeAsync();
                    else await created.DisposeAsync();
                }
                catch (Exception cleanupFailure) { throw new AggregateException(failure, cleanupFailure); }
                throw;
            }
        }

        private void EnsureAvailable()
        {
            if (this == null || !isActiveAndEnabled)
                throw new InvalidOperationException("Enable the AIAvatar Pipeline component before connecting.");
            if (IsBound) throw new InvalidOperationException("This AIAvatar Pipeline component is already used by a running or stopping pipeline.");
        }

        private static string EmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;

        private static IAIAvatarConnection CreateDefaultConnection()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new WebGLAIAvatarConnection();
#else
            return new NativeAIAvatarConnection();
#endif
        }
    }
}

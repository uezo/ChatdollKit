using ChatdollKit.Avatar.LipSync;
#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.Avatar;
using ChatdollKit.SpeechPipeline.STT;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Speech = ChatdollKit.Avatar.SpeechController;

namespace ChatdollKit.Tests.Avatar
{
    public class MfccLipSyncUnityTests
    {
        [Test]
        public void NamedMappingsBlendScaleAndPreserveUnmappedExpressions()
        {
            var go = Create(out var renderer, out var lip);
            try
            {
                renderer.SetBlendShapeWeight(2, 37);
                lip.Smoothness = 0;
                lip.UsePhonemeBlend = true;
                lip.Mappings = new[]
                {
                    Map(Viseme.A, "mouth-a", 80), Map(Viseme.I, "mouth-i", 100),
                    Map(Viseme.U, "mouth-a", 40), Map(Viseme.O, "missing", 100)
                };
                lip.RebuildMappings();
                lip.SetResult(new LipSyncResult(0.5, 0.25, 0.25, 0, 0, Viseme.A, 1, 0.1));
                lip.ApplyVisemes(1f / 60);
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(50).Within(1e-4));
                Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(25).Within(1e-4));
                Assert.That(renderer.GetBlendShapeWeight(2), Is.EqualTo(37));
                lip.ResetViseme();
                Assert.That(renderer.GetBlendShapeWeight(0), Is.Zero);
                Assert.That(renderer.GetBlendShapeWeight(1), Is.Zero);
                Assert.That(renderer.GetBlendShapeWeight(2), Is.EqualTo(37));
            }
            finally { Dispose(go, renderer); }
        }

        [Test]
        public void DominantVowelUsesVolumeAndDisableClearsSmoothing()
        {
            var go = Create(out var renderer, out var lip);
            try
            {
                lip.Smoothness = 0;
                lip.SetResult(new LipSyncResult(0.2, 0.3, 0, 0, 0, Viseme.I, 0.5, 0.01));
                lip.ApplyVisemes(1f / 60);
                Assert.That(renderer.GetBlendShapeWeight(0), Is.Zero);
                Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(50));
                lip.Smoothness = 0.1f;
                lip.enabled = false;
                Assert.That(renderer.GetBlendShapeWeight(1), Is.Zero);
                lip.enabled = true;
                lip.ApplyVisemes(0); // A zero-duration update must not poison SmoothDamp velocities.
                lip.ApplyVisemes(1f / 60);
                Assert.That(renderer.GetBlendShapeWeight(1), Is.Zero, "No stale target or velocity survives disable.");
                lip.SetResult(new LipSyncResult(1, 0, 0, 0, 0, Viseme.A, 1, 0.1));
                lip.ApplyVisemes(1f / 60);
                Assert.That(renderer.GetBlendShapeWeight(0), Is.InRange(0.01f, 99.99f));
            }
            finally { Dispose(go, renderer); }
        }

        [Test]
        public void RebindingByNameReleasesOldMouthAndKeepsUnrelatedShapes()
        {
            var go = Create(out var renderer, out var lip);
            var next = new GameObject("Replacement avatar");
            var nextRenderer = next.AddComponent<SkinnedMeshRenderer>();
            nextRenderer.sharedMesh = MeshWithShapes("smile", "mouth-i", "mouth-a");
            try
            {
                lip.Smoothness = 0;
                lip.SetResult(new LipSyncResult(1, 0, 0, 0, 0, Viseme.A, 1, 0.1));
                lip.ApplyVisemes(0.02f);
                var helper = go.AddComponent<MfccLipSyncHelper>();
                helper.BlendShapeNameForMouthA = "mouth-a";
                helper.BlendShapeNameForMouthI = "mouth-i";
                helper.BlendShapeNameForMouthU = helper.BlendShapeNameForMouthE = helper.BlendShapeNameForMouthO = "";
                helper.ConfigureViseme(next);
                Assert.That(renderer.GetBlendShapeWeight(0), Is.Zero);
                nextRenderer.SetBlendShapeWeight(0, 23);
                lip.SetResult(new LipSyncResult(1, 0, 0, 0, 0, Viseme.A, 1, 0.1));
                lip.ApplyVisemes(0.02f);
                Assert.That(nextRenderer.GetBlendShapeWeight(2), Is.EqualTo(100));
                Assert.That(nextRenderer.GetBlendShapeWeight(0), Is.EqualTo(23));
            }
            finally { Dispose(go, renderer); Dispose(next, nextRenderer); }
        }

        [Test]
        public void BundledProfileDecodesAndAnalysisVolumeGainCanCloseTheMouth()
        {
            var go = Create(out var renderer, out var lip);
            try
            {
                Assert.That(Resources.Load<TextAsset>(MfccLipSync.DefaultProfileResource), Is.Not.Null);
                lip.Profile = null;
                lip.BeginPlayback(WaveAudio.Decode(ToneWave(0.15)));
                lip.UpdatePlayback(1600);
                Assert.That(lip.CurrentResult.Volume, Is.GreaterThan(0));
                lip.VolumeGain = 0;
                lip.UpdatePlayback(1600);
                Assert.That(lip.CurrentResult.Volume, Is.Zero);
                lip.VolumeGain = 1;
                lip.ResetViseme();
                lip.UpdatePlayback(1600);
                Assert.That(lip.CurrentResult.Volume, Is.Zero);
            }
            finally { Dispose(go, renderer); }
        }

        [UnityTest]
        public IEnumerator MutedSpeechStillAnalyzesDecodedPcmAndResetsOnNaturalEnd() => Run(async () =>
        {
            var go = Create(out var renderer, out var lip);
            var speech = go.AddComponent<Speech>();
            speech.AudioSource.playOnAwake = false;
            speech.AudioSource.volume = 0;
            speech.AudioSource.mute = true;
            speech.LipSyncEngine = lip;
            var sawMouth = false;
            speech.PlayingSamples += (samples, channels, rate) => sawMouth |= lip.CurrentResult.Volume > 0;
            try
            {
                await speech.PlayAsync(ToneWave(0.25));
                Assert.That(sawMouth, Is.True, "Decoded PCM drives the mouth even when AudioSource volume is zero and muted.");
                Assert.That(lip.CurrentResult.Volume, Is.Zero);
                Assert.That(renderer.GetBlendShapeWeight(0), Is.Zero);
                Assert.That(renderer.GetBlendShapeWeight(1), Is.Zero);
                Assert.That(speech.AudioSource.clip, Is.Null);
            }
            finally { await speech.StopAsync(); Dispose(go, renderer); }
        });

        [UnityTest]
        public IEnumerator ExplicitHelperOnAnotherObjectResetsAfterCancellation() => Run(async () =>
        {
            var mouth = Create(out var renderer, out var lip);
            var player = new GameObject("Separate speech player");
            var speech = player.AddComponent<Speech>();
            speech.AudioSource.playOnAwake = false;
            speech.AudioSource.volume = 0;
            speech.LipSyncEngine = lip;
            var started = new SpeechCompletionSource<bool>();
            speech.PlayingSamples += (samples, channels, rate) =>
            { if (lip.CurrentResult.Volume > 0) started.TrySetResult(true); };
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    var play = speech.PlayAsync(ToneWave(5), cancellation.Token);
                    await started.Task;
                    cancellation.Cancel();
                    try { await play; Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
                    Assert.That(lip.CurrentResult.Volume, Is.Zero);
                    Assert.That(renderer.GetBlendShapeWeight(0), Is.Zero);
                    Assert.That(renderer.GetBlendShapeWeight(1), Is.Zero);
                    Assert.That(speech.AudioSource.clip, Is.Null);
                }
                finally
                {
                    cancellation.Cancel(); await speech.StopAsync();
                    UnityEngine.Object.DestroyImmediate(player); Dispose(mouth, renderer);
                }
            }
        });

        [UnityTest]
        public IEnumerator UnassignedSpeechInputDoesNotDiscoverAttachedMfcc() => Run(async () =>
        {
            var go = Create(out var renderer, out var lip);
            var speech = go.AddComponent<Speech>();
            speech.AudioSource.volume = 0;
            speech.AudioSource.playOnAwake = false;
            var receivedAnalysis = false;
            speech.PlayingSamples += (samples, channels, rate) => receivedAnalysis |= lip.CurrentResult.RawVolume > 0;
            try
            {
                Assert.That(speech.LipSyncEngine, Is.Null);
                await speech.PlayAsync(ToneWave(0.15));
                Assert.That(receivedAnalysis, Is.False, "An empty input explicitly opts out of PCM-driven lip sync.");
            }
            finally { await speech.StopAsync(); Dispose(go, renderer); }
        });

        [UnityTest]
        public IEnumerator SpeechAcceptsPlainInterfaceAndKeepsTheSameReceiverUntilCompletion() => Run(async () =>
        {
            var go = new GameObject("Interface speech test");
            var speech = go.AddComponent<Speech>();
            speech.AudioSource.volume = 0;
            speech.AudioSource.playOnAwake = false;
            var original = new RecordingLipSync();
            var replacement = new RecordingLipSync();
            speech.LipSyncEngine = original;
            speech.PlayingSamples += (samples, channels, rate) => speech.LipSyncEngine = replacement;
            try
            {
                await speech.PlayAsync(ToneWave(0.15));
                Assert.That(original.Audio.SampleRate, Is.EqualTo(16000));
                Assert.That(original.Positions, Is.GreaterThan(0));
                Assert.That(original.ResetCount, Is.EqualTo(1));
                Assert.That(replacement.Audio, Is.Null);
                Assert.That(replacement.ResetCount, Is.Zero);
                speech.LipSyncEngine = null;
                Assert.That(speech.LipSyncEngine, Is.Null);
            }
            finally { await speech.StopAsync(); UnityEngine.Object.DestroyImmediate(go); }
        });

        [UnityTest]
        public IEnumerator HelperOnlyPlaybackResetsOnNaturalEndWithoutConfiguringTheAvatar() => Run(async () =>
        {
            var go = new GameObject("Helper-only speech test");
            var speech = go.AddComponent<Speech>();
            speech.AudioSource.volume = 0;
            speech.AudioSource.playOnAwake = false;
            var helper = go.AddComponent<RecordingLipSyncHelper>();
            speech.LipSyncHelper = helper;
            var played = false;
            speech.PlayingSamples += (samples, channels, rate) => played = true;
            try
            {
                Assert.That(speech.LipSyncEngine, Is.Null);
                await speech.PlayAsync(ToneWave(0.15));
                Assert.That(played, Is.True);
                Assert.That(helper.ResetCount, Is.EqualTo(1));
                Assert.That(helper.ConfigureCount, Is.Zero, "Speech cleanup must not reconfigure the avatar.");
                Assert.That(speech.AudioSource.clip, Is.Null);
            }
            finally { await speech.StopAsync(); UnityEngine.Object.DestroyImmediate(go); }
        });

        [UnityTest]
        public IEnumerator HelperOnlyPlaybackResetsAfterCancellationWithoutConfiguringTheAvatar() => Run(async () =>
        {
            var go = new GameObject("Cancelled helper-only speech test");
            var speech = go.AddComponent<Speech>();
            speech.AudioSource.volume = 0;
            speech.AudioSource.playOnAwake = false;
            var helper = go.AddComponent<RecordingLipSyncHelper>();
            speech.LipSyncHelper = helper;
            using (var cancellation = new CancellationTokenSource())
            {
                var played = false;
                speech.PlayingSamples += (samples, channels, rate) =>
                {
                    played = true;
                    cancellation.Cancel();
                };
                try
                {
                    Assert.That(speech.LipSyncEngine, Is.Null);
                    try { await speech.PlayAsync(ToneWave(5), cancellation.Token); Assert.Fail("Expected cancellation."); }
                    catch (OperationCanceledException) { }
                    Assert.That(played, Is.True, "Cancellation occurs after actual playback starts.");
                    Assert.That(helper.ResetCount, Is.EqualTo(1));
                    Assert.That(helper.ConfigureCount, Is.Zero);
                    Assert.That(speech.AudioSource.clip, Is.Null);
                }
                finally
                {
                    cancellation.Cancel();
                    await speech.StopAsync();
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        });

        [UnityTest]
        public IEnumerator EngineAndHelperCleanupStayWithTheReceiversCapturedForPlayback() => Run(async () =>
        {
            var go = new GameObject("Engine and helper speech test");
            var speech = go.AddComponent<Speech>();
            speech.AudioSource.volume = 0;
            speech.AudioSource.playOnAwake = false;
            var engine = new RecordingLipSync();
            var replacementEngine = new RecordingLipSync();
            var helper = go.AddComponent<RecordingLipSyncHelper>();
            var replacementHelper = go.AddComponent<RecordingLipSyncHelper>();
            speech.LipSyncEngine = engine;
            speech.LipSyncHelper = helper;
            var replaced = false;
            speech.PlayingSamples += (samples, channels, rate) =>
            {
                replaced = true;
                speech.LipSyncEngine = replacementEngine;
                speech.LipSyncHelper = replacementHelper;
            };
            try
            {
                await speech.PlayAsync(ToneWave(0.15));
                Assert.That(replaced, Is.True);
                Assert.That(engine.Audio.SampleRate, Is.EqualTo(16000));
                Assert.That(engine.Positions, Is.GreaterThan(0));
                Assert.That(engine.ResetCount, Is.EqualTo(1));
                Assert.That(helper.ResetCount, Is.EqualTo(1));
                Assert.That(helper.ConfigureCount, Is.Zero);
                Assert.That(replacementEngine.Audio, Is.Null);
                Assert.That(replacementEngine.ResetCount, Is.Zero);
                Assert.That(replacementHelper.ResetCount, Is.Zero);
                Assert.That(replacementHelper.ConfigureCount, Is.Zero);
            }
            finally { await speech.StopAsync(); UnityEngine.Object.DestroyImmediate(go); }
        });

        private sealed class RecordingLipSync : ILipSync
        {
            public WaveAudio Audio;
            public int Positions;
            public int ResetCount;
            public void BeginPlayback(WaveAudio audio) => Audio = audio;
            public void UpdatePlayback(int samplePosition) { if (samplePosition > 0) Positions++; }
            public void ResetViseme() => ResetCount++;
        }

        private static GameObject Create(out SkinnedMeshRenderer renderer, out MfccLipSync lip)
        {
            var go = new GameObject("MFCC mouth test");
            renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = MeshWithShapes("mouth-a", "mouth-i", "smile");
            lip = go.AddComponent<MfccLipSync>();
            lip.TargetRenderer = renderer;
            lip.Mappings = new[] { Map(Viseme.A, "mouth-a", 100), Map(Viseme.I, "mouth-i", 100) };
            lip.RebuildMappings();
            return go;
        }

        private static VisemeMapping Map(Viseme viseme, string shape, float maxWeight)
            => new VisemeMapping { Viseme = viseme, BlendShape = shape, MaxWeight = maxWeight };

        private static Mesh MeshWithShapes(params string[] names)
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            foreach (var name in names) mesh.AddBlendShapeFrame(name, 100, new[] { Vector3.up, Vector3.up, Vector3.up }, null, null);
            return mesh;
        }

        private static void Dispose(GameObject go, SkinnedMeshRenderer renderer)
        {
            var mesh = renderer != null ? renderer.sharedMesh : null;
            UnityEngine.Object.DestroyImmediate(go);
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
        }

        private static byte[] ToneWave(double seconds)
        {
            var pcm = new byte[(int)(16000 * seconds) * 2];
            for (var i = 0; i < pcm.Length / 2; i++)
            {
                var t = (double)i / 16000;
                var value = (short)(32767 * (0.08 * Math.Sin(2 * Math.PI * 220 * t) + 0.03 * Math.Sin(2 * Math.PI * 880 * t)));
                pcm[i * 2] = (byte)value; pcm[i * 2 + 1] = (byte)(value >> 8);
            }
            return Pcm16Audio.WriteWave(pcm, 16000);
        }

        private static IEnumerator Run(Func<UniTask> action)
        {
            var task = action();
            var deadline = Time.realtimeSinceStartup + 15;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.Status.IsCompleted(), Is.True, "Audio playback and lip sync cleanup must complete.");
            task.GetAwaiter().GetResult();
        }
    }
}
#endif

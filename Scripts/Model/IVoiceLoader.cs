using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Model
{
    public enum VoiceLoaderType { Web, TTS };

    public interface IVoiceLoader
    {
        VoiceLoaderType Type { get; }
        string Name { get; }
        bool IsDefault { get; set; }
        UniTask<AudioClip> GetAudioClipAsync(Voice voice, CancellationToken token);
        bool HasCache(Voice voice);
        bool IsLoading(Voice voice);
    }
}

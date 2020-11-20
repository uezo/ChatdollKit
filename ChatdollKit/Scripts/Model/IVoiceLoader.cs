using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ChatdollKit.Model
{
    public enum VoiceLoaderType { Web, TTS };

    public interface IVoiceLoader
    {
        VoiceLoaderType Type { get; }
        string Name { get; }
        bool IsDefault { get; set; }
        Task<AudioClip> GetAudioClipAsync(Voice voice, CancellationToken token);
        bool HasCache(Voice voice);
        bool IsLoading(Voice voice);
    }
}

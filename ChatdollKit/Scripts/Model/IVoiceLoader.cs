using System.Threading.Tasks;
using UnityEngine;

namespace ChatdollKit.Model
{
    public interface IVoiceLoader
    {
        Task<AudioClip> GetAudioClipAsync(Voice voice);
        bool HasCache(Voice voice);
        bool IsLoading(Voice voice);
    }
}

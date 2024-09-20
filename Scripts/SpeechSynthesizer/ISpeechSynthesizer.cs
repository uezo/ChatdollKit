using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechSynthesizer
{
    public interface ISpeechSynthesizer
    {
        bool IsEnabled { get; set; }
        UniTask<AudioClip> GetAudioClipAsync(string text, Dictionary<string, object> parameters, CancellationToken token = default);
    }
}

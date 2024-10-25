using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechListener;
using ChatdollKit.SpeechSynthesizer;

namespace ChatdollKit.Demo
{
    public class VoiceChanger : MonoBehaviour
    {
        private MicrophoneManager microphoneManager;
        private AudioSource audioSource;
        private ISpeechListener speechListener;
        private ISpeechSynthesizer speechSynthesizer;

        void Start()
        {
            microphoneManager = GetComponent<MicrophoneManager>();
            audioSource = GetComponent<AudioSource>();
            speechSynthesizer = GetComponent<ISpeechSynthesizer>();
            speechListener = GetComponent<ISpeechListener>();
            speechListener.OnRecognized = async (text) =>
            {
                microphoneManager.MuteMicrophone(true);
                await SayAsync(text);
                microphoneManager.MuteMicrophone(false);
            };
        }
        private async UniTask SayAsync(string text)
        {
            var audioClip = await speechSynthesizer.GetAudioClipAsync(text, new Dictionary<string, object>(), default);
            audioSource.PlayOneShot(audioClip);
            while (audioSource.isPlaying)
            {
                await UniTask.Delay(1);
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.LLM;
using ChatdollKit.SpeechListener;
using ChatdollKit.SpeechSynthesizer;

namespace ChatdollKit.Demo
{
    public class Translator : MonoBehaviour
    {
        private MicrophoneManager microphoneManager;
        private AudioSource audioSource;
        private ISpeechListener speechListener;
        private ISpeechSynthesizer speechSynthesizer;
        private DialogProcessor dialogProcessor;
        private LLMContentProcessor llmContentProcessor;

        void Start()
        {
            microphoneManager = GetComponent<MicrophoneManager>();
            audioSource = GetComponent<AudioSource>();
            speechSynthesizer = GetComponent<ISpeechSynthesizer>();
            speechListener = GetComponent<ISpeechListener>();
            speechListener.OnRecognized = async (text) =>
            {
                await Chat(text);
            };

            dialogProcessor = GetComponent<DialogProcessor>();
            dialogProcessor.OnRequestRecievedAsync = async (text, payloads, token) =>
            {
                microphoneManager.MuteMicrophone(true);
            };
            dialogProcessor.OnEndAsync = async (endConversation, token) =>
            {
                microphoneManager.MuteMicrophone(false);
            };

            llmContentProcessor = GetComponent<LLMContentProcessor>();
            llmContentProcessor.ShowContentItemAsync = async (contentItem, cancellationToken) =>
            {
                await SayAsync(contentItem.Text);
            };
        }

        private async UniTask Chat(string text)
        {
            await dialogProcessor.StartDialogAsync(text);
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

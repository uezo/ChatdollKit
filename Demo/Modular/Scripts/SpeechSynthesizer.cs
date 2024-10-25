using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechSynthesizer;

namespace ChatdollKit.Demo
{
    public class SpeechSynthesizer : MonoBehaviour
    {
        public InputField InputText;
        private AudioSource audioSource;
        private ISpeechSynthesizer speechSynthesizer;

        void Start()
        {
            speechSynthesizer = GetComponent<ISpeechSynthesizer>();
            audioSource = GetComponent<AudioSource>();
        }

        public void OnSayButton()
        {
            var text = InputText.text.Trim();
            InputText.text = string.Empty;
            if (string.IsNullOrEmpty(text)) return;

            Debug.Log(text);

            SayAsync(text);
        }

        private async UniTask SayAsync(string text)
        {
            var audioClip = await speechSynthesizer.GetAudioClipAsync(text, new Dictionary<string, object>(), default);
            audioSource.PlayOneShot(audioClip);
        }
    }
}

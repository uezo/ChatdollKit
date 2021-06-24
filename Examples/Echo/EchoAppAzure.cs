using UnityEngine;
using ChatdollKit.Examples.Dialogs;
using ChatdollKit.Extension.Azure;

namespace ChatdollKit.Examples.Echo
{
    [RequireComponent(typeof(EchoSkill))]
    public class EchoAppAzure : AzureApplication
    {
        [Header("Application Language")]
        public EchoLanguage AppLanguage = EchoLanguage.Japanese;

        protected override void Awake()
        {
            WakeWord = string.IsNullOrEmpty(WakeWord)
                ? AppLanguage == EchoLanguage.Japanese ? "こんにちは" : "hello"
                : WakeWord;

            CancelWord = string.IsNullOrEmpty(CancelWord)
                ? AppLanguage == EchoLanguage.Japanese ? "おしまい" : "finish"
                : CancelWord;

            PromptVoice = string.IsNullOrEmpty(PromptVoice)
                ? AppLanguage == EchoLanguage.Japanese ? "どうしたの？" : "what's up?"
                : PromptVoice;

            PromptVoiceType = Model.VoiceSource.TTS;

            Language = string.IsNullOrEmpty(Language)
                ? AppLanguage == EchoLanguage.Japanese ? "ja-JP" : "en-US"
                : Language;

            base.Awake();
        }

        public enum EchoLanguage
        {
            Japanese, English
        }
    }
}

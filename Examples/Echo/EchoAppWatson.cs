using UnityEngine;
using ChatdollKit.Examples.Dialogs;
using ChatdollKit.Extension.Watson;

namespace ChatdollKit.Examples.Echo
{
    [RequireComponent(typeof(EchoSkill))]
    public class EchoAppWatson : WatsonApplication
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

            STTModel = string.IsNullOrEmpty(STTModel)
                ? AppLanguage == EchoLanguage.Japanese ? "ja-JP_BroadbandModel" : "en-US_BroadbandModel"
                : STTModel;

            STTRemoveWordSeparation = AppLanguage == EchoLanguage.Japanese ? true : false;

            TTSSpeakerName = string.IsNullOrEmpty(TTSSpeakerName)
                ? AppLanguage == EchoLanguage.Japanese ? "ja-JP_EmiV3Voice" : "en-US_EmilyV3Voice"
                : TTSSpeakerName;

            base.Awake();
        }

        public enum EchoLanguage
        {
            Japanese, English
        }
    }
}

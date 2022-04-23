using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.Extension.Watson;

namespace ChatdollKit.Examples.SkillServer
{
    public class MainWatson : WatsonApplication
    {
        [Header("Application Language")]
        public EchoLanguage AppLanguage = EchoLanguage.Japanese;

        [Header("Server configurations")]
        public string ServerUrl = "http://localhost:12345";

        protected override void Awake()
        {
            WakeWord = string.IsNullOrEmpty(WakeWord)
                ? AppLanguage == EchoLanguage.Japanese ? "こんにちは" : "hello"
                : WakeWord;

            CancelWord = string.IsNullOrEmpty(CancelWord)
                ? AppLanguage == EchoLanguage.Japanese ? "おしまい" : "finish"
                : CancelWord;

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

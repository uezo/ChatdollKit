using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.Extension.Google;

namespace ChatdollKit.Examples.SkillServer
{
    public class MainGoogle : GoogleApplication
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

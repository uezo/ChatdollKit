using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.Extension.Azure;

namespace ChatdollKit.Examples.SkillServer
{
    [RequireComponent(typeof(HttpPrompter))]
    [RequireComponent(typeof(HttpSkillRouter))]
    public class MainAzure : AzureApplication
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

            var prompter = GetComponent<HttpPrompter>();
            prompter.PingUri = MakeUri(prompter.PingUri, "ping");
            prompter.PromptUri = MakeUri(prompter.PromptUri, "prompt");

            var router = GetComponent<HttpSkillRouter>();
            router.SkillsUri = MakeUri(router.SkillsUri, "skills");
            router.IntentExtractorUri = MakeUri(router.IntentExtractorUri, "intent");
            
            base.Awake();
        }

        private string MakeUri(string componentValue, string path)
        {
            return string.IsNullOrEmpty(componentValue)
                ? ServerUrl.EndsWith("/") ? ServerUrl + path : ServerUrl + "/" + path
                : componentValue;
        }

        public enum EchoLanguage
        {
            Japanese, English
        }
    }
}

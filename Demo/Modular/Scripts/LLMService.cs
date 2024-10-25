using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.LLM;

namespace ChatdollKit.Demo
{
    public class LLMService : MonoBehaviour
    {
        public InputField InputText;
        public Text OutputText;
        private DialogProcessor dialogProcessor;
        private LLMContentProcessor llmContentProcessor;

        void Start()
        {
            dialogProcessor = GetComponent<DialogProcessor>();
            llmContentProcessor = GetComponent<LLMContentProcessor>();
            llmContentProcessor.HandleSplittedText = (contentItem) =>
            {
                // Clear output first
                if (contentItem.IsFirstItem)
                {
                    OutputText.text = string.Empty;
                }

                // Show stream
                OutputText.text += contentItem.Text;
            };
        }

        public void OnSendButton()
        {
            var text = InputText.text.Trim();
            InputText.text = string.Empty;
            if (string.IsNullOrEmpty(text)) return;

            Debug.Log($"User: {text}");

            _ = Chat(text);
        }

        private async UniTask Chat(string text)
        {
            await dialogProcessor.StartDialogAsync(text);
        }
    }
}

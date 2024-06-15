using UnityEngine;
using UnityEngine.UI;
using ChatdollKit.Dialog;

#if UNITY_WEBGL && !UNITY_EDITOR
using ChatGPTService = ChatdollKit.LLM.ChatGPT.ChatGPTServiceWebGL;
using ClaudeService = ChatdollKit.LLM.Claude.ClaudeServiceWebGL;
using GeminiService = ChatdollKit.LLM.Gemini.GeminiServiceWebGL;
#else
using ChatGPTService = ChatdollKit.LLM.ChatGPT.ChatGPTService;
using ClaudeService = ChatdollKit.LLM.Claude.ClaudeService;
using GeminiService = ChatdollKit.LLM.Gemini.GeminiService;
#endif


namespace ChatdollKit.UI
{
    public class PromptEditorUI : MonoBehaviour
    {
        [SerializeField]
        private DialogController dialogController;

        [SerializeField]
        private GameObject PromptPanel;
        [SerializeField]
        private Dropdown PromptDropdown;
        [SerializeField]
        private InputField PromptInput;

        private ChatGPTService chatGPTService;
        private ClaudeService claudeService;
        private GeminiService geminiService;

        public void Show()
        {
            // Get LLMService components
            chatGPTService = dialogController.GetComponent<ChatGPTService>();
            claudeService = dialogController.GetComponent<ClaudeService>();
            geminiService = dialogController.GetComponent<GeminiService>();

            PromptDropdown.options.Clear();
            if (chatGPTService != null)
            {
                PromptDropdown.options.Add(new Dropdown.OptionData("ChatGPT"));
                if (chatGPTService.IsEnabled)
                {
                    PromptDropdown.value = PromptDropdown.options.Count - 1;
                }
            }
            if (claudeService != null)
            {
                PromptDropdown.options.Add(new Dropdown.OptionData("Claude"));
                if (claudeService.IsEnabled)
                {
                    PromptDropdown.value = PromptDropdown.options.Count - 1;
                }
            }
            if (geminiService != null)
            {
                PromptDropdown.options.Add(new Dropdown.OptionData("Gemini"));
                if (geminiService.IsEnabled)
                {
                    PromptDropdown.value = PromptDropdown.options.Count - 1;
                }
            }

            OnChangePromptDropdown();

            PromptPanel.SetActive(true);
        }

        public void ApplyPrompt()
        {
            var selectedItem = PromptDropdown.options[PromptDropdown.value];

            if (selectedItem.text == "ChatGPT")
            {
                chatGPTService.SystemMessageContent = PromptInput.text;
            }
            else if (selectedItem.text == "Claude")
            {
                claudeService.SystemMessageContent = PromptInput.text;
            }
            else if (selectedItem.text == "Gemini")
            {
                geminiService.SystemMessageContent = PromptInput.text;
            }

            PromptPanel.SetActive(false);
        }

        public void OnChangePromptDropdown()
        {
            var selectedItem = PromptDropdown.options[PromptDropdown.value];

            if (selectedItem.text == "ChatGPT")
            {
                PromptInput.text = chatGPTService.SystemMessageContent;
            }
            else if (selectedItem.text == "Claude")
            {
                PromptInput.text = claudeService.SystemMessageContent;
            }
            else if (selectedItem.text == "Gemini")
            {
                PromptInput.text = geminiService.SystemMessageContent;
            }
        }
    }
}

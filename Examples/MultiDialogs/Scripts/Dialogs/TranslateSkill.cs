using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Extension.Azure;

namespace ChatdollKit.Examples.MultiDialog
{
    public class TranslateSkill : SkillBase
    {
        public string AzureSubscriptionKey;
        private AzureTranslator azureTranslator;

        protected override void Awake()
        {
            base.Awake();

            azureTranslator = new AzureTranslator(AzureSubscriptionKey);
        }

        public override async Task<Response> ProcessAsync(Request request, State state, CancellationToken token)
        {
            // Translate
            var translatedText = await TranslateAsync(request.Text);

            // Build and return response message
            var response = new Response(request.Id);

            if (state.Topic.IsFirstTurn)
            {
                response.AddVoiceTTS("何を翻訳しますか？");
                response.AddAnimation("Default");
            }
            else
            {
                response.AddVoiceTTS($"{request.Text}を英語で言うと、{translatedText}、です。");
                response.AddAnimation("Default");
            }

            // Continue until stop
            state.Topic.IsFinished = false;

            return response;
        }

#pragma warning disable CS1998
        private async Task<string> TranslateAsync(string text)
        {
            // Call translation API if Azure Translate API key is set
            if (!string.IsNullOrEmpty(AzureSubscriptionKey))
            {
                return await azureTranslator?.TranslateAsync(text);
            }
            else
            {
                return "This is the translated text.";
            }
        }
#pragma warning restore CS1998
    }
}

using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Dialog;

namespace ChatdollKit.Examples.MultiDialog
{
    public class Router : SkillRouterBase
    {
        public string WeatherKeyword = "天気";
        public string TranslateKeyword = "翻訳";

        // Extract intent and entities from request and state
#pragma warning disable CS1998
        public override async Task<IntentExtractionResult> ExtractIntentAsync(Request request, State state, CancellationToken token)
        {
            if (request.Text.Contains(WeatherKeyword))
            {
                return new IntentExtractionResult("weather");

            }
            else if (request.Text.Contains(TranslateKeyword))
            {
                return new IntentExtractionResult("translate");
            }
            else
            {
                return new IntentExtractionResult("echo");
            }
        }
#pragma warning restore CS1998
    }
}

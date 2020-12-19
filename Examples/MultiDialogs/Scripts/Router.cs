using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Dialog;

namespace ChatdollKit.Examples.MultiDialog
{
    public class Router : DialogRouterBase
    {
        public string WeatherKeyword = "天気";
        public string TranslateKeyword = "翻訳";

        // Extract intent and entities from request and context
#pragma warning disable CS1998
        public override async Task ExtractIntentAsync(Request request, Context context, CancellationToken token)
        {
            if (request.Text.Contains(WeatherKeyword))
            {
                request.Intent = "weather";
            }
            else if (request.Text.Contains(TranslateKeyword))
            {
                request.Intent = "translate";
            }
            else
            {
                request.Intent = "echo";
            }
        }
#pragma warning restore CS1998
    }
}

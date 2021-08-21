using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;

namespace ChatdollKit.Examples.MultiSkills
{
    [RequireComponent(typeof(WeatherSkill))]
    [RequireComponent(typeof(TranslateSkill))]
    [RequireComponent(typeof(ChatA3RTSkill))]
    [RequireComponent(typeof(EchoSkill))]
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

            if (request.Text.Contains(TranslateKeyword))
            {
                if (IsAvailableTopic("translate", true))
                {
                    return new IntentExtractionResult("translate");
                }
            }

            if (IsAvailableTopic("chata3rt", true))
            {
                return new IntentExtractionResult("chata3rt", Priority.Lowest);
            }

            return new IntentExtractionResult("echo");
        }
#pragma warning restore CS1998
    }
}

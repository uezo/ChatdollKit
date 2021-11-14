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
    [RequireComponent(typeof(CameraSkill))]
    [RequireComponent(typeof(QRCodeSkill))]
    public class Router : SkillRouterBase
    {
        public string WeatherKeyword = "天気";
        public string TranslateKeyword = "翻訳";
        public string CameraKeyword = "写真";
        public string QRCodeKeyword = "QRコード";

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

            if (request.Text.Contains(CameraKeyword))
            {
                return new IntentExtractionResult("camera");
            }

            if (request.Text.ToUpper().Replace(" ", "").Replace("ＱＲ", "QR").Contains(QRCodeKeyword))
            {
                return new IntentExtractionResult("qrcode");
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

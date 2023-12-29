using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;

namespace ChatdollKit.Examples.MultiSkills
{
    public class QRCodeSkill : SkillBase
    {
#pragma warning disable CS1998
        public override async UniTask<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
        {
            var response = new Response(request.Id);

            if (state.Topic.IsFirstTurn)
            {
                // Continue topic to scan QRCode next turn
                response.EndTopic = false;
                response.NextTurnRequestType = RequestType.QRCode;

                response.AddVoiceTTS("QRコードを見せてください");
            }
            else
            {
                // Get extracted QRCode data
                if (request.Payloads.Count == 0)
                {
                    response.AddVoiceTTS("QRコードの読み取りに失敗しました");
                }
                else
                {
                    response.AddVoiceTTS($"QRコードのデータは、{(string)request.Payloads["qrcode"]}");
                }
            }

            return response;
        }
#pragma warning restore CS1998
    }
}

using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Dialog;

namespace ChatdollKit.Examples.Skills
{
    public class ModelControlSkill : SkillBase
    {
        public override async Task<Response> ProcessAsync(Request request, State state, CancellationToken token)
        {
            // NOTE: Register "Smile", "Angry" and "Sad" facial expression before running this example

            // Build and return response message
            var response = new Response(request.Id);

            response.AddVoiceTTS("はじめまして！");
            response.AddVoiceTTS("この声、");

            response.AddVoiceTTS("モーション、", postGap: 2.5f, asNewFrame: true);
            response.AddAnimation("AGIA_Other_walk_01");

            response.AddVoiceTTS("表情は、", postGap: 4.0f, asNewFrame: true);
            response.AddAnimation("Default");
            response.AddFace("Neutral", duration: 1.2f);
            response.AddFace("Smile", duration: 0.5f);
            response.AddFace("Angry", duration: 0.5f);
            response.AddFace("Sad", duration: 0.5f);
            response.AddFace("Smile");

            response.AddVoiceTTS("チャットドールキットを使ってコードベースで制御しています。", asNewFrame: true);
            response.AddFace("Neutral");

            return response;
        }
    }
}

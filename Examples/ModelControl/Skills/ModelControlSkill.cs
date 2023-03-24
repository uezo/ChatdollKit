using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;

namespace ChatdollKit.Examples.ModelControl
{
    public class ModelControlSkill : SkillBase
    {
#pragma warning disable CS1998
        public override async UniTask<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
        {
            // NOTE: Register "Smile", "Angry" and "Sad" facial expression before running this example

            // Build and return response message
            var response = new Response(request.Id);

            response.AddVoiceTTS("はじめまして！");
            response.AddVoiceTTS("この声、");

            response.AddVoiceTTS("モーション、", postGap: 2.5f, asNewFrame: true);
            response.AddAnimation("BaseParam", 9, 3.0f);

            response.AddVoiceTTS("表情は、", postGap: 4.0f, asNewFrame: true);
            response.AddAnimation("BaseParam", 6);
            response.AddFace("Neutral", duration: 1.2f);
            response.AddFace("Smile", duration: 0.5f);
            response.AddFace("Angry", duration: 0.5f);
            response.AddFace("Sad", duration: 0.5f);
            response.AddFace("Smile");

            response.AddVoiceTTS("チャットドールキットを使ってコードベースで制御しています。", asNewFrame: true);
            response.AddFace("Neutral");

            return response;
        }
#pragma warning restore CS1998
    }
}

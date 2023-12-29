using System.IO;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;

namespace ChatdollKit.Examples.MultiSkills
{
    public class CameraSkill : SkillBase
    {
#pragma warning disable CS1998
        public override async UniTask<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
        {
            var response = new Response(request.Id);

            if (state.Topic.IsFirstTurn)
            {
                // Continue topic to take a photo next turn
                response.EndTopic = false;
                response.NextTurnRequestType = RequestType.Camera;

                response.AddVoiceTTS("写真を撮ります。笑ってください");
            }
            else
            {
                // Save photo
                var jpg = ((Texture2D)request.Payloads["photo"]).EncodeToJPG();
                File.WriteAllBytes("Assets/Resources/ExamplePhoto.jpg", jpg);

                response.AddVoiceTTS("写真を撮りました");
            }

            return response;
        }
#pragma warning restore CS1998
    }
}

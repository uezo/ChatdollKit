﻿using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Dialog;
using UnityEngine;

namespace ChatdollKit.Examples.MultiSkills
{
    public class CameraSkill : SkillBase
    {
#pragma warning disable CS1998
        public override async Task<Response> ProcessAsync(Request request, State state, CancellationToken token)
        {
            var response = new Response(request.Id);

            if (state.Topic.IsFirstTurn)
            {
                // Continue topic to take a photo next turn
                state.Topic.IsFinished = false;
                state.Topic.RequiredRequestType = RequestType.Camera;

                response.AddVoiceTTS("写真を撮ります。笑ってください");
            }
            else
            {
                // Save photo
                var photos = request.Payloads as List<Texture2D>;
                var jpg = photos[0].EncodeToJPG();
                File.WriteAllBytes("Assets/Resources/ExamplePhoto.jpg", jpg);

                response.AddVoiceTTS("写真を撮りました");
            }

            return response;
        }
#pragma warning restore CS1998
    }
}
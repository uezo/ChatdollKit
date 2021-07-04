using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.Examples.Skills;

namespace ChatdollKit.Examples.ModelControl
{
    [RequireComponent(typeof(EchoSkill))]
    [RequireComponent(typeof(ModelControlSkill))]
    public class Router : SkillRouterBase
    {
        public string ModelKeyword = "モデル";

        // Extract intent and entities from request and state
#pragma warning disable CS1998
        public override async Task<IntentExtractionResult> ExtractIntentAsync(Request request, State state, CancellationToken token)
        {
            if (request.Text.Contains(ModelKeyword))
            {
                return new IntentExtractionResult("modelcontrol");

            }
            else
            {
                return new IntentExtractionResult("echo");
            }
        }
#pragma warning restore CS1998
    }
}

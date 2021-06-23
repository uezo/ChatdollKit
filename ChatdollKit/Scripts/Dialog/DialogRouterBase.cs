using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Model;

namespace ChatdollKit.Dialog
{
    public class SkillRouterBase : MonoBehaviour, ISkillRouter
    {
        protected Dictionary<string, ISkill> topicResolver = new Dictionary<string, ISkill>();

        public virtual void Configure()
        {
            
        }

        public void RegisterSkill(ISkill skill)
        {
            skill.Configure();
            topicResolver.Add(skill.TopicName, skill);
        }

#pragma warning disable CS1998
        public virtual async Task ExtractIntentAsync(Request request, State state, CancellationToken token)
        {
            throw new NotImplementedException("DialogRouterBase.ProcessAsync must be implemented");
        }
#pragma warning restore CS1998

        public virtual ISkill Route(Request request, State state, CancellationToken token)
        {
            // Update topic
            ISkill skill;
            if (topicResolver.ContainsKey(request.Intent) && (request.IntentPriority > state.Topic.Priority || string.IsNullOrEmpty(state.Topic.Name)))
            {
                skill = topicResolver[request.Intent];
                if (!request.IsAdhoc)
                {
                    state.Topic.Name = skill.TopicName;
                    state.Topic.Status = "";
                    if (request.IntentPriority >= Priority.Highest)
                    {
                        // Set slightly lower priority to enable to update Highest priority intent
                        state.Topic.Priority = Priority.Highest - 1;
                    }
                    else
                    {
                        state.Topic.Priority = request.IntentPriority;
                    }
                    state.Topic.IsNew = true;
                }
                else
                {
                    // Do not update topic when request is adhoc
                    if (!string.IsNullOrEmpty(state.Topic.Name))
                    {
                        state.Topic.ContinueTopic = true;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(state.Topic.Name))
            {
                // Continue topic
                skill = topicResolver[state.Topic.Name];
            }
            else
            {
                // Use default when the intent is not determined
                throw new Exception("No dialog processor found");
            }

            return skill;
        }
    }
}

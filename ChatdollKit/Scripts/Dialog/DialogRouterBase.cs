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
        public virtual async Task<IntentExtractionResult> ExtractIntentAsync(Request request, State state, CancellationToken token)
        {
            throw new NotImplementedException("SkillRouterBase.ProcessAsync must be implemented");
        }
#pragma warning restore CS1998

        public virtual ISkill Route(Request request, State state, CancellationToken token)
        {
            if (shouldStartTopic(request, state))
            {
                if (!request.Intent.IsAdhoc)
                {
                    state.Topic.Name = request.Intent.Name;
                    state.Topic.Status = "";
                    if (request.Intent.Priority >= Priority.Highest)
                    {
                        // Set slightly lower priority to enable to update Highest priority intent
                        state.Topic.Priority = Priority.Highest - 1;
                    }
                    else
                    {
                        state.Topic.Priority = request.Intent.Priority;
                    }
                    state.Topic.IsFirstTurn = true;
                }
                else
                {
                    // Do not update topic when request is adhoc
                    if (!string.IsNullOrEmpty(state.Topic.Name))
                    {
                        state.Topic.IsFinished = false;
                    }
                }

                return topicResolver[request.Intent.Name];
            }
            else if (!string.IsNullOrEmpty(state.Topic.Name))
            {
                // Continue topic
                return topicResolver[state.Topic.Name];
            }
            else
            {
                throw new Exception("No skill found");
            }
        }

        private bool shouldStartTopic(Request request, State state)
        {
            if (!request.HasIntent())
            {
                // Return false if intent is not set
                return false;
            }

            if (!topicResolver.ContainsKey(request.Intent.Name))
            {
                // Return false if no skills found match to the intent
                return false;
            }

            if (request.Intent.Priority > state.Topic.Priority || string.IsNullOrEmpty(state.Topic.Name))
            {
                // Return true if the priority of intent is higher than that of topic or topic is not set
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog.Processor
{
    public class SkillRouterBase : MonoBehaviour, ISkillRouter
    {
        protected Dictionary<string, ISkill> topicResolver = new Dictionary<string, ISkill>();

        public virtual List<ISkill> RegisterSkills()
        {
            var skills = GetComponents<ISkill>();

            // Register skills to router
            if (skills.Length > 0)
            {
                foreach (var skill in skills)
                {
                    try
                    {
                        topicResolver.Add(skill.TopicName, skill);
                        Debug.Log($"Skill '{skill.TopicName}' registered successfully");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to register '{skill.TopicName}': {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }
            else
            {
                Debug.LogError("No skills registered");
            }

            return topicResolver.Values.ToList();
        }

        public bool IsAvailableTopic(string topicName, bool warnInavailable = false)
        {
            if (topicResolver.ContainsKey(topicName) && topicResolver[topicName].IsAvailable)
            {
                return true;
            }
            else
            {
                if (warnInavailable)
                {
                    Debug.LogWarning($"Topic not available: {topicName}");
                }
                return false;
            }
        }

#pragma warning disable CS1998
        public virtual async UniTask<IntentExtractionResult> ExtractIntentAsync(Request request, State state, CancellationToken token)
        {
            throw new NotImplementedException("SkillRouterBase.ProcessAsync must be implemented");
        }
#pragma warning restore CS1998

        public virtual ISkill Route(Request request, State state, CancellationToken token)
        {
            if (ShouldStartTopic(request, state))
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

        private bool ShouldStartTopic(Request request, State state)
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

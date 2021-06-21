using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Model;

namespace ChatdollKit.Dialog
{
    public class DialogRouterBase : MonoBehaviour, IDialogRouter
    {
        protected Dictionary<string, IDialogProcessor> intentResolver = new Dictionary<string, IDialogProcessor>();
        protected Dictionary<string, IDialogProcessor> topicResolver = new Dictionary<string, IDialogProcessor>();

        public virtual void Configure()
        {
            
        }

        public void RegisterIntent(string intentName, IDialogProcessor dialogProcessor)
        {
            dialogProcessor.Configure();
            intentResolver.Add(intentName, dialogProcessor);
            topicResolver.Add(dialogProcessor.TopicName, dialogProcessor);
        }

#pragma warning disable CS1998
        public virtual async Task ExtractIntentAsync(Request request, State state, CancellationToken token)
        {
            throw new NotImplementedException("DialogRouterBase.ProcessAsync must be implemented");
        }
#pragma warning restore CS1998

        public virtual IDialogProcessor Route(Request request, State state, CancellationToken token)
        {
            // Update topic
            IDialogProcessor dialogProcessor;
            if (intentResolver.ContainsKey(request.Intent) && (request.IntentPriority > state.Topic.Priority || string.IsNullOrEmpty(state.Topic.Name)))
            {
                dialogProcessor = intentResolver[request.Intent];
                if (!request.IsAdhoc)
                {
                    state.Topic.Name = dialogProcessor.TopicName;
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
                dialogProcessor = topicResolver[state.Topic.Name];
            }
            else
            {
                // Use default when the intent is not determined
                throw new Exception("No dialog processor found");
            }

            return dialogProcessor;
        }
    }
}

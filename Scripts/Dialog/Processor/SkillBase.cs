using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Model;

namespace ChatdollKit.Dialog.Processor
{
    public class SkillBase : MonoBehaviour, ISkill
    {
        public string Name;
        protected ModelController modelController;

        protected virtual void Awake()
        {
            modelController = gameObject.GetComponent<ModelController>();
        }

        // Get topic name
        public virtual string TopicName
        {
            get
            {
                // Use Name if configured
                if (!string.IsNullOrEmpty(Name))
                {
                    return Name;
                }

                // Create name from ClassName
                var name = GetType().Name;
                if (name.ToLower().EndsWith("skill"))
                {
                    name = name.Substring(0, name.Length - 5);
                }
                return name.ToLower();
            }
        }

        public virtual bool IsAvailable
        {
            get
            {
                // Always returns true(available) in base
                return true;
            }
        }

        public virtual void Configure()
        {
            //
        }

#pragma warning disable CS1998
        public virtual async UniTask<Response> PreProcessAsync(Request request, State state, CancellationToken token)
        {
            // Return AnimatedVoices or something to do when ProcessAsync is estimated to take much times in this method.
            return null;
        }
#pragma warning restore CS1998

        public virtual async UniTask ShowWaitingAnimationAsync(Response response, Request request, State state, CancellationToken token)
        {
            if (response != null)
            {
                await ShowResponseAsync(response, request, state, token);
            }
        }

        // Process skill
#pragma warning disable CS1998
        public virtual async UniTask<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
        {
            throw new NotImplementedException("SkillBase.ProcessAsync must be implemented");
        }
#pragma warning restore CS1998

        // Show response
        public virtual async UniTask ShowResponseAsync(Response response, Request request, State state, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (response.AnimatedVoiceRequest != null && modelController != null)
            {
                await modelController.AnimatedSay(response.AnimatedVoiceRequest, token);
            }
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Model;

namespace ChatdollKit.Dialog
{
    public class DialogProcessorBase : MonoBehaviour, IDialogProcessor
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
                if (name.ToLower().EndsWith("dialogprocessor"))
                {
                    name = name.Substring(0, name.Length - 15);
                }
                else if (name.ToLower().EndsWith("dialog"))
                {
                    name = name.Substring(0, name.Length - 6);
                }
                return name.ToLower();
            }
        }

        public virtual void Configure()
        {
            //
        }

#pragma warning disable CS1998
        public virtual async Task<Response> PreProcessAsync(Request request, Context context, CancellationToken token)
        {
            // Return AnimatedVoices or something to do when ProcessAsync is estimated to take much times in this method.
            return null;
        }
#pragma warning restore CS1998

        public virtual async Task ShowWaitingAnimationAsync(Response response, Request request, Context context, CancellationToken token)
        {
            if (request != null)
            {
                await ShowResponseAsync(response, request, context, token);
            }
        }

        // Process dialog
#pragma warning disable CS1998
        public virtual async Task<Response> ProcessAsync(Request request, Context context, CancellationToken token)
        {
            throw new NotImplementedException("DialogProcessorBase.ProcessAsync must be implemented");
        }
#pragma warning restore CS1998

        // Show response
        public virtual async Task ShowResponseAsync(Response response, Request request, Context context, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (response.AnimatedVoiceRequest != null)
            {
                await modelController?.AnimatedSay(response.AnimatedVoiceRequest, token);
            }
        }
    }
}

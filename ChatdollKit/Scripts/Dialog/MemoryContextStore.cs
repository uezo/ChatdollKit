using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;

namespace ChatdollKit.Dialog
{
    public class MemoryContextStore : MonoBehaviour, IContextStore
    {
        public int TimeoutSeconds = 300;
        private Dictionary<string, Context> contexts = new Dictionary<string, Context>();

#pragma warning disable CS1998

        public async Task<Context> GetContextAsync(string userId)
        {
            Context context;
            if (contexts.ContainsKey(userId))
            {
                context = contexts[userId];

                if ((int)(DateTime.UtcNow - context.Timestamp).TotalSeconds > TimeoutSeconds)
                {
                    // Create new context when timeout
                    Debug.Log("Context created (Timeout)");
                    context = new Context(userId);
                }
                else
                {
                    // Just update timestamp
                    context.Timestamp = DateTime.UtcNow;
                }
            }
            else
            {
                // Create and store new Context
                context = new Context(userId);
                contexts[userId] = context;
            }

            // Return the deep copy of the context
            return JsonConvert.DeserializeObject<Context>(JsonConvert.SerializeObject(context));
        }

        public async Task SaveContextAsync(Context context)
        {
            contexts[context.UserId] = context;
        }

        public async Task DeleteContextAsync(string userId)
        {
            contexts.Remove(userId);
        }

#pragma warning restore CS1998
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace ChatdollKit.Dialog.Processor
{
    public class MemoryStateStore : IStateStore
    {
        public int TimeoutSeconds = 300;
        private Dictionary<string, State> states = new Dictionary<string, State>();

#pragma warning disable CS1998

        public async UniTask<State> GetStateAsync(string userId)
        {
            State state;
            if (states.ContainsKey(userId))
            {
                state = states[userId];

                if ((int)(DateTime.UtcNow - state.UpdatedAt).TotalSeconds > TimeoutSeconds)
                {
                    // Create new state when timeout
                    Debug.Log("State created (Timeout)");
                    return new State(userId);
                }
                else
                {
                    // Just update timestamp and IsNew
                    Debug.Log("Using existing state");
                    state.UpdatedAt = DateTime.UtcNow;
                    state.IsNew = false;
                    // Return the deep copy of the state to avoid updating stored state directly
                    return JsonConvert.DeserializeObject<State>(JsonConvert.SerializeObject(state));
                }
            }
            else
            {
                // Create new State
                return new State(userId);
            }
        }

        public async UniTask SaveStateAsync(State state)
        {
            states[state.UserId] = state;
        }

        public async UniTask DeleteStateAsync(string userId)
        {
            states.Remove(userId);
        }

#pragma warning restore CS1998
    }
}

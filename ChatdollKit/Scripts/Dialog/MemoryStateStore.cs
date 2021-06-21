using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;

namespace ChatdollKit.Dialog
{
    public class MemoryStateStore : MonoBehaviour, IStateStore
    {
        public int TimeoutSeconds = 300;
        private Dictionary<string, State> states = new Dictionary<string, State>();

#pragma warning disable CS1998

        public async Task<State> GetStateAsync(string userId)
        {
            State state;
            if (states.ContainsKey(userId))
            {
                state = states[userId];

                if ((int)(DateTime.UtcNow - state.Timestamp).TotalSeconds > TimeoutSeconds)
                {
                    // Create new state when timeout
                    Debug.Log("State created (Timeout)");
                    state = new State(userId);
                }
                else
                {
                    // Just update timestamp
                    state.Timestamp = DateTime.UtcNow;
                }
            }
            else
            {
                // Create and store new State
                state = new State(userId);
                states[userId] = state;
            }

            // Return the deep copy of the state
            return JsonConvert.DeserializeObject<State>(JsonConvert.SerializeObject(state));
        }

        public async Task SaveStateAsync(State state)
        {
            states[state.UserId] = state;
        }

        public async Task DeleteStateAsync(string userId)
        {
            states.Remove(userId);
        }

#pragma warning restore CS1998
    }
}

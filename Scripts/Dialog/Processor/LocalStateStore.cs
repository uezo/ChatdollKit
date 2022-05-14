using System;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog.Processor
{
    public class LocalStateStore : MonoBehaviour, IStateStore
    {
        public int TimeoutSeconds = 300;
        private string saveDirectory;

        private void Awake()
        {
            saveDirectory = Application.persistentDataPath;
        }

        // Get state from file
        public async UniTask<State> GetStateAsync(string userId)
        {
            var filePath = Path.Combine(saveDirectory, $"state_{userId}.json");
            if (!File.Exists(filePath))
            {
                Debug.Log($"State created (File not found): {filePath}");
                return new State(userId);
            }

            try
            {
                var jsonString = "";
                using (var reader = File.OpenText(filePath))
                {
                    jsonString = await reader.ReadToEndAsync();
                }
                var state = JsonConvert.DeserializeObject<State>(jsonString);

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
                    return state;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("State created (Read error)");
                Debug.LogWarning(ex.StackTrace);
                return new State(userId);
            }
        }

        // Save state to file
        public async UniTask SaveStateAsync(State state)
        {
            var filePath = Path.Combine(saveDirectory, $"state_{state.UserId}.json");
            var jsonString = JsonConvert.SerializeObject(state);
            using (var writer = new StreamWriter(filePath))
            {
                await writer.WriteAsync(jsonString);
            }
        }

#pragma warning disable CS1998
        // Delete state by removing file
        public async UniTask DeleteStateAsync(string userId)
        {
            var filePath = Path.Combine(saveDirectory, $"state_{userId}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
#pragma warning restore CS1998
    }
}

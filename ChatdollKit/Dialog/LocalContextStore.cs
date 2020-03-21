using System;
using System.Threading.Tasks;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;


namespace ChatdollKit.Dialog
{
    public class LocalContextStore : MonoBehaviour, IContextStore
    {
        public int TimeoutSeconds = 300;
        private string saveDirectory;

        private void Awake()
        {
            saveDirectory = Application.persistentDataPath;
        }

        // Get context from file
        public async Task<Context> GetContextAsync(string userId)
        {
            var filePath = Path.Combine(saveDirectory, $"context_{userId}.json");
            if (!File.Exists(filePath))
            {
                Debug.Log("Context created (File not found)");
                return new Context(userId);
            }

            try
            {
                var jsonString = "";
                using (var reader = File.OpenText(filePath))
                {
                    jsonString = await reader.ReadToEndAsync();
                }
                var context = JsonConvert.DeserializeObject<Context>(jsonString);
                if (DateTime.UtcNow.Ticks - context.Timestamp.Ticks > TimeoutSeconds * 10000000)
                {
                    // Create new context when timeout
                    Debug.Log("Context created (Timeout)");
                    return new Context(userId);
                }
                else
                {
                    // Just update timestamp
                    Debug.Log("Using existing context");
                    context.Timestamp = DateTime.UtcNow;
                    return context;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Context created (Read error)");
                Debug.LogWarning(ex.StackTrace);
                return new Context(userId);
            }
        }

        // Save context to file
        public async Task SaveContextAsync(Context context)
        {
            var filePath = Path.Combine(saveDirectory, $"context_{context.UserId}.json");
            var jsonString = JsonConvert.SerializeObject(context);
            using (var writer = new StreamWriter(filePath))
            {
                await writer.WriteAsync(jsonString);
            }
        }

        // Delete context by removing file
        public async Task DeleteContextAsync(string userId)
        {
            var filePath = Path.Combine(saveDirectory, $"context_{userId}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

    }
}

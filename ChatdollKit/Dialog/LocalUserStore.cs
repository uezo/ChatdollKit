using System;
using System.Threading.Tasks;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;


namespace ChatdollKit.Dialog
{
    public class LocalUserStore : MonoBehaviour, IUserStore
    {
        private string saveDirectory;

        private void Awake()
        {
            saveDirectory = Application.persistentDataPath;
        }

        // Get user from file
        public async Task<User> GetUserAsync(string userId)
        {
            var filePath = Path.Combine(saveDirectory, $"user_{userId}.json");
            if (!File.Exists(filePath))
            {
                Debug.Log("User created (File not found)");
                return new User(userId, SaveUserAsync);
            }

            try
            {
                var jsonString = "";
                using (var reader = File.OpenText(filePath))
                {
                    jsonString = await reader.ReadToEndAsync();
                }

                var user = JsonConvert.DeserializeObject<User>(jsonString);
                user.saveFunc = SaveUserAsync;
                Debug.Log("Using existing user");
                return user;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("User created (Read error)");
                Debug.LogWarning(ex.StackTrace);
                return new User(userId, SaveUserAsync);
            }
        }

        // Save user to file
        public async Task SaveUserAsync(User user)
        {
            var filePath = Path.Combine(saveDirectory, $"user_{user.Id}.json");
            var jsonString = JsonConvert.SerializeObject(user);
            using (var writer = new StreamWriter(filePath))
            {
                await writer.WriteAsync(jsonString);
            }
        }

        // Clear user by removing file
        public async Task DeleteUserAsync(string userId)
        {
            var filePath = Path.Combine(saveDirectory, $"user_{userId}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}

using System;
using System.IO;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;


namespace ChatdollKit.Dialog.Processor
{
    public class LocalUserStore : MonoBehaviour, IUserStore
    {
        private string saveDirectory;

        private void Awake()
        {
            saveDirectory = Application.persistentDataPath;
        }

        // Get user from file
        public async UniTask<User> GetUserAsync(string userId)
        {
            var filePath = Path.Combine(saveDirectory, $"user_{userId}.json");
            if (!File.Exists(filePath))
            {
                Debug.Log($"User created (File not found): {filePath}");
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
        public async UniTask SaveUserAsync(User user)
        {
            var filePath = Path.Combine(saveDirectory, $"user_{user.Id}.json");
            var jsonString = JsonConvert.SerializeObject(user);
            using (var writer = new StreamWriter(filePath))
            {
                await writer.WriteAsync(jsonString);
            }
        }

#pragma warning disable CS1998
        // Clear user by removing file
        public async UniTask DeleteUserAsync(string userId)
        {
            var filePath = Path.Combine(saveDirectory, $"user_{userId}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
#pragma warning restore CS1998
    }
}

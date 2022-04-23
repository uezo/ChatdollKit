using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace ChatdollKit.Dialog.Processor
{
    public class MemoryUserStore : IUserStore
    {
        private Dictionary<string, User> users = new Dictionary<string, User>();

#pragma warning disable CS1998
        // Get user from file
        public async UniTask<User> GetUserAsync(string userId)
        {
            User user;
            if (users.ContainsKey(userId))
            {
                user = users[userId];
            }
            else
            {
                user = new User(userId, SaveUserAsync);
            }

            // Return the deep copy of the user
            return JsonConvert.DeserializeObject<User>(JsonConvert.SerializeObject(user));
        }
#pragma warning restore CS1998


#pragma warning disable CS1998
        // Save user to file
        public async UniTask SaveUserAsync(User user)
        {
            // Save the deep copy of user
            users[user.Id] = JsonConvert.DeserializeObject<User>(JsonConvert.SerializeObject(user));
        }
#pragma warning restore CS1998


#pragma warning disable CS1998
        // Clear user by removing file
        public async UniTask DeleteUserAsync(string userId)
        {
            users.Remove(userId);
        }
#pragma warning restore CS1998
    }
}

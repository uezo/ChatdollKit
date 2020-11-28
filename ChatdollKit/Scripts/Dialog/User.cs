using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;


namespace ChatdollKit.Dialog
{
    public class User
    {
        public string Id { get; }
        public string DeviceId { get; }
        public string Name { get; set; }
        public string Nickname { get; set; }
        public Dictionary<string, string> Data { get; set; }
        [JsonIgnore]
        public Func<User, Task> saveFunc { get; set; }

        public User(string id = null, Func<User, Task> saveFunc = null)
        {
            Id = id ?? Guid.NewGuid().ToString();
            Data = new Dictionary<string, string>();
            this.saveFunc = saveFunc;
        }

        public async Task SaveAsync()
        {
            await saveFunc?.Invoke(this);
        }
    }
}

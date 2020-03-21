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
        public Dictionary<string, object> Data { get; set; }
        [JsonIgnore]
        public Func<User, Task> saveFunc { get; set; }

        public User(string id = null, Func<User, Task> saveFunc = null)
        {
            Id = id == null ? Guid.NewGuid().ToString() : id;
            Data = new Dictionary<string, object>();
            this.saveFunc = saveFunc;
        }

        public async Task SaveAsync()
        {
            await saveFunc?.Invoke(this);
        }
    }
}

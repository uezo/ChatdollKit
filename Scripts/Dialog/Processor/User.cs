using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace ChatdollKit.Dialog.Processor
{
    public class User
    {
        public string Id { get; }
        public string DeviceId { get; }
        public string Name { get; set; }
        public string Nickname { get; set; }
        public Dictionary<string, string> Data { get; set; }
        [JsonIgnore]
        public Func<User, UniTask> saveFunc { get; set; }

        public User(string id = null, Func<User, UniTask> saveFunc = null)
        {
            Id = id ?? Guid.NewGuid().ToString();
            Data = new Dictionary<string, string>();
            this.saveFunc = saveFunc;
        }

        public async UniTask SaveAsync()
        {
            await saveFunc.Invoke(this);
        }
    }
}

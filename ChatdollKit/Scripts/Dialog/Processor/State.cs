using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace ChatdollKit.Dialog.Processor
{
    public class State
    {
        public string Id { get; }
        public string UserId { get; }
        public DateTime UpdatedAt { get; set; }
        public bool IsNew { get; set; }
        public Topic Topic { get; set; }
        public Dictionary<string, object> Data { get; set; }
        [JsonIgnore]
        public Func<State, UniTask> saveFunc { get; set; }

        public State(string userId, string id = null, Func<State, UniTask> saveFunc = null)
        {
            Id = id == null ? Guid.NewGuid().ToString() : id;
            UserId = userId;
            UpdatedAt = DateTime.UtcNow;
            IsNew = true;
            Topic = new Topic();
            Data = new Dictionary<string, object>();
            this.saveFunc = saveFunc;
        }

        public void Clear()
        {
            Topic = new Topic();
            Data = new Dictionary<string, object>();
        }

        public async UniTask SaveAsync()
        {
            await saveFunc.Invoke(this);
        }
    }

    public class Topic
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public bool IsFirstTurn { get; set; }
        public Priority Priority { get; set; }

        public Topic()
        {
            IsFirstTurn = true;
        }
    }
}

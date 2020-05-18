using System;
using System.Collections.Generic;
using System.Linq;


namespace ChatdollKit.Model
{
    public class ActionHistoryRecorder
    {
        public bool Enabled { get; set; }
        public List<ActionHistory> Histories { get; set; }
        public Action<ActionHistory> SendHistory { get; set; }

        public ActionHistoryRecorder(bool enabled = false, Action<ActionHistory> sendHistoryFunc = null)
        {
            Enabled = enabled;
            Histories = new List<ActionHistory>();
            SendHistory = sendHistoryFunc;
        }

        public string Add(object action)
        {
            if (!Enabled) return string.Empty;

            var history = new ActionHistory(action);
            Histories.Add(history);
            SendHistory?.Invoke(history);
            return history.Id;
        }

        public void Add(string id, string description)
        {
            if (!Enabled) return;

            var history = new ActionHistory()
            {
                Id = id,
                Description = description,
                Timestamp = DateTime.UtcNow
            };
            var originalAction = Histories.Where(j => j.Id == id).FirstOrDefault();
            if (originalAction != null)
            {
                history.ActionType = originalAction.ActionType;
                Histories.Add(history);
                SendHistory?.Invoke(history);
            }
        }

    }

    public class ActionHistory
    {
        public string Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string ActionType { get; set; }
        public object ActionElement { get; set; }
        public string Description { get; set; }

        public ActionHistory()
        {

        }

        public ActionHistory(object action, string description = null)
        {
            Id = Guid.NewGuid().ToString();
            Timestamp = DateTime.UtcNow;
            Description = description;
            ActionElement = action;

            if (action is Voice)
            {
                ActionType = "voice";
            }
            else if (action is Animation)
            {
                ActionType = "animation";
            }
            else if (action is FaceExpression)
            {
                ActionType = "face";
            }
            else if (action is string)
            {
                ActionType = "message";
            }
            else
            {
                ActionType = "other";
            }
        }
    }
}

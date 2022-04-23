namespace ChatdollKit.Dialog
{
    public class Intent
    {
        public string Name { get; set; }
        public Priority Priority { get; set; }
        public bool IsAdhoc { get; set; }

        public Intent(string name, Priority priority = Priority.Normal, bool isAdhoc = false)
        {
            Name = name;
            Priority = priority;
            IsAdhoc = isAdhoc;
        }
    }
}

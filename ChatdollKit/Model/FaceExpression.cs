namespace ChatdollKit.Model
{
    // Face expression
    public class FaceExpression
    {
        public string Name { get; set; }
        public float Duration { get; set; }
        public string Description { get; set; }

        public FaceExpression(string name, float duration, string description)
        {
            Name = name;
            Duration = duration;
            Description = description;
        }
    }
}

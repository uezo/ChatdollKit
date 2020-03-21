namespace ChatdollKit.Model
{
    // Face expression
    public class FaceExpression
    {
        public string Name { get; set; }
        public float Duration { get; set; }

        public FaceExpression(string name, float duration)
        {
            Name = name;
            Duration = duration;
        }
    }
}

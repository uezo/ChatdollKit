namespace ChatdollKit.Model
{
    // Voice
    public class Voice
    {
        public string Name { get; set; }
        public float PreGap { get; set; }
        public float PostGap { get; set; }

        public Voice(string name, float preGap, float postGap)
        {
            Name = name;
            PreGap = preGap;
            PostGap = postGap;
        }
    }
}

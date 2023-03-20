using System.Collections.Generic;


namespace ChatdollKit.Model
{
    // Face expression request
    public class FaceRequest
    {
        public List<FaceExpression> Faces { get; set; }

        public FaceRequest(List<FaceExpression> faces = null)
        {
            Faces = faces ?? new List<FaceExpression>();
        }

        public void AddFace(string name, float duration = 0.0f, string description = null)
        {
            Faces.Add(new FaceExpression(name, duration, description));
        }
    }
}

using Cysharp.Threading.Tasks;

namespace ChatdollKit.Model
{
    public interface IFaceExpressionProxy
    {
        void SetExpression(string name = "Neutral", float value = 1.0f);
        void SetExpressionSmoothly(string name = "Neutral", float value = 1.0f);
    }
}

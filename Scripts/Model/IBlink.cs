using Cysharp.Threading.Tasks;

namespace ChatdollKit.Model
{
    public interface IBlink
    {
        UniTask StartBlinkAsync(bool startNew = false);
        void StopBlink();
    }
}

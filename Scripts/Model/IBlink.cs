using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Model
{
    public interface IBlink
    {
        UniTask StartBlinkAsync();
        void StopBlink();
        void Setup(GameObject avatarObject);
    }
}

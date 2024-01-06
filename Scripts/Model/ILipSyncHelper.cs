using UnityEngine;

namespace ChatdollKit.Model
{
    public interface ILipSyncHelper
    {
        void ResetViseme();
        void ConfigureViseme(GameObject avatarObject);
    }
}

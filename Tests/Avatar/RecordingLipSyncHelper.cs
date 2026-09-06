using ChatdollKit.Avatar;
using ChatdollKit.Avatar.LipSync;
#if UNITY_5_3_OR_NEWER
using ChatdollKit.Model;
using UnityEngine;

namespace ChatdollKit.Tests.Avatar
{
    /// <summary>Records setup and cleanup calls without processing speech audio.</summary>
    public sealed class RecordingLipSyncHelper : MonoBehaviour, ILipSyncHelper
    {
        public int ResetCount;
        public int ConfigureCount;
        public void ResetViseme() => ResetCount++;
        public void ConfigureViseme(GameObject avatarObject) => ConfigureCount++;
    }
}
#endif

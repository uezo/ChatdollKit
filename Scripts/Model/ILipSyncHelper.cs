namespace ChatdollKit.Model
{
    public interface ILipSyncHelper
    {
        void ResetViseme();
#if UNITY_EDITOR
        void ConfigureViseme();
#endif
    }
}

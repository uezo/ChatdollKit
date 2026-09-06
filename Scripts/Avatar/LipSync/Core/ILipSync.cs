using ChatdollKit.Avatar;
namespace ChatdollKit.Avatar.LipSync
{
    /// <summary>Receives complete decoded audio and its current playback position on Unity's main thread.</summary>
    public interface ILipSync
    {
        void BeginPlayback(WaveAudio audio);
        /// <param name="samplePosition">PCM frame position (not the interleaved array index).</param>
        void UpdatePlayback(int samplePosition);
        void ResetViseme();
    }
}

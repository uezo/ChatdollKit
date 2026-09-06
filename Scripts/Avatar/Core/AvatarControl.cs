using System;

namespace ChatdollKit.Avatar
{
    public enum AvatarControlKind { Face, Animation }

    public sealed class AvatarControl
    {
        public AvatarControlKind Kind { get; }
        public string Name { get; }
        public double? DurationSeconds { get; }
        public AvatarControl(AvatarControlKind kind, string name, double? durationSeconds = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A control name is required.", nameof(name));
            if (durationSeconds.HasValue && (double.IsNaN(durationSeconds.Value) || double.IsInfinity(durationSeconds.Value) || durationSeconds < 0))
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            Kind = kind; Name = name; DurationSeconds = durationSeconds;
        }
    }
}

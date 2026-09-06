namespace ChatdollKit.Avatar.LipSync
{
    public enum Viseme { None = 0, A = 1, I = 2, U = 3, E = 4, O = 5 }

    /// <summary>Mouth weights in the range 0 to 1, with the unsmoothed input RMS.</summary>
    public readonly struct LipSyncResult
    {
        public double A { get; }
        public double I { get; }
        public double U { get; }
        public double E { get; }
        public double O { get; }
        public double Volume { get; }
        public double RawVolume { get; }
        public Viseme MainViseme { get; }
        public double MainVisemeWeight { get; }

        public LipSyncResult(double a, double i, double u, double e, double o,
            Viseme mainViseme, double volume, double rawVolume)
        {
            A = a; I = i; U = u; E = e; O = o;
            Volume = volume; RawVolume = rawVolume;
            MainViseme = mainViseme; MainVisemeWeight = mainViseme != Viseme.None ? volume : 0;
        }

        public double GetWeight(Viseme viseme)
        {
            switch (viseme)
            {
                case Viseme.A: return A;
                case Viseme.I: return I;
                case Viseme.U: return U;
                case Viseme.E: return E;
                case Viseme.O: return O;
                default: return 0;
            }
        }
    }
}

using System;

namespace ChatdollKit.Avatar.LipSync
{
    /// <summary>JSON data compatible with a uLipSync v3 profile.</summary>
    [Serializable]
    public sealed class MfccProfile
    {
        public int mfccNum = 12;
        public int mfccDataCount = 16;
        public int melFilterBankChannels = 30;
        public int targetSampleRate = 16000;
        public int sampleCount = 1024;
        public bool useStandardization;
        // 0: L1 norm, 1: L2 norm, 2: cosine similarity.
        public int compareMethod = 1;
        public MfccProfileEntry[] mfccs;
    }

    [Serializable]
    public sealed class MfccProfileEntry
    {
        public string name;
        public MfccCalibrationData[] mfccCalibrationDataList;
    }

    [Serializable]
    public sealed class MfccCalibrationData
    {
        public double[] array;
    }
}

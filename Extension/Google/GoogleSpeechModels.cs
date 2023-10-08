using System;
using System.Collections.Generic;
using UnityEngine;
using ChatdollKit.IO;

namespace ChatdollKit.Extension.Google
{
    public class SpeechRecognitionRequest
    {
        public SpeechRecognitionConfig config;
        public SpeechRecognitionAudio audio;

        public SpeechRecognitionRequest(AudioClip audioClip, string languageCode, bool useEnhancedModel, List<SpeechContext> speechContexts, float[] samplingData = null)
        {
            config = new SpeechRecognitionConfig(audioClip, languageCode, useEnhancedModel, speechContexts);
            audio = new SpeechRecognitionAudio(AudioConverter.AudioClipToBase64(audioClip, samplingData));
        }
    }

    public class SpeechRecognitionConfig
    {
        public int encoding;
        public double sampleRateHertz;
        public double audioChannelCount;
        public bool enableSeparateRecognitionPerChannel;
        public string languageCode;
        public string model;
        public bool useEnhanced;
        public List<SpeechContext> speechContexts;

        public SpeechRecognitionConfig(AudioClip audioClip, string languageCode, bool useEnhancedModel, List<SpeechContext> speechContexts)
        {
            encoding = 1;   // 1: 16-bit linear PCM
            sampleRateHertz = audioClip.frequency;
            audioChannelCount = audioClip.channels;
            enableSeparateRecognitionPerChannel = false;
            this.languageCode = languageCode;
            model = useEnhancedModel ? null : "default";
            useEnhanced = useEnhancedModel;
            this.speechContexts = speechContexts;
        }
    }

    public class SpeechRecognitionAudio
    {
        public string content;

        public SpeechRecognitionAudio(string content)
        {
            this.content = content;
        }
    }

    [Serializable]
    public class SpeechContext
    {
        public List<string> phrases;
        public int boost;
    }

    public class SpeechRecognitionResponse
    {
        public SpeechRecognitionResult[] results;
    }

    public class SpeechRecognitionResult
    {
        public SpeechRecognitionAlternative[] alternatives;
    }

    public class SpeechRecognitionAlternative
    {
        public string transcript;
        public double confidence;
    }
}

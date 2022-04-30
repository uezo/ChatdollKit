﻿using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;


namespace ChatdollKit.Model
{
    // Request for amination with voice and face expression
    public class AnimatedVoiceRequest
    {
        public List<AnimatedVoice> AnimatedVoices { get; set; }
        public bool DisableBlink { get; set; }
        public bool StopIdlingOnStart { get; set; }
        public bool StartIdlingOnEnd { get; set; }
        public bool StopLayeredAnimations { get; set; }
        public string BaseLayerName { get; set; }

        [JsonConstructor]
        public AnimatedVoiceRequest(List<AnimatedVoice> animatedVoice = null, bool disableBlink = true, bool startIdlingOnEnd = true, bool stopIdlingOnStart = true, bool stopLayeredAnimations = true, string baseLayerName = null)
        {
            AnimatedVoices = animatedVoice ?? new List<AnimatedVoice>();
            DisableBlink = disableBlink;
            StartIdlingOnEnd = startIdlingOnEnd;
            StopIdlingOnStart = stopIdlingOnStart;
            StopLayeredAnimations = stopLayeredAnimations;
            BaseLayerName = baseLayerName ?? string.Empty;
        }

        public void AddAnimatedVoice(string voiceName, string animationName, string faceName = null, float voicePreGap = 0.0f, float voicePostGap = 0.0f, float animationDuration = 0.0f, float animationFadeLength = -1.0f, float animationWeight = 1.0f, float animationPreGap = 0.0f, float faceDuration = 0.0f, string description = null, bool asNewFrame = false)
        {
            if (asNewFrame || AnimatedVoices.Count == 0)
            {
                CreateNewFrame();
            }
            AddVoice(voiceName, voicePreGap, voicePostGap);
            AddAnimation(animationName, animationDuration, animationFadeLength, animationWeight, animationPreGap, description);
            if (faceName != null)
            {
                AddFace(faceName, faceDuration, description);
            }
        }

        public void AddVoice(string name, float preGap = 0.0f, float postGap = 0.0f, string description = null, bool asNewFrame = false)
        {
            if (asNewFrame || AnimatedVoices.Count == 0)
            {
                CreateNewFrame();
            }
            AnimatedVoices.Last().AddVoice(name, preGap, postGap, description: description);
        }

        public void AddVoiceWeb(string url, float preGap = 0.0f, float postGap = 0.0f, string name = null, string text = null, string description = null, bool asNewFrame = false)
        {
            if (asNewFrame || AnimatedVoices.Count == 0)
            {
                CreateNewFrame();
            }
            AnimatedVoices.Last().AddVoiceWeb(url, preGap, postGap, name, text, description: description);
        }

        public void AddVoiceTTS(string text, float preGap = 0.0f, float postGap = 0.0f, string name = null, TTSConfiguration ttsConfig = null, string description = null, bool asNewFrame = false)
        {
            if (asNewFrame || AnimatedVoices.Count == 0)
            {
                CreateNewFrame();
            }
            AnimatedVoices.Last().AddVoiceTTS(text, preGap, postGap, name, ttsConfig, description: description);
        }

        public void AddAnimation(string name, float duration = 0.0f, float fadeLength = -1.0f, float weight = 1.0f, float preGap = 0.0f, string description = null, bool asNewFrame = false)
        {
            AddAnimation(name, BaseLayerName, duration, fadeLength, weight, preGap, description, asNewFrame);
        }

        public void AddAnimation(string name, string layerName, float duration = 0.0f, float fadeLength = -1.0f, float weight = 1.0f, float preGap = 0.0f, string description = null, bool asNewFrame = false)
        {
            if (asNewFrame || AnimatedVoices.Count == 0)
            {
                CreateNewFrame();
            }
            AnimatedVoices.Last().AddAnimation(name, layerName, duration, fadeLength, weight, preGap, description);
        }

        public void AddFace(string name, float duration = 0.0f, string description = null, bool asNewFrame = false)
        {
            if (asNewFrame || AnimatedVoices.Count == 0)
            {
                CreateNewFrame();
            }
            AnimatedVoices.Last().AddFace(name, duration, description);
        }

        public int CreateNewFrame()
        {
            AnimatedVoices.Add(new AnimatedVoice());
            return AnimatedVoices.Count;
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using ChatdollKit.Model;

namespace ChatdollKit.Tests
{
    public class TestModelRequests
    {
        [SetUp]
        public void SetUp()
        {
            if (SceneManager.GetActiveScene().name != Constants.SceneName)
            {
                SceneManager.LoadScene(Constants.SceneName);
            }
        }

        [Test]
        public void TestVoice()
        {
            Debug.LogWarning("case start");
            // Local
            var localVoice = new Voice("TestVoice", 0.1f, 0.2f, null, null, null, VoiceSource.Local, false, "local voice");
            Assert.AreEqual("TestVoice", localVoice.Name);
            Assert.AreEqual(0.1f, localVoice.PreGap);
            Assert.AreEqual(0.2f, localVoice.PostGap);
            Assert.IsNull(localVoice.Text);
            Assert.IsNull(localVoice.Url);
            Assert.IsNull(localVoice.TTSConfig);
            Assert.AreEqual(string.Empty, localVoice.GetTTSFunctionName());
            Assert.IsNull(localVoice.GetTTSParam("key1"));
            Assert.AreEqual(VoiceSource.Local, localVoice.Source);
            Assert.IsFalse(localVoice.UseCache);
            Assert.AreEqual("local voice", localVoice.Description);

            var webVoice = new Voice(null, 0.1f, 0.2f, null, "https://test.url", null, VoiceSource.Web, true, "web voice"); ;
            Assert.IsNull(webVoice.Name);
            Assert.AreEqual(0.1f, webVoice.PreGap);
            Assert.AreEqual(0.2f, webVoice.PostGap);
            Assert.IsNull(webVoice.Text);
            Assert.AreEqual("https://test.url", webVoice.Url);
            Assert.IsNull(webVoice.TTSConfig);
            Assert.AreEqual(string.Empty, webVoice.GetTTSFunctionName());
            Assert.IsNull(webVoice.GetTTSParam("key1"));
            Assert.AreEqual(VoiceSource.Web, webVoice.Source);
            Assert.IsTrue(webVoice.UseCache);
            Assert.AreEqual("https://test.url", webVoice.CacheKey);
            Assert.AreEqual("web voice", webVoice.Description);

            var ttsConfig = new TTSConfiguration("TestTTSFuncName");
            ttsConfig.Params.Add("key1", "val1");
            ttsConfig.Params.Add("key2", 2.0f);
            var ttsVoice = new Voice(null, 0.1f, 0.2f, "Chatdoll speek this text.", null, ttsConfig, VoiceSource.TTS, true, "tts voice");
            Assert.IsNull(ttsVoice.Name);
            Assert.AreEqual(0.1f, ttsVoice.PreGap);
            Assert.AreEqual(0.2f, ttsVoice.PostGap);
            Assert.AreEqual("Chatdoll speek this text.", ttsVoice.Text);
            Assert.IsNull(ttsVoice.Url);
            Assert.AreEqual("TestTTSFuncName", ttsVoice.GetTTSFunctionName());
            Assert.AreEqual("val1", ttsVoice.GetTTSParam("key1"));
            Assert.AreEqual(2.0f, ttsVoice.GetTTSParam("key2"));
            Assert.AreEqual(VoiceSource.TTS, ttsVoice.Source);
            Assert.IsTrue(ttsVoice.UseCache);
            Assert.AreEqual("Chatdoll speek this text.", ttsVoice.CacheKey);
            Assert.AreEqual("tts voice", ttsVoice.Description);
        }

        [Test]
        public void TestVoiceRequest()
        {
            // Without params
            var voiceRequest = new VoiceRequest();

            voiceRequest.AddVoice("LocalVoice");
            voiceRequest.AddVoiceWeb("https://voice.url");
            voiceRequest.AddVoiceTTS("This text will be read by chatdoll.");

            var localVoice = voiceRequest.Voices[0];
            Assert.AreEqual("LocalVoice", localVoice.Name);
            Assert.AreEqual(0.0f, localVoice.PreGap);
            Assert.AreEqual(0.0f, localVoice.PostGap);
            Assert.AreEqual(string.Empty, localVoice.Text);
            Assert.AreEqual(string.Empty, localVoice.Url);
            Assert.IsNull(localVoice.TTSConfig);
            Assert.AreEqual(VoiceSource.Local, localVoice.Source);

            var webVoice = voiceRequest.Voices[1];
            Assert.AreEqual(string.Empty, webVoice.Name);
            Assert.AreEqual(0.0f, webVoice.PreGap);
            Assert.AreEqual(0.0f, webVoice.PostGap);
            Assert.AreEqual(string.Empty, webVoice.Text);
            Assert.AreEqual("https://voice.url", webVoice.Url);
            Assert.IsNull(webVoice.TTSConfig);
            Assert.AreEqual(VoiceSource.Web, webVoice.Source);
            Assert.IsTrue(webVoice.UseCache);

            var ttsVoice = voiceRequest.Voices[2];
            Assert.AreEqual(string.Empty, ttsVoice.Name);
            Assert.AreEqual(0.0f, ttsVoice.PreGap);
            Assert.AreEqual(0.0f, ttsVoice.PostGap);
            Assert.AreEqual("This text will be read by chatdoll.", ttsVoice.Text);
            Assert.AreEqual(string.Empty, ttsVoice.Url);
            Assert.IsNull(ttsVoice.TTSConfig);
            Assert.AreEqual(VoiceSource.TTS, ttsVoice.Source);
            Assert.IsTrue(ttsVoice.UseCache);

            // With params
            var voiceRequestP = new VoiceRequest();

            voiceRequestP.AddVoice("LocalVoice", 0.1f, 0.2f);
            voiceRequestP.AddVoiceWeb("https://voice.url", 0.1f, 0.2f, useCache: false);
            var ttsConfig = new TTSConfiguration("TestTTSFuncName");
            ttsConfig.Params.Add("key1", "val1");
            ttsConfig.Params.Add("key2", 2.0f);
            voiceRequestP.AddVoiceTTS("This text will be read by chatdoll.", 0.1f, 0.2f, ttsConfig: ttsConfig, useCache: false);

            var localVoiceP = voiceRequestP.Voices[0];
            Assert.AreEqual(0.1f, localVoiceP.PreGap);
            Assert.AreEqual(0.2f, localVoiceP.PostGap);

            var webVoiceP = voiceRequestP.Voices[1];
            Assert.AreEqual(0.1f, webVoiceP.PreGap);
            Assert.AreEqual(0.2f, webVoiceP.PostGap);
            Assert.IsFalse(webVoiceP.UseCache);

            var ttsVoiceP = voiceRequestP.Voices[2];
            Assert.AreEqual(0.1f, ttsVoiceP.PreGap);
            Assert.AreEqual(0.2f, ttsVoiceP.PostGap);
            Assert.AreEqual("TestTTSFuncName", ttsVoiceP.GetTTSFunctionName());
            Assert.AreEqual("val1", ttsVoiceP.GetTTSParam("key1"));
            Assert.AreEqual(2.0f, ttsVoiceP.GetTTSParam("key2"));
            Assert.IsFalse(ttsVoiceP.UseCache);
        }

        [Test]
        public void TestFaceClip()
        {
            var model = GameObject.Find(Constants.ChatdollModelName)?.GetComponent<ModelController>();
            if (model == null)
            {
                Debug.LogError("Chatdoll model not found");
                return;
            }
            var skinnedMeshRenderer = model.SkinnedMeshRenderer;

            var weights = new Dictionary<string, float>();
            weights.Add("mouth_a", 0.5f);
            weights.Add("mouth_smile", 0.3f);
            weights.Add("eye_smile_L", 1.0f);

            var face = new FaceClip("Smile", skinnedMeshRenderer, weights);

            Assert.AreEqual("Smile", face.Name);
            Assert.AreEqual(60, face.Values.Count);
            var configuredWeightCount = 0;
            foreach (var w in face.Values)
            {
                if (w.Name == "mouth_a")
                {
                    Assert.AreEqual(53, w.Index);
                    Assert.AreEqual(0.5f, w.Weight);
                    configuredWeightCount++;
                }
                else if (w.Name == "mouth_smile")
                {
                    Assert.AreEqual(40, w.Index);
                    Assert.AreEqual(0.3f, w.Weight);
                    configuredWeightCount++;
                }
                else if (w.Name == "eye_smile_L")
                {
                    Assert.AreEqual(30, w.Index);
                    Assert.AreEqual(1.0f, w.Weight);
                    configuredWeightCount++;
                }
                else
                {
                    Assert.AreEqual(0.0f, w.Weight);
                }
            }
            Assert.AreEqual(3, configuredWeightCount);
        }

        [Test]
        public void TestFaceClipConfiguration()
        {
            var faceClipConfig = new FaceClipConfiguration();
            Assert.IsNotNull(faceClipConfig.FaceClips);
        }

        [Test]
        public void TestFaceExpression()
        {
            var faceExpression = new FaceExpression("Smile", 0.1f, "face test");
            Assert.AreEqual("Smile", faceExpression.Name);
            Assert.AreEqual(0.1f, faceExpression.Duration);
            Assert.AreEqual("face test", faceExpression.Description);
        }

        [Test]
        public void TestFaceRequest()
        {
            var faceRequest = new FaceRequest();
            Assert.IsTrue(faceRequest.DefaultOnEnd);
            faceRequest.AddFace("Smile");
            faceRequest.AddFace("Angry", 1.1f, "angry face");

            Assert.AreEqual("Smile", faceRequest.Faces[0].Name);
            Assert.AreEqual(0.0f, faceRequest.Faces[0].Duration);
            Assert.AreEqual(null, faceRequest.Faces[0].Description);

            Assert.AreEqual("Angry", faceRequest.Faces[1].Name);
            Assert.AreEqual(1.1f, faceRequest.Faces[1].Duration);
            Assert.AreEqual("angry face", faceRequest.Faces[1].Description);
        }

        [Test]
        public void TestAnimation()
        {
            var animation = new Model.Animation("IdleGeneric", "Base Layer", 3.0f, 0.5f, 1.0f, 0.1f, "idle animation");
            Assert.AreEqual("IdleGeneric", animation.Name);
            Assert.AreEqual("Base Layer", animation.LayerName);
            Assert.AreEqual(3.0f, animation.Duration);
            Assert.AreEqual(0.5f, animation.FadeLength);
            Assert.AreEqual(1.0f, animation.Weight);
            Assert.AreEqual(0.1f, animation.PreGap);
            Assert.AreEqual("idle animation", animation.Description);
            Assert.AreEqual(3.1f, animation.Length);
        }

        [Test]
        public void TestAnimationRequest()
        {
            var animationRequest = new AnimationRequest();
            Assert.AreEqual(new Dictionary<string, List<Model.Animation>>(), animationRequest.Animations);
            Assert.IsTrue(animationRequest.StartIdlingOnEnd);
            Assert.IsTrue(animationRequest.StopIdlingOnStart);
            Assert.IsTrue(animationRequest.StopLayeredAnimations);
            Assert.AreEqual(string.Empty, animationRequest.BaseLayerName);

            animationRequest.AddAnimation("Smile");
            animationRequest.AddAnimation("Angry", 2.0f, 1.0f, 0.5f, 0.1f);
            animationRequest.AddAnimation("WaveHands", "UpperBody", 3.0f, 2.0f, 0.8f, 0.2f, "upper animation");

            var animation01 = animationRequest.Animations[string.Empty][0];
            Assert.AreEqual("Smile", animation01.Name);
            Assert.AreEqual(string.Empty, animation01.LayerName);
            Assert.AreEqual(0.0f, animation01.Duration);
            Assert.AreEqual(-1.0f, animation01.FadeLength);
            Assert.AreEqual(1.0f, animation01.Weight);
            Assert.AreEqual(0.0f, animation01.PreGap);
            Assert.IsNull(animation01.Description);
            Assert.AreEqual(0.0f, animation01.Length);

            var animation02 = animationRequest.Animations[string.Empty][1];
            Assert.AreEqual("Angry", animation02.Name);
            Assert.AreEqual(string.Empty, animation02.LayerName);
            Assert.AreEqual(2.0f, animation02.Duration);
            Assert.AreEqual(1.0f, animation02.FadeLength);
            Assert.AreEqual(0.5f, animation02.Weight);
            Assert.AreEqual(0.1f, animation02.PreGap);
            Assert.IsNull(animation02.Description);
            Assert.AreEqual(2.1f, animation02.Length);

            var animation03 = animationRequest.Animations["UpperBody"][0];
            Assert.AreEqual("WaveHands", animation03.Name);
            Assert.AreEqual("UpperBody", animation03.LayerName);
            Assert.AreEqual(3.0f, animation03.Duration);
            Assert.AreEqual(2.0f, animation03.FadeLength);
            Assert.AreEqual(0.8f, animation03.Weight);
            Assert.AreEqual(0.2f, animation03.PreGap);
            Assert.AreEqual("upper animation", animation03.Description);
            Assert.AreEqual(3.2f, animation03.Length);

            Assert.AreEqual(2, animationRequest.BaseLayerAnimations.Count);
            Assert.AreEqual("Smile", animationRequest.BaseLayerAnimations[0].Name);
            Assert.AreEqual("Angry", animationRequest.BaseLayerAnimations[1].Name);

            // With params
            var animations = new Dictionary<string, List<Model.Animation>>()
            {
                {
                    string.Empty, new List<Model.Animation>()
                    {
                        new Model.Animation("Smile", null, 0.1f, 0.2f, 0.3f, 0.4f, "smile animation"),
                        new Model.Animation("Angry", null, 0.1f, 0.2f, 0.3f, 0.4f, "angry animation")
                    }
                },
                {
                    "UpperBody", new List<Model.Animation>()
                    {
                        new Model.Animation("WaveHands", "UpperBody", 0.1f, 0.2f, 0.3f, 0.4f, "wave animation")
                    }
                }
            };
            var animationRequestP = new AnimationRequest(animations, false, false, false, "Base Layer");
            Assert.AreEqual(2, animationRequestP.Animations.Count);
            Assert.AreEqual(2, animationRequestP.Animations[string.Empty].Count);   // not "Base Layer"
            Assert.AreEqual(1, animationRequestP.Animations["UpperBody"].Count);
            Assert.AreEqual(2, animationRequestP.BaseLayerAnimations.Count);
            Assert.IsFalse(animationRequestP.StartIdlingOnEnd);
            Assert.IsFalse(animationRequestP.StopIdlingOnStart);
            Assert.IsFalse(animationRequestP.StopLayeredAnimations);
            Assert.AreEqual("Base Layer", animationRequestP.BaseLayerName);

            // Others
            var animationRequestO1 = new AnimationRequest(baseLayerName: "Base Layer");
            animationRequestO1.AddAnimation("Smile");
            Assert.AreEqual(1, animationRequestO1.Animations["Base Layer"].Count);  // "Base Layer" when added after instancing

            var animationRequestO2 = new AnimationRequest();
            Assert.AreEqual(string.Empty, animationRequestO2.BaseLayerName);
            animationRequestO2.AddAnimation("Smile", "Dummy Layer");
            Assert.AreEqual("Dummy Layer", animationRequestO2.BaseLayerName);
        }

        [Test]
        public void TestAnimatedVoiceRequest()
        {
            var animatedVoiceRequest = new AnimatedVoiceRequest();
            Assert.AreEqual(new Dictionary<string, List<AnimatedVoice>>(), animatedVoiceRequest.AnimatedVoices);
            Assert.IsTrue(animatedVoiceRequest.StartIdlingOnEnd);
            Assert.IsTrue(animatedVoiceRequest.StopIdlingOnStart);
            Assert.IsTrue(animatedVoiceRequest.StopLayeredAnimations);
            Assert.AreEqual(string.Empty, animatedVoiceRequest.BaseLayerName);

            // 1st frame
            animatedVoiceRequest.AddAnimation("Walk");
            animatedVoiceRequest.AddAnimation("Run", 2.0f, 1.0f, 0.5f, 0.1f);
            animatedVoiceRequest.AddAnimation("WaveHands", "UpperBody", 3.0f, 2.0f, 0.8f, 0.2f, "upper animation");
            animatedVoiceRequest.AddVoice("Hello");
            animatedVoiceRequest.AddVoice("Goodby", 0.1f, 0.2f);
            animatedVoiceRequest.AddFace("Neutral");
            animatedVoiceRequest.AddFace("Default", 0.1f, "default face");

            // 2nd frame
            animatedVoiceRequest.AddAnimation("Jump", asNewFrame: true);
            animatedVoiceRequest.AddVoiceWeb("https://voice.local/goodmorning");
            animatedVoiceRequest.AddVoiceWeb("https://voice.local/goodafternoon", 0.1f, 0.2f);
            animatedVoiceRequest.AddFace("Cry");

            // 3rd frame
            var ttsConfig = new TTSConfiguration("TestTTSFuncName");
            ttsConfig.Params.Add("key1", "val1");
            ttsConfig.Params.Add("key2", 2.0f);
            animatedVoiceRequest.AddVoiceTTS("Good afternoon.", asNewFrame: true);
            animatedVoiceRequest.AddVoiceTTS("Good evening.", 0.1f, 0.2f, ttsConfig: ttsConfig);
            animatedVoiceRequest.AddAnimation("HandsFront");
            animatedVoiceRequest.AddFace("Jito");

            // 4th frame
            animatedVoiceRequest.AddFace("Surprise", asNewFrame: true);
            animatedVoiceRequest.AddVoice("GoodNight");
            animatedVoiceRequest.AddAnimation("HandsBack");

            // 1st frame
            var animation0101 = animatedVoiceRequest.AnimatedVoices[0].Animations[string.Empty][0];
            Assert.AreEqual("Walk", animation0101.Name);
            Assert.AreEqual(string.Empty, animation0101.LayerName);
            Assert.AreEqual(0.0f, animation0101.Duration);
            Assert.AreEqual(-1.0f, animation0101.FadeLength);
            Assert.AreEqual(1.0f, animation0101.Weight);
            Assert.AreEqual(0.0f, animation0101.PreGap);
            Assert.IsNull(animation0101.Description);
            Assert.AreEqual(0.0f, animation0101.Length);

            var animation0102 = animatedVoiceRequest.AnimatedVoices[0].Animations[string.Empty][1];
            Assert.AreEqual("Run", animation0102.Name);
            Assert.AreEqual(string.Empty, animation0102.LayerName);
            Assert.AreEqual(2.0f, animation0102.Duration);
            Assert.AreEqual(1.0f, animation0102.FadeLength);
            Assert.AreEqual(0.5f, animation0102.Weight);
            Assert.AreEqual(0.1f, animation0102.PreGap);
            Assert.IsNull(animation0102.Description);
            Assert.AreEqual(2.1f, animation0102.Length);

            var animation0103 = animatedVoiceRequest.AnimatedVoices[0].Animations["UpperBody"][0];
            Assert.AreEqual("WaveHands", animation0103.Name);
            Assert.AreEqual("UpperBody", animation0103.LayerName);
            Assert.AreEqual(3.0f, animation0103.Duration);
            Assert.AreEqual(2.0f, animation0103.FadeLength);
            Assert.AreEqual(0.8f, animation0103.Weight);
            Assert.AreEqual(0.2f, animation0103.PreGap);
            Assert.AreEqual("upper animation", animation0103.Description);
            Assert.AreEqual(3.2f, animation0103.Length);

            var voice0101 = animatedVoiceRequest.AnimatedVoices[0].Voices[0];
            Assert.AreEqual("Hello", voice0101.Name);
            Assert.AreEqual(0.0f, voice0101.PreGap);
            Assert.AreEqual(0.0f, voice0101.PostGap);
            Assert.IsNull(voice0101.Text);
            Assert.IsNull(voice0101.Url);
            Assert.IsNull(voice0101.TTSConfig);
            Assert.AreEqual(string.Empty, voice0101.GetTTSFunctionName());
            Assert.IsNull(voice0101.GetTTSParam("key1"));
            Assert.AreEqual(VoiceSource.Local, voice0101.Source);
            Assert.IsFalse(voice0101.UseCache);

            var voice0102 = animatedVoiceRequest.AnimatedVoices[0].Voices[1];
            Assert.AreEqual("Goodby", voice0102.Name);
            Assert.AreEqual(0.1f, voice0102.PreGap);
            Assert.AreEqual(0.2f, voice0102.PostGap);
            Assert.IsNull(voice0102.Text);
            Assert.IsNull(voice0102.Url);
            Assert.IsNull(voice0102.TTSConfig);
            Assert.AreEqual(string.Empty, voice0102.GetTTSFunctionName());
            Assert.IsNull(voice0102.GetTTSParam("key1"));
            Assert.AreEqual(VoiceSource.Local, voice0102.Source);
            Assert.IsFalse(voice0102.UseCache);

            var face0101 = animatedVoiceRequest.AnimatedVoices[0].Faces[0];
            Assert.AreEqual("Neutral", face0101.Name);
            Assert.AreEqual(0.0f, face0101.Duration);
            Assert.IsNull(face0101.Description);

            var face0102 = animatedVoiceRequest.AnimatedVoices[0].Faces[1];
            Assert.AreEqual("Default", face0102.Name);
            Assert.AreEqual(0.1f, face0102.Duration);
            Assert.AreEqual("default face", face0102.Description);

            // 2nd frame
            var animation0201 = animatedVoiceRequest.AnimatedVoices[1].Animations[string.Empty][0];
            Assert.AreEqual("Jump", animation0201.Name);

            var voice0201 = animatedVoiceRequest.AnimatedVoices[1].Voices[0];
            Assert.AreEqual(string.Empty, voice0201.Name);
            Assert.AreEqual(0.0f, voice0201.PreGap);
            Assert.AreEqual(0.0f, voice0201.PostGap);
            Assert.IsNull(voice0201.Text);
            Assert.AreEqual("https://voice.local/goodmorning", voice0201.Url);
            Assert.IsNull(voice0201.TTSConfig);
            Assert.AreEqual(string.Empty, voice0201.GetTTSFunctionName());
            Assert.IsNull(voice0201.GetTTSParam("key1"));
            Assert.AreEqual(VoiceSource.Web, voice0201.Source);
            Assert.IsTrue(voice0201.UseCache);
            Assert.AreEqual("https://voice.local/goodmorning", voice0201.CacheKey);

            var voice0202 = animatedVoiceRequest.AnimatedVoices[1].Voices[1];
            Assert.AreEqual(string.Empty, voice0202.Name);
            Assert.AreEqual(0.1f, voice0202.PreGap);
            Assert.AreEqual(0.2f, voice0202.PostGap);
            Assert.IsNull(voice0202.Text);
            Assert.AreEqual("https://voice.local/goodafternoon", voice0202.Url);
            Assert.IsNull(voice0202.TTSConfig);
            Assert.AreEqual(string.Empty, voice0202.GetTTSFunctionName());
            Assert.IsNull(voice0202.GetTTSParam("key1"));
            Assert.AreEqual(VoiceSource.Web, voice0202.Source);
            Assert.IsTrue(voice0202.UseCache);
            Assert.AreEqual("https://voice.local/goodafternoon", voice0202.CacheKey);

            var face0201 = animatedVoiceRequest.AnimatedVoices[1].Faces[0];
            Assert.AreEqual("Cry", face0201.Name);

            // 3rd frame
            var voice0301 = animatedVoiceRequest.AnimatedVoices[2].Voices[0];
            Assert.AreEqual(string.Empty, voice0301.Name);
            Assert.AreEqual(0.0f, voice0301.PreGap);
            Assert.AreEqual(0.0f, voice0301.PostGap);
            Assert.AreEqual("Good afternoon.", voice0301.Text);
            Assert.AreEqual(string.Empty, voice0301.Url);
            Assert.IsNull(voice0301.TTSConfig);
            Assert.AreEqual(VoiceSource.TTS, voice0301.Source);
            Assert.IsTrue(voice0301.UseCache);

            var voice0302 = animatedVoiceRequest.AnimatedVoices[2].Voices[1];
            Assert.AreEqual(string.Empty, voice0302.Name);
            Assert.AreEqual(0.1f, voice0302.PreGap);
            Assert.AreEqual(0.2f, voice0302.PostGap);
            Assert.AreEqual("Good evening.", voice0302.Text);
            Assert.AreEqual(string.Empty, voice0302.Url);
            Assert.AreEqual("TestTTSFuncName", voice0302.GetTTSFunctionName());
            Assert.AreEqual("val1", voice0302.GetTTSParam("key1"));
            Assert.AreEqual(2.0f, voice0302.GetTTSParam("key2"));
            Assert.AreEqual(VoiceSource.TTS, voice0302.Source);
            Assert.IsTrue(voice0302.UseCache);

            var animation0301 = animatedVoiceRequest.AnimatedVoices[2].Animations[string.Empty][0];
            Assert.AreEqual("HandsFront", animation0301.Name);

            var face0301 = animatedVoiceRequest.AnimatedVoices[2].Faces[0];
            Assert.AreEqual("Jito", face0301.Name);

            // 4th frame
            var face0401 = animatedVoiceRequest.AnimatedVoices[3].Faces[0];
            Assert.AreEqual("Surprise", face0401.Name);

            var voice0401 = animatedVoiceRequest.AnimatedVoices[3].Voices[0];
            Assert.AreEqual("GoodNight", voice0401.Name);

            var animation0401 = animatedVoiceRequest.AnimatedVoices[3].Animations[string.Empty][0];
            Assert.AreEqual("HandsBack", animation0401.Name);
        }
    }
}

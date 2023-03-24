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
            var cdk = GameObject.Find(Constants.ChatdollModelName);
            if (cdk == null)
            {
                Debug.LogError($"{Constants.ChatdollModelName} not found");
                return;
            }

            var model = cdk.GetComponent<ModelController>();
            if (model == null)
            {
                Debug.LogError("ModelController not found");
                return;
            }
            var skinnedMeshRenderer = model.SkinnedMeshRenderer;

            var weights = new Dictionary<string, float>();
            weights.Add("mouth_aa", 0.5f);
            weights.Add("mouth_:D", 0.3f);
            weights.Add("eyes_close_1", 1.0f);

            var face = new FaceClip("Smile", skinnedMeshRenderer, weights);

            Assert.AreEqual("Smile", face.Name);
            Assert.Greater(face.Values.Count, 30);
            var configuredWeightCount = 0;
            foreach (var w in face.Values)
            {
                if (w.Name == "mouth_aa")
                {
                    Assert.AreEqual(0.5f, w.Weight);
                    configuredWeightCount++;
                }
                else if (w.Name == "mouth_:D")
                {
                    Assert.AreEqual(0.3f, w.Weight);
                    configuredWeightCount++;
                }
                else if (w.Name == "eyes_close_1")
                {
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
            var animation = new Model.Animation("BaseParam", 6, 3.0f, "additiveAnim", "Additive Layer");
            Assert.AreEqual("BaseParam", animation.ParameterKey);
            Assert.AreEqual(6, animation.ParameterValue);
            Assert.AreEqual(3.0f, animation.Duration);
            Assert.AreEqual("additiveAnim", animation.LayeredAnimationName);
            Assert.AreEqual("Additive Layer", animation.LayeredAnimationLayerName);
        }

        [Test]
        public void TestAnimatedVoiceRequest()
        {
            var animatedVoiceRequest = new AnimatedVoiceRequest();
            Assert.AreEqual(new Dictionary<string, List<AnimatedVoice>>(), animatedVoiceRequest.AnimatedVoices);
            Assert.IsTrue(animatedVoiceRequest.StartIdlingOnEnd);
            Assert.IsTrue(animatedVoiceRequest.StopIdlingOnStart);

            // 1st frame
            animatedVoiceRequest.AddAnimation("BaseParam", 1);
            animatedVoiceRequest.AddAnimation("BaseParam", 2, 2.0f);
            animatedVoiceRequest.AddAnimation("BaseParam", 3, 3.0f, "WaveHands", "Additive Layer");
            animatedVoiceRequest.AddVoice("Hello");
            animatedVoiceRequest.AddVoice("Goodby", 0.1f, 0.2f);
            animatedVoiceRequest.AddFace("Neutral");
            animatedVoiceRequest.AddFace("Smile", 0.1f, "smile face");

            // 2nd frame
            animatedVoiceRequest.AddAnimation("BaseParam", 4, asNewFrame: true);
            animatedVoiceRequest.AddVoiceWeb("https://voice.local/goodmorning");
            animatedVoiceRequest.AddVoiceWeb("https://voice.local/goodafternoon", 0.1f, 0.2f);
            animatedVoiceRequest.AddFace("Cry");

            // 3rd frame
            var ttsConfig = new TTSConfiguration("TestTTSFuncName");
            ttsConfig.Params.Add("key1", "val1");
            ttsConfig.Params.Add("key2", 2.0f);
            animatedVoiceRequest.AddVoiceTTS("Good afternoon.", asNewFrame: true);
            animatedVoiceRequest.AddVoiceTTS("Good evening.", 0.1f, 0.2f, ttsConfig: ttsConfig);
            animatedVoiceRequest.AddAnimation("BaseParam", 5);
            animatedVoiceRequest.AddFace("Jito");

            // 4th frame
            animatedVoiceRequest.AddFace("Surprise", asNewFrame: true);
            animatedVoiceRequest.AddVoice("GoodNight");
            animatedVoiceRequest.AddAnimation("BaseParam", 6);

            // 1st frame
            var animation0101 = animatedVoiceRequest.AnimatedVoices[0].Animations[0];
            Assert.AreEqual("BaseParam", animation0101.ParameterKey);
            Assert.AreEqual(1, animation0101.ParameterValue);
            Assert.AreEqual(0.0f, animation0101.Duration);
            Assert.IsNull(animation0101.LayeredAnimationName);
            Assert.IsNull(animation0101.LayeredAnimationLayerName);

            var animation0102 = animatedVoiceRequest.AnimatedVoices[0].Animations[1];
            Assert.AreEqual("BaseParam", animation0102.ParameterKey);
            Assert.AreEqual(2, animation0102.ParameterValue);
            Assert.AreEqual(2.0f, animation0102.Duration);
            Assert.IsNull(animation0102.LayeredAnimationName);
            Assert.IsNull(animation0102.LayeredAnimationLayerName);

            var animation0103 = animatedVoiceRequest.AnimatedVoices[0].Animations[2];
            Assert.AreEqual("BaseParam", animation0103.ParameterKey);
            Assert.AreEqual(3, animation0103.ParameterValue);
            Assert.AreEqual(3.0f, animation0103.Duration);
            Assert.AreEqual("WaveHands", animation0103.LayeredAnimationName);
            Assert.AreEqual("Additive Layer", animation0103.LayeredAnimationLayerName);

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
            Assert.AreEqual("Smile", face0102.Name);
            Assert.AreEqual(0.1f, face0102.Duration);
            Assert.AreEqual("smile face", face0102.Description);

            // 2nd frame
            var animation0201 = animatedVoiceRequest.AnimatedVoices[1].Animations[0];
            Assert.AreEqual(4, animation0201.ParameterValue);

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

            var animation0301 = animatedVoiceRequest.AnimatedVoices[2].Animations[0];
            Assert.AreEqual(5, animation0301.ParameterValue);

            var face0301 = animatedVoiceRequest.AnimatedVoices[2].Faces[0];
            Assert.AreEqual("Jito", face0301.Name);

            // 4th frame
            var face0401 = animatedVoiceRequest.AnimatedVoices[3].Faces[0];
            Assert.AreEqual("Surprise", face0401.Name);

            var voice0401 = animatedVoiceRequest.AnimatedVoices[3].Voices[0];
            Assert.AreEqual("GoodNight", voice0401.Name);

            var animation0401 = animatedVoiceRequest.AnimatedVoices[3].Animations[0];
            Assert.AreEqual(6, animation0401.ParameterValue);
        }
    }
}

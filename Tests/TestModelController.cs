using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ChatdollKit.Model;

namespace ChatdollKit.Tests
{
    public class TestModelController
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (SceneManager.GetActiveScene().name != Constants.SceneName)
            {
                SceneManager.LoadScene(Constants.SceneName);
                yield return null;
            }
        }

        [UnityTest, Timeout(100000), Order(1)]
        public IEnumerator TestAnimatedSay()
        {
            var model = GameObject.Find(Constants.ChatdollModelName)?.GetComponent<ModelController>();
            if (model == null)
            {
                Debug.LogError("Chatdoll model not found");
                yield break;
            }

            // Normal case
            var animatedVoiceRequest = new AnimatedVoiceRequest();

            // 1st frame
            animatedVoiceRequest.AddVoiceTTS("これはアニメーションと声、表情のテストです。", description: "voice0101");
            animatedVoiceRequest.AddAnimation("AGIA_Idle_classy_01_left_hand_on_waist", description: "animation0101");
            animatedVoiceRequest.AddFace("LittleSmile", description: "face0101");

            // 2nd frame
            animatedVoiceRequest.AddVoiceTTS("アニメーションと表情が途中で切り替わります。瞬きがとまっていることを確認してください。", description: "voice0201", postGap: 3.0f, asNewFrame: true);
            animatedVoiceRequest.AddAnimation("AGIA_Idle_calm_02_hands_on_front", duration: 2.0f, description: "animation0201");
            animatedVoiceRequest.AddAnimation("AGIA_Idle_calm_01_hands_on_back", description: "animation0202");
            animatedVoiceRequest.AddFace("Smile", duration: 3.0f, description: "face0201");
            animatedVoiceRequest.AddFace("Neutral", description: "face0202");

            var recorder = new ActionHistoryRecorder(true);
            model.History = recorder;
            var animTask = model.AnimatedSay(animatedVoiceRequest, CancellationToken.None);

            // Blink is disabled while talking
            yield return new WaitForSeconds(1.0f);  // Wait 1sec to ensure start
            Assert.IsFalse(model.IsBlinkEnabled);

            // Wait for end
            yield return new WaitUntil(() => animTask.IsCompleted);

            // Confirm blink started
            yield return new WaitForSeconds(0.1f);
            Assert.IsTrue(model.IsBlinkEnabled);

            //var startTick = animationStartHistory.Timestamp;
            Assert.LessOrEqual(recorder.GetGapOfHistories("animation0101", "face0101"), 1);
            Assert.LessOrEqual(recorder.GetGapOfHistories("animation0101", "voice0101"), 1000);  // 1000 = start delay by download tts :(

            // Start at the same time
            Assert.LessOrEqual(recorder.GetGapOfHistories("animation0201", "face0201"), 1);
            Assert.LessOrEqual(recorder.GetGapOfHistories("animation0201", "voice0201"), 1000);  // 1000 = start delay by download tts :(
            
            // Duration
            Assert.GreaterOrEqual(recorder.GetGapOfHistories("animation0201", "animation0202"), 2000);
            Assert.LessOrEqual(recorder.GetGapOfHistories("animation0201", "animation0202"), 2100);
            Assert.GreaterOrEqual(recorder.GetGapOfHistories("face0201", "face0202"), 3000);
            Assert.LessOrEqual(recorder.GetGapOfHistories("face0201", "face0202"), 3100);

            // Interval to next case (idling)
            yield return new WaitForSeconds(5.0f);
        }

        [UnityTest, Timeout(100000), Order(2)]
        public IEnumerator TestAnimatedSayBlinking()
        {
            var model = GameObject.Find(Constants.ChatdollModelName)?.GetComponent<ModelController>();
            if (model == null)
            {
                Debug.LogError("Chatdoll model not found");
                yield break;
            }

            var animatedVoiceRequestNoBlink = new AnimatedVoiceRequest(disableBlink: false);
            animatedVoiceRequestNoBlink.AddVoiceTTS("これはまばたきをしながら、発話やアニメーションをするテストです。まばたきしていることを確認してください。");
            animatedVoiceRequestNoBlink.AddAnimation("AGIA_Idle_classy_01_left_hand_on_waist");
            animatedVoiceRequestNoBlink.AddFace("LittleSmile");

            // Blinking before start
            Assert.IsTrue(model.IsBlinkEnabled);
            var animNBTask = model.AnimatedSay(animatedVoiceRequestNoBlink, CancellationToken.None);
            yield return new WaitForSeconds(1.0f);
            // Continue blinking while speaking
            Assert.IsTrue(model.IsBlinkEnabled);
            yield return new WaitUntil(() => animNBTask.IsCompleted);

            // Interval to next case (idling)
            yield return new WaitForSeconds(5.0f);
        }

        [UnityTest, Timeout(100000), Order(3)]
        public IEnumerator TestAnimatedSayCancel()
        {
            var model = GameObject.Find(Constants.ChatdollModelName)?.GetComponent<ModelController>();
            if (model == null)
            {
                Debug.LogError("Chatdoll model not found");
                yield break;
            }

            var cts = new CancellationTokenSource();
            var animatedVoiceRequestToCancel = new AnimatedVoiceRequest();
            animatedVoiceRequestToCancel.AddVoiceTTS("この話は途中でキャンセルされます。アニメーションも止まることを確認してください。そのため、最後まで聴けてしまったらテストは失敗ということになります。");
            animatedVoiceRequestToCancel.AddAnimation("AGIA_Idle_classy_01_left_hand_on_waist");
            animatedVoiceRequestToCancel.AddFace("Smile");

            var animCancelTask = model.AnimatedSay(animatedVoiceRequestToCancel, cts.Token);

            // Cancel after continue animation 3 seconds
            yield return new WaitForSeconds(5.0f);
            cts.Cancel();
            model.StopSpeech(); // Speech doesn't stop by canceling
            yield return new WaitForSeconds(1.0f);
            // Confirm that animation task is stopped
            Assert.IsTrue(animCancelTask.IsCompleted);

            // Interval to next case (idling)
            yield return new WaitForSeconds(5.0f);
        }
    }
}

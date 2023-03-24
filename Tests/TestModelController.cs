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
            animatedVoiceRequest.AddAnimation("BaseParam", 7, 10.0f);
            //animatedVoiceRequest.AddFace("LittleSmile", description: "face0101");

            // 2nd frame
            animatedVoiceRequest.AddVoiceTTS("アニメーションと表情が途中で切り替わります。", description: "voice0201", postGap: 3.0f, asNewFrame: true);
            animatedVoiceRequest.AddAnimation("BaseParam", 5, 2.0f);
            animatedVoiceRequest.AddAnimation("BaseParam", 4, 2.0f);
            //animatedVoiceRequest.AddFace("Smile", duration: 3.0f, description: "face0201");
            //animatedVoiceRequest.AddFace("Neutral", description: "face0202");

            var animTask = model.AnimatedSay(animatedVoiceRequest, CancellationToken.None);

            // Wait for end
            yield return new WaitUntil(() => animTask.GetAwaiter().IsCompleted);

            // Interval to next case (idling)
            yield return new WaitForSeconds(5.0f);
        }

        [UnityTest, Timeout(100000), Order(2)]
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
            animatedVoiceRequestToCancel.AddAnimation("BaseParam", 10, 20.0f);
            //animatedVoiceRequestToCancel.AddFace("Smile");

            var animCancelTask = model.AnimatedSay(animatedVoiceRequestToCancel, cts.Token);

            // Cancel after continue animation 3 seconds
            yield return new WaitForSeconds(5.0f);
            cts.Cancel();
            model.StopSpeech(); // Speech doesn't stop by canceling
            yield return new WaitForSeconds(1.0f);
            // Confirm that animation task is stopped
            Assert.IsTrue(animCancelTask.GetAwaiter().IsCompleted);

            // Interval to next case (idling)
            yield return new WaitForSeconds(5.0f);
        }
    }
}

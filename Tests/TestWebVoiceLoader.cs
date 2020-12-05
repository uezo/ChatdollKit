using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ChatdollKit.Model;

namespace ChatdollKit.Tests
{
    public class TestWebVoiceLoader
    {
        [SetUp]
        public void SetUp()
        {
            if (SceneManager.GetActiveScene().name != Constants.SceneName)
            {
                SceneManager.LoadScene(Constants.SceneName);
            }
        }

        [UnityTest]
        public IEnumerator TestGetAudioClipAsync()
        {
            var webVoiceLoader = GameObject.Find(Constants.ChatdollModelName)?.AddComponent<WebVoiceLoader>();
            if (webVoiceLoader == null)
            {
                Debug.LogError("WebVoiceLoader not found");
                yield break;
            }

            var voice = new Voice(null, 0.0f, 0.0f, null, Constants.WebVoiceUri + "?test=TestGetAudioClipAsync", null, VoiceSource.Web, true, string.Empty);
            // Start download
            var task = webVoiceLoader.GetAudioClipAsync(voice, CancellationToken.None);
            Assert.IsTrue(webVoiceLoader.IsLoading(voice));

            // Wait for complete
            yield return new WaitWhile(() => webVoiceLoader.IsLoading(voice));

            // Assert
            Assert.IsInstanceOf<AudioClip>(task.Result);
            Assert.IsTrue(webVoiceLoader.HasCache(voice));
        }

        [UnityTest]
        public IEnumerator TestGetAudioClipAsyncCancel()
        {
            var webVoiceLoader = GameObject.Find(Constants.ChatdollModelName)?.AddComponent<WebVoiceLoader>();
            if (webVoiceLoader == null)
            {
                Debug.LogError("WebVoiceLoader not found");
                yield break;
            }

            var voice = new Voice(null, 0.0f, 0.0f, null, Constants.WebVoiceUri + "?test=TestGetAudioClipAsyncCancel", null, VoiceSource.Web, true, string.Empty);
            var cts = new CancellationTokenSource();

            // Start download
            var task = webVoiceLoader.GetAudioClipAsync(voice, cts.Token);
            Assert.IsTrue(webVoiceLoader.IsLoading(voice));

            // Cancel
            cts.Cancel();

            // Wait a bit to stop downloading
            yield return new WaitForSeconds(0.1f);

            // Assert
            Assert.IsNull(task.Result);
            Assert.IsTrue(webVoiceLoader.IsLoading(voice)); // download doesn't stop after cancel
            Assert.IsFalse(webVoiceLoader.HasCache(voice));
        }

        [UnityTest]
        public IEnumerator TestGetAudioClipAsyncTimeout()
        {
            var webVoiceLoader = GameObject.Find(Constants.ChatdollModelName)?.AddComponent<WebVoiceLoader>();
            if (webVoiceLoader == null)
            {
                Debug.LogError("WebVoiceLoader not found");
                yield break;
            }

            var voice = new Voice(null, 0.0f, 0.0f, null, Constants.WebVoiceUri + "?test=TestGetAudioClipAsyncTimeout", null, VoiceSource.Web, true, string.Empty);
            webVoiceLoader.Timeout = 0; // Timeout immediately

            // Start download
            var task = webVoiceLoader.GetAudioClipAsync(voice, CancellationToken.None);

            // Wait for complete download (doesn't stop after timeout)
            yield return new WaitWhile(() => webVoiceLoader.IsLoading(voice));

            // Assert
            Assert.IsFalse(webVoiceLoader.HasCache(voice)); // downloaded but not cached when timeout
        }
    }
}

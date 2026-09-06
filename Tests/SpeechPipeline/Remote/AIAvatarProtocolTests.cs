using ChatdollKit.SpeechPipeline.LLM;
using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using ChatdollKit.SpeechPipeline;
using ChatdollKit.SpeechPipeline.Remote;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline.Remote
{
    public class AIAvatarProtocolTests
    {
        [Test]
        public void StartSerializesTheWireSessionAndOwnedMetadataWithoutConnectionCredentials()
        {
            var options = new AIAvatarSpeechPipelineOptions
            {
                ApiKey = "test-credential-never-in-json", UserId = "user", SessionId = "public-session",
                ContextId = "configured-context", Metadata = JObject.Parse("{\"nested\":{\"value\":1}}")
            };
            var message = (JObject)Call("Start", options, "wire-session", "active-context");
            Assert.That((string)message["type"], Is.EqualTo("start"));
            Assert.That((string)message["session_id"], Is.EqualTo("wire-session"));
            Assert.That((string)message["user_id"], Is.EqualTo("user"));
            Assert.That((string)message["context_id"], Is.EqualTo("active-context"));
            Assert.That(message.ToString(), Does.Not.Contain(options.ApiKey));
            options.Metadata["nested"]["value"] = 2;
            Assert.That((int)message["metadata"]["nested"]["value"], Is.EqualTo(1));
            message["metadata"]["nested"]["value"] = 3;
            Assert.That((int)options.Metadata["nested"]["value"], Is.EqualTo(2));
        }

        [TestCase("accepted", SpeechPipelineResponseType.Accepted)]
        [TestCase("start", SpeechPipelineResponseType.Start)]
        [TestCase("chunk", SpeechPipelineResponseType.Chunk)]
        [TestCase("tool_call", SpeechPipelineResponseType.ToolCall)]
        [TestCase("final", SpeechPipelineResponseType.Final)]
        [TestCase("vision", SpeechPipelineResponseType.Final)]
        [TestCase("canceled", SpeechPipelineResponseType.Canceled)]
        [TestCase("cancelled", SpeechPipelineResponseType.Canceled)]
        [TestCase("error", SpeechPipelineResponseType.Error)]
        [TestCase("stop", SpeechPipelineResponseType.Stop)]
        public void ResponseMapsProtocolEventsToThePublicSessionAndTransaction(string eventType, SpeechPipelineResponseType expected)
        {
            var response = Decode(new JObject
            {
                ["type"] = eventType, ["session_id"] = "wire-session", ["transaction_id"] = "wire-turn",
                ["user_id"] = "user", ["context_id"] = "context", ["text"] = "表示", ["voice_text"] = "よみあげ",
                ["language"] = "ja"
            });
            Assert.That(response.Type, Is.EqualTo(expected));
            Assert.That(response.SessionId, Is.EqualTo("public-session"));
            Assert.That(response.TransactionId, Is.EqualTo("public-turn"));
            Assert.That(response.UserId, Is.EqualTo("user"));
            Assert.That(response.ContextId, Is.EqualTo("context"));
            Assert.That(response.Text, Is.EqualTo("表示"));
            Assert.That(response.VoiceText, Is.EqualTo("よみあげ"));
            Assert.That(response.Language, Is.EqualTo("ja"));
            Assert.That(response.AudioData, Is.Null);
            if (eventType == "vision") Assert.That((string)response.Metadata["remote_event_type"], Is.EqualTo("vision"));
        }

        [Test]
        public void ResponsePreservesIndependentMetadataControlsAndStructuredContent()
        {
            var message = JObject.Parse("{\"type\":\"chunk\",\"metadata\":{\"nested\":{\"value\":1}},"
                + "\"control_tags\":{\"face\":\"smile\"},\"avatar_control_request\":{\"animation\":19},"
                + "\"structured_content\":{\"answer\":\"original\"}}");
            var response = Decode(message);
            message["metadata"]["nested"]["value"] = 99;
            message["control_tags"]["face"] = "changed";
            message["avatar_control_request"]["animation"] = 6;
            message["structured_content"]["answer"] = "changed";
            Assert.That((int)response.Metadata["nested"]["value"], Is.EqualTo(1));
            Assert.That((string)response.Metadata["control_tags"]["face"], Is.EqualTo("smile"));
            Assert.That((int)response.Metadata["avatar_control_request"]["animation"], Is.EqualTo(19));
            Assert.That((string)response.StructuredContent["answer"], Is.EqualTo("original"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ToolArgumentsSupportBothJsonObjectsAndEncodedJsonStrings(bool asString)
        {
            var arguments = JObject.Parse("{\"location\":\"東京\",\"count\":2}");
            var response = Decode(new JObject
            {
                ["type"] = "tool_call", ["metadata"] = new JObject
                {
                    ["tool_call"] = new JObject
                    {
                        ["id"] = "call-id", ["name"] = "weather",
                        ["arguments"] = asString ? (JToken)arguments.ToString() : arguments
                    }
                }
            });
            Assert.That(response.ToolCall.Id, Is.EqualTo("call-id"));
            Assert.That(response.ToolCall.Name, Is.EqualTo("weather"));
            Assert.That(JToken.DeepEquals(JObject.Parse(response.ToolCall.Arguments), arguments), Is.True);
        }

        [Test]
        public void ResponseAudioAcceptsTheExactLimitAndRejectsEncodedAndDecodedOversize()
        {
            var audio = new byte[] { 0, 128, 255 };
            var message = new JObject { ["type"] = "chunk", ["audio_data"] = Convert.ToBase64String(audio) };
            CollectionAssert.AreEqual(audio, Decode(message, 3).AudioData);
            // Three decoded bytes and two decoded bytes both fit in four encoded characters.
            Assert.Throws<FormatException>(() => Decode(message, 2));
            message["audio_data"] = Convert.ToBase64String(new byte[4]);
            Assert.Throws<FormatException>(() => Decode(message, 3));
            message["audio_data"] = "not-base64!";
            Assert.Throws<FormatException>(() => Decode(message));
            message["audio_data"] = string.Empty;
            Assert.That(Decode(message).AudioData, Is.Null);
        }

        [Test]
        public void RawPcmChunksAndUnknownEventsFailExplicitly()
        {
            var message = JObject.Parse("{\"type\":\"chunk\",\"metadata\":{\"pcm_format\":{\"sample_rate\":16000}}}");
            Assert.Throws<NotSupportedException>(() => Decode(message));
            message["metadata"]["pcm_format"] = null;
            Assert.That(Decode(message).Type, Is.EqualTo(SpeechPipelineResponseType.Chunk));
            Assert.Throws<FormatException>(() => Decode(new JObject { ["type"] = "unknown-event" }));
        }

        private static SpeechPipelineResponse Decode(JObject message, int maxAudioBytes = 1024) =>
            (SpeechPipelineResponse)Call("Response", message, "public-session", "public-turn", maxAudioBytes);

        // Keep the wire codec internal while exercising the same implementation from the Unity test assembly.
        private static object Call(string methodName, params object[] arguments)
        {
            var type = typeof(IAIAvatarConnection).Assembly.GetType("ChatdollKit.SpeechPipeline.Remote.AIAvatarProtocol", true);
            var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            try { return method.Invoke(null, arguments); }
            catch (TargetInvocationException error)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
    }
}

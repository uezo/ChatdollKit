using System.Linq;
using ChatdollKit.Avatar;
using ChatdollKit.Orchestration;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChatdollKit.Tests.Orchestration
{
    public class AvatarControlParserTests
    {
        [Test]
        public void BracketControlsPreserveOrderAndBothAnimationSpellings()
        {
            var controls = AvatarControlParser.Parse("[face:笑顔]Hi[anim:wave][animation:nod][unknown:ignored]");
            CollectionAssert.AreEqual(new[] { "笑顔", "wave", "nod" }, controls.Select(control => control.Name));
            Assert.That(controls[0].Kind, Is.EqualTo(AvatarControlKind.Face));
            Assert.That(controls[1].Kind, Is.EqualTo(AvatarControlKind.Animation));
            Assert.That(controls.All(control => control.DurationSeconds == null), Is.True);
        }

        [Test]
        public void XmlControlsReadQuotedAttributesAndInvariantDurations()
        {
            var controls = AvatarControlParser.Parse("<face duration='1.25' name=\"smile\"/><animation name='wave' duration=\"2.5\">hello</animation>");
            Assert.That(controls.Count, Is.EqualTo(2));
            Assert.That(controls[0].Name, Is.EqualTo("smile"));
            Assert.That(controls[0].DurationSeconds, Is.EqualTo(1.25));
            Assert.That(controls[1].DurationSeconds, Is.EqualTo(2.5));
        }

        [TestCase("<face name='a' name='b'>")]
        [TestCase("<face name=unquoted>")]
        [TestCase("<face name='a' duration='-1'>")]
        [TestCase("<face name='a' duration='NaN'>")]
        [TestCase("<animation name='a' duration='Infinity'>")]
        [TestCase("<face name='a' garbage>")]
        public void MalformedControlDoesNotSuppressLaterValidControl(string malformed)
        {
            var controls = AvatarControlParser.Parse(malformed + "[face:valid]");
            Assert.That(controls.Single().Name, Is.EqualTo("valid"));
        }

        [Test]
        public void NormalizedMetadataTakesPrecedenceOverTextTags()
        {
            var metadata = JObject.Parse("{\"control_tags\":[{\"name\":\"face\",\"attributes\":{\"name\":\"resolved\",\"duration\":2}},{\"name\":\"artifact\",\"attributes\":{\"name\":\"ignored\"}}]}");
            var controls = AvatarControlParser.Parse("[face:raw]", metadata);
            Assert.That(controls.Single().Name, Is.EqualTo("resolved"));
            Assert.That(controls[0].DurationSeconds, Is.EqualTo(2));
        }

        [Test]
        public void LegacyAvatarMetadataIsSupportedAndInvalidMetadataFallsBackToText()
        {
            var metadata = JObject.Parse("{\"avatar_control_request\":{\"face_name\":\"smile\",\"face_duration\":4,\"animation_name\":\"wave\",\"animation_duration\":3}}");
            CollectionAssert.AreEqual(new[] { "smile", "wave" }, AvatarControlParser.Parse(null, metadata).Select(item => item.Name));
            Assert.That(AvatarControlParser.Parse("[anim:nod]", JObject.Parse("{\"control_tags\":[null,3,{}]}" )).Single().Name, Is.EqualTo("nod"));
        }

    }
}

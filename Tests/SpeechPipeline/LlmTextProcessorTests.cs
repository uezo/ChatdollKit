using System;
using System.Collections.Generic;
using System.Linq;
using ChatdollKit.SpeechPipeline.LLM;
using NUnit.Framework;

namespace ChatdollKit.Tests.SpeechPipeline
{
    public class LlmTextProcessorTests
    {
        private static List<LlmResponse> Process(LlmTextProcessor processor, params string[] chunks)
        {
            var responses = new List<LlmResponse>();
            foreach (string chunk in chunks) responses.AddRange(processor.Append(chunk));
            responses.AddRange(processor.Complete());
            return responses;
        }

        private static string Text(IEnumerable<LlmResponse> responses) => string.Concat(responses.Select(response => response.Text));
        private static string Voice(IEnumerable<LlmResponse> responses) => string.Concat(responses.Select(response => response.VoiceText));

        [Test]
        public void DefaultSentenceBoundariesRetainPunctuationAndContextId()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions(), "context");
            var responses = Process(processor, "こんにちは。元気？はい！Next. Done?Yes!\n");
            CollectionAssert.AreEqual(new[] { "こんにちは。", "元気？", "はい！", "Next. ", "Done?", "Yes!\n" }, responses.Select(response => response.Text));
            Assert.That(responses.All(response => response.ContextId == "context"), Is.True);
            Assert.That(processor.Text, Is.EqualTo("こんにちは。元気？はい！Next. Done?Yes!\n"));
        }

        [Test]
        public void MultiCharacterSentenceDelimiterCanCrossDeltaBoundary()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions(), "context");
            Assert.That(processor.Append("Hello."), Is.Empty);
            Assert.That(processor.Text, Is.EqualTo("Hello."));
            var responses = new List<LlmResponse>(processor.Append(" Next."));
            Assert.That(responses.Single().Text, Is.EqualTo("Hello. "));
            responses.AddRange(processor.Complete());
            CollectionAssert.AreEqual(new[] { "Hello. ", "Next." }, responses.Select(response => response.Text));
        }

        [Test]
        public void TextWithoutBoundaryWaitsUntilComplete()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions(), "context");
            Assert.That(processor.Append("hello"), Is.Empty);
            Assert.That(processor.Append(" world"), Is.Empty);
            Assert.That(processor.Text, Is.EqualTo("hello world"));
            var result = processor.Complete().Single();
            Assert.That(result.Text, Is.EqualTo("hello world"));
            Assert.That(result.VoiceText, Is.EqualTo("hello world"));
        }

        [Test]
        public void OptionSplitUsesLastDelimiterAndKeepsItsOptionalSpace()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { OptionSplitThreshold = 5 }, "context");
            var first = processor.Append("123、456、 789");
            Assert.That(first.Single().Text, Is.EqualTo("123、456、 "));
            Assert.That(processor.Complete().Single().Text, Is.EqualTo("789"));
            Assert.That(processor.Text, Is.EqualTo("123、456、 789"));
        }

        [Test]
        public void OptionSplitConsumesAdditionalWhitespaceAfterCommaSpace()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { OptionSplitThreshold = 5 }, "context");
            var responses = Process(processor, "12345,   abcde");
            CollectionAssert.AreEqual(new[] { "12345, ", "abcde" }, responses.Select(response => response.Text));
            Assert.That(processor.Text, Is.EqualTo("12345, abcde"));
        }

        [Test]
        public void OptionThresholdIsStrictAndCountsUnicodeCodePoints()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { OptionSplitThreshold = 4 }, "context");
            Assert.That(processor.Append("😀、ab"), Is.Empty, "This is four code points, despite five UTF-16 code units.");
            Assert.That(processor.Append("c").Single().Text, Is.EqualTo("😀、"));
            Assert.That(processor.Complete().Single().Text, Is.EqualTo("abc"));
        }

        [Test]
        public void ControlTagBoundaryRetainsRawTextButRemovesControlFromVoice()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions(), "context");
            var responses = Process(processor, "前半[face:happy]後半。");
            CollectionAssert.AreEqual(new[] { "前半", "[face:happy]後半。" }, responses.Select(response => response.Text));
            CollectionAssert.AreEqual(new[] { "前半", "後半。" }, responses.Select(response => response.VoiceText));
            Assert.That(processor.Text, Is.EqualTo("前半[face:happy]後半。"));
        }

        [Test]
        public void DisablingControlBoundaryStillRemovesControlsFromVoice()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { SplitOnControlTags = false }, "context");
            var result = Process(processor, "前半[face:happy]後半。").Single();
            Assert.That(result.Text, Is.EqualTo("前半[face:happy]後半。"));
            Assert.That(result.VoiceText, Is.EqualTo("前半後半。"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PunctuationInsideControlAttributesIsNeverSpoken(bool splitDelta)
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { VoiceTextTags = new[] { "ack", "answer" } }, "context");
            string prefix = "<answer><artifact type=\"presentation\" src=\"https://example.com/player?";
            string suffix = "slide=1\" />最初のスライドです。</answer>";
            var responses = splitDelta ? Process(processor, prefix, suffix) : Process(processor, prefix + suffix);
            Assert.That(Text(responses), Is.EqualTo(prefix + suffix));
            Assert.That(Voice(responses), Is.EqualTo("最初のスライドです。"));
        }

        [Test]
        public void IncompleteKnownControlTagRemainsVisibleButIsNotSpoken()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { VoiceTextTags = new[] { "answer" } }, "context");
            const string raw = "<answer>こちらです。<artifact type=\"presentation\" src=\"https://example.com?";
            var responses = Process(processor, raw);
            Assert.That(Text(responses), Is.EqualTo(raw));
            Assert.That(Voice(responses), Is.EqualTo("こちらです。"));
        }

        [Test]
        public void MathComparisonsRemainSpokenText()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { VoiceTextTags = new[] { "answer" } }, "context");
            var responses = Process(processor, "<answer>x<1かつy>1です。</answer>");
            Assert.That(Voice(responses), Is.EqualTo("x<1かつy>1です。"));
        }

        [Test]
        public void RemoveControlTagsMatchesSourceRatherThanRemovingAllMarkup()
        {
            Assert.That(LlmTextProcessor.RemoveControlTags(" [face:happy]<animation name='wave' /> Hello <answer>text</answer> "), Is.EqualTo("Hello <answer>text</answer>"));
            Assert.That(LlmTextProcessor.RemoveControlTags("前半<ARTIFACT src='unfinished"), Is.EqualTo("前半"));
            Assert.That(LlmTextProcessor.RemoveControlTags("x<1 and y>1"), Is.EqualTo("x1"),
                "Upstream's attribute-tag regex also removes tag-shaped mathematical text containing a space.");
        }

        [Test]
        public void OnlySelectedVoiceTagIsSpokenAcrossSentenceSegments()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { VoiceTextTags = new[] { "answer" } }, "context");
            const string raw = "<think>検討。</think><answer>一文目。二文目。</answer>";
            var responses = Process(processor, raw);
            Assert.That(Text(responses), Is.EqualTo(raw));
            Assert.That(Voice(responses), Is.EqualTo("一文目。二文目。"));
            Assert.That(responses[0].VoiceText, Is.Null);
        }

        [Test]
        public void MultipleSelectedVoiceTagsAreSpokenAndOtherTagsAreSilent()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { VoiceTextTags = new[] { "ack", "answer" } }, "context");
            const string raw = "<ack>了解。</ack><think>検討。</think><answer>答え。</answer>";
            var responses = Process(processor, raw);
            Assert.That(Text(responses), Is.EqualTo(raw));
            Assert.That(Voice(responses), Is.EqualTo("了解。答え。"));
        }

        [Test]
        public void MissingVoiceTagsEmitVoiceOnlyFallbackAtCompletion()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { VoiceTextTags = new[] { "answer" } }, "context");
            var initial = processor.Append("タグがありません。");
            Assert.That(initial.Single().VoiceText, Is.Null);
            var fallback = processor.Complete().Single();
            Assert.That(fallback.Text, Is.EqualTo(""));
            Assert.That(fallback.VoiceText, Is.EqualTo("タグがありません。"));
            Assert.That(processor.Text, Is.EqualTo("タグがありません。"));
        }

        [Test]
        public void TagNameSubstringSuppressesFallbackAsInUpstream()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { VoiceTextTags = new[] { "answer" } }, "context");
            var responses = Process(processor, "This answer has no markup.");
            Assert.That(responses.All(response => response.VoiceText == null), Is.True);
            Assert.That(processor.Text, Is.EqualTo("This answer has no markup."));
        }

        [Test]
        public void FirstConfiguredTagWinsWhenSeveralCompleteTagsShareOneSegment()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { VoiceTextTags = new[] { "ack", "answer" } }, "context");
            var responses = Process(processor, "<ack>a</ack><answer>b</answer>");
            Assert.That(Voice(responses), Is.EqualTo("a"));
            Assert.That(processor.Text, Is.EqualTo("<ack>a</ack><answer>b</answer>"));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TerminalTagTruncatesSameDeltaLaterDeltasAndSplitClosingTags(int mode)
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions
            {
                VoiceTextTags = new[] { "answer" }, TerminalVoiceTextTag = "answer"
            }, "context");
            string[][] chunks =
            {
                new[] { "<answer>first</answer>\n<answer>duplicate</answer>" },
                new[] { "<answer>first</answer>\n", "<answer>duplicate</answer>" },
                new[] { "<ans", "wer>first</ans", "wer>\n<answer>duplicate</answer>" }
            };
            var responses = Process(processor, chunks[mode]);
            Assert.That(Text(responses), Is.EqualTo("<answer>first</answer>"));
            Assert.That(Voice(responses), Is.EqualTo("first"));
            Assert.That(processor.Text, Is.EqualTo("<answer>first</answer>"));
        }

        [Test]
        public void NamedTerminalTagAllowsEarlierVoiceTagsAndDropsEverythingAfterItsClose()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions
            {
                VoiceTextTags = new[] { "ack", "answer" }, TerminalVoiceTextTag = "answer"
            }, "context");
            const string accepted = "<ack>了解。</ack><answer>答え。</answer>";
            var responses = Process(processor, accepted + "same delta suffix", "later delta");
            Assert.That(Text(responses), Is.EqualTo(accepted));
            Assert.That(Voice(responses), Is.EqualTo("了解。答え。"));
        }

        [Test]
        public void DisabledTerminalTagPreservesDuplicateAnswers()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { VoiceTextTags = new[] { "answer" } }, "context");
            const string raw = "<answer>first</answer>\n<answer>duplicate</answer>";
            var responses = Process(processor, raw);
            Assert.That(Text(responses), Is.EqualTo(raw));
            Assert.That(Voice(responses), Is.EqualTo("firstduplicate"));
        }

        [Test]
        public void MissingTerminalClosePreservesAllFollowingText()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions
            {
                VoiceTextTags = new[] { "answer" }, TerminalVoiceTextTag = "answer"
            }, "context");
            const string raw = "<answer>終端タグがありません。後続も保持されます。";
            var responses = Process(processor, raw);
            Assert.That(Text(responses), Is.EqualTo(raw));
            Assert.That(Voice(responses), Is.EqualTo("終端タグがありません。後続も保持されます。"));
        }

        [Test]
        public void LiteralPipesRemainSeparatorsAndDisappearFromVisibleText()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions(), "context");
            var responses = Process(processor, "a|b||c");
            CollectionAssert.AreEqual(new[] { "a", "b", "", "c" }, responses.Select(response => response.Text));
            Assert.That(processor.Text, Is.EqualTo("abc"));
            Assert.That(Voice(responses), Is.EqualTo("abc"));
        }

        [Test]
        public void OptionalSplitIgnoresDelimitersInsideAttributes()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions
            {
                OptionSplitThreshold = 5, SplitOnControlTags = false
            }, "context");
            var first = processor.Append("前文、後半<artifact src=\"a, b\" />末尾");
            Assert.That(first.Single().Text, Is.EqualTo("前文、"));
            Assert.That(processor.Complete().Single().VoiceText, Is.EqualTo("後半末尾"));
        }

        [Test]
        public void IncompleteKnownControlSuppressesOptionalSplittingUntilStreamEnd()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { OptionSplitThreshold = 3 }, "context");
            const string raw = "長い前文、後半<artifact src='unfinished";
            Assert.That(processor.Append(raw), Is.Empty);
            var result = processor.Complete().Single();
            Assert.That(result.Text, Is.EqualTo(raw));
            Assert.That(result.VoiceText, Is.EqualTo("長い前文、後半"));
        }

        [Test]
        public void EmptyDelimiterListsUseSourceDefaultsAndSettingsAreSnapshotted()
        {
            var options = new LlmServiceOptions
            {
                SplitChars = Array.Empty<string>(), OptionSplitChars = Array.Empty<string>(), VoiceTextTags = new[] { "answer" }
            };
            var processor = new LlmTextProcessor(options, "context");
            options.VoiceTextTags[0] = "other";
            options.SplitChars = new[] { "x" };
            var responses = Process(processor, "<answer>答え。</answer>");
            Assert.That(responses[0].Text, Is.EqualTo("<answer>答え。"));
            Assert.That(Voice(responses), Is.EqualTo("答え。"));
        }

        [Test]
        public void CompleteIsIdempotentAndFurtherAppendIsRejected()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions(), "context");
            processor.Append("hello");
            Assert.That(processor.Complete().Count, Is.EqualTo(1));
            Assert.That(processor.Complete(), Is.Empty);
            Assert.That(processor.Text, Is.EqualTo("hello"));
            Assert.Throws<InvalidOperationException>(() => processor.Append("later"));
        }

        [Test]
        public void FlushPreservesVoiceTagStateAndAllowsAppendingAfterToolBoundary()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { VoiceTextTags = new[] { "answer" } }, "context");
            Assert.That(processor.Append("<answer>first"), Is.Empty);
            var first = processor.Flush().Single();
            Assert.That(first.Text, Is.EqualTo("<answer>first"));
            Assert.That(first.VoiceText, Is.EqualTo("first"));
            Assert.That(processor.Flush(), Is.Empty);
            Assert.That(processor.Append(" second"), Is.Empty);
            Assert.That(processor.Flush().Single().VoiceText, Is.EqualTo("second"));
            processor.Append("</answer>");
            Assert.That(processor.Complete().Single().VoiceText, Is.EqualTo(""));
            Assert.That(processor.Text, Is.EqualTo("<answer>first second</answer>"));
        }

        [Test]
        public void FlushDoesNotEmitMissingTagFallbackBeforeComplete()
        {
            var processor = new LlmTextProcessor(new LlmServiceOptions { VoiceTextTags = new[] { "answer" } }, "context");
            processor.Append("missing tags");
            Assert.That(processor.Flush().Single().VoiceText, Is.Null);
            processor.Append(" still missing");
            Assert.That(processor.Flush().Single().VoiceText, Is.Null);
            var fallback = processor.Complete().Single();
            Assert.That(fallback.Text, Is.EqualTo(""));
            Assert.That(fallback.VoiceText, Is.EqualTo("missing tags still missing"));
        }
    }
}

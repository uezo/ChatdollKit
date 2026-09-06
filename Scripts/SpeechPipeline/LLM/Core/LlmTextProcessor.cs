// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ChatdollKit.SpeechPipeline.LLM
{
    /// <summary>Turns model text deltas into speech segments while preserving AIAvatarKit's tag policy.</summary>
    /// <remarks>Create one processor per generation. Calls must be sequential.</remarks>
    public sealed class LlmTextProcessor
    {
        private static readonly string[] DefaultSplitChars = { "。", "？", "！", ". ", "?", "!", "\n" };
        private static readonly string[] DefaultOptionSplitChars = { "、", ", " };
        private static readonly Regex XmlTagPattern = new Regex(@"(</?[A-Za-z_]\w*(?:""[^""]*""|'[^']*'|[^""'<>])*>)", RegexOptions.CultureInvariant);
        private static readonly Regex IncompleteControlTagPattern = new Regex(@"</?(?:artifact|face|animation|vision|language|tools)\b[^<>]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex BracketControlTagPattern = new Regex(@"\[(\w+):([^\]]+)\]", RegexOptions.CultureInvariant);
        private static readonly Regex XmlControlTagPattern = new Regex(@"<\w+\s[^>]*>", RegexOptions.CultureInvariant);
        private static readonly Regex BeforeBracketControlTagPattern = new Regex(@"(?=\[\w+:[^\]]+\])", RegexOptions.CultureInvariant);
        private static readonly Regex BeforeXmlControlTagPattern = new Regex(@"(?=<\w+\s[^>]*>)", RegexOptions.CultureInvariant);

        private readonly string contextId;
        private readonly Regex sentencePattern;
        private readonly Regex optionPattern;
        private readonly int optionSplitThreshold;
        private readonly bool splitOnControlTags;
        private readonly string[] voiceTags;
        private readonly string terminalTagEnd;
        private readonly StringBuilder emittedText = new StringBuilder();
        private string buffer = "";
        private string currentVoiceTag;
        private bool terminalClosed;
        private bool completed;

        /// <summary>Visible response text, including the segment still waiting for a boundary.</summary>
        public string Text => emittedText.ToString() + buffer;

        public LlmTextProcessor(LlmServiceOptions options, string contextId)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            this.contextId = contextId;
            var splitChars = options.SplitChars == null || options.SplitChars.Length == 0 ? DefaultSplitChars : options.SplitChars;
            var optionSplitChars = options.OptionSplitChars == null || options.OptionSplitChars.Length == 0 ? DefaultOptionSplitChars : options.OptionSplitChars;
            string delimiters = string.Join("|", splitChars.OrderByDescending(CodePointLength).Select(Regex.Escape));
            sentencePattern = new Regex("((" + delimiters + ")+)", RegexOptions.CultureInvariant);
            var optionPatterns = optionSplitChars.OrderByDescending(CodePointLength)
                .Select(delimiter => Regex.Escape(delimiter) + (delimiter.EndsWith(" ", StringComparison.Ordinal) ? "" : @"\s?"));
            string optionalDelimiters = string.Join("|", optionPatterns);
            optionPattern = new Regex("(" + optionalDelimiters + @")\s*(?!.*(" + optionalDelimiters + "))", RegexOptions.CultureInvariant);
            optionSplitThreshold = options.OptionSplitThreshold;
            splitOnControlTags = options.SplitOnControlTags;
            voiceTags = options.VoiceTextTags == null ? Array.Empty<string>() : (string[])options.VoiceTextTags.Clone();
            terminalTagEnd = options.TerminalVoiceTextTag == null ? null : "</" + options.TerminalVoiceTextTag + ">";
        }

        public IReadOnlyList<LlmResponse> Append(string delta)
        {
            if (completed) throw new InvalidOperationException("Cannot append after completing a text processor.");
            if (delta == null) throw new ArgumentNullException(nameof(delta));
            var responses = new List<LlmResponse>();
            if (terminalClosed) return responses;
            buffer += delta;
            if (terminalTagEnd != null)
            {
                int terminalIndex = buffer.IndexOf(terminalTagEnd, StringComparison.Ordinal);
                if (terminalIndex >= 0)
                {
                    buffer = buffer.Substring(0, terminalIndex + terminalTagEnd.Length);
                    terminalClosed = true;
                }
            }

            bool hasIncompleteTag;
            buffer = ReplaceSentenceSplitsOutsideMarkup(buffer, out hasIncompleteTag);
            if (splitOnControlTags)
            {
                buffer = BeforeBracketControlTagPattern.Replace(buffer, "|");
                buffer = BeforeXmlControlTagPattern.Replace(buffer, "|");
            }
            if (!hasIncompleteTag && CodePointLength(RemoveControlTags(buffer)) > optionSplitThreshold)
                buffer = ReplaceLastOptionSplitOutsideMarkup(buffer);

            // Preserve upstream compatibility: literal pipes are also separators and disappear from Text.
            var segments = buffer.Split('|');
            for (int index = 0; index < segments.Length - 1; index++)
            {
                string segment = segments[index];
                responses.Add(CreateResponse(segment, ToVoiceText(segment)));
                emittedText.Append(segment);
            }
            buffer = segments[segments.Length - 1];
            return responses;
        }

        /// <summary>Emits pending text before a tool event without ending voice-tag tracking.</summary>
        public IReadOnlyList<LlmResponse> Flush()
        {
            var responses = new List<LlmResponse>();
            if (buffer.Length != 0)
            {
                responses.Add(CreateResponse(buffer, ToVoiceText(buffer)));
                emittedText.Append(buffer);
                buffer = "";
            }
            return responses;
        }

        public IReadOnlyList<LlmResponse> Complete()
        {
            if (completed) return Array.Empty<LlmResponse>();
            completed = true;
            var responses = new List<LlmResponse>(Flush());
            string text = emittedText.ToString();
            // Deliberately check tag-name substrings, rather than markup, as in the source.
            if (voiceTags.Length != 0 && !voiceTags.Any(tag => text.IndexOf(tag, StringComparison.Ordinal) >= 0))
            {
                string voice = RemoveControlTags(text);
                if (voice.Length != 0) responses.Add(CreateResponse("", voice));
            }
            return responses;
        }

        private LlmResponse CreateResponse(string text, string voiceText)
            => new LlmResponse { ContextId = contextId, Text = text, VoiceText = voiceText };

        private string ReplaceSentenceSplitsOutsideMarkup(string text, out bool hasIncompleteTag)
        {
            var incomplete = IncompleteControlTagPattern.Match(text);
            hasIncompleteTag = incomplete.Success;
            string suffix = hasIncompleteTag ? text.Substring(incomplete.Index) : "";
            var parts = XmlTagPattern.Split(hasIncompleteTag ? text.Substring(0, incomplete.Index) : text);
            for (int index = 0; index < parts.Length; index += 2)
                parts[index] = sentencePattern.Replace(parts[index], "$1|");
            return string.Concat(parts) + suffix;
        }

        private string ReplaceLastOptionSplitOutsideMarkup(string text)
        {
            var parts = XmlTagPattern.Split(text);
            for (int index = parts.Length - 1; index >= 0; index -= 2)
            {
                string replaced = optionPattern.Replace(parts[index], "$1|");
                if (replaced == parts[index]) continue;
                parts[index] = replaced;
                break;
            }
            return string.Concat(parts);
        }

        private string ToVoiceText(string segment)
        {
            if (voiceTags.Length == 0) return RemoveControlTags(segment);
            foreach (string tag in voiceTags)
            {
                string startTag = "<" + tag + ">";
                string endTag = "</" + tag + ">";
                int start = segment.IndexOf(startTag, StringComparison.Ordinal);
                int end = segment.IndexOf(endTag, StringComparison.Ordinal);
                if (start >= 0 && end >= 0)
                {
                    currentVoiceTag = null;
                    start += startTag.Length;
                    return RemoveControlTags(end > start ? segment.Substring(start, end - start) : "");
                }
                if (start >= 0)
                {
                    currentVoiceTag = tag;
                    return RemoveControlTags(segment.Substring(start + startTag.Length));
                }
            }
            if (currentVoiceTag == null) return null;
            int closing = segment.IndexOf("</" + currentVoiceTag + ">", StringComparison.Ordinal);
            if (closing < 0) return RemoveControlTags(segment);
            currentVoiceTag = null;
            return RemoveControlTags(segment.Substring(0, closing));
        }

        public static string RemoveControlTags(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var incomplete = IncompleteControlTagPattern.Match(text);
            if (incomplete.Success) text = text.Substring(0, incomplete.Index);
            text = BracketControlTagPattern.Replace(text, "");
            return XmlControlTagPattern.Replace(text, "").Trim();
        }

        private static int CodePointLength(string text)
        {
            int length = 0;
            for (int index = 0; index < text.Length; index++, length++)
                if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])) index++;
            return length;
        }
    }
}

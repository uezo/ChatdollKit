using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.LLM
{
    public class LLMContentProcessor : MonoBehaviour
    {
        [Header("Response stream settings")]
        public List<string> SplitChars = new List<string>() { "。", "！", "？", ". ", "!", "?" };
        private List<string> splitCharsWithNewLine;
        public List<string> OptionalSplitChars = new List<string>() { "、", ", " };
        public string ThinkTag = "thinking";
        public int MaxLengthBeforeOptionalSplit = 0;
        private Queue<LLMContentItem> llmContentQueue = new Queue<LLMContentItem>();
        public Action<LLMContentItem> HandleSplittedText;
        public Func<LLMContentItem, CancellationToken, UniTask> ProcessContentItemAsync;
        public Func<LLMContentItem, CancellationToken, UniTask> ShowContentItemAsync;
        public bool IsParsing { get; protected set; } = false;

        [Header("Debug")]
        [SerializeField]
        private bool debugMode = false;

        public async UniTask ProcessContentStreamAsync(ILLMSession llmSession, CancellationToken token)
        {
            llmContentQueue.Clear();

            IsParsing = true;

            // Split current buffer with the marks that represents the end of a sentence
            splitCharsWithNewLine = new List<string>(SplitChars) { "\n" };

            try
            {
                var splitIndex = 0;
                var isFirstWord = true;
                var language = string.Empty;
                var isInsideThinkTag = false;
                var thinkStart = $"<{ThinkTag}>";
                var thinkEnd = $"</{ThinkTag}>";

                while (!token.IsCancellationRequested)
                {
                    // Split current buffer with the marks that represents the end of a sentence
                    var splittedBuffer = SplitString(llmSession.StreamBuffer);

                    if (llmSession.IsResponseDone && splitIndex == splittedBuffer.Count)
                    {
                        // Exit while loop when stream response ends and all sentences has been processed
                        if (debugMode)
                        {
                            Debug.Log($"Exit from content stream loop: splitted={string.Join(",", splittedBuffer)} / streamBuffer={llmSession.StreamBuffer}");
                        }
                        break;
                    }

                    // Process if the response is complete or if we have enough buffered chunks to process.
                    bool hasProcessableChunks = splittedBuffer.Count() > splitIndex + 1;
                    if (llmSession.IsResponseDone || hasProcessableChunks)
                    {
                        // Process each splitted unprocessed sentence
                        foreach (var text in splittedBuffer.Skip(splitIndex).Take(llmSession.IsResponseDone ? splittedBuffer.Count - splitIndex : 1))
                        {
                            splitIndex += 1;
                            if (!string.IsNullOrEmpty(text.Trim()))
                            {
                                if (debugMode)
                                {
                                    Debug.Log($"Content stream: {text} (splitted={string.Join(",", splittedBuffer)} / isFirst={isFirstWord})");
                                }

                                var processedText = string.Empty;

                                if (isInsideThinkTag)
                                {
                                    var endIndex = text.IndexOf(thinkEnd);
                                    if (endIndex != -1)
                                    {
                                        // Think tag is closed. Use the text after end tag.
                                        isInsideThinkTag = false;
                                        processedText = text.Substring(endIndex + thinkEnd.Length);
                                    }
                                    else
                                    {
                                        continue;   // Think tag is still open
                                    }
                                }
                                else
                                {
                                    var startIndex = text.IndexOf(thinkStart);
                                    if (startIndex != -1)
                                    {
                                        var endIndex = text.IndexOf(thinkEnd, startIndex);
                                        if (endIndex != -1)
                                        {
                                            // Think tag opened and also closed.
                                            var beforeTag = text.Substring(0, startIndex);
                                            var afterTag = text.Substring(endIndex + thinkEnd.Length);
                                            processedText = beforeTag + afterTag;
                                        }
                                        else
                                        {
                                            // Think tag opened but not closed.
                                            isInsideThinkTag = true;
                                            processedText = text.Substring(0, startIndex);
                                        }
                                    }
                                    else
                                    {
                                        processedText = text;
                                    }
                                }

                                if (string.IsNullOrEmpty(processedText.Trim()))
                                {
                                    continue;
                                }

                                var match = Regex.Match(processedText, @"\[lang:([a-zA-Z-]+)\]");
                                if (match.Success)
                                {
                                    language = match.Groups[1].Value;
                                }

                                var contentItem = new LLMContentItem(processedText, isFirstWord, language);          
                                HandleSplittedText?.Invoke(contentItem);
                                if (contentItem != null)
                                {
                                    if (ProcessContentItemAsync != null)
                                    {
                                        await ProcessContentItemAsync(contentItem, token);
                                    }
                                    llmContentQueue.Enqueue(contentItem);
                                }
                                isFirstWord = false;
                            }
                        }
                    }

                    // Wait for a bit before processing buffer next time
                    await UniTask.Delay(10, cancellationToken: token);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"ParseAnimatedVoiceAsync was cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error at ParseAnimatedVoiceAsync: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                IsParsing = false;
            }
        }

        private List<string> SplitString(string input)
        {
            var result = new List<string>();
            var tempBuffer = "";

            for (int i = 0; i < input.Length; i++)
            {
                tempBuffer += input[i];

                if (IsSplitChar(input[i].ToString()))
                {
                    // Check if the next character is also a split character
                    if (i + 1 < input.Length && IsSplitChar(input[i + 1].ToString()))
                    {
                        // Continue the buffer if the next character is also a split character
                        continue;
                    }
                    else
                    {
                        // Add to result if it's the end of the sequence of split characters
                        result.Add(tempBuffer);
                        tempBuffer = "";
                    }
                }
                else if (IsOptionalSplitChar(input[i].ToString()))
                {
                    if (tempBuffer.Length >= MaxLengthBeforeOptionalSplit)
                    {
                        result.Add(tempBuffer);
                        tempBuffer = "";
                    }
                }
            }

            if (!string.IsNullOrEmpty(tempBuffer))
            {
                result.Add(tempBuffer);
            }

            return result;
        }

        private bool IsSplitChar(string character)
        {
            foreach (var splitChar in splitCharsWithNewLine)
            {
                if (character == splitChar)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsOptionalSplitChar(string character)
        {
            foreach (var splitChar in OptionalSplitChars)
            {
                if (character == splitChar)
                {
                    return true;
                }
            }
            return false;
        }

        public virtual async UniTask ShowContentAsync(ILLMSession llmSession, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // Performance ends when streaming response and parsing ends and all response animated voices are done
                if (llmSession.IsResponseDone && !IsParsing && llmContentQueue.Count == 0) break;

                if (llmContentQueue.Count > 0)
                {
                    // Retrive content from queue
                    var contentItem = llmContentQueue.Dequeue();

                    // Perform
                    if (ShowContentItemAsync != null)
                    {
                        await ShowContentItemAsync(contentItem, token);
                    }
                }
                else
                {
                    // Do nothing (just wait a bit) when no AnimatedVoice in the queue while receiving data
                    await UniTask.Delay(10, cancellationToken: token);
                }
            }
        }
    }

    public class LLMContentItem
    {
        public string Text { get; set; }
        public bool IsFirstItem { get; set; }
        public string Language { get; set; }
        public object Data { get; set; }

        public LLMContentItem(string text, bool isFirstItem, string language, object data = null)
        {
            Text = text;
            IsFirstItem = isFirstItem;
            Language = language;
            Data = data;
        }
    }
}

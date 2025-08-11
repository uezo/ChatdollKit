using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.LLM
{
    public class LLMServiceBase : MonoBehaviour, ILLMService
    {
        public bool _IsEnabled;
        public virtual bool IsEnabled {
            get {
#if UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                return _IsEnabled;
#endif
            }
            set
            {
                _IsEnabled = value;
                if (value == true)
                {
                    OnEnabled?.Invoke();
                }
            }
        }

        public string CustomParameterKey { get; } = "CustomParameters";
        public string CustomHeaderKey { get; } = "CustomHeaders";

        [Header("Debug")]
        public bool DebugMode = false;

        [Header("Context configuration")]
        [TextArea(1, 6)]
        public string SystemMessageContent;
        public string ErrorMessageContent;
        [SerializeField]
        protected int historyTurns = 100;
        [SerializeField]
        protected int contextTimeout = 600;    // 10 min
        protected float contextUpdatedAt;
        protected List<ILLMMessage> context = new List<ILLMMessage>();
        protected string contextId = string.Empty;

        public Action OnEnabled { get; set; }
        public Action <Dictionary<string, string>, ILLMSession> HandleExtractedTags { get; set; }
        public Func<string, UniTask<byte[]>> CaptureImage { get; set; }
        public Func<string, Dictionary<string, object>, ILLMSession, CancellationToken, UniTask> OnStreamingEnd { get; set; }
        public List<ILLMTool> Tools { get; set; } = new List<ILLMTool>();

        public virtual List<ILLMMessage> GetContext(int count)
        {
            if (Time.time - contextUpdatedAt > contextTimeout)
            {
                ClearContext();
            }

            // Return copy not to update context directly
            if (string.IsNullOrEmpty(contextId))
            {
                contextId = Guid.NewGuid().ToString();
            }
            return context.Skip(context.Count - count).ToList();
        }

        public virtual List<ILLMMessage> GetContextRaw()
        {
            return context;
        }

        protected virtual void UpdateContext(LLMSession llmSession)
        {
            Debug.LogWarning("Override this method to update context. Nothing is done at the base class.");
        }

        public virtual void ClearContext()
        {
            context.Clear();
            contextId = string.Empty;
            contextUpdatedAt = Time.time;
        }

#pragma warning disable CS1998
        public virtual async UniTask<List<ILLMMessage>> MakePromptAsync(string userId, string inputText, Dictionary<string, object> payloads, CancellationToken token = default)
        {
            throw new NotImplementedException("LLMServiceBase.MakePromptAsync must be implemented");
        }

        public virtual async UniTask<ILLMSession> GenerateContentAsync(List<ILLMMessage> messages, Dictionary<string, object> payloads, bool useFunctions = true, int retryCounter = 1, CancellationToken token = default)
        {
            throw new NotImplementedException("LLMServiceBase.GenerateContentAsync must be implemented");
        }
#pragma warning restore CS1998

        protected virtual Dictionary<string, string> ExtractTags(string text)
        {
            var tagPattern = @"\[(\w+):([^\]]+)\]";
            var matches = Regex.Matches(text, tagPattern);
            var result = new Dictionary<string, string>();

            foreach (Match match in matches)
            {
                if (match.Groups.Count == 3)
                {
                    var key = match.Groups[1].Value;
                    var value = match.Groups[2].Value;
                    result[key] = value;
                }
            }

            return result;
        }
    }

    public class LLMSession : ILLMSession
    {
        public bool IsResponseDone { get; set; } = false;
        public string StreamBuffer { get; set; }
        public string CurrentStreamBuffer { get; set; }
        public bool IsVisionAvailable { get; set; } = true;
        public ResponseType ResponseType { get; set; } = ResponseType.None;
        public UniTask StreamingTask { get; set; }
        public string FunctionName { get; set; }
        public string FunctionArguments { get; set; }
        public List<ILLMMessage> Contexts { get; set; }
        public string ContextId { get; set; }
        public bool ProcessLastChunkImmediately { get; set; } = false;

        public LLMSession()
        {
            IsResponseDone = false;
            StreamBuffer = string.Empty;
            CurrentStreamBuffer = string.Empty;
            ResponseType = ResponseType.None;
            Contexts = new List<ILLMMessage>();
            ContextId = string.Empty;
        }
    }

    public class LLMTool : ILLMTool
    {
        public string name { get; set; }
        public string description { get; set; }
        public ILLMToolParameters parameters { get; set; }

        public LLMTool(string name, string description)
        {
            this.name = name;
            this.description = description;
            parameters = new LLMToolParameters();
        }

        public void AddProperty(string key, Dictionary<string, object> value)
        {
            parameters.properties.Add(key, value);
        }
    }

    public class LLMToolParameters : ILLMToolParameters
    {
        public string type { get; set; }
        public Dictionary<string, Dictionary<string, object>> properties { get; set; }

        public LLMToolParameters()
        {
            type = "object";
            properties = new Dictionary<string, Dictionary<string, object>>();
        }
    }
}

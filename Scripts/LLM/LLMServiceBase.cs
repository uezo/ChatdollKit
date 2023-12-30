using System;
using System.Collections.Generic;
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
        [Header("Debug")]
        public bool DebugMode = false;

        [Header("Context configuration")]
        [TextArea(1, 6)]
        public string SystemMessageContent;
        public string ErrorMessageContent;
        public int HistoryTurns = 10;

        public Action OnEnabled { get; set; }

        protected List<ILLMTool> llmTools = new List<ILLMTool>();

        public virtual void AddTool(ILLMTool tool)
        {
            llmTools.Add(tool);
        }

#pragma warning disable CS1998
        public virtual ILLMMessage CreateMessageAfterFunction(string role = null, string content = null, Dictionary<string, object> function_call = null, string name = null, Dictionary<string, object> arguments = null)
        {
            throw new NotImplementedException("LLMServiceBase.CreateMessageAfterFunction must be implemented");
        }

        public virtual UniTask AddHistoriesAsync(ILLMSession llmSession, object dataStore, CancellationToken token = default)
        {
            throw new NotImplementedException("LLMServiceBase.AddHistoriesAsync must be implemented");
        }

        public virtual async UniTask<List<ILLMMessage>> MakePromptAsync(string userId, string inputText, Dictionary<string, object> payloads, CancellationToken token = default)
        {
            throw new NotImplementedException("LLMServiceBase.MakePromptAsync must be implemented");
        }

        public virtual async UniTask<ILLMSession> GenerateContentAsync(List<ILLMMessage> messages, Dictionary<string, object> payloads, bool useFunctions = true, int retryCounter = 1, CancellationToken token = default)
        {
            throw new NotImplementedException("LLMServiceBase.GenerateContentAsync must be implemented");
        }
#pragma warning restore CS1998
    }

    public class LLMSession : ILLMSession
    {
        public bool IsResponseDone { get; set; } = false;
        public string StreamBuffer { get; set; }
        public ResponseType ResponseType { get; set; } = ResponseType.None;
        public UniTask StreamingTask { get; set; }
        public string FunctionName { get; set; }
        public List<ILLMMessage> Contexts { get; set; }

        public LLMSession()
        {
            IsResponseDone = false;
            StreamBuffer = string.Empty;
            ResponseType = ResponseType.None;
            Contexts = new List<ILLMMessage>();
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

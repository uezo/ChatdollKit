using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.LLM
{
    public interface ILLMService
    {
        bool IsEnabled { get; set; }
        Action OnEnabled { get; set; }
        void AddTool(ILLMTool tool);
        UniTask AddHistoriesAsync(ILLMSession llmSession, object dataStore, CancellationToken token = default);
        UniTask<List<ILLMMessage>> MakePromptAsync(string userId, string inputText, Dictionary<string, object> payloads, CancellationToken token = default);
        UniTask<ILLMSession> GenerateContentAsync(List<ILLMMessage> messages, Dictionary<string, object> payloads = null, bool useFunctions = true, int retryCounter = 1, CancellationToken token = default);
        ILLMMessage CreateMessageAfterFunction(string role = null, string content = null, Dictionary<string, object> function_call = null, string name = null, Dictionary<string, object> arguments = null);
    }

    public enum ResponseType
    {
        None, Content, FunctionCalling, Error, Timeout
    }

    public interface ILLMSession
    {
        bool IsResponseDone { get; set; }
        string StreamBuffer { get; set; }
        ResponseType ResponseType { get; set; }
        UniTask StreamingTask { get; set; }
        string FunctionName { get; set; }
        List<ILLMMessage> Contexts { get; set; }
    }

    public interface ILLMMessage { }

    public interface ILLMTool
    {
        string name { get; set; }
        string description { get; set; }
        ILLMToolParameters parameters { get; set; }

        void AddProperty(string key, Dictionary<string, object> value);
    }

    public interface ILLMToolParameters
    {
        string type { get; set; }
        Dictionary<string, Dictionary<string, object>> properties { get; set; }
    }
}

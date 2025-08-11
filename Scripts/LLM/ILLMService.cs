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
        List<ILLMTool> Tools { get; set; }
        List<ILLMMessage> GetContext(int count);
        void ClearContext();
        UniTask<List<ILLMMessage>> MakePromptAsync(string userId, string inputText, Dictionary<string, object> payloads, CancellationToken token = default);
        UniTask<ILLMSession> GenerateContentAsync(List<ILLMMessage> messages, Dictionary<string, object> payloads = null, bool useFunctions = true, int retryCounter = 1, CancellationToken token = default);
        Func<string, Dictionary<string, object>, ILLMSession, CancellationToken, UniTask> OnStreamingEnd { get; set; }
        Action <Dictionary<string, string>, ILLMSession> HandleExtractedTags { get; set; }
        Func<string, UniTask<byte[]>> CaptureImage { get; set; }
    }

    public enum ResponseType
    {
        None, Content, FunctionCalling, Error, Timeout
    }

    public interface ILLMSession
    {
        bool IsResponseDone { get; set; }
        string StreamBuffer { get; set; }
        string CurrentStreamBuffer { get; set; }
        bool IsVisionAvailable { get; set; }
        ResponseType ResponseType { get; set; }
        UniTask StreamingTask { get; set; }
        string FunctionName { get; set; }
        string FunctionArguments { get; set; }
        List<ILLMMessage> Contexts { get; set; }
        string ContextId { get; set; }
        bool ProcessLastChunkImmediately { get; set; }
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

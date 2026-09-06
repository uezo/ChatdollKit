// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
using System;
using System.Net.Http;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.SpeechPipeline.LLM
{
    public sealed class OpenAIResponsesClient : HttpLlmServiceBase
    {
        public OpenAIResponsesClient(LlmServiceOptions options, HttpClient httpClient = null) : base(options, httpClient) { }

        protected override async UniTask<LlmProviderResult> GenerateCoreAsync(LlmRequest request, LlmServiceOptions options,
            Func<LlmResponse, UniTask> emit, CancellationToken token)
        {
            var body = LlmRequestBuilder.BuildResponsesRequest(request, options);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var state = new ResponsesStreamState(request.ContextId, emit);
                var error = await StreamRequestAsync("responses", body, options, async data =>
                {
                    if (data == "[DONE]") return false;
                    await state.ProcessEventAsync(ParseEvent(data));
                    return !state.IsCompleted;
                }, token);
                if (error == null)
                {
                    if (!state.IsCompleted) throw LlmErrorParser.Failure("unexpected_eof", "LLM stream ended before response.completed.");
                    state.Result.RecoveredPreviousResponse = attempt > 0;
                    state.Result.CanUsePreviousResponse = body["store"]?.Type != Newtonsoft.Json.Linq.JTokenType.Boolean || (bool)body["store"];
                    return state.Result;
                }
                if (attempt != 0 || !options.EnablePreviousResponseFallback || !LlmRequestBuilder.CanRecoverPreviousResponse(body, request, error))
                    throw new LlmServiceException(error);
                body = LlmRequestBuilder.BuildResponsesRecoveryRequest(body, request, options);
                await emit(new LlmResponse { ContextId = request.ContextId, IsRecovery = true });
            }
            throw new InvalidOperationException("Recovery attempt did not terminate.");
        }
    }
}

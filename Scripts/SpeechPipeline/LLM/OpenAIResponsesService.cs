using UnityEngine;

namespace ChatdollKit.SpeechPipeline.LLM
{
    [AddComponentMenu("ChatdollKit/Speech Pipeline/LLM/OpenAI Responses Service")]
    public sealed class OpenAIResponsesService : LlmServiceComponent
    {
        public string ApiKey;
        public string BaseUrl = "https://api.openai.com/v1";
        // Retained only so the editor can migrate existing serialized settings.

        public LlmServiceSettings Settings = new LlmServiceSettings();
        public bool EnablePreviousResponseFallback = true;

        public override LlmServiceOptions BuildOptions(LlmServiceOptions current = null)
        {
            var options = current?.Copy() ?? new LlmServiceOptions();
            Settings.ApplyTo(options);
            options.ApiKey = ApiKey;
            options.BaseUrl = BaseUrl;
            options.EnablePreviousResponseFallback = EnablePreviousResponseFallback;
            return options;
        }

        public override ILlmService CreateService(LlmServiceOptions options) => new OpenAIResponsesClient(options);
    }
}

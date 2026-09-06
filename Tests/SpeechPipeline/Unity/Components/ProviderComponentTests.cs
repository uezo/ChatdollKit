using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;
using NUnitTask = System.Threading.Tasks.Task;
using ChatdollKit.SpeechPipeline.LLM;
using ChatdollKit.SpeechPipeline.STT;
using ChatdollKit.SpeechPipeline.TTS;
using ChatdollKit.SpeechPipeline;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ChatdollKit.Tests.SpeechPipeline.Unity
{
    public sealed class ProviderComponentTests
    {
        private GameObject gameObject;

        [SetUp]
        public void SetUp()
        {
            gameObject = new GameObject("Speech provider settings test");
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(gameObject);

        [TestCase(typeof(OpenAISpeechRecognizer), typeof(OpenAISpeechRecognizerClient))]
        [TestCase(typeof(AzureSpeechRecognizer), typeof(AzureSpeechRecognizerClient))]
        public async NUnitTask RecognizerComponentsCreatePureProviders(Type componentType, Type serviceType)
        {
            var component = (SpeechRecognizerComponent)gameObject.AddComponent(componentType);
            SetOpenAICredentials(component, "provider-test-key");
            if (component is AzureSpeechRecognizer azure) azure.ApiKey = "provider-test-key";
            var options = component.BuildOptions();
            options.Validate();
            var service = component.CreateRecognizer(options);
            try { Assert.That(service.GetType(), Is.EqualTo(serviceType)); }
            finally { await ((SpeechRecognizerBase)service).DisposeAsync(); }
        }

        [TestCase(typeof(ChatCompletionsService), typeof(ChatCompletionsClient))]
        [TestCase(typeof(OpenAIResponsesService), typeof(OpenAIResponsesClient))]
        [TestCase(typeof(OpenAIResponsesWebSocketService), typeof(OpenAIResponsesWebSocketClient))]
        public async NUnitTask LlmComponentsPreserveCodeConfigurationAndCreatePureProviders(Type componentType, Type serviceType)
        {
            var component = (LlmServiceComponent)gameObject.AddComponent(componentType);
            SetOpenAICredentials(component, "provider-test-key");
            var original = component.BuildOptions();
            var guardrail = new TestGuardrail();
            Action<JObject, LlmRequest> edit = (body, request) => { };
            original.Tools = new[] { new LlmTool { Name = "lookup" } };
            original.Guardrails = new ILlmGuardrail[] { guardrail };
            original.EditRequestParameters = edit;
            original.InitialMessages = new JArray(new JObject { ["role"] = "system", ["content"] = "seed" });
            original.ExtraBody = new JObject { ["custom"] = 123 };
            original.ToolDefinitions = new JArray(new JObject { ["type"] = "function" });

            var updated = component.BuildOptions(original);
            updated.Validate();
            Assert.That(updated.Tools[0].Name, Is.EqualTo("lookup"));
            Assert.That(updated.Tools[0], Is.Not.SameAs(original.Tools[0]));
            Assert.That(updated.Guardrails[0], Is.SameAs(guardrail));
            Assert.That(updated.EditRequestParameters, Is.SameAs(edit));
            Assert.That(updated.InitialMessages.Count, Is.EqualTo(1));
            Assert.That(updated.ToolDefinitions.Count, Is.EqualTo(1));
            Assert.That((int)updated.ExtraBody["custom"], Is.EqualTo(123));
            updated.ExtraBody["custom"] = 456;
            updated.InitialMessages[0]["content"] = "changed";
            Assert.That((int)original.ExtraBody["custom"], Is.EqualTo(123));
            Assert.That((string)original.InitialMessages[0]["content"], Is.EqualTo("seed"));

            var service = component.CreateService(updated);
            try { Assert.That(service.GetType(), Is.EqualTo(serviceType)); }
            finally { await service.DisposeAsync(); }
        }

        [TestCase(typeof(AzureSpeechSynthesizer), typeof(AzureSpeechSynthesizerClient))]
        [TestCase(typeof(GoogleSpeechSynthesizer), typeof(GoogleSpeechSynthesizerClient))]
        [TestCase(typeof(OpenAISpeechSynthesizer), typeof(OpenAISpeechSynthesizerClient))]
        [TestCase(typeof(VoicevoxSpeechSynthesizer), typeof(VoicevoxSpeechSynthesizerClient))]
        public async NUnitTask SynthesizerComponentsPreserveProcessorsAndCreatePureProviders(Type componentType, Type serviceType)
        {
            var component = (SpeechSynthesizerComponent)gameObject.AddComponent(componentType);
            SetOpenAICredentials(component, "provider-test-key");
            if (component is AzureSpeechSynthesizer azure) azure.ApiKey = "provider-test-key";
            if (component is GoogleSpeechSynthesizer google) google.ApiKey = "provider-test-key";
            var original = component.BuildOptions();
            var processor = new TestProcessor();
            original.Preprocessors = new ITtsPreprocessor[] { processor };
            original.Postprocessors = new ITtsPostprocessor[] { processor };

            var updated = component.BuildOptions(original);
            updated.Validate();
            Assert.That(updated.Preprocessors[0], Is.SameAs(processor));
            Assert.That(updated.Postprocessors[0], Is.SameAs(processor));
            Assert.That(updated.Preprocessors, Is.Not.SameAs(original.Preprocessors));
            Assert.That(updated.Postprocessors, Is.Not.SameAs(original.Postprocessors));

            var service = component.CreateSynthesizer(updated);
            try { Assert.That(service.GetType(), Is.EqualTo(serviceType)); }
            finally { await service.DisposeAsync(); }
        }

        [Test]
        public void RecognizerOptionsOwnArraysAndSampleRateChangesRequireRestart()
        {
            var component = gameObject.AddComponent<OpenAISpeechRecognizer>();
            component.Settings.AlternativeLanguages = new[] { "en" };
            var original = component.BuildOptions();
            var restartKey = component.GetRestartKey();
            component.Settings.AlternativeLanguages[0] = "fr";
            component.Settings.SampleRate = 24000;
            var updated = component.BuildOptions(original);

            Assert.That(original.AlternativeLanguages, Is.EqualTo(new[] { "en" }));
            Assert.That(original.SampleRate, Is.EqualTo(16000));
            Assert.That(updated.AlternativeLanguages, Is.EqualTo(new[] { "fr" }));
            Assert.That(updated.SampleRate, Is.EqualTo(24000));
            Assert.That(component.GetRestartKey(), Is.Not.EqualTo(restartKey));
        }

        [Test]
        public void LlmOptionalFieldsCanBeEnabledAndCleared()
        {
            var settings = new LlmServiceSettings
            {
                TerminalVoiceTextTag = "", ReasoningEffort = "",
                UseTemperature = true, Temperature = 0.2f,
                UseMaxOutputTokens = true, MaxOutputTokens = 512
            };
            var options = new LlmServiceOptions { ApiKey = "provider-test-key" };
            settings.ApplyTo(options);
            options.Validate();
            Assert.That(options.TerminalVoiceTextTag, Is.Null);
            Assert.That(options.ReasoningEffort, Is.Null);
            Assert.That(options.Temperature, Is.EqualTo(0.2).Within(0.00001));
            Assert.That(options.MaxOutputTokens, Is.EqualTo(512));

            settings.UseTemperature = false;
            settings.UseMaxOutputTokens = false;
            settings.ApplyTo(options);
            Assert.That(options.Temperature, Is.Null);
            Assert.That(options.MaxOutputTokens, Is.Null);
        }

        [Test]
        public void SynthesizerStyleMappingsAndSampleRateAreOwnedSnapshots()
        {
            var component = gameObject.AddComponent<VoicevoxSpeechSynthesizer>();
            component.Settings.StyleMapper = new[] { new SpeechSynthesisMapping { Key = "happy", Value = "47" } };
            component.Settings.UseSampleRate = true;
            component.Settings.SampleRate = 24000;
            var options = component.BuildOptions();
            component.Settings.StyleMapper[0].Value = "48";
            Assert.That(options.StyleMapper["happy"], Is.EqualTo("47"));
            Assert.That(options.SampleRate, Is.EqualTo(24000));

            component.Settings.UseSampleRate = false;
            Assert.That(component.BuildOptions(options).SampleRate, Is.Null);
        }

        [TestCase(typeof(AzureSpeechSynthesizer))]
        [TestCase(typeof(GoogleSpeechSynthesizer))]
        public void VoiceMapsBecomeIndependentDictionaries(Type componentType)
        {
            var component = (SpeechSynthesizerComponent)gameObject.AddComponent(componentType);
            var mappings = new[] { new SpeechSynthesisMapping { Key = "en-US", Value = "test-voice" } };
            if (component is AzureSpeechSynthesizer azure) azure.VoiceMap = mappings;
            if (component is GoogleSpeechSynthesizer google) google.VoiceMap = mappings;
            var options = component.BuildOptions();
            mappings[0].Value = "changed-voice";
            var voices = options is AzureSpeechSynthesizerOptions azureOptions
                ? azureOptions.VoiceMap : ((GoogleSpeechSynthesizerOptions)options).VoiceMap;
            Assert.That(voices["en-US"], Is.EqualTo("test-voice"));
        }

        [Test]
        public void InvalidSynthesisMappingsFailBeforeUpdatingAService()
        {
            Assert.Throws<ArgumentException>(() => SpeechSynthesisMapping.ToDictionary(new[]
            {
                new SpeechSynthesisMapping { Key = "same", Value = "1" },
                new SpeechSynthesisMapping { Key = "same", Value = "2" }
            }));
            Assert.Throws<ArgumentException>(() => SpeechSynthesisMapping.ToDictionary(new[]
            {
                new SpeechSynthesisMapping { Key = "", Value = "1" }
            }));
        }

        [Test]
        public void ProviderSpecificCoreDefaultsAreRetained()
        {
            var azure = gameObject.AddComponent<AzureSpeechRecognizer>().BuildOptions();
            Assert.That(azure.Language, Is.EqualTo(new AzureSpeechRecognizerOptions().Language));
            Assert.That(azure.TimeoutSeconds, Is.EqualTo(new AzureSpeechRecognizerOptions().TimeoutSeconds));
            var google = gameObject.AddComponent<GoogleSpeechSynthesizer>().BuildOptions();
            Assert.That(google.CacheExtension, Is.EqualTo(new GoogleSpeechSynthesizerOptions().CacheExtension));
            var openai = (OpenAISpeechSynthesizerOptions)gameObject.AddComponent<OpenAISpeechSynthesizer>().BuildOptions();
            Assert.That(openai.Model, Is.EqualTo(new OpenAISpeechSynthesizerOptions().Model));
            Assert.That(openai.Speaker, Is.EqualTo(new OpenAISpeechSynthesizerOptions().Speaker));
            Assert.That(openai.Instructions, Is.Null);
        }

        [TestCase(typeof(OpenAISpeechRecognizer))]
        [TestCase(typeof(ChatCompletionsService))]
        [TestCase(typeof(OpenAIResponsesService))]
        [TestCase(typeof(OpenAIResponsesWebSocketService))]
        [TestCase(typeof(OpenAISpeechSynthesizer))]
        public void OpenAIProvidersApplyTheirOwnCredentialUpdates(Type componentType)
        {
            var component = (LiveSpeechComponent)gameObject.AddComponent(componentType);
            var endpoint = component is OpenAIResponsesWebSocketService
                ? "wss://component.invalid/v1/responses" : "https://component.invalid/v1";
            var editedEndpoint = component is OpenAIResponsesWebSocketService
                ? "wss://edited.invalid/v1/responses" : "https://edited.invalid/v1";
            SetOpenAICredentials(component, "component-key", endpoint);

            var original = ReadOpenAICredentials(component);
            Assert.That(original.Item1, Is.EqualTo("component-key"));
            Assert.That(original.Item2, Is.EqualTo(endpoint));

            SetOpenAICredentials(component, "edited-component-key", editedEndpoint);
            var updated = ReadOpenAICredentials(component);
            Assert.That(updated.Item1, Is.EqualTo("edited-component-key"));
            Assert.That(updated.Item2, Is.EqualTo(editedEndpoint));
        }

        private static void SetOpenAICredentials(Component component, string apiKey,
            string endpoint = null)
        {
            // Non-OpenAI providers configure their credentials separately.
            if (!(component is OpenAISpeechRecognizer)
                && !(component is ChatCompletionsService)
                && !(component is OpenAIResponsesService)
                && !(component is OpenAIResponsesWebSocketService)
                && !(component is OpenAISpeechSynthesizer)) return;
            component.GetType().GetField("ApiKey").SetValue(component, apiKey);
            if (component is OpenAIResponsesWebSocketService webSocket)
                webSocket.WebSocketUrl = endpoint ?? "wss://api.openai.com/v1/responses";
            else
                component.GetType().GetField("BaseUrl").SetValue(component, endpoint ?? "https://api.openai.com/v1");
        }

        private static Tuple<string, string> ReadOpenAICredentials(LiveSpeechComponent component)
        {
            if (component is SpeechRecognizerComponent stt)
            {
                var options = (OpenAISpeechRecognizerOptions)stt.BuildOptions();
                options.Validate();
                return Tuple.Create(options.ApiKey, options.BaseUrl);
            }
            if (component is LlmServiceComponent llm)
            {
                var options = llm.BuildOptions();
                options.Validate();
                var endpoint = options is OpenAIResponsesWebSocketServiceOptions webSocket
                    ? webSocket.WebSocketUrl : options.BaseUrl;
                return Tuple.Create(options.ApiKey, endpoint);
            }
            var ttsOptions = (OpenAISpeechSynthesizerOptions)((SpeechSynthesizerComponent)component).BuildOptions();
            ttsOptions.Validate();
            return Tuple.Create(ttsOptions.ApiKey, ttsOptions.BaseUrl);
        }

        private sealed class TestGuardrail : ILlmGuardrail
        {
            public LlmGuardrailScope Scope => LlmGuardrailScope.Both;
            public UniTask<LlmGuardrailResult> ApplyAsync(LlmRequest request, string text, CancellationToken token)
                => UniTask.FromResult(new LlmGuardrailResult());
        }

        private sealed class TestProcessor : ITtsPreprocessor, ITtsPostprocessor
        {
            public UniTask<string> ProcessAsync(SpeechSynthesisRequest request, SpeechSynthesizerOptions options, CancellationToken token = default)
                => UniTask.FromResult(request.Text);
            public UniTask<byte[]> ProcessAsync(byte[] audio, SpeechSynthesizerOptions options, CancellationToken token = default)
                => UniTask.FromResult(audio);
            public JToken GetCacheConfiguration(SpeechSynthesizerOptions options) => new JObject();
        }
    }
}

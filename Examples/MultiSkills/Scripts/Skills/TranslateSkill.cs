using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;
using ChatdollKit.Network;

namespace ChatdollKit.Examples.MultiSkills
{
    public class TranslateSkill : SkillBase
    {
        public enum TranslationEngine
        {
            Azure, Google
        }

        public TranslationEngine Engine = TranslationEngine.Azure;
        public string ApiKey;
        private ChatdollHttp client { get; } = new ChatdollHttp();

        public override bool IsAvailable
        {
            get
            {
                return !string.IsNullOrEmpty(ApiKey);
            }
        }

        private void OnDestroy()
        {
            client.Dispose();
        }

        public override async Task<Response> ProcessAsync(Request request, State state, CancellationToken token)
        {
            var response = new Response(request.Id);

            if (!IsAvailable)
            {
                response.AddVoiceTTS("翻訳は利用できません");
                response.AddAnimation("Default");
            }

            if (state.Topic.IsFirstTurn)
            {
                response.AddVoiceTTS("何を翻訳しますか？");
                response.AddAnimation("Default");
            }
            else
            {
                // Translate
                var translatedText = await (Engine == TranslationEngine.Azure ? TranslateWithAzureAsync(request.Text) : TranslateWithGoogleAsync(request.Text));
                response.AddVoiceTTS($"{request.Text}を英語で言うと、{translatedText}、です。");
                response.AddAnimation("Default");
            }

            // Continue until stop
            state.Topic.IsFinished = false;

            return response;
        }

        private async Task<string> TranslateWithAzureAsync(string text, string language = "en")
        {
            // Compose url
            var url = $"https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to={language}";

            // Create headers
            var headers = new Dictionary<string, string>();
            headers.Add("Ocp-Apim-Subscription-Key", ApiKey);

            // Create data
            var data = new List<AzureTranslationText>() { new AzureTranslationText(text) };

            // Send request
            var translatedText = string.Empty;
            try
            {
                var response = await client.PostJsonAsync<List<AzureTranslationResponse>>(url, data, headers);
                translatedText = response[0].Translations[0].Text;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured while translation: {ex.Message}");
            }
            return translatedText;
        }

        class AzureTranslationResponse
        {
            public List<AzureTranslationText> Translations { get; set; }
        }

        class AzureTranslationText
        {
            public string Text { get; set; }

            public AzureTranslationText(string text)
            {
                Text = text;
            }
        }

        private async Task<string> TranslateWithGoogleAsync(string text, string language = "en")
        {
            // Compose url
            var url = $"https://translation.googleapis.com/language/translate/v2";

            // Set data
            var data = new Dictionary<string, string>()
            {
                { "key", ApiKey },
                { "q", text },
                { "target", language },
                { "source", "ja" },
                { "model", "nmt" },
                { "format", "text" },
            };

            // Send request
            var translatedText = string.Empty;
            try
            {
                var response = await client.PostFormAsync<GoogleTranslationResponse>(url, data);
                translatedText = response.Data.Translations[0].TranslatedText;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured while translation: {ex.Message}");
            }
            return translatedText;
        }

        class GoogleTranslationResponse
        {
            public GoogleTranslationData Data { get; set; }
        }

        class GoogleTranslationData
        {
            public List<GoogleTranslationText> Translations { get; set; }
        }

        class GoogleTranslationText
        {
            public string TranslatedText { get; set; }
        }
    }
}

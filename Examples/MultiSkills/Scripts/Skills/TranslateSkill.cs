using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.Network;

namespace ChatdollKit.Examples.MultiSkills
{
    public class TranslateSkill : SkillBase
    {
        public enum TranslationEngine
        {
            Azure, Google, Watson
        }

        public TranslationEngine Engine = TranslationEngine.Azure;
        public string ApiKey;
        public string BaseUrl;
        private ChatdollHttp client { get; } = new ChatdollHttp();

        public override bool IsAvailable
        {
            get
            {
                return !string.IsNullOrEmpty(ApiKey);
            }
        }

        public override async UniTask<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
        {
            var response = new Response(request.Id);

            if (!IsAvailable)
            {
                response.AddVoiceTTS("翻訳は利用できません");
            }

            if (state.Topic.IsFirstTurn)
            {
                response.AddVoiceTTS("何を翻訳しますか？");
            }
            else
            {
                // Translate
                var translatedText = string.Empty;
                if (Engine == TranslationEngine.Azure)
                {
                    translatedText = await TranslateWithAzureAsync(request.Text);
                }
                else if (Engine == TranslationEngine.Google)
                {
                    translatedText = await TranslateWithGoogleAsync(request.Text);
                }
                else if (Engine == TranslationEngine.Watson)
                {
                    translatedText = await TranslateWithWatsonAsync(request.Text);
                }

                response.AddVoiceTTS($"{request.Text}を英語で言うと、{translatedText}、です。");
            }

            // Continue until stop
            response.EndTopic = false;

            return response;
        }

        private async UniTask<string> TranslateWithAzureAsync(string text, string language = "en")
        {
            // Compose url
            var url = BaseUrl + $"/translate?api-version=3.0&to={language}";

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

        private async UniTask<string> TranslateWithGoogleAsync(string text, string language = "en")
        {
            // Compose url
            var url = BaseUrl;

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

        private async UniTask<string> TranslateWithWatsonAsync(string text, string language = "en")
        {
            // Compose url
            var url = BaseUrl + "/v3/translate?version=2018-05-01";

            // Set header
            var headers = new Dictionary<string, string>()
            {
                { "Authorization", client.GetBasicAuthenticationHeaderValue("apikey", ApiKey).ToString() },
            };

            // Set data
            var data = new Dictionary<string, string>()
            {
                { "text", text },
                { "target", language },
                { "source", "ja" }
            };

            // Send request
            var translatedText = string.Empty;
            try
            {
                var response = await client.PostJsonAsync<WatsonTranslationResponse>(url, data, headers);
                translatedText = response.translations[0].translation;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured while translation: {ex.Message}");
            }
            return translatedText;

        }

        class WatsonTranslationResponse
        {
            public List<WatsonTranslationData> translations { get; set; }
        }

        class WatsonTranslationData
        {
            public string translation { get; set; }
        }
    }
}

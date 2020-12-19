using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Network;


namespace ChatdollKit.Extension.Azure
{
    public class AzureTranslator : IDisposable
    {
        public string ApiKey { get; set; }
        private ChatdollHttp client { get; }

        public AzureTranslator(string apiKey)
        {
            ApiKey = apiKey;
            client = new ChatdollHttp();
        }

        // Translate
        public async Task<string> TranslateAsync(string text, string language = "en")
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

        public void Dispose()
        {
            client?.Dispose();
        }
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
}

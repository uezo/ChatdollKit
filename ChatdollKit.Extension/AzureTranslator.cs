using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using ChatdollKit.Network;


namespace ChatdollKit.Extension
{
    public class AzureTranslator
    {
        public string ApiKey { get; set; }

        public AzureTranslator(string apiKey)
        {
            ApiKey = apiKey;
        }

        // Translate
        public async Task<string> TranslateAsync(string text, string language = "en")
        {
            // Compose url
            var url = $"https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to={language}";

            using (var www = UnityWebRequest.Post(url, string.Empty))
            {
                www.timeout = 10;

                // Set headers
                www.SetRequestHeader("Ocp-Apim-Subscription-Key", ApiKey);
                www.SetRequestHeader("Content-Type", "application/json; charset=UTF-8");

                // Set data
                var data = new List<AzureTranslationText>() { new AzureTranslationText(text) };
                www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data)));

                // Send request
                await www.SendWebRequest();

                // Handle response
                if (www.isNetworkError || www.isHttpError)
                {
                    Debug.LogError($"Error occured while processing text-to-speech voice: {www.error}");
                }
                else if (www.isDone)
                {
                    var responseString = DownloadHandlerBuffer.GetContent(www);
                    var translationResponse = JsonConvert.DeserializeObject<List<AzureTranslationResponse>>(responseString);
                    var translatedText = translationResponse[0].Translations[0].Text;
                    return translatedText;
                }
            }
            return null;
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

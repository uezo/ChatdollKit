using System.Collections.Generic;
using System.Threading.Tasks;
using ChatdollKit.Network;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;


namespace ChatdollKit.Extension
{
    public class GoogleTranslator
    {
        public string ApiKey { get; set; }

        public GoogleTranslator(string apiKey)
        {
            ApiKey = apiKey;
        }

        // Translate
        public async Task<string> TranslateAsync(string text, string language = "en")
        {
            // Compose url
            var url = $"https://translation.googleapis.com/language/translate/v2";

            // Set data
            var data = new WWWForm();
            data.AddField("key", ApiKey);
            data.AddField("q", text);
            data.AddField("target", language);
            data.AddField("source", "ja");
            data.AddField("model", "nmt");

            using (var www = UnityWebRequest.Post(url, data))
            {
                www.timeout = 10;

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
                    var translationResponse = JsonConvert.DeserializeObject<GoogleTranslationResponse>(responseString);
                    var translatedText = translationResponse.Data.Translations[0].TranslatedText;
                    return translatedText;
                }
            }
            return null;
        }
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

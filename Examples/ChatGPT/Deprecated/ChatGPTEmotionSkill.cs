using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using ChatdollKit.Dialog;

namespace ChatdollKit.Examples.ChatGPT
{
    public class ChatGPTEmotionSkill : ChatGPTSkill
    {
        public string CurrentEmotion { get; private set; }
        public int CurrentEmotionValue { get; private set; }

        [SerializeField]
        private int emotionGapCriteria = 2;
        [SerializeField]
        private float emotionDuration = 7.0f;

        protected override Response UpdateResponse(Response response, string chatGPTResponseText)
        {
            var responseJson = JsonConvert.DeserializeObject<Dictionary<string, string>>(chatGPTResponseText);

            var emotions = new Dictionary<string, int>()
            {
                { "Joy", int.Parse(responseJson["joy"]) },
                { "Angry", int.Parse(responseJson["angry"]) },
                { "Sorrow", int.Parse(responseJson["sorrow"]) },
                { "Fun", int.Parse(responseJson["fun"]) }
            };
            Debug.Log(JsonConvert.SerializeObject(emotions));

            var maxEmotion = emotions.FirstOrDefault(c => c.Value == emotions.Values.Max());
            if (CurrentEmotion != maxEmotion.Key || maxEmotion.Value - CurrentEmotionValue >= emotionGapCriteria)
            {
                response.AddFace(maxEmotion.Key, emotionDuration);
                response.AddFace("Neutral", emotionDuration);
            }
            CurrentEmotion = maxEmotion.Key;
            CurrentEmotionValue = maxEmotion.Value;

            base.UpdateResponse(response, responseJson["response_text"]);

            return response;
        }
    }
}

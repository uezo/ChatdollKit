using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Dialog;

namespace ChatdollKit.Examples.MultiSkills
{
    public class WeatherSkill : SkillBase
    {

#pragma warning disable CS1998
        public override async Task<Response> ProcessAsync(Request request, State state, CancellationToken token)
        {
            // Get weather
            var weather = GetWeather(request);

            // Build and return response message
            var response = new Response(request.Id);
            response.AddVoiceTTS($"今日の天気は、{weather.Weather}、最高気温は{weather.Temperature}度の見込みです。");
            response.AddAnimation("Default");
            return response;
        }
#pragma warning restore CS1998

        private WeatherInfo GetWeather(Request request)
        {
            // Call weather API here instead
            return new WeatherInfo();
        }

        class WeatherInfo
        {
            private List<string> weathers = new List<string>() { "晴れ", "晴ときどき曇り", "曇り", "曇りときどき雨", "雨" };
            public string Weather;
            public int Temperature;

            public WeatherInfo()
            {
                Weather = weathers[Random.Range(0, weathers.Count - 1)];
                Temperature = Random.Range(0, 40);
            }
        }
    }
}

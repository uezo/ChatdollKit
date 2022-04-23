using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.Network;

namespace ChatdollKit.Examples.MultiSkills
{
    public class WeatherSkill : SkillBase
    {
        public enum WeatherLocation
        {
            Sapporo = 016010,
            Sendai = 040010,
            Tokyo = 130010,
            Nagoya = 230010,
            Osaka = 270000,
            Hiroshima = 340010,
            Fukuoka = 400010,
            Naha = 471010
        }

        public WeatherLocation MyLocation;
        private ChatdollHttp client { get; } = new ChatdollHttp();

        public override async UniTask<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
        {
            // Get weather
            var weatherResponse = await client.GetJsonAsync<WeatherResponse>($"https://weather.tsukumijima.net/api/forecast/city/{((int)MyLocation).ToString("d6")}");

            // Build response message
            var response = new Response(request.Id);
            response.AddVoiceTTS($"今日の{weatherResponse.location.city}の天気は、{weatherResponse.forecasts[0].telop}。");
            if (weatherResponse.forecasts[0].temperature.max.celsius != null)
            {
                response.AddVoiceTTS($"最高気温は{weatherResponse.forecasts[0].temperature.max.celsius}度の見込みです。");
            }
            else if (weatherResponse.forecasts[0].temperature.min.celsius != null)
            {
                response.AddVoiceTTS($"最低気温は{weatherResponse.forecasts[0].temperature.min.celsius}度の見込みです。");
            }
            else
            {
                response.AddVoiceTTS("気温に関する情報はありません。");
            }

            return response;
        }

        class WeatherResponse
        {
            public Location location;
            public List<Forecast> forecasts;
        }

        class Location
        {
            public string city;
        }

        class Forecast
        {
            public string telop;
            public Temperature temperature;
        }

        class Temperature
        {
            public TemperatureItem max;
            public TemperatureItem min;
        }

        class TemperatureItem
        {
            public string celsius;
        }
    }
}

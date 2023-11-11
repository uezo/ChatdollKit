using System.Collections.Generic;
using System.Threading;
using ChatdollKit.Network;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace ChatdollKit.Examples.ChatGPT
{
    public class ExampleFunctions
    {
        private ChatdollHttp client = new ChatdollHttp(timeout: 20000);

        public async UniTask<string> GetWeatherAsync(string jsonString, CancellationToken token)
        {
            var funcArgs = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonString);
            var locationName = funcArgs.GetValueOrDefault("location");

            var locationCode = string.Empty;
            if (locationName == "札幌")
            {
                locationCode = "016010";
            }
            else if (locationName == "仙台")
            {
                locationCode = "040010";
            }
            else if (locationName == "東京")
            {
                locationCode = "130010";
            }
            else if (locationName == "名古屋")
            {
                locationCode = "230010";
            }
            else if (locationName == "大阪")
            {
                locationCode = "270000";
            }
            else if (locationName == "広島")
            {
                locationCode = "340010";
            }
            else if (locationName == "福岡")
            {
                locationCode = "400010";
            }
            else if (locationName == "那覇")
            {
                locationCode = "471010";
            }

            if (string.IsNullOrEmpty(locationName) || string.IsNullOrEmpty(locationCode))
            {
                return "ロケーションが指定されていないか、不明なロケーションです。ユーザーに質問してください。";
            }

            // Get weather
            var weatherResponse = await client.GetJsonAsync<WeatherResponse>($"https://weather.tsukumijima.net/api/forecast/city/{locationCode}", cancellationToken: token);

            var ret = $"- 都市: {weatherResponse.location.city}\n- 天気: {weatherResponse.forecasts[0].telop}\n";

            if (weatherResponse.forecasts[0].temperature.max.celsius != null)
            {
                ret += $"- 最高気温: {weatherResponse.forecasts[0].temperature.max.celsius}度";
            }
            else if (weatherResponse.forecasts[0].temperature.min.celsius != null)
            {
                ret += $"- 最低気温: {weatherResponse.forecasts[0].temperature.min.celsius}度";
            }
            else
            {
                ret += "- 気温: 情報なし";
            }

            return $"以下は天気予報の確認結果です。あなたの言葉でユーザーに伝えてください。\n\n\"\"\"{ret}\"\"\"";
        }

        private class WeatherResponse
        {
            public Location location;
            public List<Forecast> forecasts;
        }

        private class Location
        {
            public string city;
        }

        private class Forecast
        {
            public string telop;
            public Temperature temperature;
        }

        private class Temperature
        {
            public TemperatureItem max;
            public TemperatureItem min;
        }

        private class TemperatureItem
        {
            public string celsius;
        }

#pragma warning disable CS1998
        public async UniTask<string> GetBalanceAsync(string jsonString, CancellationToken token)
#pragma warning restore CS1998
        {
            var funcArgs = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonString);
            var bank_name = funcArgs.GetValueOrDefault("bank_name");

            var balances = new Dictionary<string, int>()
            {
                { "自由が丘銀行", 1234567 },
                { "目黒銀行", 2345678 },
                { "大岡山信金", 3456789 },
            };

            var ret = string.Empty;
            if (string.IsNullOrEmpty(bank_name))
            {
                foreach (var b in balances)
                {
                    ret += $"- {b.Key}: {b.Value}\n";
                }
            }
            else if (!balances.ContainsKey(bank_name))
            {
                ret = $"{bank_name}には預金口座がありません。";
            }
            else
            {
                ret = $"- {bank_name}: {balances[bank_name]}\n";
            }

            return $"以下は銀行預金残高の確認結果です。あなたの言葉でユーザーに伝えてください。\n\n\"\"\"{ret}\"\"\"";
        }
    }
}

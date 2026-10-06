using System.Globalization;
using System.Text.Json;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Models;
using ImmichFrame.Core.Interfaces;

public class OpenWeatherMapService : IWeatherService
{
    private readonly IGeneralSettings _settings;
    private readonly IApiCache _weatherCache = new ApiCache(TimeSpan.FromMinutes(5));
    private readonly IApiCache _forecastCache = new ApiCache(TimeSpan.FromMinutes(15));
    private static readonly HttpClient ForecastHttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    public OpenWeatherMapService(IGeneralSettings settings)
    {
        _settings = settings;
    }

    public async Task<IWeather?> GetWeather()
    {
        return await _weatherCache.GetOrAddAsync("weather", async () =>
        {
            var weatherLatLong = _settings.WeatherLatLong;

            var weatherLat = !string.IsNullOrWhiteSpace(weatherLatLong) ? float.Parse(weatherLatLong!.Split(',')[0]) : 0f;
            var weatherLong = !string.IsNullOrWhiteSpace(weatherLatLong) ? float.Parse(weatherLatLong!.Split(',')[1]) : 0f;

            var weather = await GetWeather(weatherLat, weatherLong);

            return weather;
        });
    }

    public async Task<IWeather?> GetWeather(double latitude, double longitude)
    {
        OpenWeatherMap.OpenWeatherMapOptions options = new OpenWeatherMap.OpenWeatherMapOptions
        {
            ApiKey = _settings.WeatherApiKey,
            UnitSystem = _settings.UnitSystem,
            Language = _settings.Language,
        };

        try
        {
            OpenWeatherMap.IOpenWeatherMapService openWeatherMapService = new OpenWeatherMap.OpenWeatherMapService(options);
            var weatherInfo = await openWeatherMapService.GetCurrentWeatherAsync(latitude, longitude);

            return weatherInfo.ToWeather();
        }
        catch
        {
            //do nothing and return null
        }

        return null;
    }

    public async Task<IList<WeatherForecastEntry>> GetForecast(int count)
    {
        // The free OpenWeatherMap plan only offers the 5 day forecast in 3 hour steps.
        var all = await _forecastCache.GetOrAddAsync("forecast", async () =>
        {
            var entries = new List<WeatherForecastEntry>();
            var weatherLatLong = _settings.WeatherLatLong;
            if (string.IsNullOrWhiteSpace(_settings.WeatherApiKey) || string.IsNullOrWhiteSpace(weatherLatLong))
                return entries;

            var parts = weatherLatLong.Split(',');
            var lat = double.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
            var lon = double.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
            var units = string.IsNullOrWhiteSpace(_settings.UnitSystem) ? "metric" : _settings.UnitSystem;
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.openweathermap.org/data/2.5/forecast?lat={lat}&lon={lon}&units={Uri.EscapeDataString(units)}&lang={Uri.EscapeDataString(_settings.Language)}&cnt=8&appid={Uri.EscapeDataString(_settings.WeatherApiKey)}");

            try
            {
                using var stream = await ForecastHttpClient.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);
                foreach (var item in doc.RootElement.GetProperty("list").EnumerateArray())
                {
                    var weather = item.GetProperty("weather")[0];
                    entries.Add(new WeatherForecastEntry
                    {
                        Time = DateTimeOffset.FromUnixTimeSeconds(item.GetProperty("dt").GetInt64()),
                        Temperature = item.GetProperty("main").GetProperty("temp").GetDouble(),
                        Description = weather.GetProperty("description").GetString() ?? "",
                        IconId = weather.GetProperty("icon").GetString() ?? ""
                    });
                }
            }
            catch
            {
                //do nothing and return what we have
            }

            return entries;
        });

        return all.Where(e => e.Time > DateTimeOffset.UtcNow).Take(count).ToList();
    }
}

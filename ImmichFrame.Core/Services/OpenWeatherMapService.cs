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
    private readonly IApiCache _brightnessCache = new ApiCache(TimeSpan.FromMinutes(15));
    private readonly IApiCache _detailsCache = new ApiCache(TimeSpan.FromMinutes(30));
    private static readonly HttpClient ForecastHttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    public OpenWeatherMapService(IGeneralSettings settings)
    {
        _settings = settings;
    }

    public async Task<IWeather?> GetWeather()
    {
        var weather = await _weatherCache.GetOrAddAsync("weather", async () =>
        {
            var weatherLatLong = _settings.WeatherLatLong;

            var weatherLat = !string.IsNullOrWhiteSpace(weatherLatLong) ? float.Parse(weatherLatLong!.Split(',')[0]) : 0f;
            var weatherLong = !string.IsNullOrWhiteSpace(weatherLatLong) ? float.Parse(weatherLatLong!.Split(',')[1]) : 0f;

            var weather = await GetWeather(weatherLat, weatherLong);

            return weather;
        });

        // OpenWeatherMap names the nearest place in its own database, which is often a district.
        if (weather != null && !string.IsNullOrWhiteSpace(_settings.WeatherLocationName))
            weather.Location = _settings.WeatherLocationName.Trim();

        return weather;
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

    private const int ForecastStepHours = 2;

    public async Task<IList<WeatherForecastEntry>> GetForecast(int count)
    {
        // OpenWeatherMap's free plan only has 3 hour steps, so the forecast comes from Open-Meteo,
        // which offers hourly data without an API key. Icons are mapped to OpenWeatherMap icon ids
        // so the configured WeatherIconUrl keeps working.
        var all = await GetOrFetch(_forecastCache, "forecast", new List<WeatherForecastEntry>(), async () =>
        {
            var entries = new List<WeatherForecastEntry>();
            var weatherLatLong = _settings.WeatherLatLong;
            if (string.IsNullOrWhiteSpace(weatherLatLong))
                return entries;

            var parts = weatherLatLong.Split(',');
            var lat = double.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
            var lon = double.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
            var fahrenheit = string.Equals(_settings.UnitSystem, "imperial", StringComparison.OrdinalIgnoreCase);
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&hourly=temperature_2m,weather_code,is_day&forecast_hours=24&timezone=UTC&timeformat=unixtime{(fahrenheit ? "&temperature_unit=fahrenheit" : "")}");

            using var stream = await ForecastHttpClient.GetStreamAsync(url);
            using var doc = await JsonDocument.ParseAsync(stream);
            var hourly = doc.RootElement.GetProperty("hourly");
            var times = hourly.GetProperty("time");
            var temps = hourly.GetProperty("temperature_2m");
            var codes = hourly.GetProperty("weather_code");
            var isDay = hourly.GetProperty("is_day");
            var german = _settings.Language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
            for (var i = 0; i < times.GetArrayLength(); i++)
            {
                var code = codes[i].GetInt32();
                entries.Add(new WeatherForecastEntry
                {
                    Time = DateTimeOffset.FromUnixTimeSeconds(times[i].GetInt64()),
                    Temperature = temps[i].GetDouble(),
                    Description = WeatherCodeDescription(code, german),
                    IconId = WeatherCodeIcon(code) + (isDay[i].GetInt32() == 1 ? "d" : "n")
                });
            }

            return entries;
        });

        // Next full hours in 2 hour steps, aligned to even local hours (e.g. 20:00, 22:00, 00:00).
        var now = DateTimeOffset.Now;
        return all
            .Where(e => e.Time > now && e.Time.ToLocalTime().Hour % ForecastStepHours == 0)
            .Take(count)
            .ToList();
    }

    // WMO weather interpretation codes, see https://open-meteo.com/en/docs
    private static string WeatherCodeIcon(int code) => code switch
    {
        0 => "01",
        1 => "02",
        2 => "03",
        3 => "04",
        45 or 48 => "50",
        51 or 53 or 55 or 56 or 57 or 80 or 81 or 82 => "09",
        61 or 63 or 65 or 66 or 67 => "10",
        71 or 73 or 75 or 77 or 85 or 86 => "13",
        95 or 96 or 99 => "11",
        _ => "03"
    };

    private static string WeatherCodeDescription(int code, bool german) => code switch
    {
        0 => german ? "Klar" : "Clear",
        1 or 2 => german ? "Leicht bewölkt" : "Partly cloudy",
        3 => german ? "Bedeckt" : "Overcast",
        45 or 48 => german ? "Nebel" : "Fog",
        51 or 53 or 55 or 56 or 57 => german ? "Nieselregen" : "Drizzle",
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => german ? "Regen" : "Rain",
        71 or 73 or 75 or 77 or 85 or 86 => german ? "Schnee" : "Snow",
        95 or 96 or 99 => german ? "Gewitter" : "Thunderstorm",
        _ => ""
    };

    private const int DetailHours = 25;
    // Today plus the next five days.
    private const int DetailDays = 6;

    public async Task<WeatherDetails> GetWeatherDetails()
    {
        // Hourly and daily forecast for the weather overlay, also from Open-Meteo.
        var details = await GetOrFetch(_detailsCache, "details", new WeatherDetails(), async () =>
        {
            var result = new WeatherDetails();
            var weatherLatLong = _settings.WeatherLatLong;
            if (string.IsNullOrWhiteSpace(weatherLatLong))
                return result;

            var parts = weatherLatLong.Split(',');
            var lat = double.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
            var lon = double.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
            var fahrenheit = string.Equals(_settings.UnitSystem, "imperial", StringComparison.OrdinalIgnoreCase);
            const string hourlyFields = "temperature_2m,weather_code,is_day,precipitation_probability,precipitation";
            const string dailyFields = "weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,precipitation_sum,precipitation_probability_max";
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&hourly={hourlyFields}&daily={dailyFields}&forecast_days={DetailDays}&timezone=auto&timeformat=unixtime{(fahrenheit ? "&temperature_unit=fahrenheit&precipitation_unit=inch" : "")}");

            using var stream = await ForecastHttpClient.GetStreamAsync(url);
            using var doc = await JsonDocument.ParseAsync(stream);
            var german = _settings.Language.StartsWith("de", StringComparison.OrdinalIgnoreCase);

            var hourly = doc.RootElement.GetProperty("hourly");
            var hTimes = hourly.GetProperty("time");
            for (var i = 0; i < hTimes.GetArrayLength(); i++)
            {
                var code = hourly.GetProperty("weather_code")[i].GetInt32();
                result.Hours.Add(new WeatherHour
                {
                    Time = DateTimeOffset.FromUnixTimeSeconds(hTimes[i].GetInt64()),
                    Temperature = hourly.GetProperty("temperature_2m")[i].GetDouble(),
                    Description = WeatherCodeDescription(code, german),
                    IconId = WeatherCodeIcon(code) + (hourly.GetProperty("is_day")[i].GetInt32() == 1 ? "d" : "n"),
                    PrecipitationProbability = OptionalInt(hourly.GetProperty("precipitation_probability")[i]),
                    Precipitation = OptionalDouble(hourly.GetProperty("precipitation")[i]) ?? 0d
                });
            }

            var daily = doc.RootElement.GetProperty("daily");
            var dTimes = daily.GetProperty("time");
            for (var i = 0; i < dTimes.GetArrayLength(); i++)
            {
                var code = daily.GetProperty("weather_code")[i].GetInt32();
                var sunrise = daily.GetProperty("sunrise")[i];
                var sunset = daily.GetProperty("sunset")[i];
                result.Days.Add(new WeatherDay
                {
                    Date = DateTimeOffset.FromUnixTimeSeconds(dTimes[i].GetInt64()),
                    TemperatureMax = daily.GetProperty("temperature_2m_max")[i].GetDouble(),
                    TemperatureMin = daily.GetProperty("temperature_2m_min")[i].GetDouble(),
                    Description = WeatherCodeDescription(code, german),
                    IconId = WeatherCodeIcon(code) + "d",
                    PrecipitationProbability = OptionalInt(daily.GetProperty("precipitation_probability_max")[i]),
                    PrecipitationSum = OptionalDouble(daily.GetProperty("precipitation_sum")[i]) ?? 0d,
                    Sunrise = sunrise.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(sunrise.GetInt64()) : null,
                    Sunset = sunset.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(sunset.GetInt64()) : null
                });
            }

            return result;
        });

        // Today from 00:00 to 24:00 (25 hourly points), and the days from today on. Filtering by the local date
        // keeps a series cached shortly before midnight correct after midnight.
        var today = DateTime.Today;
        var todayStart = new DateTimeOffset(today, TimeZoneInfo.Local.GetUtcOffset(today));
        return new WeatherDetails
        {
            Hours = details.Hours.Where(h => h.Time >= todayStart).Take(DetailHours).ToList(),
            Days = details.Days.Where(d => d.Date >= todayStart).ToList()
        };
    }

    // Open-Meteo results are cached, but a failed fetch is not: the exception keeps it out of the cache,
    // so the next request tries again instead of serving an empty result for the whole cache period.
    private static async Task<T> GetOrFetch<T>(IApiCache cache, string key, T fallback, Func<Task<T>> fetch)
    {
        try
        {
            return await cache.GetOrAddAsync(key, fetch);
        }
        catch
        {
            return fallback;
        }
    }

    private static int? OptionalInt(JsonElement element)
        => element.ValueKind == JsonValueKind.Number ? (int)Math.Round(element.GetDouble()) : null;

    private static double? OptionalDouble(JsonElement element)
        => element.ValueKind == JsonValueKind.Number ? element.GetDouble() : null;

    // Global horizontal irradiance at which the screen reaches its maximum brightness. A bright
    // overcast day is around 200-300 W/m², direct midday sun in summer 700-900 W/m².
    private const double FullBrightnessIrradiance = 500d;

    public async Task<ScreenBrightness> GetScreenBrightness()
    {
        if (!_settings.AutoBrightness)
            return new ScreenBrightness { Enabled = false };

        // Outdoor brightness from Open-Meteo's 15 minute shortwave radiation, which covers both the
        // sun's position and the cloud cover. Values in between are interpolated for smooth changes.
        var series = await GetOrFetch(_brightnessCache, "irradiance", new List<(DateTimeOffset Time, double Irradiance)>(), async () =>
        {
            var points = new List<(DateTimeOffset Time, double Irradiance)>();
            var weatherLatLong = _settings.WeatherLatLong;
            if (string.IsNullOrWhiteSpace(weatherLatLong))
                return points;

            var parts = weatherLatLong.Split(',');
            var lat = double.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
            var lon = double.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&minutely_15=shortwave_radiation&past_minutely_15=4&forecast_minutely_15=8&timezone=UTC&timeformat=unixtime");

            using var stream = await ForecastHttpClient.GetStreamAsync(url);
            using var doc = await JsonDocument.ParseAsync(stream);
            var minutely = doc.RootElement.GetProperty("minutely_15");
            var times = minutely.GetProperty("time");
            var values = minutely.GetProperty("shortwave_radiation");
            for (var i = 0; i < times.GetArrayLength(); i++)
            {
                if (values[i].ValueKind == JsonValueKind.Number)
                    points.Add((DateTimeOffset.FromUnixTimeSeconds(times[i].GetInt64()), values[i].GetDouble()));
            }

            return points;
        });

        var min = Math.Clamp(_settings.AutoBrightnessMin, 1, 100) / 100d;
        var max = Math.Clamp(_settings.AutoBrightnessMax, 1, 100) / 100d;
        if (max < min) (min, max) = (max, min);

        var irradiance = InterpolateIrradiance(series, DateTimeOffset.UtcNow);
        if (irradiance == null)
            return new ScreenBrightness { Enabled = true, Brightness = max };

        // Square root, because perceived brightness is not linear: dusk should already lift the screen noticeably.
        var level = Math.Sqrt(Math.Clamp(irradiance.Value / FullBrightnessIrradiance, 0d, 1d));
        return new ScreenBrightness
        {
            Enabled = true,
            Brightness = Math.Round(min + (max - min) * level, 3),
            Irradiance = Math.Round(irradiance.Value, 1)
        };
    }

    private static double? InterpolateIrradiance(List<(DateTimeOffset Time, double Irradiance)> series, DateTimeOffset now)
    {
        if (series.Count == 0)
            return null;

        for (var i = 1; i < series.Count; i++)
        {
            var (t0, v0) = series[i - 1];
            var (t1, v1) = series[i];
            if (now >= t0 && now <= t1)
            {
                var fraction = (now - t0).TotalSeconds / (t1 - t0).TotalSeconds;
                return v0 + (v1 - v0) * fraction;
            }
        }

        return now < series[0].Time ? series[0].Irradiance : series[^1].Irradiance;
    }
}

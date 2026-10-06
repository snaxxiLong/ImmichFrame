using ImmichFrame.Core.Interfaces;

public interface IWeatherService
{
    Task<IWeather?> GetWeather();
    Task<IWeather?> GetWeather(double latitude, double longitude);
    Task<IList<ImmichFrame.Core.Models.WeatherForecastEntry>> GetForecast(int count);
}
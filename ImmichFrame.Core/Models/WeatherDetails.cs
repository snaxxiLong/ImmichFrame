namespace ImmichFrame.Core.Models
{
    public class WeatherDetails
    {
        public List<WeatherHour> Hours { get; set; } = new();
        public List<WeatherDay> Days { get; set; } = new();
    }

    public class WeatherHour
    {
        public DateTimeOffset Time { get; set; }
        public double Temperature { get; set; }
        public string Description { get; set; } = "";
        public string IconId { get; set; } = "";
        public int? PrecipitationProbability { get; set; }
        public double Precipitation { get; set; }
    }

    public class WeatherDay
    {
        public DateTimeOffset Date { get; set; }
        public double TemperatureMax { get; set; }
        public double TemperatureMin { get; set; }
        public string Description { get; set; } = "";
        public string IconId { get; set; } = "";
        public int? PrecipitationProbability { get; set; }
        public double PrecipitationSum { get; set; }
        public DateTimeOffset? Sunrise { get; set; }
        public DateTimeOffset? Sunset { get; set; }
    }
}

namespace ImmichFrame.Core.Models
{
    public class WeatherForecastEntry
    {
        public DateTimeOffset Time { get; set; }
        public double Temperature { get; set; }
        public string Description { get; set; } = "";
        public string IconId { get; set; } = "";
    }
}

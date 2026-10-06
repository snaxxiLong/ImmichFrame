namespace ImmichFrame.Core.Interfaces
{
    public interface IServerBehaviorSettings
    {
        public List<string> Webcalendars { get; }
        public int RefreshAlbumPeopleInterval { get; }
        public string? WeatherApiKey { get; }
        public string? WeatherLatLong { get; }
        public string? UnitSystem { get; }
        public bool AutoBrightness { get; }
        public int AutoBrightnessMin { get; }
        public int AutoBrightnessMax { get; }
        public string? Webhook { get; }
        public string? AuthenticationSecret { get; }
        public string? AdminPassword { get; }
    }
}

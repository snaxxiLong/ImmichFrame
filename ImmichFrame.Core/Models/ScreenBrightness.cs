namespace ImmichFrame.Core.Models
{
    public class ScreenBrightness
    {
        public bool Enabled { get; set; }
        /// <summary>Target screen brightness between 0 and 1.</summary>
        public double Brightness { get; set; } = 1d;
        /// <summary>Current outdoor irradiance in W/m² the brightness was derived from, if known.</summary>
        public double? Irradiance { get; set; }
    }
}

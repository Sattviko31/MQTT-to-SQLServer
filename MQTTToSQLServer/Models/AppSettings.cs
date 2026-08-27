namespace MQTTToSQLServer.Models
{
    public class AppSettings
    {
        public int EmaPeriod { get; set; } = 20;
        public string EmaMode { get; set; } = "manual";
        public int EmaUpperThreshold { get; set; } = 30;
        public int EmaLowerThreshold { get; set; } = 50;
        public double EmaFibUpper { get; set; } = 1.618;
        public double EmaFibLower { get; set; } = 0.618;
        public bool EmaShowLine { get; set; } = true;
        public bool EmaShowThresholds { get; set; } = true;
        public int RefreshInterval { get; set; } = 10;
        public int ChartDataPoints { get; set; } = 20;
        public decimal? TariffPerKWh { get; set; } = 1500m;
        public bool useInitial100ForEma { get; set; } = false; // Added for EMA initial data points setting
    }
}

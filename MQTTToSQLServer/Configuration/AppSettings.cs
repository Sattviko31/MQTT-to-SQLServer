#nullable enable
namespace MQTTToSQLServer.Configuration
{
    public class MqttSettings
    {
        public string BrokerHost { get; set; } = "192.168.168.38";
        public int BrokerPort { get; set; } = 1883;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string Topic { get; set; } = "#";
        public string ClientIdPrefix { get; set; } = "KWHApp_";
    }

    public class ProcessingSettings
    {
        public int MonitorIntervalSeconds { get; set; } = 5;
        public int MaxRetries { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 500;
    }

    public class AppConfiguration
    {
        public string DefaultConnection { get; set; } = string.Empty;
        public MqttSettings Mqtt { get; set; } = new MqttSettings();
        public ProcessingSettings Processing { get; set; } = new ProcessingSettings();
    }
}

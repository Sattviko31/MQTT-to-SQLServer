namespace KWHMonitoring.Configuration;

public class AppConfig
{
    public MqttConfig Mqtt { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
}

public class MqttConfig
{
    public string BrokerIp { get; set; } = "192.168.150.10";
    public int Port { get; set; } = 1883;
    public string? Username { get; set; } = "";
    public string? Password { get; set; } = "";
}

public class DatabaseConfig
{
    public string Server { get; set; } = "192.168.168.38";
    public string DatabaseName { get; set; } = "HaiwellElectrical";
    public string Username { get; set; } = "kwhapp";
    public string Password { get; set; } = "kwhapp1234";

    public string GetConnectionString() =>
        $"Server={Server},1433;Database={DatabaseName};User Id={Username};Password={Password};Encrypt=False;TrustServerCertificate=True;";

    public string GetMasterConnectionString() =>
        $"Server={Server},1433;Database=master;User Id={Username};Password={Password};Encrypt=False;TrustServerCertificate=True;";
}
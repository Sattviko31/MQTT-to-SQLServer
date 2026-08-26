using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace MQTTToSQLServer
{
    public class DynamicMqttData
    {
        public Dictionary<string, string?> Properties { get; set; } = new();
        public string? GetProperty(string name) =>
            Properties.TryGetValue(name, out string? value) ? value : null;
    }

    public class MqttMessageBuffer
    {
        public string Topic { get; set; } = "";
        public string Payload { get; set; } = "";
        public DateTime ReceivedAt { get; set; } = DateTime.Now;
    }

    class Program
    {
        // Konfigurasi MQTT
        private static string mqttIp = "192.168.6.15";
        private static int mqttPort = 1883;
        private static string? mqttUsername;
        private static string? mqttPassword;

        // Konfigurasi Database
        private static string sqlServer = "192.168.6.15";
        private static string sqlDatabase = "HaiwellElectrical";
        private static string sqlUser = "kwhapp";
        private static string sqlPassword = "kwhapp1234";

        private static string connectionString = "";
        private static IMqttClient? mqttClient = null!;
        private static long messageCount = 0;
        private static long errorCount = 0;
        private static long successCount = 0;

        private static readonly ConcurrentQueue<MqttMessageBuffer> MessageQueue = new();
        private static readonly ConcurrentDictionary<string, string> DeviceKeyCache = new();
        private static HashSet<string> ExistingColumns = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, decimal> ScaleConfig = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> ColumnMapping = new(StringComparer.OrdinalIgnoreCase);

        static async Task Main(string[] args)
        {
            Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   HAIWELL ELECTRICAL - MQTT TO SQL SERVER                ║");
            Console.WriteLine("║   Version 6.1 - Final Production Ready                   ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Setup konfigurasi
            SetupConfiguration();

            // Setup koneksi
            SetupConnections();

            // Load konfigurasi database
            await LoadConfigurationsAsync();

            _ = Task.Run(() => ProcessQueueAsync());
            _ = Task.Run(() => MonitorQueueAsync());

            await ConnectAndSubscribeAsync();

            Console.WriteLine("\nAplikasi berjalan. Tekan Enter untuk keluar...");
            Console.ReadLine();

            Console.WriteLine("Shutting down...");
            await Task.Delay(3000);
            await DisconnectAsync();
        }

        private static void SetupConfiguration()
        {
            Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   SETTING KONFIGURASI                                    ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Konfigurasi MQTT
            Console.WriteLine("=== Konfigurasi MQTT ===");
            Console.Write("MQTT Broker IP (default: 192.168.6.15): ");
            string inputMqttIp = Console.ReadLine() ?? "";
            if (!string.IsNullOrEmpty(inputMqttIp))
                mqttIp = inputMqttIp;

            Console.Write("MQTT Port (default: 1883): ");
            string inputMqttPort = Console.ReadLine() ?? "";
            if (!string.IsNullOrEmpty(inputMqttPort) && int.TryParse(inputMqttPort, out int port))
                mqttPort = port;

            Console.Write("MQTT Username (opsional, tekan Enter jika tidak ada): ");
            mqttUsername = Console.ReadLine();

            Console.Write("MQTT Password (opsional, tekan Enter jika tidak ada): ");
            mqttPassword = Console.ReadLine();

            Console.WriteLine("\n=== Konfigurasi Database ===");
            Console.Write("SQL Server (default: 192.168.6.15): ");
            string inputSqlServer = Console.ReadLine() ?? "";
            if (!string.IsNullOrEmpty(inputSqlServer))
                sqlServer = inputSqlServer;

            Console.Write("Database Name (default: HaiwellElectrical): ");
            string inputSqlDatabase = Console.ReadLine() ?? "";
            if (!string.IsNullOrEmpty(inputSqlDatabase))
                sqlDatabase = inputSqlDatabase;

            Console.Write("SQL Username (default: kwhapp): ");
            string inputSqlUser = Console.ReadLine() ?? "";
            if (!string.IsNullOrEmpty(inputSqlUser))
                sqlUser = inputSqlUser;

            Console.Write("SQL Password (default: kwhapp1234): ");
            string inputSqlPassword = Console.ReadLine() ?? "";
            if (!string.IsNullOrEmpty(inputSqlPassword))
                sqlPassword = inputSqlPassword;

            Console.WriteLine("\n╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   KONFIGURASI YANG AKAN DIGUNAKAN                        ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
            Console.WriteLine($"- MQTT Broker: {mqttIp}:{mqttPort}");
            Console.WriteLine($"- MQTT Username: {(string.IsNullOrEmpty(mqttUsername) ? "Tidak ada" : mqttUsername)}");
            Console.WriteLine($"- SQL Server: {sqlServer}");
            Console.WriteLine($"- Database: {sqlDatabase}");
            Console.WriteLine($"- SQL User: {sqlUser}");
            Console.WriteLine("\nApakah konfigurasi ini sudah benar? (Y/N)");

            char confirm = Console.ReadKey().KeyChar;
            Console.WriteLine();
            if (char.ToUpper(confirm) != 'Y')
            {
                Console.WriteLine("\n\nSilakan jalankan ulang program untuk mengonfigurasi ulang.");
                Environment.Exit(0);
            }
        }

        private static void SetupConnections()
        {
            // Setup koneksi database
            connectionString = $"Server={sqlServer},1433;Database={sqlDatabase};User Id={sqlUser};Password={sqlPassword};Encrypt=False;TrustServerCertificate=True;";

            Console.WriteLine("\n[✓] Konfigurasi koneksi database selesai");
            Console.WriteLine($"  - SQL Server: {sqlServer}");
            Console.WriteLine($"  - Database: {sqlDatabase}");
            Console.WriteLine($"  - User: {sqlUser}");
        }

        private static async Task LoadConfigurationsAsync()
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    // Load Scale Config (case-insensitive)
                    using (SqlCommand cmd = new SqlCommand("SELECT ColumnName, ScaleFactor FROM ColumnScaleConfig", conn))
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            ScaleConfig[reader.GetString(0).ToUpper()] = reader.GetDecimal(1);
                    }

                    // Load Column Mapping (case-insensitive)
                    using (SqlCommand cmd = new SqlCommand("SELECT OldColumnName, NewColumnName FROM ColumnMapping WHERE IsActive = 1", conn))
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            ColumnMapping[reader.GetString(0).ToUpper()] = reader.GetString(1);
                    }

                    // Load Existing Columns (case-insensitive)
                    using (SqlCommand cmd = new SqlCommand(
                        "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'KWHData'", conn))
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            ExistingColumns.Add(reader.GetString(0).ToUpper());
                    }
                }

                Console.WriteLine($"[✓] Loaded {ScaleConfig.Count} scale configurations");
                Console.WriteLine($"[✓] Loaded {ColumnMapping.Count} column mappings");
                Console.WriteLine($"[✓] Loaded {ExistingColumns.Count} existing columns");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Failed to load configurations: {ex.Message}");
                Console.WriteLine("[!] Using fallback defaults...");

                // Fallback dengan nama kolom UPPERCASE
                ScaleConfig = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                {
                    { "PHASE_R", 10 }, { "PHASE_S", 10 }, { "PHASE_T", 10 },
                    { "AMPERE_R", 1000 }, { "AMPERE_S", 1000 }, { "AMPERE_T", 1000 },
                    { "COSPHI", 1000 }, { "W", 10 },
                    { "AKTIF_POWER", 100 }, { "TOTALW", 100 }, { "TOTALW1M", 100 },
                    { "F", 100 }
                };
                ColumnMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "VR", "PHASE_R" }, { "VS", "PHASE_S" }, { "VT", "PHASE_T" },
                    { "AKTIF_W", "Aktif_Power" }
                };
            }
        }

        private static async Task ConnectAndSubscribeAsync()
        {
            try
            {
                var factory = new MqttFactory();
                mqttClient = factory.CreateMqttClient();

                mqttClient.ConnectedAsync += e =>
                {
                    Console.WriteLine("[✓] Connected to MQTT Broker");
                    return Task.CompletedTask;
                };

                mqttClient.DisconnectedAsync += e =>
                {
                    Console.WriteLine($"[✗] Disconnected: {e.Reason}");
                    return Task.CompletedTask;
                };

                mqttClient.ApplicationMessageReceivedAsync += e =>
                {
                    MessageQueue.Enqueue(new MqttMessageBuffer
                    {
                        Topic = e.ApplicationMessage.Topic,
                        Payload = e.ApplicationMessage.ConvertPayloadToString() ?? "",
                        ReceivedAt = DateTime.Now
                    });
                    return Task.CompletedTask;
                };

                // 1. Buat Builder
                var optionsBuilder = new MqttClientOptionsBuilder()
                    .WithTcpServer(mqttIp, mqttPort)
                    .WithClientId($"KWHApp_{Guid.NewGuid():N}".Substring(0, 20))
                    .WithCleanSession();

                // 2. Tambahkan Credentials jika ada (harus di-assign ulang)
                if (!string.IsNullOrEmpty(mqttUsername))
                {
                    optionsBuilder = optionsBuilder.WithCredentials(mqttUsername, mqttPassword);
                }

                // 3. PERBAIKAN: Tambahkan .Build() untuk mengubah Builder menjadi Options
                var clientOptions = optionsBuilder.Build();

                // 4. Connect menggunakan clientOptions
                await mqttClient.ConnectAsync(clientOptions);

                await mqttClient.SubscribeAsync("#", MqttQualityOfServiceLevel.AtMostOnce);
                Console.WriteLine("[✓] Subscribed to all topics (#)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[✗] Connection error: {ex.Message}");
            }
        }

        private static async Task ProcessQueueAsync()
        {
            while (true)
            {
                try
                {
                    if (MessageQueue.TryDequeue(out MqttMessageBuffer? buffer))
                        await ProcessSingleMessageAsync(buffer);
                    else
                        await Task.Delay(50);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[✗] Queue error: {ex.Message}");
                    await Task.Delay(1000);
                }
            }
        }

        private static async Task MonitorQueueAsync()
        {
            while (true)
            {
                await Task.Delay(5000);
                Console.WriteLine($"[STATS] Total: {messageCount} | Success: {successCount} | Errors: {errorCount} | Queue: {MessageQueue.Count}");
            }
        }

        private static async Task ProcessSingleMessageAsync(MqttMessageBuffer buffer)
        {
            try
            {
                if (string.IsNullOrEmpty(buffer.Payload)) return;

                var mqttData = ParseDynamicJson(buffer.Payload);
                if (mqttData == null)
                {
                    await LogFailedMessage(buffer, "JSON parse failed");
                    return;
                }

                ApplyColumnMapping(mqttData);

                string deviceId = ExtractDeviceId(buffer.Topic, mqttData);
                string deviceKey = await GetOrCreateDeviceKeyAsync(deviceId, mqttData.GetProperty("_groupName"));

                await EnsureColumnsExistAsync(mqttData);
                await SaveToDatabaseAsync(mqttData, deviceId, deviceKey);

                successCount++;
                messageCount++;
            }
            catch (Exception ex)
            {
                errorCount++;
                messageCount++;
                Console.WriteLine($"[✗] Error: {ex.Message}");
                await LogFailedMessage(buffer, ex.Message);
            }
        }

        private static DynamicMqttData? ParseDynamicJson(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var data = new DynamicMqttData();
                foreach (var prop in doc.RootElement.EnumerateObject())
                    data.Properties[prop.Name] = prop.Value.ToString();
                return data;
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyColumnMapping(DynamicMqttData data)
        {
            var newProperties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var keysToRemove = new List<string>();

            foreach (var kvp in data.Properties)
            {
                string keyUpper = kvp.Key.ToUpper();

                if (ColumnMapping.TryGetValue(keyUpper, out string? mappedName))
                {
                    newProperties[mappedName] = kvp.Value;
                    keysToRemove.Add(kvp.Key);
                }
                else
                {
                    newProperties[kvp.Key] = kvp.Value;
                }
            }

            foreach (var key in keysToRemove)
            {
                data.Properties.Remove(key);
            }

            foreach (var kvp in newProperties)
            {
                if (!data.Properties.ContainsKey(kvp.Key))
                {
                    data.Properties[kvp.Key] = kvp.Value;
                }
            }
        }

        private static string ExtractDeviceId(string topic, DynamicMqttData data)
        {
            string? groupName = data.GetProperty("_groupName");
            if (!string.IsNullOrEmpty(groupName) && groupName.ToUpper().Contains("KWHMETER"))
                return $"kwhapp{groupName.ToUpper().Replace("KWHMETER", "")}";

            string[] parts = topic.Split('/');
            if (parts.Length >= 3) return parts[2];

            return "unknown";
        }

        private static async Task<string> GetOrCreateDeviceKeyAsync(string deviceId, string? groupName)
        {
            if (DeviceKeyCache.TryGetValue(deviceId, out string? cached))
                return cached;

            // DeviceKey sama dengan DeviceId dalam huruf kapital
            string deviceKey = deviceId.ToUpper();

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();

                // Cek apakah device sudah terdaftar
                using (SqlCommand checkCmd = new SqlCommand(
                    "SELECT DeviceKey FROM DeviceRegistry WHERE DeviceId = @DeviceId", conn))
                {
                    checkCmd.Parameters.AddWithValue("@DeviceId", deviceId);
                    var existingKey = await checkCmd.ExecuteScalarAsync();

                    if (existingKey != null)
                    {
                        // Device sudah ada, gunakan DeviceKey yang sudah ada (untuk menghindari FK conflict)
                        deviceKey = existingKey.ToString()!;

                        // Update info lainnya saja
                        using (SqlCommand updateCmd = new SqlCommand(@"
                            UPDATE DeviceRegistry 
                            SET LastSeen = GETDATE(), 
                                GroupName = ISNULL(@GroupName, GroupName), 
                                UpdatedAt = GETDATE()
                            WHERE DeviceId = @DeviceId", conn))
                        {
                            updateCmd.Parameters.AddWithValue("@DeviceId", deviceId);
                            updateCmd.Parameters.AddWithValue("@GroupName", (object?)groupName ?? DBNull.Value);
                            await updateCmd.ExecuteNonQueryAsync();
                        }
                    }
                    else
                    {
                        // Device baru, insert dengan DeviceKey format baru
                        using (SqlCommand insertCmd = new SqlCommand(@"
                            INSERT INTO DeviceRegistry (DeviceKey, DeviceId, GroupName, FirstSeen, LastSeen)
                            VALUES (@DeviceKey, @DeviceId, @GroupName, GETDATE(), GETDATE())", conn))
                        {
                            insertCmd.Parameters.AddWithValue("@DeviceKey", deviceKey);
                            insertCmd.Parameters.AddWithValue("@DeviceId", deviceId);
                            insertCmd.Parameters.AddWithValue("@GroupName", (object?)groupName ?? DBNull.Value);
                            await insertCmd.ExecuteNonQueryAsync();
                        }
                    }
                }
            }

            DeviceKeyCache.TryAdd(deviceId, deviceKey);
            Console.WriteLine($"[+] Registered: {deviceId} -> {deviceKey}");
            return deviceKey;
        }

        private static async Task EnsureColumnsExistAsync(DynamicMqttData data)
        {
            var systemCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "_terminalTime", "_groupName", "Id", "DeviceKey", "TerminalTime",
                "GroupName", "DeviceId", "ReceivedTime"
            };

            foreach (var key in data.Properties.Keys.ToList())
            {
                if (systemCols.Contains(key) || ExistingColumns.Contains(key)) continue;

                await AddColumnToDatabaseAsync(key);
                ExistingColumns.Add(key);
                Console.WriteLine($"[+] New column added: {key}");
            }
        }

        private static async Task AddColumnToDatabaseAsync(string columnName)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand($"ALTER TABLE KWHData ADD [{columnName}] DECIMAL(18,3) NULL", conn))
                        await cmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[✗] Failed to add column {columnName}: {ex.Message}");
            }
        }

        private static async Task SaveToDatabaseAsync(DynamicMqttData data, string deviceId, string deviceKey)
        {
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    using (SqlConnection conn = new SqlConnection(connectionString))
                    {
                        await conn.OpenAsync();

                        var columns = new List<string> { "DeviceKey", "TerminalTime", "GroupName", "DeviceId" };
                        var values = new List<string> { "@DeviceKey", "@TerminalTime", "@GroupName", "@DeviceId" };
                        var parameters = new List<SqlParameter>
                        {
                            new("@DeviceKey", deviceKey),
                            new("@TerminalTime", ParseDateTime(data.GetProperty("_terminalTime"))),
                            new("@GroupName", (object?)data.GetProperty("_groupName") ?? DBNull.Value),
                            new("@DeviceId", deviceId)
                        };

                        foreach (var kvp in data.Properties)
                        {
                            if (kvp.Key.StartsWith("_")) continue;
                            if (!ExistingColumns.Contains(kvp.Key)) continue;

                            columns.Add($"[{kvp.Key}]");
                            values.Add($"@{kvp.Key}");
                            parameters.Add(new($"@{kvp.Key}", ParseValueWithScale(kvp.Key, kvp.Value)));
                        }

                        string query = $"INSERT INTO KWHData ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";

                        using (SqlCommand cmd = new SqlCommand(query, conn))
                        {
                            cmd.Parameters.AddRange(parameters.ToArray());
                            await cmd.ExecuteNonQueryAsync();
                        }

                        using (SqlCommand cmd = new SqlCommand(
                            "UPDATE DeviceRegistry SET MessageCount = MessageCount + 1, LastSeen = GETDATE(), UpdatedAt = GETDATE() WHERE DeviceKey = @DeviceKey", conn))
                        {
                            cmd.Parameters.AddWithValue("@DeviceKey", deviceKey);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                    return;
                }
                catch (Exception)
                {
                    if (retry == 2) throw;
                    await Task.Delay(500 * (retry + 1));
                }
            }
        }

        private static decimal ParseValueWithScale(string columnName, string? value)
        {
            if (string.IsNullOrEmpty(value) || !decimal.TryParse(value, out decimal raw))
                return 0;

            string colUpper = columnName.ToUpper();

            // VOLTAGE - Dynamic Scaling
            if (colUpper is "PHASE_R" or "PHASE_S" or "PHASE_T")
            {
                decimal result;
                if (raw >= 10000m)
                    result = raw / 100m;
                else if (raw >= 1000m)
                    result = raw / 10m;
                else if (raw > 500m)
                    result = raw / 10m;
                else
                    result = raw;

                return Math.Round(result, 2);
            }

            // Current
            if (colUpper is "AMPERE_R" or "AMPERE_S" or "AMPERE_T")
                return Math.Round(raw / 10m, 2);

            // Power Factor
            if (colUpper == "COSPHI")
                return Math.Round(raw / 10m, 3);

            // Active Power
            if (colUpper == "W")
                return Math.Round(raw / 10m, 1);

            // Energy
            if (colUpper is "TOTALW1M" or "AKTIF_POWER" or "TOTALW")
                return Math.Round(raw / 100m, 2);

            // Frequency
            if (colUpper == "F")
                return Math.Round(raw / 10m, 2);

            return raw;
        }

        private static DateTime ParseDateTime(string? str)
        {
            if (string.IsNullOrEmpty(str)) return DateTime.Now;
            if (DateTime.TryParseExact(str, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
                return result;
            if (DateTime.TryParse(str, out DateTime result2)) return result2;
            return DateTime.Now;
        }

        private static async Task LogFailedMessage(MqttMessageBuffer buffer, string reason)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(
                        "INSERT INTO FailedMessages (Topic, Payload, Reason, ReceivedAt) VALUES (@Topic, @Payload, @Reason, @ReceivedAt)", conn))
                    {
                        cmd.Parameters.AddWithValue("@Topic", buffer.Topic);
                        cmd.Parameters.AddWithValue("@Payload", buffer.Payload);
                        cmd.Parameters.AddWithValue("@Reason", reason);
                        cmd.Parameters.AddWithValue("@ReceivedAt", buffer.ReceivedAt);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch { }
        }

        private static async Task DisconnectAsync()
        {
            if (mqttClient?.IsConnected == true)
            {
                await mqttClient.DisconnectAsync();
                mqttClient.Dispose();
            }
            Console.WriteLine($"\n╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║   Final Stats: Total={messageCount} | Success={successCount} | Errors={errorCount}   ║");
            Console.WriteLine($"╚══════════════════════════════════════════════════════════╝");
            Console.WriteLine("Application stopped.");
        }
    }
}
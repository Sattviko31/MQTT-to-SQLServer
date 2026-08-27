#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTToSQLServer.Configuration;
using MQTTToSQLServer.Models;

namespace MQTTToSQLServer
{
    public class DynamicMqttData
    {
        public Dictionary<string, string?> Properties { get; set; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
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
        private static AppConfiguration _config = new AppConfiguration();
        private static string _connectionString = "";
        private static IMqttClient? _mqttClient;
        private static readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private static long _messageCount = 0;
        private static long _errorCount = 0;
        private static long _successCount = 0;

        private static readonly ConcurrentQueue<MqttMessageBuffer> MessageQueue = new ConcurrentQueue<MqttMessageBuffer>();
        private static readonly ConcurrentDictionary<string, string> DeviceKeyCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ExistingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, decimal> ScaleConfig = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> ColumnMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex SafeSqlColumnNameRegex = new Regex(@"^[a-zA-Z0-9_]+$", RegexOptions.Compiled);

        static async Task Main(string[] args)
        {
            Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   HAIWELL ELECTRICAL - MQTT TO SQL SERVER                ║");
            Console.WriteLine("║   Version 7.0 - Production Architecture (EF Core + JSON) ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                _cts.Cancel();
                Console.WriteLine("\n[!] Shutdown signal received. Draining queue...");
            };

            // 1. Load Configuration from appsettings.json
            LoadConfiguration();

            // 2. Run Database Migrations / Ensure Database Exists
            await InitializeDatabaseAsync();

            // 3. Load Configurations (ScaleConfig, ColumnMapping, ExistingColumns)
            await LoadConfigurationsAsync();

            // 4. Start Background Workers
            var queueTask = Task.Run(() => ProcessQueueAsync(_cts.Token));
            var monitorTask = Task.Run(() => MonitorQueueAsync(_cts.Token));

            // 5. Connect and Subscribe to MQTT
            await ConnectAndSubscribeAsync();

            Console.WriteLine("\n[✓] Service is running. Press Ctrl+C or Enter to exit.");

            var readKeyTask = Task.Run(() => Console.ReadLine());
            await Task.WhenAny(readKeyTask, Task.Delay(-1, _cts.Token)).ContinueWith(_ => { });

            _cts.Cancel();
            Console.WriteLine("\nStopping MQTT client and shutting down...");
            await DisconnectAsync();

            await Task.WhenAll(queueTask, monitorTask);
            Console.WriteLine("Application exited cleanly.");
        }

        private static void LoadConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();

            var configuration = builder.Build();

            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? "Server=192.168.6.15,1433;Database=HaiwellElectrical;User Id=kwhapp;Password=kwhapp1234;Encrypt=False;TrustServerCertificate=True;";

            _config.DefaultConnection = _connectionString;

            var mqttSection = configuration.GetSection("MqttSettings");
            if (mqttSection.Exists())
            {
                _config.Mqtt = mqttSection.Get<MqttSettings>() ?? new MqttSettings();
            }

            var procSection = configuration.GetSection("ProcessingSettings");
            if (procSection.Exists())
            {
                _config.Processing = procSection.Get<ProcessingSettings>() ?? new ProcessingSettings();
            }

            Console.WriteLine("[✓] Configuration loaded successfully:");
            Console.WriteLine($"  - MQTT Broker: {_config.Mqtt.BrokerHost}:{_config.Mqtt.BrokerPort}");
            Console.WriteLine($"  - MQTT Topic: {_config.Mqtt.Topic}");
            Console.WriteLine($"  - Database Connection: {_connectionString.Split(';')[0]}");
        }

        private static async Task InitializeDatabaseAsync()
        {
            Console.WriteLine("\n[*] Initializing Database & Applying Migrations...");
            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
                optionsBuilder.UseSqlServer(_connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null));

                using (var dbContext = new ApplicationDbContext(optionsBuilder.Options))
                {
                    await dbContext.Database.MigrateAsync();
                }
                Console.WriteLine("[✓] Database schema verified and up to date.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Database migration check notice: {ex.Message}");
                Console.WriteLine("[*] Continuing with existing database schema...");
            }
        }

        private static async Task LoadConfigurationsAsync()
        {
            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
                optionsBuilder.UseSqlServer(_connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null));

                using (var db = new ApplicationDbContext(optionsBuilder.Options))
                {
                    // 1. Load Scale Configs
                    var scales = await db.ColumnScaleConfigs.AsNoTracking().ToListAsync();
                    foreach (var s in scales)
                    {
                        if (!string.IsNullOrEmpty(s.ColumnName))
                            ScaleConfig[s.ColumnName.ToUpper()] = s.ScaleFactor;
                    }

                    // 2. Load Column Mappings
                    var mappings = await db.ColumnMappings.AsNoTracking().Where(m => m.IsActive).ToListAsync();
                    foreach (var m in mappings)
                    {
                        if (!string.IsNullOrEmpty(m.OldColumnName) && !string.IsNullOrEmpty(m.NewColumnName))
                            ColumnMapping[m.OldColumnName.ToUpper()] = m.NewColumnName;
                    }
                }

                // 3. Load Existing Columns from Database
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'KWHData'", conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            ExistingColumns.Add(reader.GetString(0).ToUpper());
                        }
                    }
                }

                Console.WriteLine($"[✓] Loaded {ScaleConfig.Count} scale configurations from DB");
                Console.WriteLine($"[✓] Loaded {ColumnMapping.Count} column mappings from DB");
                Console.WriteLine($"[✓] Loaded {ExistingColumns.Count} existing columns in KWHData table");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Failed to load configurations from DB: {ex.Message}");
                Console.WriteLine("[!] Using fallback defaults...");

                ScaleConfig["PHASE_R"] = 10m;
                ScaleConfig["PHASE_S"] = 10m;
                ScaleConfig["PHASE_T"] = 10m;
                ScaleConfig["AMPERE_R"] = 1000m;
                ScaleConfig["AMPERE_S"] = 1000m;
                ScaleConfig["AMPERE_T"] = 1000m;
                ScaleConfig["COSPHI"] = 1000m;
                ScaleConfig["W"] = 10m;
                ScaleConfig["AKTIF_POWER"] = 100m;
                ScaleConfig["TOTALW"] = 100m;
                ScaleConfig["TOTALW1M"] = 100m;
                ScaleConfig["F"] = 10m;

                ColumnMapping["VR"] = "PHASE_R";
                ColumnMapping["VS"] = "PHASE_S";
                ColumnMapping["VT"] = "PHASE_T";
                ColumnMapping["AKTIF_W"] = "Aktif_Power";
            }
        }

        private static async Task ConnectAndSubscribeAsync()
        {
            try
            {
                var factory = new MqttFactory();
                _mqttClient = factory.CreateMqttClient();

                _mqttClient.ConnectedAsync += e =>
                {
                    Console.WriteLine("[✓] Connected to MQTT Broker");
                    return Task.CompletedTask;
                };

                _mqttClient.DisconnectedAsync += async e =>
                {
                    Console.WriteLine($"[✗] Disconnected from MQTT: {e.Reason}");
                    if (!_cts.IsCancellationRequested)
                    {
                        await Task.Delay(5000);
                        try
                        {
                            if (_mqttClient != null && !_mqttClient.IsConnected)
                            {
                                Console.WriteLine("[*] Reconnecting to MQTT Broker...");
                                await ConnectAndSubscribeAsync();
                            }
                        }
                        catch { }
                    }
                };

                _mqttClient.ApplicationMessageReceivedAsync += e =>
                {
                    MessageQueue.Enqueue(new MqttMessageBuffer
                    {
                        Topic = e.ApplicationMessage.Topic,
                        Payload = e.ApplicationMessage.ConvertPayloadToString() ?? "",
                        ReceivedAt = DateTime.Now
                    });
                    return Task.CompletedTask;
                };

                string clientId = $"{_config.Mqtt.ClientIdPrefix}{Guid.NewGuid():N}".Substring(0, 20);

                var optionsBuilder = new MqttClientOptionsBuilder()
                    .WithTcpServer(_config.Mqtt.BrokerHost, _config.Mqtt.BrokerPort)
                    .WithClientId(clientId)
                    .WithCleanSession();

                if (!string.IsNullOrEmpty(_config.Mqtt.Username))
                {
                    optionsBuilder = optionsBuilder.WithCredentials(_config.Mqtt.Username, _config.Mqtt.Password);
                }

                var clientOptions = optionsBuilder.Build();
                await _mqttClient.ConnectAsync(clientOptions);

                await _mqttClient.SubscribeAsync(_config.Mqtt.Topic, MqttQualityOfServiceLevel.AtMostOnce);
                Console.WriteLine($"[✓] Subscribed to topic ({_config.Mqtt.Topic})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[✗] Connection error: {ex.Message}");
            }
        }

        private static async Task ProcessQueueAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested || !MessageQueue.IsEmpty)
            {
                try
                {
                    if (MessageQueue.TryDequeue(out MqttMessageBuffer? buffer) && buffer != null)
                    {
                        await ProcessSingleMessageAsync(buffer);
                    }
                    else
                    {
                        await Task.Delay(50, ct).ContinueWith(_ => { });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[✗] Queue error: {ex.Message}");
                    await Task.Delay(1000, ct).ContinueWith(_ => { });
                }
            }
        }

        private static async Task MonitorQueueAsync(CancellationToken ct)
        {
            int interval = Math.Max(1, _config.Processing.MonitorIntervalSeconds) * 1000;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, ct);
                    Console.WriteLine($"[STATS] Total: {_messageCount} | Success: {_successCount} | Errors: {_errorCount} | Queue: {MessageQueue.Count}");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch { }
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

                Interlocked.Increment(ref _successCount);
                Interlocked.Increment(ref _messageCount);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _errorCount);
                Interlocked.Increment(ref _messageCount);
                Console.WriteLine($"[✗] Error: {ex.Message}");
                await LogFailedMessage(buffer, ex.Message);
            }
        }

        private static DynamicMqttData? ParseDynamicJson(string json)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var data = new DynamicMqttData();
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        data.Properties[prop.Name] = prop.Value.ToString();
                    }
                    return data;
                }
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
            if (!string.IsNullOrEmpty(groupName) && groupName!.ToUpper().Contains("KWHMETER"))
                return $"kwhapp{groupName!.ToUpper().Replace("KWHMETER", "")}";

            string[] parts = topic.Split('/');
            if (parts.Length >= 3) return parts[2];

            return "unknown";
        }

        private static async Task<string> GetOrCreateDeviceKeyAsync(string deviceId, string? groupName)
        {
            if (DeviceKeyCache.TryGetValue(deviceId, out string? cached))
                return cached;

            string deviceKey = deviceId.ToUpper();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                using (SqlCommand checkCmd = new SqlCommand(
                    "SELECT DeviceKey FROM DeviceRegistry WHERE DeviceId = @DeviceId", conn))
                {
                    checkCmd.Parameters.AddWithValue("@DeviceId", deviceId);
                    var existingKey = await checkCmd.ExecuteScalarAsync();

                    if (existingKey != null)
                    {
                        deviceKey = existingKey.ToString()!;

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
                        using (SqlCommand insertCmd = new SqlCommand(@"
                            INSERT INTO DeviceRegistry (DeviceKey, DeviceId, GroupName, FirstSeen, LastSeen, IsActive, MessageCount, CreatedAt, UpdatedAt)
                            VALUES (@DeviceKey, @DeviceId, @GroupName, GETDATE(), GETDATE(), 1, 0, GETDATE(), GETDATE())", conn))
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

                if (!SafeSqlColumnNameRegex.IsMatch(key))
                {
                    Console.WriteLine($"[!] Skipping invalid dynamic column name: '{key}'");
                    continue;
                }

                await AddColumnToDatabaseAsync(key);
                ExistingColumns.Add(key);
                Console.WriteLine($"[+] New column added: {key}");
            }
        }

        private static async Task AddColumnToDatabaseAsync(string columnName)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand($"ALTER TABLE KWHData ADD [{columnName}] DECIMAL(18,3) NULL", conn))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[✗] Failed to add column {columnName}: {ex.Message}");
            }
        }

        private static async Task SaveToDatabaseAsync(DynamicMqttData data, string deviceId, string deviceKey)
        {
            int maxRetries = Math.Max(1, _config.Processing.MaxRetries);
            int retryDelay = Math.Max(100, _config.Processing.RetryDelayMs);

            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    using (SqlConnection conn = new SqlConnection(_connectionString))
                    {
                        await conn.OpenAsync();

                        var columns = new List<string> { "DeviceKey", "TerminalTime", "GroupName", "DeviceId" };
                        var values = new List<string> { "@DeviceKey", "@TerminalTime", "@GroupName", "@DeviceId" };
                        var parameters = new List<SqlParameter>
                        {
                            new SqlParameter("@DeviceKey", deviceKey),
                            new SqlParameter("@TerminalTime", ParseDateTime(data.GetProperty("_terminalTime"))),
                            new SqlParameter("@GroupName", (object?)data.GetProperty("_groupName") ?? DBNull.Value),
                            new SqlParameter("@DeviceId", deviceId)
                        };

                        foreach (var kvp in data.Properties)
                        {
                            if (kvp.Key.StartsWith("_")) continue;
                            if (!ExistingColumns.Contains(kvp.Key)) continue;

                            columns.Add($"[{kvp.Key}]");
                            values.Add($"@{kvp.Key}");
                            parameters.Add(new SqlParameter($"@{kvp.Key}", ParseValueWithScale(kvp.Key, kvp.Value)));
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
                    if (retry == maxRetries - 1) throw;
                    await Task.Delay(retryDelay * (retry + 1));
                }
            }
        }

        private static decimal ParseValueWithScale(string columnName, string? value)
        {
            if (string.IsNullOrEmpty(value) || !decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal raw))
            {
                if (!string.IsNullOrEmpty(value) && decimal.TryParse(value, out decimal fallbackParsed))
                    raw = fallbackParsed;
                else
                    return 0;
            }

            string colUpper = columnName.ToUpper();

            // 1. Check if database ScaleConfig has an explicit factor for this column
            if (ScaleConfig.TryGetValue(colUpper, out decimal scaleFactor) && scaleFactor > 0)
            {
                return Math.Round(raw / scaleFactor, 3);
            }

            // 2. Fallback scaling rules based on known sensor types
            if (colUpper is "PHASE_R" or "PHASE_S" or "PHASE_T")
            {
                decimal result;
                if (raw >= 10000m) result = raw / 100m;
                else if (raw >= 1000m) result = raw / 10m;
                else if (raw > 500m) result = raw / 10m;
                else result = raw;
                return Math.Round(result, 2);
            }

            if (colUpper is "AMPERE_R" or "AMPERE_S" or "AMPERE_T")
                return Math.Round(raw / 10m, 3);

            if (colUpper == "COSPHI")
                return Math.Round(raw / 1000m, 3);

            if (colUpper == "W")
                return Math.Round(raw / 10m, 1);

            if (colUpper is "TOTALW1M" or "AKTIF_POWER" or "TOTALW")
                return Math.Round(raw / 100m, 2);

            if (colUpper == "F")
                return Math.Round(raw / 10m, 2);

            return raw;
        }

        private static DateTime ParseDateTime(string? str)
        {
            if (string.IsNullOrEmpty(str)) return DateTime.Now;
            if (DateTime.TryParseExact(str, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
                return result;
            if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result2))
                return result2;
            if (DateTime.TryParse(str, out DateTime result3))
                return result3;
            return DateTime.Now;
        }

        private static async Task LogFailedMessage(MqttMessageBuffer buffer, string reason)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(
                        "INSERT INTO FailedMessages (Topic, Payload, Reason, ReceivedAt, RetryCount, IsResolved) VALUES (@Topic, @Payload, @Reason, @ReceivedAt, 0, 0)", conn))
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
            if (_mqttClient != null)
            {
                try
                {
                    if (_mqttClient.IsConnected)
                    {
                        await _mqttClient.DisconnectAsync();
                    }
                    _mqttClient.Dispose();
                }
                catch { }
            }
            Console.WriteLine($"\n╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║   Final Stats: Total={_messageCount} | Success={_successCount} | Errors={_errorCount}   ║");
            Console.WriteLine($"╚══════════════════════════════════════════════════════════╝");
        }
    }
}

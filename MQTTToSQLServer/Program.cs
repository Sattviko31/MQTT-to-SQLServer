using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        private static string mqttIp = "192.168.168.38";
        private static int mqttPort = 1883;
        private static string? mqttUsername = "admin";
        private static string? mqttPassword = "12345678";

        private static string sqlServer = "192.168.168.38";
        private static string sqlDatabase = "KWHMonitoring";
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
            Console.WriteLine("║   Version 6.8 - Fixed Migration Timeout                  ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            SetupConfiguration();
            await RunMigrationAsync();
            SetupConnections();
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

        private static async Task RunMigrationAsync()
        {
            Console.WriteLine("\n[*] Memeriksa dan menyiapkan struktur database...");
            Console.WriteLine($"[*] Target Database: {sqlDatabase} | Target User: {sqlUser}");

            string masterConnStr = $"Server={sqlServer},1433;Database=master;User Id={sqlUser};Password={sqlPassword};Encrypt=False;TrustServerCertificate=True;";

            try
            {
                using (var conn = new SqlConnection(masterConnStr))
                {
                    await conn.OpenAsync();

                    // 1. CEK & CREATE DATABASE
                    bool dbExists = false;
                    using (var cmd = new SqlCommand($"SELECT DB_ID('{sqlDatabase}')", conn))
                    {
                        dbExists = await cmd.ExecuteScalarAsync() != DBNull.Value;
                    }

                    if (!dbExists)
                    {
                        Console.WriteLine($"[+] Database '{sqlDatabase}' belum ada. Membuat database baru...");
                        using (var cmd = new SqlCommand($"CREATE DATABASE [{sqlDatabase}]", conn))
                        {
                            cmd.CommandTimeout = 120;
                            await cmd.ExecuteNonQueryAsync();
                        }
                        Console.WriteLine($"[✓] Database '{sqlDatabase}' berhasil dibuat.");
                    }
                    else
                    {
                        Console.WriteLine($"[✓] Database '{sqlDatabase}' sudah ada.");
                    }

                    // 2. CEK & CREATE LOGIN (Di Master)
                    Console.WriteLine("[*] Memeriksa Login SQL...");
                    string loginScript = $@"
                        IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = '{sqlUser}')
                        BEGIN
                            CREATE LOGIN [{sqlUser}] WITH PASSWORD = '{sqlPassword}', DEFAULT_DATABASE = [{sqlDatabase}], CHECK_EXPIRATION = OFF, CHECK_POLICY = OFF;
                        END";

                    using (var cmd = new SqlCommand(loginScript, conn))
                    {
                        cmd.CommandTimeout = 60;
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // 3. CEK & CREATE USER & ROLES (Di Database Target)
                    Console.WriteLine("[*] Memeriksa User dan Permissions...");
                    string userScript = $@"
                        USE [{sqlDatabase}];
                        
                        DECLARE @UserExists BIT = 0;
                        DECLARE @IsDbo BIT = 0;
                        
                        IF EXISTS (SELECT 1 FROM sys.database_principals WHERE sid = SUSER_SID('{sqlUser}'))
                        BEGIN
                            SET @UserExists = 1;
                            IF EXISTS (SELECT 1 FROM sys.database_principals WHERE sid = SUSER_SID('{sqlUser}') AND name = 'dbo')
                                SET @IsDbo = 1;
                        END
                        
                        IF @UserExists = 0
                        BEGIN
                            CREATE USER [{sqlUser}] FOR LOGIN [{sqlUser}];
                        END
                        
                        IF @IsDbo = 0
                        BEGIN
                            IF IS_ROLEMEMBER('db_ddladmin', '{sqlUser}') IS NULL OR IS_ROLEMEMBER('db_ddladmin', '{sqlUser}') = 0
                                ALTER ROLE [db_ddladmin] ADD MEMBER [{sqlUser}];
                            IF IS_ROLEMEMBER('db_datareader', '{sqlUser}') IS NULL OR IS_ROLEMEMBER('db_datareader', '{sqlUser}') = 0
                                ALTER ROLE [db_datareader] ADD MEMBER [{sqlUser}];
                            IF IS_ROLEMEMBER('db_datawriter', '{sqlUser}') IS NULL OR IS_ROLEMEMBER('db_datawriter', '{sqlUser}') = 0
                                ALTER ROLE [db_datawriter] ADD MEMBER [{sqlUser}];
                        END
                    ";

                    using (var cmd = new SqlCommand(userScript, conn))
                    {
                        cmd.CommandTimeout = 60;
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // 4. Eksekusi Skrip Tabel, View, Index, dan SP
                    Console.WriteLine("[*] Membuat Tabel, View, Index, dan Stored Procedure...");

                    string cleanScript = Regex.Replace(MigrationScript, @"CREATE\s+DATABASE\s+\[.*?\][\s\S]*?GO", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    cleanScript = Regex.Replace(cleanScript, @"CREATE\s+USER\s+\[.*?\][\s\S]*?GO", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    cleanScript = Regex.Replace(cleanScript, @"ALTER\s+ROLE\s+\[.*?\][\s\S]*?GO", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

                    // PERBAIKAN: Skip ALTER DATABASE statements jika database sudah ada
                    if (dbExists)
                    {
                        cleanScript = Regex.Replace(cleanScript, @"ALTER\s+DATABASE\s+\[.*?\][\s\S]*?GO", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    }

                    cleanScript = cleanScript.Replace("[HaiwellElectrical]", $"[{sqlDatabase}]");

                    string[] batches = Regex.Split(cleanScript, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

                    int successBatches = 0;
                    int skippedBatches = 0;

                    foreach (var batch in batches)
                    {
                        if (string.IsNullOrWhiteSpace(batch)) continue;
                        if (batch.Trim().StartsWith("USE [master]", StringComparison.OrdinalIgnoreCase)) continue;

                        try
                        {
                            // PERBAIKAN: CommandTimeout lebih panjang (300 detik)
                            using (var cmd = new SqlCommand(batch, conn))
                            {
                                cmd.CommandTimeout = 300;
                                await cmd.ExecuteNonQueryAsync();
                                successBatches++;
                            }
                        }
                        catch (SqlException ex)
                        {
                            if (ex.Number == 2714 || ex.Number == 1913 || ex.Number == 2705 ||
                                ex.Number == 15023 || ex.Number == 2627 || ex.Number == 2601 ||
                                ex.Number == 15231 || ex.Number == 15151 || ex.Number == 1781)
                            {
                                skippedBatches++;
                            }
                            else
                            {
                                Console.WriteLine($"[!] Migration warning: {ex.Message}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[!] Migration error: {ex.Message}");
                        }
                    }
                    Console.WriteLine($"[✓] Eksekusi skrip selesai. ({successBatches} berhasil, {skippedBatches} dilewati).");
                }
                Console.WriteLine("[✓] Struktur database siap digunakan.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Gagal melakukan migrasi database: {ex.Message}");
                Console.WriteLine("[*] Pastikan user SQL yang dikonfigurasi memiliki hak akses 'sysadmin' atau 'dbcreator' untuk migrasi awal.");
            }
        }

        private static void SetupConfiguration()
        {
            Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   SETTING KONFIGURASI                                    ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("=== Konfigurasi MQTT ===");
            Console.Write($"MQTT Broker IP (default: {mqttIp}): ");
            string inputMqttIp = Console.ReadLine() ?? "";
            if (!string.IsNullOrEmpty(inputMqttIp)) mqttIp = inputMqttIp;

            Console.Write($"MQTT Port (default: {mqttPort}): ");
            string inputMqttPort = Console.ReadLine() ?? "";
            if (!string.IsNullOrEmpty(inputMqttPort) && int.TryParse(inputMqttPort, out int port)) mqttPort = port;

            Console.Write($"MQTT Username (default: {mqttUsername}): ");
            string inputMqttUsername = Console.ReadLine() ?? "";
            if (!string.IsNullOrEmpty(inputMqttUsername)) mqttUsername = inputMqttUsername;

            Console.Write($"MQTT Password (default: {mqttPassword}): ");
            string inputMqttPassword = Console.ReadLine() ?? "";
            if (!string.IsNullOrEmpty(inputMqttPassword)) mqttPassword = inputMqttPassword;

            Console.WriteLine("\n=== Konfigurasi Database ===");
            Console.Write($"SQL Server (default: {sqlServer}): ");
            string inputSqlServer = Console.ReadLine() ?? "";
            if (!string.IsNullOrEmpty(inputSqlServer)) sqlServer = inputSqlServer;

            Console.Write($"Database Name (default: {sqlDatabase}): ");
            string inputSqlDatabase = Console.ReadLine() ?? "";
            if (!string.IsNullOrEmpty(inputSqlDatabase)) sqlDatabase = inputSqlDatabase;

            Console.Write($"SQL Username (default: {sqlUser}): ");
            string inputSqlUser = Console.ReadLine() ?? "";
            if (!string.IsNullOrEmpty(inputSqlUser)) sqlUser = inputSqlUser;

            Console.Write($"SQL Password (default: {sqlPassword}): ");
            string inputSqlPassword = Console.ReadLine() ?? "";
            if (!string.IsNullOrEmpty(inputSqlPassword)) sqlPassword = inputSqlPassword;

            Console.WriteLine("\n╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   KONFIGURASI YANG AKAN DIGUNAKAN                        ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
            Console.WriteLine($"- MQTT Broker: {mqttIp}:{mqttPort}");
            Console.WriteLine($"- MQTT Username: {mqttUsername}");
            Console.WriteLine($"- MQTT Password: {new string('*', mqttPassword?.Length ?? 0)}");
            Console.WriteLine($"- SQL Server: {sqlServer}");
            Console.WriteLine($"- Database: {sqlDatabase}");
            Console.WriteLine($"- SQL User: {sqlUser}");

            Console.WriteLine("\nApakah konfigurasi ini sudah benar? (Y/N)");
            char confirm = Console.ReadKey().KeyChar;
            Console.WriteLine();

            if (char.ToUpper(confirm) != 'Y')
            {
                Console.WriteLine("\nSilakan jalankan ulang program untuk mengonfigurasi ulang.");
                Environment.Exit(0);
            }
        }

        private static void SetupConnections()
        {
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

                    using (SqlCommand cmd = new SqlCommand("SELECT ColumnName, ScaleFactor FROM ColumnScaleConfig", conn))
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            ScaleConfig[reader.GetString(0).ToUpper()] = reader.GetDecimal(1);
                    }

                    using (SqlCommand cmd = new SqlCommand("SELECT OldColumnName, NewColumnName FROM ColumnMapping WHERE IsActive = 1", conn))
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            ColumnMapping[reader.GetString(0).ToUpper()] = reader.GetString(1);
                    }

                    using (SqlCommand cmd = new SqlCommand("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'KWHData'", conn))
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            ExistingColumns.Add(reader.GetString(0).ToUpper());
                    }
                }
                Console.WriteLine($"[✓] Loaded {ScaleConfig.Count} scale configurations");
                Console.WriteLine($"[✓] Loaded {ColumnMapping.Count} column mappings");
                Console.WriteLine($"[✓] Loaded {ExistingColumns.Count} existing columns");

                if (ColumnMapping.Count == 0)
                {
                    Console.WriteLine("[*] ColumnMapping kosong, menggunakan default mapping...");
                    ColumnMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "VR", "PHASE_R" },
                        { "VS", "PHASE_S" },
                        { "VT", "PHASE_T" },
                        { "AKTIF_W", "Aktif_Power" }
                    };
                    Console.WriteLine($"[✓] Loaded {ColumnMapping.Count} default column mappings");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Failed to load configurations: {ex.Message}");
                Console.WriteLine("[!] Using fallback defaults...");

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

                mqttClient.ConnectedAsync += e => { Console.WriteLine("[✓] Connected to MQTT Broker"); return Task.CompletedTask; };
                mqttClient.DisconnectedAsync += e => { Console.WriteLine($"[✗] Disconnected: {e.Reason}"); return Task.CompletedTask; };
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

                var optionsBuilder = new MqttClientOptionsBuilder()
                    .WithTcpServer(mqttIp, mqttPort)
                    .WithClientId($"KWHApp_{Guid.NewGuid():N}".Substring(0, 20))
                    .WithCleanSession();

                if (!string.IsNullOrEmpty(mqttUsername))
                {
                    optionsBuilder = optionsBuilder.WithCredentials(mqttUsername, mqttPassword);
                }

                var clientOptions = optionsBuilder.Build();
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
            catch { return null; }
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

            foreach (var key in keysToRemove) data.Properties.Remove(key);
            foreach (var kvp in newProperties)
            {
                if (!data.Properties.ContainsKey(kvp.Key))
                    data.Properties[kvp.Key] = kvp.Value;
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
            if (DeviceKeyCache.TryGetValue(deviceId, out string? cached)) return cached;

            string deviceKey = deviceId.ToUpper();
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (SqlCommand checkCmd = new SqlCommand("SELECT DeviceKey FROM DeviceRegistry WHERE DeviceId = @DeviceId", conn))
                {
                    checkCmd.Parameters.AddWithValue("@DeviceId", deviceId);
                    var existingKey = await checkCmd.ExecuteScalarAsync();

                    if (existingKey != null)
                    {
                        deviceKey = existingKey.ToString()!;
                        using (SqlCommand updateCmd = new SqlCommand(@"
                            UPDATE DeviceRegistry
                            SET LastSeen = GETDATE(), GroupName = ISNULL(@GroupName, GroupName), UpdatedAt = GETDATE()
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
            if (string.IsNullOrEmpty(value) || !decimal.TryParse(value, out decimal raw)) return 0;

            string colUpper = columnName.ToUpper();

            if (colUpper is "PHASE_R" or "PHASE_S" or "PHASE_T")
            {
                decimal result = raw >= 10000m ? raw / 100m : raw >= 1000m ? raw / 10m : raw > 500m ? raw / 10m : raw;
                return Math.Round(result, 2);
            }
            if (colUpper is "AMPERE_R" or "AMPERE_S" or "AMPERE_T") return Math.Round(raw / 10m, 2);
            if (colUpper == "COSPHI") return Math.Round(raw / 10m, 3);
            if (colUpper == "W") return Math.Round(raw / 10m, 1);
            if (colUpper is "TOTALW1M" or "AKTIF_POWER" or "TOTALW") return Math.Round(raw / 100m, 2);
            if (colUpper == "F") return Math.Round(raw / 10m, 2);

            return raw;
        }

        private static DateTime ParseDateTime(string? str)
        {
            if (string.IsNullOrEmpty(str)) return DateTime.Now;
            if (DateTime.TryParseExact(str, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result)) return result;
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

        private const string MigrationScript = @"
USE [master]
GO
ALTER DATABASE [HaiwellElectrical] SET COMPATIBILITY_LEVEL = 170
GO
IF (1 = FULLTEXTSERVICEPROPERTY('IsFullTextInstalled'))
begin
EXEC [HaiwellElectrical].[dbo].[sp_fulltext_database] @action = 'enable'
end
GO
ALTER DATABASE [HaiwellElectrical] SET ANSI_NULL_DEFAULT OFF
GO
ALTER DATABASE [HaiwellElectrical] SET ANSI_NULLS OFF
GO
ALTER DATABASE [HaiwellElectrical] SET ANSI_PADDING OFF
GO
ALTER DATABASE [HaiwellElectrical] SET ANSI_WARNINGS OFF
GO
ALTER DATABASE [HaiwellElectrical] SET ARITHABORT OFF
GO
ALTER DATABASE [HaiwellElectrical] SET AUTO_CLOSE ON
GO
ALTER DATABASE [HaiwellElectrical] SET AUTO_SHRINK OFF
GO
ALTER DATABASE [HaiwellElectrical] SET AUTO_UPDATE_STATISTICS ON
GO
ALTER DATABASE [HaiwellElectrical] SET CURSOR_CLOSE_ON_COMMIT OFF
GO
ALTER DATABASE [HaiwellElectrical] SET CURSOR_DEFAULT  GLOBAL
GO
ALTER DATABASE [HaiwellElectrical] SET CONCAT_NULL_YIELDS_NULL OFF
GO
ALTER DATABASE [HaiwellElectrical] SET NUMERIC_ROUNDABORT OFF
GO
ALTER DATABASE [HaiwellElectrical] SET QUOTED_IDENTIFIER OFF
GO
ALTER DATABASE [HaiwellElectrical] SET RECURSIVE_TRIGGERS OFF
GO
ALTER DATABASE [HaiwellElectrical] SET  DISABLE_BROKER
GO
ALTER DATABASE [HaiwellElectrical] SET AUTO_UPDATE_STATISTICS_ASYNC OFF
GO
ALTER DATABASE [HaiwellElectrical] SET DATE_CORRELATION_OPTIMIZATION OFF
GO
ALTER DATABASE [HaiwellElectrical] SET TRUSTWORTHY OFF
GO
ALTER DATABASE [HaiwellElectrical] SET ALLOW_SNAPSHOT_ISOLATION OFF
GO
ALTER DATABASE [HaiwellElectrical] SET PARAMETERIZATION SIMPLE
GO
ALTER DATABASE [HaiwellElectrical] SET READ_COMMITTED_SNAPSHOT OFF
GO
ALTER DATABASE [HaiwellElectrical] SET HONOR_BROKER_PRIORITY OFF
GO
ALTER DATABASE [HaiwellElectrical] SET RECOVERY SIMPLE
GO
ALTER DATABASE [HaiwellElectrical] SET  MULTI_USER
GO
ALTER DATABASE [HaiwellElectrical] SET PAGE_VERIFY CHECKSUM
GO
ALTER DATABASE [HaiwellElectrical] SET DB_CHAINING OFF
GO
ALTER DATABASE [HaiwellElectrical] SET FILESTREAM( NON_TRANSACTED_ACCESS = OFF )
GO
ALTER DATABASE [HaiwellElectrical] SET TARGET_RECOVERY_TIME = 60 SECONDS
GO
ALTER DATABASE [HaiwellElectrical] SET DELAYED_DURABILITY = DISABLED
GO
ALTER DATABASE [HaiwellElectrical] SET OPTIMIZED_LOCKING = OFF
GO
ALTER DATABASE [HaiwellElectrical] SET ACCELERATED_DATABASE_RECOVERY = OFF
GO
ALTER DATABASE [HaiwellElectrical] SET QUERY_STORE = ON
GO
ALTER DATABASE [HaiwellElectrical] SET QUERY_STORE (OPERATION_MODE = READ_WRITE, CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30), DATA_FLUSH_INTERVAL_SECONDS = 900, INTERVAL_LENGTH_MINUTES = 60, MAX_STORAGE_SIZE_MB = 1000, QUERY_CAPTURE_MODE = AUTO, SIZE_BASED_CLEANUP_MODE = AUTO, MAX_PLANS_PER_QUERY = 200, WAIT_STATS_CAPTURE_MODE = ON)
GO
USE [HaiwellElectrical]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[DeviceRegistry](
[Id] [int] IDENTITY(1,1) NOT NULL,
[DeviceKey] [varchar](20) NOT NULL,
[DeviceId] [varchar](50) NOT NULL,
[GroupName] [varchar](100) NULL,
[Location] [varchar](200) NULL,
[FirstSeen] [datetime2](7) NOT NULL,
[LastSeen] [datetime2](7) NOT NULL,
[IsActive] [bit] NOT NULL,
[MessageCount] [bigint] NOT NULL,
[CreatedAt] [datetime2](7) NOT NULL,
[UpdatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED ([Id] ASC),
UNIQUE NONCLUSTERED ([DeviceKey] ASC),
UNIQUE NONCLUSTERED ([DeviceId] ASC)
) ON [PRIMARY]
GO
CREATE TABLE [dbo].[KWHData](
[Id] [bigint] IDENTITY(1,1) NOT NULL,
[DeviceKey] [varchar](20) NOT NULL,
[TerminalTime] [datetime2](7) NULL,
[ReceivedTime] [datetime2](7) NOT NULL,
[GroupName] [nvarchar](100) NULL,
[DeviceId] [nvarchar](50) NULL,
[PHASE_R] [decimal](18, 2) NULL,
[PHASE_S] [decimal](18, 2) NULL,
[PHASE_T] [decimal](18, 2) NULL,
[AMPERE_R] [decimal](18, 3) NULL,
[AMPERE_S] [decimal](18, 3) NULL,
[AMPERE_T] [decimal](18, 3) NULL,
[W] [decimal](18, 1) NULL,
[CosPhi] [decimal](18, 3) NULL,
[F] [decimal](18, 2) NULL,
[Aktif_Power] [decimal](18, 2) NULL,
[TotalW] [decimal](18, 2) NULL,
[TotalW1M] [decimal](18, 2) NULL,
[Haiwell_PLC_1_Curent_R] [decimal](18, 3) NULL,
[Haiwell_PLC_1_Daya_Active_Power] [decimal](18, 3) NULL,
[Haiwell_PLC_1_Total_power_factor] [decimal](18, 3) NULL,
PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY]
GO
CREATE VIEW [dbo].[vLatestKWHData] AS
SELECT k.Id, k.DeviceKey, d.DeviceId, d.GroupName, k.TerminalTime, k.ReceivedTime,
k.PHASE_R, k.PHASE_S, k.PHASE_T, k.AMPERE_R, k.AMPERE_S, k.AMPERE_T,
k.CosPhi, k.W, k.Aktif_Power, k.TotalW, k.TotalW1M, k.F
FROM KWHData k INNER JOIN DeviceRegistry d ON k.DeviceKey = d.DeviceKey
INNER JOIN (SELECT DeviceKey, MAX(ReceivedTime) AS MaxTime FROM KWHData GROUP BY DeviceKey) latest 
ON k.DeviceKey = latest.DeviceKey AND k.ReceivedTime = latest.MaxTime;
GO
CREATE VIEW [dbo].[vDeviceSummary] AS
SELECT d.DeviceKey, d.DeviceId, d.GroupName, d.FirstSeen, d.LastSeen, d.IsActive, d.MessageCount,
COUNT(k.Id) AS TotalRecords, MAX(k.ReceivedTime) AS LastDataReceived
FROM DeviceRegistry d LEFT JOIN KWHData k ON d.DeviceKey = k.DeviceKey
GROUP BY d.DeviceKey, d.DeviceId, d.GroupName, d.FirstSeen, d.LastSeen, d.IsActive, d.MessageCount;
GO
CREATE VIEW [dbo].[vDailyEnergy] AS
SELECT k.DeviceKey, d.GroupName, CAST(k.TerminalTime AS DATE) AS ReportDate,
MIN(k.TotalW) AS EnergyStart_kWh, MAX(k.TotalW) AS EnergyEnd_kWh,
(MAX(k.TotalW) - MIN(k.TotalW)) AS DailyConsumption_kWh, COUNT(*) AS ReadingCount
FROM KWHData k INNER JOIN DeviceRegistry d ON k.DeviceKey = d.DeviceKey
GROUP BY k.DeviceKey, d.GroupName, CAST(k.TerminalTime AS DATE);
GO
CREATE TABLE [dbo].[__EFMigrationsHistory]([MigrationId] [nvarchar](150) NOT NULL, [ProductVersion] [nvarchar](32) NOT NULL, PRIMARY KEY CLUSTERED ([MigrationId] ASC)) ON [PRIMARY]
GO
CREATE TABLE [dbo].[AnomalyLogs]([Id] [bigint] IDENTITY(1,1) NOT NULL, [DeviceKey] [nvarchar](50) NOT NULL, [DeviceId] [nvarchar](50) NULL, [AnomalyType] [nvarchar](20) NOT NULL, [PowerValue] [decimal](18, 2) NOT NULL, [ThresholdValue] [decimal](18, 2) NOT NULL, [Deviation] [decimal](5, 2) NOT NULL, [DetectedTime] [datetime2](7) NOT NULL, [EMAValue] [decimal](18, 2) NULL, [ThresholdMode] [nvarchar](20) NULL, [Acknowledged] [bit] NULL, [AcknowledgedTime] [datetime2](7) NULL, [Notes] [nvarchar](500) NULL, PRIMARY KEY CLUSTERED ([Id] ASC)) ON [PRIMARY]
GO
CREATE TABLE [dbo].[AppLog]([Id] [bigint] IDENTITY(1,1) NOT NULL, [LogLevel] [varchar](20) NOT NULL, [Message] [nvarchar](max) NOT NULL, [Topic] [varchar](200) NULL, [DeviceKey] [varchar](20) NULL, [CreatedAt] [datetime2](7) NOT NULL, PRIMARY KEY CLUSTERED ([Id] ASC)) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
CREATE TABLE [dbo].[AppSettings]([Id] [int] IDENTITY(1,1) NOT NULL, [SettingKey] [nvarchar](100) NOT NULL, [SettingValue] [nvarchar](500) NOT NULL, [UpdatedAt] [datetime2](7) NULL, PRIMARY KEY CLUSTERED ([Id] ASC), UNIQUE NONCLUSTERED ([SettingKey] ASC)) ON [PRIMARY]
GO
CREATE TABLE [dbo].[ColumnMapping]([Id] [int] IDENTITY(1,1) NOT NULL, [OldColumnName] [varchar](50) NOT NULL, [NewColumnName] [varchar](50) NOT NULL, [IsActive] [bit] NOT NULL, [CreatedAt] [datetime2](7) NOT NULL, PRIMARY KEY CLUSTERED ([Id] ASC)) ON [PRIMARY]
GO
CREATE TABLE [dbo].[ColumnScaleConfig]([ColumnName] [varchar](50) NOT NULL, [ScaleFactor] [decimal](18, 5) NOT NULL, [RegisterAddress] [varchar](10) NULL, [DataType] [varchar](20) NOT NULL, [Unit] [varchar](50) NULL, [Category] [varchar](50) NULL, [Description] [varchar](500) NULL, [IsDynamic] [bit] NOT NULL, [LastUpdated] [datetime2](7) NOT NULL, PRIMARY KEY CLUSTERED ([ColumnName] ASC)) ON [PRIMARY]
GO
CREATE TABLE [dbo].[DailyEnergy]([Id] [bigint] IDENTITY(1,1) NOT NULL, [DeviceKey] [nvarchar](100) NOT NULL, [Date] [date] NOT NULL, [EnergyKWh] [decimal](18, 4) NOT NULL, [CalculatedAt] [datetime2](7) NOT NULL, PRIMARY KEY CLUSTERED ([Id] ASC)) ON [PRIMARY]
GO
CREATE TABLE [dbo].[FailedMessages]([Id] [bigint] IDENTITY(1,1) NOT NULL, [Topic] [varchar](500) NOT NULL, [Payload] [nvarchar](max) NOT NULL, [Reason] [nvarchar](500) NULL, [RetryCount] [int] NOT NULL, [IsResolved] [bit] NOT NULL, [ReceivedAt] [datetime2](7) NOT NULL, [ResolvedAt] [datetime2](7) NULL, PRIMARY KEY CLUSTERED ([Id] ASC)) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
CREATE TABLE [dbo].[HourlyEnergy]([Id] [bigint] IDENTITY(1,1) NOT NULL, [DeviceKey] [nvarchar](100) NOT NULL, [Hour] [datetime2](7) NOT NULL, [EnergyKWh] [decimal](18, 4) NOT NULL, [CalculatedAt] [datetime2](7) NOT NULL, PRIMARY KEY CLUSTERED ([Id] ASC)) ON [PRIMARY]
GO
CREATE TABLE [dbo].[KWHData_History]([HistoryId] [bigint] IDENTITY(1,1) NOT NULL, [OriginalId] [bigint] NOT NULL, [DeviceKey] [varchar](20) NOT NULL, [TerminalTime] [datetime2](7) NULL, [ReceivedTime] [datetime2](7) NOT NULL, [GroupName] [nvarchar](100) NULL, [DeviceId] [nvarchar](50) NULL, [PHASE_R] [decimal](18, 2) NULL, [PHASE_S] [decimal](18, 2) NULL, [PHASE_T] [decimal](18, 2) NULL, [AMPERE_R] [decimal](18, 3) NULL, [AMPERE_S] [decimal](18, 3) NULL, [AMPERE_T] [decimal](18, 3) NULL, [W] [decimal](18, 1) NULL, [CosPhi] [decimal](18, 3) NULL, [F] [decimal](18, 2) NULL, [Aktif_Power] [decimal](18, 2) NULL, [TotalW] [decimal](18, 2) NULL, [TotalW1M] [decimal](18, 2) NULL, [ArchivedAt] [datetime2](7) NOT NULL, PRIMARY KEY CLUSTERED ([HistoryId] ASC)) ON [PRIMARY]
GO
CREATE TABLE [dbo].[MonthlyEnergy]([Id] [bigint] IDENTITY(1,1) NOT NULL, [DeviceKey] [nvarchar](100) NOT NULL, [Year] [int] NOT NULL, [Month] [int] NOT NULL, [EnergyKWh] [decimal](18, 4) NOT NULL, [CalculatedAt] [datetime2](7) NOT NULL, PRIMARY KEY CLUSTERED ([Id] ASC)) ON [PRIMARY]
GO
CREATE TABLE [dbo].[YearlyEnergy]([Id] [bigint] IDENTITY(1,1) NOT NULL, [DeviceKey] [nvarchar](100) NOT NULL, [Year] [int] NOT NULL, [EnergyKWh] [decimal](18, 4) NOT NULL, [CalculatedAt] [datetime2](7) NOT NULL, PRIMARY KEY CLUSTERED ([Id] ASC)) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_AnomalyLogs_DetectedTime] ON [dbo].[AnomalyLogs]([DetectedTime] ASC) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_AnomalyLogs_DeviceKey] ON [dbo].[AnomalyLogs]([DeviceKey] ASC) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_AppLog_CreatedAt] ON [dbo].[AppLog]([CreatedAt] DESC) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_AppLog_Level] ON [dbo].[AppLog]([LogLevel] ASC) ON [PRIMARY]
GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_ColumnMapping_OldName] ON [dbo].[ColumnMapping]([OldColumnName] ASC) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_DeviceRegistry_DeviceId] ON [dbo].[DeviceRegistry]([DeviceId] ASC) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_DeviceRegistry_DeviceKey] ON [dbo].[DeviceRegistry]([DeviceKey] ASC) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_FailedMessages_IsResolved] ON [dbo].[FailedMessages]([IsResolved] ASC) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_FailedMessages_ReceivedAt] ON [dbo].[FailedMessages]([ReceivedAt] DESC) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_DeviceKey] ON [dbo].[KWHData]([DeviceKey] ASC) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_DeviceKey_Only] ON [dbo].[KWHData]([DeviceKey] ASC) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_DeviceKey_ReceivedTime] ON [dbo].[KWHData]([DeviceKey] ASC, [ReceivedTime] DESC) INCLUDE([DeviceId],[GroupName],[TerminalTime],[PHASE_R],[PHASE_S],[PHASE_T],[AMPERE_R],[AMPERE_S],[AMPERE_T],[CosPhi],[W],[TotalW1M],[Aktif_Power],[TotalW],[F]) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_DeviceKey_TerminalTime] ON [dbo].[KWHData]([DeviceKey] ASC, [TerminalTime] DESC) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_ReceivedTime] ON [dbo].[KWHData]([ReceivedTime] ASC) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_ReceivedTime_DeviceKey] ON [dbo].[KWHData]([ReceivedTime] DESC, [DeviceKey] ASC) INCLUDE([DeviceId],[GroupName],[TerminalTime],[PHASE_R],[PHASE_S],[PHASE_T],[AMPERE_R],[AMPERE_S],[AMPERE_T],[CosPhi],[W],[TotalW1M],[Aktif_Power],[TotalW],[F]) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_TerminalTime] ON [dbo].[KWHData]([TerminalTime] ASC) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_History_ArchivedAt] ON [dbo].[KWHData_History]([ArchivedAt] DESC) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_History_DeviceKey] ON [dbo].[KWHData_History]([DeviceKey] ASC) ON [PRIMARY]
GO
ALTER TABLE [dbo].[AnomalyLogs] ADD DEFAULT (getdate()) FOR [DetectedTime]
GO
ALTER TABLE [dbo].[AnomalyLogs] ADD DEFAULT ('manual') FOR [ThresholdMode]
GO
ALTER TABLE [dbo].[AnomalyLogs] ADD DEFAULT ((0)) FOR [Acknowledged]
GO
ALTER TABLE [dbo].[AppLog] ADD DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[AppSettings] ADD DEFAULT (getdate()) FOR [UpdatedAt]
GO
ALTER TABLE [dbo].[ColumnMapping] ADD DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[ColumnMapping] ADD DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[ColumnScaleConfig] ADD DEFAULT ('DECIMAL(18,3)') FOR [DataType]
GO
ALTER TABLE [dbo].[ColumnScaleConfig] ADD DEFAULT ((0)) FOR [IsDynamic]
GO
ALTER TABLE [dbo].[ColumnScaleConfig] ADD DEFAULT (getdate()) FOR [LastUpdated]
GO
ALTER TABLE [dbo].[DailyEnergy] ADD DEFAULT (getdate()) FOR [CalculatedAt]
GO
ALTER TABLE [dbo].[DeviceRegistry] ADD DEFAULT (getdate()) FOR [FirstSeen]
GO
ALTER TABLE [dbo].[DeviceRegistry] ADD DEFAULT (getdate()) FOR [LastSeen]
GO
ALTER TABLE [dbo].[DeviceRegistry] ADD DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[DeviceRegistry] ADD DEFAULT ((0)) FOR [MessageCount]
GO
ALTER TABLE [dbo].[DeviceRegistry] ADD DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[DeviceRegistry] ADD DEFAULT (getdate()) FOR [UpdatedAt]
GO
ALTER TABLE [dbo].[FailedMessages] ADD DEFAULT ((0)) FOR [RetryCount]
GO
ALTER TABLE [dbo].[FailedMessages] ADD DEFAULT ((0)) FOR [IsResolved]
GO
ALTER TABLE [dbo].[FailedMessages] ADD DEFAULT (getdate()) FOR [ReceivedAt]
GO
ALTER TABLE [dbo].[HourlyEnergy] ADD DEFAULT (getdate()) FOR [CalculatedAt]
GO
ALTER TABLE [dbo].[KWHData] ADD DEFAULT (getdate()) FOR [ReceivedTime]
GO
ALTER TABLE [dbo].[KWHData_History] ADD DEFAULT (getdate()) FOR [ArchivedAt]
GO
ALTER TABLE [dbo].[MonthlyEnergy] ADD DEFAULT (getdate()) FOR [CalculatedAt]
GO
ALTER TABLE [dbo].[YearlyEnergy] ADD DEFAULT (getdate()) FOR [CalculatedAt]
GO
ALTER TABLE [dbo].[KWHData] WITH CHECK ADD CONSTRAINT [FK_KWHData_DeviceRegistry] FOREIGN KEY([DeviceKey]) REFERENCES [dbo].[DeviceRegistry] ([DeviceKey])
GO
ALTER TABLE [dbo].[KWHData] CHECK CONSTRAINT [FK_KWHData_DeviceRegistry]
GO
CREATE PROCEDURE [dbo].[sp_CleanupOldData] @DaysToKeep INT = 90 AS BEGIN SET NOCOUNT ON; DELETE FROM KWHData WHERE ReceivedTime < DATEADD(DAY, -@DaysToKeep, GETDATE()); DELETE FROM AppLog WHERE CreatedAt < DATEADD(DAY, -@DaysToKeep, GETDATE()); DELETE FROM FailedMessages WHERE ReceivedAt < DATEADD(DAY, -30, GETDATE()) AND IsResolved = 1; END;
GO
CREATE PROCEDURE [dbo].[sp_RegisterDevice] @DeviceId VARCHAR(50), @GroupName VARCHAR(100) = NULL, @DeviceKey VARCHAR(20) OUTPUT AS BEGIN SET NOCOUNT ON; IF NOT EXISTS (SELECT 1 FROM DeviceRegistry WHERE DeviceId = @DeviceId) BEGIN DECLARE @NextNumber INT; SELECT @NextNumber = ISNULL(MAX(CAST(SUBSTRING(DeviceKey, 5, 3) AS INT)), 0) + 1 FROM DeviceRegistry WHERE DeviceKey LIKE 'KWH-%'; SET @DeviceKey = 'KWH-' + RIGHT('000' + CAST(@NextNumber AS VARCHAR), 3); INSERT INTO DeviceRegistry (DeviceKey, DeviceId, GroupName, FirstSeen, LastSeen) VALUES (@DeviceKey, @DeviceId, @GroupName, GETDATE(), GETDATE()); END ELSE BEGIN SELECT @DeviceKey = DeviceKey FROM DeviceRegistry WHERE DeviceId = @DeviceId; UPDATE DeviceRegistry SET LastSeen = GETDATE(), GroupName = ISNULL(@GroupName, GroupName), UpdatedAt = GETDATE() WHERE DeviceId = @DeviceId; END END;
GO
USE [master]
GO
ALTER DATABASE [HaiwellElectrical] SET READ_WRITE
GO
";
    }
}
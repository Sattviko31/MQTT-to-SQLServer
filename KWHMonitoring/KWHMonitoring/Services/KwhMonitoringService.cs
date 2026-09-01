using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using KWHMonitoring.Configuration;

namespace KWHMonitoring.Services;

public class KwhMonitoringService : BackgroundService
{
    private readonly ILogger<KwhMonitoringService> _logger;
    private readonly AppConfig _config;
    private readonly string _connectionString;

    private IMqttClient? _mqttClient;
    private long _messageCount, _errorCount, _successCount;

    private readonly ConcurrentQueue<MqttMessageBuffer> _messageQueue = new();
    private readonly ConcurrentDictionary<string, string> _deviceKeyCache = new();
    private readonly HashSet<string> _existingColumns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _columnMapping = new(StringComparer.OrdinalIgnoreCase);

    public KwhMonitoringService(ILogger<KwhMonitoringService> logger, IOptions<AppConfig> config)
    {
        _logger = logger;
        _config = config.Value;
        _connectionString = _config.Database.GetConnectionString();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("╔══════════════════════════════════════════════════════════╗");
        _logger.LogInformation("║   HAIWELL ELECTRICAL - MQTT TO SQL SERVER SERVICE        ║");
        _logger.LogInformation("╚══════════════════════════════════════════════════════════╝");

        // LOG KONFIGURASI YANG DIGUNAKAN (penting untuk troubleshooting)
        _logger.LogInformation($"[*] Konfigurasi aktif:");
        _logger.LogInformation($"    - SQL Server: {_config.Database.Server}");
        _logger.LogInformation($"    - Database: {_config.Database.DatabaseName}");
        _logger.LogInformation($"    - SQL User: {_config.Database.Username}");
        _logger.LogInformation($"    - MQTT Broker: {_config.Mqtt.BrokerIp}:{_config.Mqtt.Port}");

        try
        {
            // 1. Validasi koneksi database dengan retry
            _logger.LogInformation("[*] Memvalidasi koneksi ke SQL Server...");
            await ValidateDatabaseConnectionAsync(stoppingToken);

            _logger.LogInformation("[*] Memulai Smart Migration Database...");
            await RunMigrationAsync(stoppingToken);

            _logger.LogInformation("[*] Memuat konfigurasi dari database...");
            await LoadConfigurationsAsync(stoppingToken);

            _ = Task.Run(() => ProcessQueueAsync(stoppingToken), stoppingToken);
            _ = Task.Run(() => MonitorQueueAsync(stoppingToken), stoppingToken);

            _logger.LogInformation("[*] Menghubungkan ke MQTT Broker...");
            await ConnectMqttAsync(stoppingToken);

            _logger.LogInformation("[✓] Service berjalan normal di background.");
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[*] Service dihentikan.");
        }
        catch (SqlException ex) when (ex.Number == 258 || ex.Number == -2 || ex.Number == 10060 || ex.Number == 10061 || ex.Number == 18456 || ex.Number == 4060)
        {
            // Error koneksi SQL Server - berikan instruksi troubleshooting
            _logger.LogCritical(ex, "[✗] Gagal koneksi ke SQL Server");
            _logger.LogError("═══════════════════════════════════════════════════════════");
            _logger.LogError("TROUBLESHOOTING - SQL Server Connection Failed:");
            _logger.LogError($"1. Cek SQL Server dapat diakses: ping {_config.Database.Server}");
            _logger.LogError($"2. Cek port 1433 terbuka: telnet {_config.Database.Server} 1433");
            _logger.LogError("3. Cek SQL Server Configuration Manager:");
            _logger.LogError("   - TCP/IP → Enabled");
            _logger.LogError("   - SQL Server Browser → Running");
            _logger.LogError("4. Cek Windows Firewall → Allow port 1433");
            _logger.LogError("5. Cek SQL Server Authentication Mode → Mixed Mode");
            _logger.LogError($"6. Cek instance name (jika pakai SQLEXPRESS):");
            _logger.LogError($"   Gunakan: {_config.Database.Server}\\SQLEXPRESS");
            _logger.LogError("7. Jalankan ulang setup: hapus appsettings.user.json");
            _logger.LogError("═══════════════════════════════════════════════════════════");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "[✗] Fatal error pada service");
        }
        finally
        {
            if (_mqttClient?.IsConnected == true) await _mqttClient.DisconnectAsync();
            _logger.LogInformation($"[STATS FINAL] Total: {_messageCount} | Success: {_successCount} | Errors: {_errorCount}");
        }
    }

    // ============================================================
    // VALIDASI KONEKSI DATABASE DENGAN RETRY
    // ============================================================
    private async Task ValidateDatabaseConnectionAsync(CancellationToken ct)
    {
        const int maxRetries = 5;
        const int initialDelayMs = 3000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation($"[*] Attempt koneksi ke SQL Server ({attempt}/{maxRetries})...");

                using var conn = new SqlConnection(_config.Database.GetMasterConnectionString());
                await conn.OpenAsync(ct);

                _logger.LogInformation($"[✓] Koneksi ke SQL Server berhasil (attempt {attempt})");
                return;
            }
            catch (SqlException ex) when (attempt < maxRetries)
            {
                _logger.LogWarning($"[!] Koneksi gagal (attempt {attempt}): {ex.Message}");

                var delay = initialDelayMs * attempt;
                _logger.LogInformation($"[*] Retry dalam {delay / 1000} detik...");
                await Task.Delay(delay, ct);
            }
        }

        throw new Exception($"Gagal koneksi ke SQL Server setelah {maxRetries} percobaan. " +
            $"Server: {_config.Database.Server}, Database: {_config.Database.DatabaseName}");
    }

    #region Smart Migration - DIPERBAIKI untuk DBO Issue
    private async Task RunMigrationAsync(CancellationToken ct)
    {
        var startTime = DateTime.Now;
        using var conn = new SqlConnection(_config.Database.GetMasterConnectionString());
        await conn.OpenAsync(ct);

        // 1. Cek database
        using var checkDbCmd = new SqlCommand($"SELECT DB_ID('{_config.Database.DatabaseName}')", conn);
        bool dbExists = await checkDbCmd.ExecuteScalarAsync(ct) != DBNull.Value;

        if (!dbExists)
        {
            _logger.LogInformation($"[+] Membuat database '{_config.Database.DatabaseName}'...");
            using var createDbCmd = new SqlCommand($"CREATE DATABASE [{_config.Database.DatabaseName}]", conn);
            createDbCmd.CommandTimeout = 120;
            await createDbCmd.ExecuteNonQueryAsync(ct);
            _logger.LogInformation($"[✓] Database berhasil dibuat.");
        }
        else
        {
            _logger.LogInformation($"[✓] Database '{_config.Database.DatabaseName}' sudah ada.");
        }

        // 2. PERBAIKAN: Cek apakah user yang connect adalah dbo dari database yang baru dibuat
        _logger.LogInformation("[*] Memeriksa status user dan permissions...");

        var checkUserScript = $@"
            USE [{_config.Database.DatabaseName}];
            
            -- Cek apakah login ini sudah menjadi dbo di database ini
            DECLARE @IsDbo BIT = 0;
            IF EXISTS (
                SELECT 1 FROM sys.database_principals dp
                INNER JOIN sys.server_principals sp ON dp.sid = sp.sid
                WHERE sp.name = '{_config.Database.Username}' 
                AND dp.name = 'dbo'
            )
            BEGIN
                SET @IsDbo = 1;
            END
            
            SELECT @IsDbo AS IsDbo;";

        bool isDbo = false;
        using (var cmd = new SqlCommand(checkUserScript, conn))
        {
            cmd.CommandTimeout = 60;
            var result = await cmd.ExecuteScalarAsync(ct);
            isDbo = result != null && Convert.ToBoolean(result);
        }

        if (isDbo)
        {
            _logger.LogInformation($"[✓] User '{_config.Database.Username}' adalah dbo di database '{_config.Database.DatabaseName}'. Skip pembuatan user & role.");
        }
        else
        {
            // User bukan dbo, buat user dan berikan role
            _logger.LogInformation($"[*] Membuat user '{_config.Database.Username}' dan memberikan permissions...");

            var userScript = $@"
                USE [{_config.Database.DatabaseName}];
                
                -- Buat login di master jika belum ada
                IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = '{_config.Database.Username}')
                    CREATE LOGIN [{_config.Database.Username}] WITH PASSWORD = '{_config.Database.Password}', 
                        DEFAULT_DATABASE = [{_config.Database.DatabaseName}], CHECK_EXPIRATION = OFF, CHECK_POLICY = OFF;
                
                -- Buat user di database jika belum ada
                IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = '{_config.Database.Username}')
                    CREATE USER [{_config.Database.Username}] FOR LOGIN [{_config.Database.Username}];
                
                -- Berikan role yang diperlukan
                IF IS_ROLEMEMBER('db_ddladmin', '{_config.Database.Username}') IS NULL OR IS_ROLEMEMBER('db_ddladmin', '{_config.Database.Username}') = 0
                    ALTER ROLE [db_ddladmin] ADD MEMBER [{_config.Database.Username}];
                IF IS_ROLEMEMBER('db_datareader', '{_config.Database.Username}') IS NULL OR IS_ROLEMEMBER('db_datareader', '{_config.Database.Username}') = 0
                    ALTER ROLE [db_datareader] ADD MEMBER [{_config.Database.Username}];
                IF IS_ROLEMEMBER('db_datawriter', '{_config.Database.Username}') IS NULL OR IS_ROLEMEMBER('db_datawriter', '{_config.Database.Username}') = 0
                    ALTER ROLE [db_datawriter] ADD MEMBER [{_config.Database.Username}];";

            using (var cmd = new SqlCommand(userScript, conn))
            {
                cmd.CommandTimeout = 60;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            _logger.LogInformation($"[✓] User dan permissions berhasil dibuat.");
        }

        // 3. SMART MIGRATION: Cek objek yang sudah ada
        _logger.LogInformation("[*] Memeriksa objek database yang sudah ada...");

        var existingObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = new SqlCommand(@"
            SELECT name FROM sys.tables WHERE type = 'U'
            UNION SELECT name FROM sys.views WHERE type = 'V'
            UNION SELECT name FROM sys.procedures WHERE type IN ('P', 'PC')
            UNION SELECT name FROM sys.indexes WHERE type IN (1, 2) AND name IS NOT NULL", conn))
        using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                existingObjects.Add(reader.GetString(0));
        }
        _logger.LogInformation($"[✓] Ditemukan {existingObjects.Count} objek yang sudah ada.");

        // 4. Eksekusi script dengan SKIP CERDAS
        _logger.LogInformation("[*] Membuat objek yang belum ada...");

        var cleanScript = MigrationScript.Replace("[HaiwellElectrical]", $"[{_config.Database.DatabaseName}]");

        // HAPUS SEMUA ALTER DATABASE jika database sudah ada (hemat waktu, hindari timeout)
        if (dbExists)
        {
            cleanScript = Regex.Replace(cleanScript, @"ALTER\s+DATABASE\s+\[.*?\].*?GO", "",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            cleanScript = Regex.Replace(cleanScript, @"CREATE\s+DATABASE\s+\[.*?\][\s\S]*?GO", "",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
        }

        // Hapus USE [master]
        cleanScript = Regex.Replace(cleanScript, @"USE\s+\[master\].*?GO", "",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // PERBAIKAN: Hapus CREATE USER dan ALTER ROLE jika user adalah dbo
        if (isDbo)
        {
            cleanScript = Regex.Replace(cleanScript, @"CREATE\s+USER\s+\[.*?\].*?GO", "",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            cleanScript = Regex.Replace(cleanScript, @"ALTER\s+ROLE\s+\[.*?\].*?GO", "",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        var batches = Regex.Split(cleanScript, @"^\s*GO\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        int success = 0, skipped = 0;

        foreach (var batch in batches)
        {
            var b = batch.Trim();
            if (string.IsNullOrEmpty(b)) continue;

            // SMART SKIP: Cek apakah objek sudah ada
            var match = Regex.Match(b, @"CREATE\s+(?:TABLE|VIEW|PROC(?:EDURE)?)\s+\[?(?:dbo\.)?\[?(\w+)\]?",
                RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var objectName = match.Groups[1].Value;
                if (existingObjects.Contains(objectName))
                {
                    skipped++;
                    continue;
                }
            }

            // Cek CREATE INDEX
            var indexMatch = Regex.Match(b, @"CREATE\s+(?:UNIQUE\s+)?(?:NONCLUSTERED\s+)?INDEX\s+\[?(\w+)\]?",
                RegexOptions.IgnoreCase);
            if (indexMatch.Success)
            {
                var indexName = indexMatch.Groups[1].Value;
                if (existingObjects.Contains(indexName))
                {
                    skipped++;
                    continue;
                }
            }

            try
            {
                using var cmd = new SqlCommand(b, conn);
                cmd.CommandTimeout = 60;
                await cmd.ExecuteNonQueryAsync(ct);
                success++;
            }
            catch (SqlException ex) when (ex.Number is 2714 or 1913 or 2705 or 15023 or 2627 or 2601 or 15231 or 15151 or 1781 or 15063)
            {
                skipped++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[!] Warning: {ex.Message}");
            }
        }

        var elapsed = (DateTime.Now - startTime).TotalSeconds;
        _logger.LogInformation($"[✓] Smart Migration selesai dalam {elapsed:F1} detik. ({success} dibuat, {skipped} dilewati)");
    }
    #endregion

    #region Configuration & MQTT
    private async Task LoadConfigurationsAsync(CancellationToken ct)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        using (var cmd = new SqlCommand("SELECT OldColumnName, NewColumnName FROM ColumnMapping WHERE IsActive = 1", conn))
        using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) _columnMapping[reader.GetString(0).ToUpper()] = reader.GetString(1);

        using (var cmd = new SqlCommand("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'KWHData'", conn))
        using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) _existingColumns.Add(reader.GetString(0).ToUpper());

        if (_columnMapping.Count == 0)
        {
            _columnMapping["VR"] = "PHASE_R";
            _columnMapping["VS"] = "PHASE_S";
            _columnMapping["VT"] = "PHASE_T";
            _columnMapping["AKTIF_W"] = "Aktif_Power";
        }
        _logger.LogInformation($"[✓] Loaded {_columnMapping.Count} mappings, {_existingColumns.Count} columns");
    }

    private async Task ConnectMqttAsync(CancellationToken ct)
    {
        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();

        _mqttClient.ConnectedAsync += _ => { _logger.LogInformation("[✓] Connected to MQTT"); return Task.CompletedTask; };
        _mqttClient.DisconnectedAsync += async _ =>
        {
            _logger.LogWarning("[✗] Disconnected. Reconnecting in 5s...");
            await Task.Delay(5000, ct);
            if (!ct.IsCancellationRequested && _mqttClient != null)
            {
                try { await _mqttClient.ConnectAsync(_mqttClient.Options, ct); }
                catch { }
            }
        };
        _mqttClient.ApplicationMessageReceivedAsync += e =>
        {
            _messageQueue.Enqueue(new MqttMessageBuffer
            {
                Topic = e.ApplicationMessage.Topic,
                Payload = e.ApplicationMessage.ConvertPayloadToString() ?? "",
                ReceivedAt = DateTime.Now
            });
            return Task.CompletedTask;
        };

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_config.Mqtt.BrokerIp, _config.Mqtt.Port)
            .WithClientId($"KWHApp_{Guid.NewGuid():N}".Substring(0, 20))
            .WithCleanSession();

        if (!string.IsNullOrEmpty(_config.Mqtt.Username))
            options.WithCredentials(_config.Mqtt.Username, _config.Mqtt.Password);

        await _mqttClient.ConnectAsync(options.Build(), ct);
        await _mqttClient.SubscribeAsync("#", MqttQualityOfServiceLevel.AtMostOnce, ct);
    }
    #endregion

    #region Message Processing
    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_messageQueue.TryDequeue(out var buffer)) await ProcessSingleMessageAsync(buffer, ct);
            else await Task.Delay(50, ct);
        }
    }

    private async Task MonitorQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(30000, ct);
            _logger.LogInformation($"[STATS] Total: {Interlocked.Read(ref _messageCount)} | Success: {Interlocked.Read(ref _successCount)} | Errors: {Interlocked.Read(ref _errorCount)} | Queue: {_messageQueue.Count}");
        }
    }

    private async Task ProcessSingleMessageAsync(MqttMessageBuffer buffer, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(buffer.Payload)) return;
            var doc = JsonDocument.Parse(buffer.Payload);
            var data = new DynamicMqttData();
            foreach (var prop in doc.RootElement.EnumerateObject()) data.Properties[prop.Name] = prop.Value.ToString();

            var newProps = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var keysToRemove = new List<string>();
            foreach (var kvp in data.Properties)
            {
                if (_columnMapping.TryGetValue(kvp.Key.ToUpper(), out var mapped))
                {
                    newProps[mapped] = kvp.Value;
                    keysToRemove.Add(kvp.Key);
                }
                else newProps[kvp.Key] = kvp.Value;
            }
            foreach (var k in keysToRemove) data.Properties.Remove(k);
            foreach (var kvp in newProps) if (!data.Properties.ContainsKey(kvp.Key)) data.Properties[kvp.Key] = kvp.Value;

            var deviceId = data.GetProperty("_groupName")?.ToUpper().Contains("KWHMETER") == true
                ? $"kwhapp{data.GetProperty("_groupName")!.ToUpper().Replace("KWHMETER", "")}"
                : buffer.Topic.Split('/').Length >= 3 ? buffer.Topic.Split('/')[2] : "unknown";

            var deviceKey = await GetOrCreateDeviceKeyAsync(deviceId, data.GetProperty("_groupName"), ct);
            await EnsureColumnsExistAsync(data, ct);
            await SaveToDatabaseAsync(data, deviceId, deviceKey, ct);

            Interlocked.Increment(ref _successCount);
            Interlocked.Increment(ref _messageCount);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            Interlocked.Increment(ref _messageCount);
            _logger.LogError(ex, $"[✗] Error processing: {buffer.Topic}");
            await LogFailedMessage(buffer, ex.Message, ct);
        }
    }

    private async Task<string> GetOrCreateDeviceKeyAsync(string deviceId, string? groupName, CancellationToken ct)
    {
        if (_deviceKeyCache.TryGetValue(deviceId, out var cached)) return cached;
        var deviceKey = deviceId.ToUpper();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var checkCmd = new SqlCommand("SELECT DeviceKey FROM DeviceRegistry WHERE DeviceId = @DeviceId", conn);
        checkCmd.Parameters.AddWithValue("@DeviceId", deviceId);
        var existing = await checkCmd.ExecuteScalarAsync(ct);

        if (existing != null)
        {
            deviceKey = existing.ToString()!;
            using var upd = new SqlCommand("UPDATE DeviceRegistry SET LastSeen = GETDATE(), GroupName = ISNULL(@GroupName, GroupName), UpdatedAt = GETDATE() WHERE DeviceId = @DeviceId", conn);
            upd.Parameters.AddWithValue("@DeviceId", deviceId);
            upd.Parameters.AddWithValue("@GroupName", (object?)groupName ?? DBNull.Value);
            await upd.ExecuteNonQueryAsync(ct);
        }
        else
        {
            using var ins = new SqlCommand("INSERT INTO DeviceRegistry (DeviceKey, DeviceId, GroupName, FirstSeen, LastSeen) VALUES (@DeviceKey, @DeviceId, @GroupName, GETDATE(), GETDATE())", conn);
            ins.Parameters.AddWithValue("@DeviceKey", deviceKey);
            ins.Parameters.AddWithValue("@DeviceId", deviceId);
            ins.Parameters.AddWithValue("@GroupName", (object?)groupName ?? DBNull.Value);
            await ins.ExecuteNonQueryAsync(ct);
            _logger.LogInformation($"[+] Registered: {deviceId} -> {deviceKey}");
        }
        _deviceKeyCache.TryAdd(deviceId, deviceKey);
        return deviceKey;
    }

    private async Task EnsureColumnsExistAsync(DynamicMqttData data, CancellationToken ct)
    {
        var systemCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "_terminalTime", "_groupName", "Id", "DeviceKey", "TerminalTime", "GroupName", "DeviceId", "ReceivedTime" };
        foreach (var key in data.Properties.Keys.ToList())
        {
            if (systemCols.Contains(key) || _existingColumns.Contains(key)) continue;
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            using var cmd = new SqlCommand($"ALTER TABLE KWHData ADD [{key}] DECIMAL(18,3) NULL", conn);
            await cmd.ExecuteNonQueryAsync(ct);
            _existingColumns.Add(key);
            _logger.LogInformation($"[+] New column added: {key}");
        }
    }

    private async Task SaveToDatabaseAsync(DynamicMqttData data, string deviceId, string deviceKey, CancellationToken ct)
    {
        for (int retry = 0; retry < 3; retry++)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var columns = new List<string> { "DeviceKey", "TerminalTime", "GroupName", "DeviceId" };
                var values = new List<string> { "@DeviceKey", "@TerminalTime", "@GroupName", "@DeviceId" };

                using var cmd = new SqlCommand("", conn);
                cmd.Parameters.AddWithValue("@DeviceKey", deviceKey);
                cmd.Parameters.AddWithValue("@TerminalTime", ParseDateTime(data.GetProperty("_terminalTime")));
                cmd.Parameters.AddWithValue("@GroupName", (object?)data.GetProperty("_groupName") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DeviceId", deviceId);

                foreach (var kvp in data.Properties)
                {
                    if (kvp.Key.StartsWith("_") || !_existingColumns.Contains(kvp.Key)) continue;
                    columns.Add($"[{kvp.Key}]");
                    values.Add($"@{kvp.Key}");
                    cmd.Parameters.AddWithValue($"@{kvp.Key}", ParseValueWithScale(kvp.Key, kvp.Value));
                }

                cmd.CommandText = $"INSERT INTO KWHData ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";
                await cmd.ExecuteNonQueryAsync(ct);

                using var upd = new SqlCommand("UPDATE DeviceRegistry SET MessageCount = MessageCount + 1, LastSeen = GETDATE(), UpdatedAt = GETDATE() WHERE DeviceKey = @DeviceKey", conn);
                upd.Parameters.AddWithValue("@DeviceKey", deviceKey);
                await upd.ExecuteNonQueryAsync(ct);
                return;
            }
            catch when (retry < 2) { await Task.Delay(500 * (retry + 1), ct); }
        }
    }

    private static decimal ParseValueWithScale(string col, string? val)
    {
        if (string.IsNullOrEmpty(val) || !decimal.TryParse(val, out var raw)) return 0;
        var c = col.ToUpper();
        if (c is "PHASE_R" or "PHASE_S" or "PHASE_T") return Math.Round(raw >= 10000m ? raw / 100m : raw >= 1000m ? raw / 10m : raw > 500m ? raw / 10m : raw, 2);
        if (c is "AMPERE_R" or "AMPERE_S" or "AMPERE_T") return Math.Round(raw / 10m, 2);
        if (c == "COSPHI") return Math.Round(raw / 10m, 3);
        if (c == "W") return Math.Round(raw / 10m, 1);
        if (c is "TOTALW1M" or "AKTIF_POWER" or "TOTALW") return Math.Round(raw / 100m, 2);
        if (c == "F") return Math.Round(raw / 10m, 2);
        return raw;
    }

    private static DateTime ParseDateTime(string? str)
    {
        if (string.IsNullOrEmpty(str)) return DateTime.Now;
        if (DateTime.TryParseExact(str, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var r1)) return r1;
        if (DateTime.TryParse(str, out var r2)) return r2;
        return DateTime.Now;
    }

    private async Task LogFailedMessage(MqttMessageBuffer buffer, string reason, CancellationToken ct)
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            using var cmd = new SqlCommand("INSERT INTO FailedMessages (Topic, Payload, Reason, ReceivedAt) VALUES (@Topic, @Payload, @Reason, @ReceivedAt)", conn);
            cmd.Parameters.AddWithValue("@Topic", buffer.Topic);
            cmd.Parameters.AddWithValue("@Payload", buffer.Payload);
            cmd.Parameters.AddWithValue("@Reason", reason);
            cmd.Parameters.AddWithValue("@ReceivedAt", buffer.ReceivedAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch { }
    }
    #endregion

    #region Embedded Migration Script
    private const string MigrationScript = @"
USE [master]
GO
CREATE DATABASE [HaiwellElectrical]
CONTAINMENT = NONE
ON  PRIMARY
( NAME = N'HaiwellElectrical', FILENAME = N'C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\DATA\HaiwellElectrical.mdf' , SIZE = 270336KB , MAXSIZE = UNLIMITED, FILEGROWTH = 65536KB )
LOG ON
( NAME = N'HaiwellElectrical_log', FILENAME = N'C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\DATA\HaiwellElectrical_log.ldf' , SIZE = 204800KB , MAXSIZE = 2048GB , FILEGROWTH = 65536KB )
WITH CATALOG_COLLATION = DATABASE_DEFAULT, LEDGER = OFF
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
CREATE USER [kwhapp] WITHOUT LOGIN WITH DEFAULT_SCHEMA=[dbo]
GO
ALTER ROLE [db_ddladmin] ADD MEMBER [kwhapp]
GO
ALTER ROLE [db_datareader] ADD MEMBER [kwhapp]
GO
ALTER ROLE [db_datawriter] ADD MEMBER [kwhapp]
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
PRIMARY KEY CLUSTERED ([Id] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
UNIQUE NONCLUSTERED ([DeviceKey] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
UNIQUE NONCLUSTERED ([DeviceId] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
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
PRIMARY KEY CLUSTERED ([Id] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE VIEW [dbo].[vLatestKWHData]
AS
SELECT
k.Id, k.DeviceKey, d.DeviceId, d.GroupName,
k.TerminalTime, k.ReceivedTime,
k.PHASE_R, k.PHASE_S, k.PHASE_T,
k.AMPERE_R, k.AMPERE_S, k.AMPERE_T,
k.CosPhi, k.W, k.Aktif_Power, k.TotalW, k.TotalW1M, k.F
FROM KWHData k
INNER JOIN DeviceRegistry d ON k.DeviceKey = d.DeviceKey
INNER JOIN (
SELECT DeviceKey, MAX(ReceivedTime) AS MaxTime
FROM KWHData GROUP BY DeviceKey
) latest ON k.DeviceKey = latest.DeviceKey AND k.ReceivedTime = latest.MaxTime;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE VIEW [dbo].[vDeviceSummary]
AS
SELECT
d.DeviceKey, d.DeviceId, d.GroupName,
d.FirstSeen, d.LastSeen, d.IsActive, d.MessageCount,
COUNT(k.Id) AS TotalRecords,
MAX(k.ReceivedTime) AS LastDataReceived
FROM DeviceRegistry d
LEFT JOIN KWHData k ON d.DeviceKey = k.DeviceKey
GROUP BY d.DeviceKey, d.DeviceId, d.GroupName,
d.FirstSeen, d.LastSeen, d.IsActive, d.MessageCount;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE VIEW [dbo].[vDailyEnergy]
AS
SELECT
k.DeviceKey, d.GroupName,
CAST(k.TerminalTime AS DATE) AS ReportDate,
MIN(k.TotalW) AS EnergyStart_kWh,
MAX(k.TotalW) AS EnergyEnd_kWh,
(MAX(k.TotalW) - MIN(k.TotalW)) AS DailyConsumption_kWh,
COUNT(*) AS ReadingCount
FROM KWHData k
INNER JOIN DeviceRegistry d ON k.DeviceKey = d.DeviceKey
GROUP BY k.DeviceKey, d.GroupName, CAST(k.TerminalTime AS DATE);
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[__EFMigrationsHistory](
[MigrationId] [nvarchar](150) NOT NULL,
[ProductVersion] [nvarchar](32) NOT NULL,
CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY CLUSTERED ([MigrationId] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[AnomalyLogs](
[Id] [bigint] IDENTITY(1,1) NOT NULL,
[DeviceKey] [nvarchar](50) NOT NULL,
[DeviceId] [nvarchar](50) NULL,
[AnomalyType] [nvarchar](20) NOT NULL,
[PowerValue] [decimal](18, 2) NOT NULL,
[ThresholdValue] [decimal](18, 2) NOT NULL,
[Deviation] [decimal](5, 2) NOT NULL,
[DetectedTime] [datetime2](7) NOT NULL,
[EMAValue] [decimal](18, 2) NULL,
[ThresholdMode] [nvarchar](20) NULL,
[Acknowledged] [bit] NULL,
[AcknowledgedTime] [datetime2](7) NULL,
[Notes] [nvarchar](500) NULL,
PRIMARY KEY CLUSTERED ([Id] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[AppLog](
[Id] [bigint] IDENTITY(1,1) NOT NULL,
[LogLevel] [varchar](20) NOT NULL,
[Message] [nvarchar](max) NOT NULL,
[Topic] [varchar](200) NULL,
[DeviceKey] [varchar](20) NULL,
[CreatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED ([Id] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[AppSettings](
[Id] [int] IDENTITY(1,1) NOT NULL,
[SettingKey] [nvarchar](100) NOT NULL,
[SettingValue] [nvarchar](500) NOT NULL,
[UpdatedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED ([Id] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
UNIQUE NONCLUSTERED ([SettingKey] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ColumnMapping](
[Id] [int] IDENTITY(1,1) NOT NULL,
[OldColumnName] [varchar](50) NOT NULL,
[NewColumnName] [varchar](50) NOT NULL,
[IsActive] [bit] NOT NULL,
[CreatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED ([Id] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ColumnScaleConfig](
[ColumnName] [varchar](50) NOT NULL,
[ScaleFactor] [decimal](18, 5) NOT NULL,
[RegisterAddress] [varchar](10) NULL,
[DataType] [varchar](20) NOT NULL,
[Unit] [varchar](50) NULL,
[Category] [varchar](50) NULL,
[Description] [varchar](500) NULL,
[IsDynamic] [bit] NOT NULL,
[LastUpdated] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED ([ColumnName] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[DailyEnergy](
[Id] [bigint] IDENTITY(1,1) NOT NULL,
[DeviceKey] [nvarchar](100) NOT NULL,
[Date] [date] NOT NULL,
[EnergyKWh] [decimal](18, 4) NOT NULL,
[CalculatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED ([Id] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[FailedMessages](
[Id] [bigint] IDENTITY(1,1) NOT NULL,
[Topic] [varchar](500) NOT NULL,
[Payload] [nvarchar](max) NOT NULL,
[Reason] [nvarchar](500) NULL,
[RetryCount] [int] NOT NULL,
[IsResolved] [bit] NOT NULL,
[ReceivedAt] [datetime2](7) NOT NULL,
[ResolvedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED ([Id] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[HourlyEnergy](
[Id] [bigint] IDENTITY(1,1) NOT NULL,
[DeviceKey] [nvarchar](100) NOT NULL,
[Hour] [datetime2](7) NOT NULL,
[EnergyKWh] [decimal](18, 4) NOT NULL,
[CalculatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED ([Id] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[KWHData_History](
[HistoryId] [bigint] IDENTITY(1,1) NOT NULL,
[OriginalId] [bigint] NOT NULL,
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
[ArchivedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED ([HistoryId] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[MonthlyEnergy](
[Id] [bigint] IDENTITY(1,1) NOT NULL,
[DeviceKey] [nvarchar](100) NOT NULL,
[Year] [int] NOT NULL,
[Month] [int] NOT NULL,
[EnergyKWh] [decimal](18, 4) NOT NULL,
[CalculatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED ([Id] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[YearlyEnergy](
[Id] [bigint] IDENTITY(1,1) NOT NULL,
[DeviceKey] [nvarchar](100) NOT NULL,
[Year] [int] NOT NULL,
[EnergyKWh] [decimal](18, 4) NOT NULL,
[CalculatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED ([Id] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_AnomalyLogs_DetectedTime] ON [dbo].[AnomalyLogs]([DetectedTime] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_AnomalyLogs_DeviceKey] ON [dbo].[AnomalyLogs]([DeviceKey] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_AppLog_CreatedAt] ON [dbo].[AppLog]([CreatedAt] DESC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_AppLog_Level] ON [dbo].[AppLog]([LogLevel] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_ColumnMapping_OldName] ON [dbo].[ColumnMapping]([OldColumnName] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_DeviceRegistry_DeviceId] ON [dbo].[DeviceRegistry]([DeviceId] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_DeviceRegistry_DeviceKey] ON [dbo].[DeviceRegistry]([DeviceKey] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_FailedMessages_IsResolved] ON [dbo].[FailedMessages]([IsResolved] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_FailedMessages_ReceivedAt] ON [dbo].[FailedMessages]([ReceivedAt] DESC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_DeviceKey] ON [dbo].[KWHData]([DeviceKey] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_DeviceKey_Only] ON [dbo].[KWHData]([DeviceKey] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_DeviceKey_ReceivedTime] ON [dbo].[KWHData]([DeviceKey] ASC,[ReceivedTime] DESC)
INCLUDE([DeviceId],[GroupName],[TerminalTime],[PHASE_R],[PHASE_S],[PHASE_T],[AMPERE_R],[AMPERE_S],[AMPERE_T],[CosPhi],[W],[TotalW1M],[Aktif_Power],[TotalW],[F]) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_DeviceKey_TerminalTime] ON [dbo].[KWHData]([DeviceKey] ASC,[TerminalTime] DESC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_ReceivedTime] ON [dbo].[KWHData]([ReceivedTime] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_ReceivedTime_DeviceKey] ON [dbo].[KWHData]([ReceivedTime] DESC,[DeviceKey] ASC)
INCLUDE([DeviceId],[GroupName],[TerminalTime],[PHASE_R],[PHASE_S],[PHASE_T],[AMPERE_R],[AMPERE_S],[AMPERE_T],[CosPhi],[W],[TotalW1M],[Aktif_Power],[TotalW],[F]) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_TerminalTime] ON [dbo].[KWHData]([TerminalTime] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_History_ArchivedAt] ON [dbo].[KWHData_History]([ArchivedAt] DESC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE NONCLUSTERED INDEX [IX_KWHData_History_DeviceKey] ON [dbo].[KWHData_History]([DeviceKey] ASC)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
ALTER TABLE [dbo].[AnomalyLogs] ADD  DEFAULT (getdate()) FOR [DetectedTime]
GO
ALTER TABLE [dbo].[AnomalyLogs] ADD  DEFAULT ('manual') FOR [ThresholdMode]
GO
ALTER TABLE [dbo].[AnomalyLogs] ADD  DEFAULT ((0)) FOR [Acknowledged]
GO
ALTER TABLE [dbo].[AppLog] ADD  DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[AppSettings] ADD  DEFAULT (getdate()) FOR [UpdatedAt]
GO
ALTER TABLE [dbo].[ColumnMapping] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[ColumnMapping] ADD  DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[ColumnScaleConfig] ADD  DEFAULT ('DECIMAL(18,3)') FOR [DataType]
GO
ALTER TABLE [dbo].[ColumnScaleConfig] ADD  DEFAULT ((0)) FOR [IsDynamic]
GO
ALTER TABLE [dbo].[ColumnScaleConfig] ADD  DEFAULT (getdate()) FOR [LastUpdated]
GO
ALTER TABLE [dbo].[DailyEnergy] ADD  DEFAULT (getdate()) FOR [CalculatedAt]
GO
ALTER TABLE [dbo].[DeviceRegistry] ADD  DEFAULT (getdate()) FOR [FirstSeen]
GO
ALTER TABLE [dbo].[DeviceRegistry] ADD  DEFAULT (getdate()) FOR [LastSeen]
GO
ALTER TABLE [dbo].[DeviceRegistry] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[DeviceRegistry] ADD  DEFAULT ((0)) FOR [MessageCount]
GO
ALTER TABLE [dbo].[DeviceRegistry] ADD  DEFAULT (getdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[DeviceRegistry] ADD  DEFAULT (getdate()) FOR [UpdatedAt]
GO
ALTER TABLE [dbo].[FailedMessages] ADD  DEFAULT ((0)) FOR [RetryCount]
GO
ALTER TABLE [dbo].[FailedMessages] ADD  DEFAULT ((0)) FOR [IsResolved]
GO
ALTER TABLE [dbo].[FailedMessages] ADD  DEFAULT (getdate()) FOR [ReceivedAt]
GO
ALTER TABLE [dbo].[HourlyEnergy] ADD  DEFAULT (getdate()) FOR [CalculatedAt]
GO
ALTER TABLE [dbo].[KWHData] ADD  DEFAULT (getdate()) FOR [ReceivedTime]
GO
ALTER TABLE [dbo].[KWHData_History] ADD  DEFAULT (getdate()) FOR [ArchivedAt]
GO
ALTER TABLE [dbo].[MonthlyEnergy] ADD  DEFAULT (getdate()) FOR [CalculatedAt]
GO
ALTER TABLE [dbo].[YearlyEnergy] ADD  DEFAULT (getdate()) FOR [CalculatedAt]
GO
ALTER TABLE [dbo].[KWHData]  WITH CHECK ADD  CONSTRAINT [FK_KWHData_DeviceRegistry] FOREIGN KEY([DeviceKey])
REFERENCES [dbo].[DeviceRegistry] ([DeviceKey])
GO
ALTER TABLE [dbo].[KWHData] CHECK CONSTRAINT [FK_KWHData_DeviceRegistry]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE PROCEDURE [dbo].[sp_CleanupOldData]
@DaysToKeep INT = 90
AS
BEGIN
SET NOCOUNT ON;
DELETE FROM KWHData WHERE ReceivedTime < DATEADD(DAY, -@DaysToKeep, GETDATE());
DELETE FROM AppLog WHERE CreatedAt < DATEADD(DAY, -@DaysToKeep, GETDATE());
DELETE FROM FailedMessages WHERE ReceivedAt < DATEADD(DAY, -30, GETDATE()) AND IsResolved = 1;
END;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE PROCEDURE [dbo].[sp_RegisterDevice]
@DeviceId VARCHAR(50),
@GroupName VARCHAR(100) = NULL,
@DeviceKey VARCHAR(20) OUTPUT
AS
BEGIN
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM DeviceRegistry WHERE DeviceId = @DeviceId)
BEGIN
DECLARE @NextNumber INT;
SELECT @NextNumber = ISNULL(MAX(CAST(SUBSTRING(DeviceKey, 5, 3) AS INT)), 0) + 1
FROM DeviceRegistry WHERE DeviceKey LIKE 'KWH-%';
SET @DeviceKey = 'KWH-' + RIGHT('000' + CAST(@NextNumber AS VARCHAR), 3);
INSERT INTO DeviceRegistry (DeviceKey, DeviceId, GroupName, FirstSeen, LastSeen)
VALUES (@DeviceKey, @DeviceId, @GroupName, GETDATE(), GETDATE());
END
ELSE
BEGIN
SELECT @DeviceKey = DeviceKey FROM DeviceRegistry WHERE DeviceId = @DeviceId;
UPDATE DeviceRegistry
SET LastSeen = GETDATE(), GroupName = ISNULL(@GroupName, GroupName), UpdatedAt = GETDATE()
WHERE DeviceId = @DeviceId;
END
END;
GO
USE [master]
GO
ALTER DATABASE [HaiwellElectrical] SET  READ_WRITE
GO
";
    #endregion
}

public class DynamicMqttData
{
    public Dictionary<string, string?> Properties { get; set; } = new();
    public string? GetProperty(string name) => Properties.TryGetValue(name, out string? value) ? value : null;
}

public class MqttMessageBuffer
{
    public string Topic { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime ReceivedAt { get; set; } = DateTime.Now;
}
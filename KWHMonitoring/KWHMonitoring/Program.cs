using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using KWHMonitoring.Configuration;
using KWHMonitoring.Services;

namespace KWHMonitoring;

public class Program
{
    private static readonly string ConfigFilePath = Path.Combine(
        AppContext.BaseDirectory, "appsettings.user.json");
    private const string ServiceName = "KWHMonitoring";
    private const string ServiceDisplayName = "KWH Monitoring Service";

    public static async Task Main(string[] args)
    {
        // DETEKSI MODE
        bool isServiceMode = args.Contains("--run-as-service", StringComparer.OrdinalIgnoreCase);

        // Jika BUKAN service mode → jalankan setup wizard
        if (!isServiceMode)
        {
            RunConsoleSetup();
            // Setelah setup selesai, program akan exit di dalam RunConsoleSetup()
            return;
        }

        // MODE SERVICE: Setup logging (file only, tanpa console)
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: Path.Combine(logDir, "kwh-monitoring-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 2,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("═══════════════════════════════════════════════════════════");
            Log.Information("  KWH Monitoring Service Starting (Windows Service Mode)...");
            Log.Information("═══════════════════════════════════════════════════════════");

            var host = CreateHostBuilder(args).Build();
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
        }
        finally
        {
            Log.Information("KWH Monitoring Service stopped");
            Log.CloseAndFlush();
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options => options.ServiceName = ServiceName)
            .UseSerilog()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddJsonFile(ConfigFilePath, optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.Configure<AppConfig>(hostContext.Configuration);
                services.AddHostedService<KwhMonitoringService>();
            });

    // ============================================================
    // CONSOLE SETUP WIZARD + AUTO INSTALL SERVICE
    // ============================================================
    private static void RunConsoleSetup()
    {
        Console.Clear();
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   HAIWELL ELECTRICAL - KWH MONITORING                    ║");
        Console.WriteLine("║   SETUP WIZARD + AUTO INSTALL SERVICE                    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var config = LoadExistingConfig();

        Console.WriteLine("=== Konfigurasi MQTT ===");
        Console.Write($"MQTT Broker IP (default: {config.Mqtt.BrokerIp}): ");
        var input = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(input)) config.Mqtt.BrokerIp = input;

        Console.Write($"MQTT Port (default: {config.Mqtt.Port}): ");
        input = Console.ReadLine()?.Trim();
        if (int.TryParse(input, out var port)) config.Mqtt.Port = port;

        Console.Write($"MQTT Username (default: {config.Mqtt.Username}): ");
        input = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(input)) config.Mqtt.Username = input;

        Console.Write($"MQTT Password (default: {new string('*', config.Mqtt.Password?.Length ?? 0)}): ");
        input = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(input)) config.Mqtt.Password = input;

        Console.WriteLine("\n=== Konfigurasi Database ===");
        Console.Write($"SQL Server (default: {config.Database.Server}): ");
        input = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(input)) config.Database.Server = input;

        Console.Write($"Database Name (default: {config.Database.DatabaseName}): ");
        input = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(input)) config.Database.DatabaseName = input;

        Console.Write($"SQL Username (default: {config.Database.Username}): ");
        input = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(input)) config.Database.Username = input;

        Console.Write($"SQL Password (default: {new string('*', config.Database.Password.Length)}): ");
        input = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(input)) config.Database.Password = input;

        Console.WriteLine("\n╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   KONFIGURASI YANG AKAN DIGUNAKAN                        ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine($"- MQTT Broker: {config.Mqtt.BrokerIp}:{config.Mqtt.Port}");
        Console.WriteLine($"- MQTT Username: {config.Mqtt.Username}");
        Console.WriteLine($"- MQTT Password: {new string('*', config.Mqtt.Password?.Length ?? 0)}");
        Console.WriteLine($"- SQL Server: {config.Database.Server}");
        Console.WriteLine($"- Database: {config.Database.DatabaseName}");
        Console.WriteLine($"- SQL User: {config.Database.Username}");

        Console.WriteLine("\nApakah konfigurasi ini sudah benar? (Y/N)");
        var confirm = Console.ReadKey().KeyChar;
        Console.WriteLine();

        if (char.ToUpper(confirm) != 'Y')
        {
            Console.WriteLine("\nDibatalkan.");
            Environment.Exit(0);
        }

        // Simpan konfigurasi
        SaveUserConfig(config);
        Console.WriteLine($"\n[✓] Konfigurasi disimpan ke: {ConfigFilePath}");

        // ============================================================
        // AUTO INSTALL WINDOWS SERVICE
        // ============================================================
        Console.WriteLine("\n[*] Menginstall sebagai Windows Service...");

        bool isAdmin = IsRunningAsAdmin();
        if (!isAdmin)
        {
            Console.WriteLine("[!] PERINGATAN: Program tidak dijalankan sebagai Administrator");
            Console.WriteLine("[!] Service tidak bisa diinstall otomatis.");
            Console.WriteLine();
            Console.WriteLine("[*] Solusi:");
            Console.WriteLine("    1. Tutup program ini");
            Console.WriteLine("    2. Klik kanan KWHMonitoring.exe → 'Run as administrator'");
            Console.WriteLine("    3. Ulangi setup");
            Console.WriteLine();
            Console.WriteLine("[*] Atau install manual (sebagai Administrator):");

            var exePath = GetExePath();
            Console.WriteLine($"    sc create {ServiceName} binPath= \"{exePath} --run-as-service\" start= auto DisplayName= \"{ServiceDisplayName}\"");
            Console.WriteLine($"    sc start {ServiceName}");
            Console.WriteLine();
            Console.WriteLine("Tekan Enter untuk keluar...");
            Console.ReadLine();
            Environment.Exit(0);
        }

        // Admin mode: Auto install service
        var installResult = InstallAndStartService();

        if (installResult)
        {
            Console.WriteLine("\n╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   [✓] SERVICE BERHASIL DIINSTALL DAN DIJALANKAN          ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"[*] Nama Service: {ServiceDisplayName}");
            Console.WriteLine("[*] Cek di: services.msc");
            Console.WriteLine($"[*] Log: {Path.Combine(AppContext.BaseDirectory, "logs")}");
            Console.WriteLine();
            Console.WriteLine("[*] Console akan tertutup dalam 3 detik...");
            Thread.Sleep(3000);
        }
        else
        {
            Console.WriteLine("\n[!] Install service gagal. Console akan ditutup.");
            Console.WriteLine("Tekan Enter untuk keluar...");
            Console.ReadLine();
        }

        // TUTUP CONSOLE
        Environment.Exit(0);
    }

    // ============================================================
    // HELPER METHODS
    // ============================================================

    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string GetExePath()
    {
        return Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
    }

    private static bool InstallAndStartService()
    {
        try
        {
            var exePath = GetExePath();

            Console.WriteLine($"[*] EXE Path: {exePath}");

            // 1. Stop service jika sudah ada
            Console.WriteLine("[*] Stop service lama (jika ada)...");
            RunCommand("sc", $"stop {ServiceName}");
            Thread.Sleep(1000);

            // 2. Delete service jika sudah ada
            Console.WriteLine("[*] Hapus service lama (jika ada)...");
            RunCommand("sc", $"delete {ServiceName}");
            Thread.Sleep(1500);

            // 3. Create service baru
            Console.WriteLine("[*] Install service baru...");
            var binPath = $"\"{exePath}\" --run-as-service";
            var createResult = RunCommand("sc",
                $"create {ServiceName} binPath= \"{binPath}\" start= auto DisplayName= \"{ServiceDisplayName}\"");

            if (!createResult.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[!] Gagal create service: {createResult}");
                return false;
            }
            Console.WriteLine("[✓] Service berhasil dibuat");

            // 4. Set description
            RunCommand("sc",
                $"description {ServiceName} \"Haiwell Electrical - MQTT to SQL Server KWH Monitoring Service\"");

            // 5. Set failure recovery (restart on failure)
            RunCommand("sc", $"failure {ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000");

            // 6. Start service
            Console.WriteLine("[*] Starting service...");
            var startResult = RunCommand("sc", $"start {ServiceName}");

            if (!startResult.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase) &&
                !startResult.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) &&
                !startResult.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[!] Gagal start service: {startResult}");
                return false;
            }

            Console.WriteLine("[✓] Service berhasil dijalankan");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Error: {ex.Message}");
            return false;
        }
    }

    private static string RunCommand(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return "";

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(10000);

            return string.IsNullOrEmpty(output) ? error : output;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static AppConfig LoadExistingConfig()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch { }

        try
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json");
            var cfg = builder.Build();
            var appConfig = new AppConfig();
            cfg.Bind(appConfig);
            return appConfig;
        }
        catch { }

        return new AppConfig();
    }

    private static void SaveUserConfig(AppConfig config)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(ConfigFilePath, json);
    }
}
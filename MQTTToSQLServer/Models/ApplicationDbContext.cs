using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MQTTToSQLServer.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext()
        {
        }

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<KWHData> KWH_Monitoring { get; set; }
        public DbSet<KWHDataHistory> KWHData_History { get; set; }
        public DbSet<DeviceRegistry> DeviceRegistry { get; set; }
        public DbSet<AnomalyLog> AnomalyLogs { get; set; }
        public DbSet<AppLog> AppLogs { get; set; }
        public DbSet<AppSettingsRecord> AppSettingsRecords { get; set; }
        public DbSet<ColumnMapping> ColumnMappings { get; set; }
        public DbSet<ColumnScaleConfig> ColumnScaleConfigs { get; set; }
        public DbSet<DailyEnergy> DailyEnergy { get; set; }
        public DbSet<FailedMessage> FailedMessages { get; set; }
        public DbSet<HourlyEnergy> HourlyEnergy { get; set; }
        public DbSet<MonthlyEnergy> MonthlyEnergy { get; set; }
        public DbSet<YearlyEnergy> YearlyEnergy { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<KWHData>(entity =>
            {
                entity.ToTable("KWHData");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).ValueGeneratedOnAdd();
                entity.Property(x => x.DeviceKey).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
                entity.Property(x => x.Waktu_Device).HasColumnName("TerminalTime").HasColumnType("datetime2");
                entity.Property(x => x.Waktu_Server).HasColumnName("ReceivedTime").HasColumnType("datetime2");
                entity.Property(x => x.GroupName).HasColumnType("nvarchar(100)").HasMaxLength(100);
                entity.Property(x => x.DeviceId).HasColumnType("nvarchar(50)").HasMaxLength(50);
                entity.Property(x => x.Volt_R).HasColumnName("PHASE_R").HasColumnType("decimal(18,2)");
                entity.Property(x => x.Volt_S).HasColumnName("PHASE_S").HasColumnType("decimal(18,2)");
                entity.Property(x => x.Volt_T).HasColumnName("PHASE_T").HasColumnType("decimal(18,2)");
                entity.Property(x => x.Amp_R).HasColumnName("AMPERE_R").HasColumnType("decimal(18,3)");
                entity.Property(x => x.Amp_S).HasColumnName("AMPERE_S").HasColumnType("decimal(18,3)");
                entity.Property(x => x.Amp_T).HasColumnName("AMPERE_T").HasColumnType("decimal(18,3)");
                entity.Property(x => x.Daya_Watt).HasColumnName("W").HasColumnType("decimal(18,1)");
                entity.Property(x => x.Cos_Phi).HasColumnName("CosPhi").HasColumnType("decimal(18,3)");
                entity.Property(x => x.Frekuensi_Hz).HasColumnName("F").HasColumnType("decimal(18,2)");
                entity.Property(x => x.Energi_Aktif_Wh).HasColumnName("Aktif_Power").HasColumnType("decimal(18,2)");
                entity.Property(x => x.Total_Energy_Wh).HasColumnName("TotalW").HasColumnType("decimal(18,2)");
                entity.Property(x => x.TotalW1M_Wh).HasColumnName("TotalW1M").HasColumnType("decimal(18,2)");

                entity.HasOne<DeviceRegistry>()
                    .WithMany()
                    .HasPrincipalKey(d => d.DeviceKey)
                    .HasForeignKey(x => x.DeviceKey)
                    .HasConstraintName("FK_KWHData_DeviceRegistry")
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.DeviceKey).HasName("IX_KWHData_DeviceKey");
                entity.HasIndex(x => x.Waktu_Server).HasName("IX_KWHData_ReceivedTime");
                entity.HasIndex(x => x.Waktu_Device).HasName("IX_KWHData_TerminalTime");
                entity.HasIndex(x => new { x.DeviceKey, x.Waktu_Server }).HasName("IX_KWHData_DeviceKey_TerminalTime");
            });

            modelBuilder.Entity<KWHDataHistory>(entity =>
            {
                entity.ToTable("KWHData_History");
                entity.HasKey(x => x.HistoryId);

                entity.Property(x => x.HistoryId).ValueGeneratedOnAdd();
                entity.Property(x => x.DeviceKey).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
                entity.Property(x => x.TerminalTime).HasColumnType("datetime2");
                entity.Property(x => x.ReceivedTime).HasColumnType("datetime2");
                entity.Property(x => x.GroupName).HasColumnType("nvarchar(100)").HasMaxLength(100);
                entity.Property(x => x.DeviceId).HasColumnType("nvarchar(50)").HasMaxLength(50);
                entity.Property(x => x.PhaseR).HasColumnName("PHASE_R").HasColumnType("decimal(18,2)");
                entity.Property(x => x.PhaseS).HasColumnName("PHASE_S").HasColumnType("decimal(18,2)");
                entity.Property(x => x.PhaseT).HasColumnName("PHASE_T").HasColumnType("decimal(18,2)");
                entity.Property(x => x.AmpereR).HasColumnName("AMPERE_R").HasColumnType("decimal(18,3)");
                entity.Property(x => x.AmpereS).HasColumnName("AMPERE_S").HasColumnType("decimal(18,3)");
                entity.Property(x => x.AmpereT).HasColumnName("AMPERE_T").HasColumnType("decimal(18,3)");
                entity.Property(x => x.W).HasColumnType("decimal(18,1)");
                entity.Property(x => x.CosPhi).HasColumnType("decimal(18,3)");
                entity.Property(x => x.F).HasColumnType("decimal(18,2)");
                entity.Property(x => x.AktifPower).HasColumnName("Aktif_Power").HasColumnType("decimal(18,2)");
                entity.Property(x => x.TotalW).HasColumnType("decimal(18,2)");
                entity.Property(x => x.TotalW1M).HasColumnType("decimal(18,2)");
                entity.Property(x => x.ArchivedAt).HasColumnType("datetime2");

                entity.HasIndex(x => x.DeviceKey).HasName("IX_KWHData_History_DeviceKey");
                entity.HasIndex(x => x.ArchivedAt).HasName("IX_KWHData_History_ArchivedAt");
            });

            modelBuilder.Entity<DeviceRegistry>(entity =>
            {
                entity.ToTable("DeviceRegistry");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).ValueGeneratedOnAdd();
                entity.Property(x => x.DeviceKey).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
                entity.Property(x => x.DeviceId).HasColumnType("varchar(50)").HasMaxLength(50).IsRequired();
                entity.Property(x => x.GroupName).HasColumnType("varchar(100)").HasMaxLength(100);
                entity.Property(x => x.Location).HasColumnType("varchar(200)").HasMaxLength(200);
                entity.Property(x => x.FirstSeen).HasColumnType("datetime2");
                entity.Property(x => x.LastSeen).HasColumnType("datetime2");
                entity.Property(x => x.IsActive);
                entity.Property(x => x.MessageCount);
                entity.Property(x => x.CreatedAt).HasColumnType("datetime2");
                entity.Property(x => x.UpdatedAt).HasColumnType("datetime2");

                entity.HasIndex(x => x.DeviceKey).IsUnique().HasName("IX_DeviceRegistry_DeviceKey");
                entity.HasIndex(x => x.DeviceId).HasName("IX_DeviceRegistry_DeviceId");
            });

            modelBuilder.Entity<AnomalyLog>(entity =>
            {
                entity.ToTable("AnomalyLogs");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).ValueGeneratedOnAdd();
                entity.Property(x => x.DeviceKey).HasColumnType("nvarchar(50)").HasMaxLength(50).IsRequired();
                entity.Property(x => x.DeviceId).HasColumnType("nvarchar(50)").HasMaxLength(50);
                entity.Property(x => x.AnomalyType).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
                entity.Property(x => x.PowerValue).HasColumnType("decimal(18,2)");
                entity.Property(x => x.ThresholdValue).HasColumnType("decimal(18,2)");
                entity.Property(x => x.Deviation).HasColumnType("decimal(5,2)");
                entity.Property(x => x.DetectedTime).HasColumnType("datetime2");
                entity.Property(x => x.EMAValue).HasColumnType("decimal(18,2)");
                entity.Property(x => x.ThresholdMode).HasColumnType("nvarchar(20)").HasMaxLength(20);
                entity.Property(x => x.Acknowledged);
                entity.Property(x => x.AcknowledgedTime).HasColumnType("datetime2");
                entity.Property(x => x.Notes).HasColumnType("nvarchar(500)").HasMaxLength(500);

                entity.HasIndex(x => x.DetectedTime).HasName("IX_AnomalyLogs_DetectedTime");
                entity.HasIndex(x => x.DeviceKey).HasName("IX_AnomalyLogs_DeviceKey");
            });

            modelBuilder.Entity<AppLog>(entity =>
            {
                entity.ToTable("AppLog");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).ValueGeneratedOnAdd();
                entity.Property(x => x.LogLevel).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
                entity.Property(x => x.Message).HasColumnType("nvarchar(max)").IsRequired();
                entity.Property(x => x.Topic).HasColumnType("varchar(200)").HasMaxLength(200);
                entity.Property(x => x.DeviceKey).HasColumnType("varchar(20)").HasMaxLength(20);
                entity.Property(x => x.CreatedAt).HasColumnType("datetime2");

                entity.HasIndex(x => x.CreatedAt).HasName("IX_AppLog_CreatedAt");
                entity.HasIndex(x => x.LogLevel).HasName("IX_AppLog_Level");
            });

            modelBuilder.Entity<AppSettingsRecord>(entity =>
            {
                entity.ToTable("AppSettings");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).ValueGeneratedOnAdd();
                entity.Property(x => x.SettingKey).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
                entity.Property(x => x.SettingValue).HasColumnType("nvarchar(500)").HasMaxLength(500).IsRequired();
                entity.Property(x => x.UpdatedAt).HasColumnType("datetime2");

                entity.HasIndex(x => x.SettingKey).IsUnique().HasName("IX_AppSettings_SettingKey");
            });

            modelBuilder.Entity<ColumnMapping>(entity =>
            {
                entity.ToTable("ColumnMapping");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).ValueGeneratedOnAdd();
                entity.Property(x => x.OldColumnName).HasColumnType("varchar(50)").HasMaxLength(50).IsRequired();
                entity.Property(x => x.NewColumnName).HasColumnType("varchar(50)").HasMaxLength(50).IsRequired();
                entity.Property(x => x.IsActive);
                entity.Property(x => x.CreatedAt).HasColumnType("datetime2");

                entity.HasIndex(x => x.OldColumnName).IsUnique().HasName("IX_ColumnMapping_OldName");
            });

            modelBuilder.Entity<ColumnScaleConfig>(entity =>
            {
                entity.ToTable("ColumnScaleConfig");
                entity.HasKey(x => x.ColumnName);

                entity.Property(x => x.ColumnName).HasColumnType("varchar(50)").HasMaxLength(50);
                entity.Property(x => x.ScaleFactor).HasColumnType("decimal(18,5)");
                entity.Property(x => x.RegisterAddress).HasColumnType("varchar(10)").HasMaxLength(10);
                entity.Property(x => x.DataType).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
                entity.Property(x => x.Unit).HasColumnType("varchar(50)").HasMaxLength(50);
                entity.Property(x => x.Category).HasColumnType("varchar(50)").HasMaxLength(50);
                entity.Property(x => x.Description).HasColumnType("varchar(500)").HasMaxLength(500);
                entity.Property(x => x.IsDynamic);
                entity.Property(x => x.LastUpdated).HasColumnType("datetime2");
            });

            modelBuilder.Entity<DailyEnergy>(entity =>
            {
                entity.ToTable("DailyEnergy");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).ValueGeneratedOnAdd();
                entity.Property(x => x.DeviceKey).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
                entity.Property(x => x.Date).HasColumnType("date");
                entity.Property(x => x.EnergyKWh).HasColumnType("decimal(18,4)");
                entity.Property(x => x.CalculatedAt).HasColumnType("datetime2");
            });

            modelBuilder.Entity<FailedMessage>(entity =>
            {
                entity.ToTable("FailedMessages");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).ValueGeneratedOnAdd();
                entity.Property(x => x.Topic).HasColumnType("varchar(500)").HasMaxLength(500).IsRequired();
                entity.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
                entity.Property(x => x.Reason).HasColumnType("nvarchar(500)").HasMaxLength(500);
                entity.Property(x => x.RetryCount);
                entity.Property(x => x.IsResolved);
                entity.Property(x => x.ReceivedAt).HasColumnType("datetime2");
                entity.Property(x => x.ResolvedAt).HasColumnType("datetime2");

                entity.HasIndex(x => x.IsResolved).HasName("IX_FailedMessages_IsResolved");
                entity.HasIndex(x => x.ReceivedAt).HasName("IX_FailedMessages_ReceivedAt");
            });

            modelBuilder.Entity<HourlyEnergy>(entity =>
            {
                entity.ToTable("HourlyEnergy");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).ValueGeneratedOnAdd();
                entity.Property(x => x.DeviceKey).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
                entity.Property(x => x.Hour).HasColumnType("datetime2");
                entity.Property(x => x.EnergyKWh).HasColumnType("decimal(18,4)");
                entity.Property(x => x.CalculatedAt).HasColumnType("datetime2");
            });

            modelBuilder.Entity<MonthlyEnergy>(entity =>
            {
                entity.ToTable("MonthlyEnergy");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).ValueGeneratedOnAdd();
                entity.Property(x => x.DeviceKey).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
                entity.Property(x => x.Year);
                entity.Property(x => x.Month);
                entity.Property(x => x.EnergyKWh).HasColumnType("decimal(18,4)");
                entity.Property(x => x.CalculatedAt).HasColumnType("datetime2");
            });

            modelBuilder.Entity<YearlyEnergy>(entity =>
            {
                entity.ToTable("YearlyEnergy");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).ValueGeneratedOnAdd();
                entity.Property(x => x.DeviceKey).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
                entity.Property(x => x.Year);
                entity.Property(x => x.EnergyKWh).HasColumnType("decimal(18,4)");
                entity.Property(x => x.CalculatedAt).HasColumnType("datetime2");
            });
        }
    }

    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? "Server=192.168.6.15,1433;Database=HaiwellElectrical;User Id=kwhapp;Password=kwhapp1234;Encrypt=False;TrustServerCertificate=True;";

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null));

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}

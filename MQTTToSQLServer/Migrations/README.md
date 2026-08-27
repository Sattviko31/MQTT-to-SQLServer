# Database Migration Guide - KWHMonitoring

## Overview
Dokumentasi lengkap untuk database migration pada project KWHMonitoring menggunakan Entity Framework Core 2.1 dengan SQL Server.

## Tables Created

### 1. KWHData (Tabel Utama)
Menyimpan data mentah konsumsi energi dari device monitoring.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | bigint | PK, Identity | Primary Key |
| DeviceKey | nvarchar(50) | NOT NULL | Identifier device |
| DeviceId | nvarchar(50) | NOT NULL | ID device human-readable |
| GroupName | nvarchar(100) | NOT NULL | Nama group device |
| TerminalTime | datetime2 | NOT NULL | Timestamp dari device |
| ReceivedTime | datetime2 | NOT NULL | Timestamp diterima server |
| PHASE_R | decimal(18,4) | NOT NULL | Voltage phase R |
| PHASE_S | decimal(18,4) | Nullable | Voltage phase S |
| PHASE_T | decimal(18,4) | Nullable | Voltage phase T |
| AMPERE_R | decimal(18,4) | NOT NULL | Current phase R |
| AMPERE_S | decimal(18,4) | Nullable | Current phase S |
| AMPERE_T | decimal(18,4) | Nullable | Current phase T |
| CosPhi | decimal(18,4) | NOT NULL | Power factor |
| W | decimal(18,4) | NOT NULL | Daya dalam Watt |
| TotalW1M | decimal(18,4) | NOT NULL | Total energy Wh (W1M) |
| Aktif_Power | decimal(18,4) | NOT NULL | Active energy Wh |
| TotalW | decimal(18,4) | NOT NULL | Total energy Wh |
| F | decimal(18,4) | NOT NULL | Frequency Hz |

**Indexes:**
- `PK_KWHData` (Id) - Primary Key
- `IX_KWHData_DeviceKey_TerminalTime` - Composite index untuk query berdasarkan device dan waktu
- `IX_KWHData_ReceivedTime` - Index untuk filter berdasarkan waktu penerimaan

### 2. AnomalyLogs
Menyimpan log anomali/penyimpangan yang terdeteksi.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | bigint | PK, Identity | Primary Key |
| DeviceKey | nvarchar(100) | NOT NULL | Identifier device |
| DeviceId | nvarchar(100) | NOT NULL | ID device |
| AnomalyType | nvarchar(500) | NOT NULL | Tipe anomali |
| PowerValue | decimal(18,4) | NOT NULL | Nilai daya saat anomaly |
| ThresholdValue | decimal(18,4) | NOT NULL | Nilai threshold |
| Deviation | decimal(18,4) | NOT NULL | Besarnya deviasi |
| DetectedTime | datetime2 | NOT NULL | Waktu terdeteksi |
| EMAValue | decimal(18,4) | Nullable | Exponential Moving Average |
| ThresholdMode | nvarchar(50) | NOT NULL | Mode threshold (manual/auto) |
| Acknowledged | bit | NOT NULL, Default: false | Status acknowledgment |
| AcknowledgedTime | datetime2 | Nullable | Waktu acknowledged |
| Notes | nvarchar(500) | NOT NULL | Catatan |

**Indexes:**
- `PK_AnomalyLogs` (Id) - Primary Key
- `IX_AnomalyLogs_DeviceKey_DetectedTime` - Query anomalies per device
- `IX_AnomalyLogs_Acknowledged` - Filter acknowledged status

### 3. AppSettings
Menyimpan konfigurasi aplikasi.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | int | PK, Identity | Primary Key |
| SettingKey | nvarchar(100) | NOT NULL, Unique | Key setting |
| SettingValue | nvarchar(500) | NOT NULL | Value setting |
| UpdatedAt | datetime2 | NOT NULL | Last update timestamp |

**Indexes:**
- `PK_AppSettings` (Id) - Primary Key
- `IX_AppSettings_SettingKey` - Unique index untuk key setting

**Default Settings (seeds):**
- `MaxCapacity` = '100000' - Kapasitas max device (Watt)
- `NotificationEnabled` = 'false' - Status notifikasi
- `CheckIntervalSeconds` = '30' - Interval pengecekan (detik)

### 4. HourlyEnergy
Data agregat energi per jam.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | bigint | PK, Identity | Primary Key |
| DeviceKey | nvarchar(100) | NOT NULL | Identifier device |
| Hour | datetime2 | NOT NULL | Jam agregat |
| EnergyKWh | decimal(18,4) | NOT NULL | Energi kWh |
| CalculatedAt | datetime2 | NOT NULL | Waktu perhitungan |

**Indexes:**
- `PK_HourlyEnergy` (Id) - Primary Key
- `IX_HourlyEnergy_DeviceKey_Hour` - Unique composite index

### 5. DailyEnergy
Data agregat energi per hari.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | bigint | PK, Identity | Primary Key |
| DeviceKey | nvarchar(100) | NOT NULL | Identifier device |
| Date | date | NOT NULL | Tanggal agregat |
| EnergyKWh | decimal(18,4) | NOT NULL | Energi kWh |
| CalculatedAt | datetime2 | NOT NULL | Waktu perhitungan |

**Indexes:**
- `PK_DailyEnergy` (Id) - Primary Key
- `IX_DailyEnergy_DeviceKey_Date` - Unique composite index

### 6. MonthlyEnergy
Data agregat energi per bulan.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | bigint | PK, Identity | Primary Key |
| DeviceKey | nvarchar(100) | NOT NULL | Identifier device |
| Year | int | NOT NULL | Tahun |
| Month | int | NOT NULL | Bulan |
| EnergyKWh | decimal(18,4) | NOT NULL | Energi kWh |
| CalculatedAt | datetime2 | NOT NULL | Waktu perhitungan |

**Indexes:**
- `PK_MonthlyEnergy` (Id) - Primary Key
- `IX_MonthlyEnergy_DeviceKey_Year_Month` - Unique composite index

### 7. YearlyEnergy
Data agregat energi per tahun.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | bigint | PK, Identity | Primary Key |
| DeviceKey | nvarchar(100) | NOT NULL | Identifier device |
| Year | int | NOT NULL | Tahun |
| EnergyKWh | decimal(18,4) | NOT NULL | Energi kWh |
| CalculatedAt | datetime2 | NOT NULL | Waktu perhitungan |

**Indexes:**
- `PK_YearlyEnergy` (Id) - Primary Key
- `IX_YearlyEnergy_DeviceKey_Year` - Unique composite index

## Setup Instructions

### Prerequisites
- .NET Core SDK 2.1 atau lebih tinggi
- SQL Server (local atau remote)
- Connection string yang sesuai di appsettings.json

### Menggunakan Entity Framework Migrations

#### 1. Pastikan connection string sudah dikonfigurasi
File: `KWHMonitoring/appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=KWHMonitoring;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
  }
}
```

#### 2. Install NuGet packages (jika belum)
```bash
dotnet restore
dotnet tool install --global dotnet-ef --version 2.1.1
```

#### 3. Generate migration pertama kali
```bash
cd "KWHMonitoring"
dotnet ef migrations add MigrationInitialCreate
```

Jika sudah ada file migration, hapus folder `Migrations` dan buat ulang.

#### 4. Apply migration ke database
```bash
dotnet ef database update
```

Ini akan:
- Membuat database jika belum ada
- Membuat semua tabel dengan struktur lengkap
- Membuat indexes untuk performa
- Seed default data di AppSettings

### Deploy ke Device Baru

Untuk deployment ke device baru:

1. **Pastikan project sudah ter-copy** ke device
2. **Konfigurasi connection string** sesuai SQL Server di device tersebut
3. **Run migration**:
   ```bash
   cd "KWHMonitoring"
   dotnet ef database update
   ```
4. **Start application**:
   ```bash
   dotnet run
   ```

### Manual SQL Script (Alternative Method)

Jika tidak menggunakan EF Core migrations, bisa menggunakan script SQL berikut (lihat file `scripts/database_schema.sql`).

## Troubleshooting

### Error: Database already exists
Migration akan otomatis attach ke database yang sudah ada. Tidak perlu create database manual.

### Error: Connection refused
Pastikan:
- SQL Server sudah running
- TCP/IP enabled di SQL Server Configuration Manager
- Firewall mengizinkan koneksi port 1433
- Connection string benar (server, database, user, password)

### Error: Table already exists
Jika migration gagal karena tabel sudah ada:
1. Cek database apakah tabel sudah ada
2. Jika iya, skip migration dengan: `dotnet ef database update` berulang sampai selesai
3. Atau drop database dan recreate: `dotnet ef database drop && dotnet ef database update`

## Backup & Restore

### Backup Database
```sql
BACKUP DATABASE [KWHMonitoring] TO DISK = 'C:\Backups\KWHMonitoring_Backup.bak'
```

### Restore Database
```sql
RESTORE DATABASE [KWHMonitoring] FROM DISK = 'C:\Backups\KWHMonitoring_Backup.bak'
```

## Performance Notes

- Semua tabel aggregated (Hourly/Daily/Monthly/Yearly) memiliki unique constraint pada combination key untuk mencegah duplikasi
- Indexes pada DeviceKey + Time field optimal untuk query time-series
- Consider partitioning KWHData table if volume exceeds millions of records

## Version History

- **v1.0** (Initial) - Complete database schema with all tables and indexes

using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MQTTToSQLServer.Migrations
{
    public partial class initialMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnomalyLogs",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    DeviceKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DeviceId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AnomalyType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PowerValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ThresholdValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Deviation = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DetectedTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EMAValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ThresholdMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Acknowledged = table.Column<bool>(nullable: true),
                    AcknowledgedTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnomalyLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppLog",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    LogLevel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Topic = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    DeviceKey = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    SettingKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SettingValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ColumnMapping",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    OldColumnName = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    NewColumnName = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColumnMapping", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ColumnScaleConfig",
                columns: table => new
                {
                    ColumnName = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    ScaleFactor = table.Column<decimal>(type: "decimal(18,5)", nullable: false),
                    RegisterAddress = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    DataType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Unit = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    Category = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsDynamic = table.Column<bool>(nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColumnScaleConfig", x => x.ColumnName);
                });

            migrationBuilder.CreateTable(
                name: "DailyEnergy",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    DeviceKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    EnergyKWh = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyEnergy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceRegistry",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    DeviceKey = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    DeviceId = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    GroupName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Location = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    FirstSeen = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    MessageCount = table.Column<long>(nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceRegistry", x => x.Id);
                    table.UniqueConstraint("AK_DeviceRegistry_DeviceKey", x => x.DeviceKey);
                });

            migrationBuilder.CreateTable(
                name: "FailedMessages",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Topic = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RetryCount = table.Column<int>(nullable: false),
                    IsResolved = table.Column<bool>(nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailedMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HourlyEnergy",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    DeviceKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Hour = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EnergyKWh = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HourlyEnergy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KWHData_History",
                columns: table => new
                {
                    HistoryId = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    OriginalId = table.Column<long>(nullable: false),
                    DeviceKey = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    TerminalTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReceivedTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PHASE_R = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PHASE_S = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PHASE_T = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AMPERE_R = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    AMPERE_S = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    AMPERE_T = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    W = table.Column<decimal>(type: "decimal(18,1)", nullable: true),
                    CosPhi = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    F = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Aktif_Power = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TotalW = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TotalW1M = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KWHData_History", x => x.HistoryId);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyEnergy",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    DeviceKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Year = table.Column<int>(nullable: false),
                    Month = table.Column<int>(nullable: false),
                    EnergyKWh = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyEnergy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YearlyEnergy",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    DeviceKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Year = table.Column<int>(nullable: false),
                    EnergyKWh = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearlyEnergy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KWHData",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    DeviceKey = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    TerminalTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReceivedTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PHASE_R = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PHASE_S = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PHASE_T = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AMPERE_R = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    AMPERE_S = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    AMPERE_T = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    W = table.Column<decimal>(type: "decimal(18,1)", nullable: true),
                    CosPhi = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    F = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Aktif_Power = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TotalW = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TotalW1M = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KWHData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KWHData_DeviceRegistry",
                        column: x => x.DeviceKey,
                        principalTable: "DeviceRegistry",
                        principalColumn: "DeviceKey",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyLogs_DetectedTime",
                table: "AnomalyLogs",
                column: "DetectedTime");

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyLogs_DeviceKey",
                table: "AnomalyLogs",
                column: "DeviceKey");

            migrationBuilder.CreateIndex(
                name: "IX_AppLog_CreatedAt",
                table: "AppLog",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AppLog_Level",
                table: "AppLog",
                column: "LogLevel");

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_SettingKey",
                table: "AppSettings",
                column: "SettingKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColumnMapping_OldName",
                table: "ColumnMapping",
                column: "OldColumnName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRegistry_DeviceId",
                table: "DeviceRegistry",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRegistry_DeviceKey",
                table: "DeviceRegistry",
                column: "DeviceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FailedMessages_IsResolved",
                table: "FailedMessages",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_FailedMessages_ReceivedAt",
                table: "FailedMessages",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_KWHData_DeviceKey",
                table: "KWHData",
                column: "DeviceKey");

            migrationBuilder.CreateIndex(
                name: "IX_KWHData_TerminalTime",
                table: "KWHData",
                column: "TerminalTime");

            migrationBuilder.CreateIndex(
                name: "IX_KWHData_ReceivedTime",
                table: "KWHData",
                column: "ReceivedTime");

            migrationBuilder.CreateIndex(
                name: "IX_KWHData_DeviceKey_TerminalTime",
                table: "KWHData",
                columns: new[] { "DeviceKey", "ReceivedTime" });

            migrationBuilder.CreateIndex(
                name: "IX_KWHData_History_ArchivedAt",
                table: "KWHData_History",
                column: "ArchivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_KWHData_History_DeviceKey",
                table: "KWHData_History",
                column: "DeviceKey");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnomalyLogs");

            migrationBuilder.DropTable(
                name: "AppLog");

            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "ColumnMapping");

            migrationBuilder.DropTable(
                name: "ColumnScaleConfig");

            migrationBuilder.DropTable(
                name: "DailyEnergy");

            migrationBuilder.DropTable(
                name: "FailedMessages");

            migrationBuilder.DropTable(
                name: "HourlyEnergy");

            migrationBuilder.DropTable(
                name: "KWHData");

            migrationBuilder.DropTable(
                name: "KWHData_History");

            migrationBuilder.DropTable(
                name: "MonthlyEnergy");

            migrationBuilder.DropTable(
                name: "YearlyEnergy");

            migrationBuilder.DropTable(
                name: "DeviceRegistry");
        }
    }
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MQTTToSQLServer.Models
{
    [Table("KWHData")]
    public class KWHData
    {
        [Key]
        [Column("Id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [Column("DeviceKey", TypeName = "varchar(20)")]
        [MaxLength(20)]
        public string DeviceKey { get; set; } = string.Empty;

        [Column("TerminalTime", TypeName = "datetime2")]
        public DateTime? Waktu_Device { get; set; }

        [Column("ReceivedTime", TypeName = "datetime2")]
        public DateTime Waktu_Server { get; set; }

        [Column("GroupName", TypeName = "nvarchar(100)")]
        [MaxLength(100)]
        public string GroupName { get; set; }

        [Column("DeviceId", TypeName = "nvarchar(50)")]
        [MaxLength(50)]
        public string DeviceId { get; set; }

        [Column("PHASE_R", TypeName = "decimal(18,2)")]
        public decimal? Volt_R { get; set; }

        [Column("PHASE_S", TypeName = "decimal(18,2)")]
        public decimal? Volt_S { get; set; }

        [Column("PHASE_T", TypeName = "decimal(18,2)")]
        public decimal? Volt_T { get; set; }

        [Column("AMPERE_R", TypeName = "decimal(18,3)")]
        public decimal? Amp_R { get; set; }

        [Column("AMPERE_S", TypeName = "decimal(18,3)")]
        public decimal? Amp_S { get; set; }

        [Column("AMPERE_T", TypeName = "decimal(18,3)")]
        public decimal? Amp_T { get; set; }

        [Column("W", TypeName = "decimal(18,1)")]
        public decimal? Daya_Watt { get; set; }

        [Column("CosPhi", TypeName = "decimal(18,3)")]
        public decimal? Cos_Phi { get; set; }

        [Column("F", TypeName = "decimal(18,2)")]
        public decimal? Frekuensi_Hz { get; set; }

        [Column("Aktif_Power", TypeName = "decimal(18,2)")]
        public decimal? Energi_Aktif_Wh { get; set; }

        [Column("TotalW", TypeName = "decimal(18,2)")]
        public decimal? Total_Energy_Wh { get; set; }

        [Column("TotalW1M", TypeName = "decimal(18,2)")]
        public decimal? TotalW1M_Wh { get; set; }

        [NotMapped]
        public string DeviceCategory { get; set; } = "Billboard";

        [NotMapped]
        // 1-phase devices report 0.0 (not null) for S/T, so require > 0
        public bool IsThreePhase => Volt_S.HasValue && Volt_S.Value > 0
                                    && Volt_T.HasValue && Volt_T.Value > 0;

        [NotMapped]
        public decimal AvgVoltage => IsThreePhase
            ? ((Volt_R ?? 0) + (Volt_S ?? 0) + (Volt_T ?? 0)) / 3
            : Volt_R ?? 0;

        [NotMapped]
        public decimal AvgAmpere => IsThreePhase
            ? ((Amp_R ?? 0) + (Amp_S ?? 0) + (Amp_T ?? 0)) / 3
            : Amp_R ?? 0;

        [NotMapped]
        public string Status => GetStatus();

        [NotMapped]
        public string StatusColor => GetStatusColor();

        [NotMapped]
        public string PhaseRColor => GetPhaseColor(Volt_R, Amp_R);

        [NotMapped]
        public string PhaseSColor => IsThreePhase ? GetPhaseColor(Volt_S, Amp_S) : "secondary";

        [NotMapped]
        public string PhaseTColor => IsThreePhase ? GetPhaseColor(Volt_T, Amp_T) : "secondary";

        private string GetStatus()
        {
            const decimal maxCapacity = 30000m;
            var loadPercent = Math.Min(((Daya_Watt ?? 0) / maxCapacity) * 100, 100);
            if (loadPercent > 70) return "HIGH";
            if (loadPercent > 30) return "MEDIUM";
            return "NORMAL";
        }

        private string GetStatusColor()
        {
            const decimal maxCapacity = 30000m;
            var loadPercent = Math.Min(((Daya_Watt ?? 0) / maxCapacity) * 100, 100);
            if (loadPercent > 70) return "danger";
            if (loadPercent > 30) return "warning";
            return "success";
        }

        private string GetPhaseColor(decimal? volt, decimal? amp)
        {
            var v = volt ?? 0;
            var a = amp ?? 0;
            if (v < 200 || a > 80) return "danger";
            if (v < 220 || a > 70) return "warning";
            return "success";
        }
    }
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MQTTToSQLServer.Models
{
    [Table("KWHData_History")]
    public class KWHDataHistory
    {
        [Key]
        [Column("HistoryId")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long HistoryId { get; set; }

        [Column("OriginalId")]
        public long OriginalId { get; set; }

        [Required]
        [Column("DeviceKey", TypeName = "varchar(20)")]
        [MaxLength(20)]
        public string DeviceKey { get; set; } = string.Empty;

        [Column("TerminalTime", TypeName = "datetime2")]
        public DateTime? TerminalTime { get; set; }

        [Column("ReceivedTime", TypeName = "datetime2")]
        public DateTime ReceivedTime { get; set; }

        [Column("GroupName", TypeName = "nvarchar(100)")]
        [MaxLength(100)]
        public string GroupName { get; set; }

        [Column("DeviceId", TypeName = "nvarchar(50)")]
        [MaxLength(50)]
        public string DeviceId { get; set; }

        [Column("PHASE_R", TypeName = "decimal(18,2)")]
        public decimal? PhaseR { get; set; }

        [Column("PHASE_S", TypeName = "decimal(18,2)")]
        public decimal? PhaseS { get; set; }

        [Column("PHASE_T", TypeName = "decimal(18,2)")]
        public decimal? PhaseT { get; set; }

        [Column("AMPERE_R", TypeName = "decimal(18,3)")]
        public decimal? AmpereR { get; set; }

        [Column("AMPERE_S", TypeName = "decimal(18,3)")]
        public decimal? AmpereS { get; set; }

        [Column("AMPERE_T", TypeName = "decimal(18,3)")]
        public decimal? AmpereT { get; set; }

        [Column("W", TypeName = "decimal(18,1)")]
        public decimal? W { get; set; }

        [Column("CosPhi", TypeName = "decimal(18,3)")]
        public decimal? CosPhi { get; set; }

        [Column("F", TypeName = "decimal(18,2)")]
        public decimal? F { get; set; }

        [Column("Aktif_Power", TypeName = "decimal(18,2)")]
        public decimal? AktifPower { get; set; }

        [Column("TotalW", TypeName = "decimal(18,2)")]
        public decimal? TotalW { get; set; }

        [Column("TotalW1M", TypeName = "decimal(18,2)")]
        public decimal? TotalW1M { get; set; }

        [Column("ArchivedAt", TypeName = "datetime2")]
        public DateTime ArchivedAt { get; set; }
    }
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MQTTToSQLServer.Models
{
    [Table("AnomalyLogs")]
    public class AnomalyLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [Column("DeviceKey", TypeName = "nvarchar(50)")]
        [MaxLength(50)]
        public string DeviceKey { get; set; } = string.Empty;

        [Column("DeviceId", TypeName = "nvarchar(50)")]
        [MaxLength(50)]
        public string DeviceId { get; set; }

        [Required]
        [Column("AnomalyType", TypeName = "nvarchar(20)")]
        [MaxLength(20)]
        public string AnomalyType { get; set; } = string.Empty;

        [Column("PowerValue", TypeName = "decimal(18,2)")]
        public decimal PowerValue { get; set; }

        [Column("ThresholdValue", TypeName = "decimal(18,2)")]
        public decimal ThresholdValue { get; set; }

        [Column("Deviation", TypeName = "decimal(5,2)")]
        public decimal Deviation { get; set; }

        [Column("DetectedTime", TypeName = "datetime2")]
        public DateTime DetectedTime { get; set; }

        [Column("EMAValue", TypeName = "decimal(18,2)")]
        public decimal? EMAValue { get; set; }

        [Column("ThresholdMode", TypeName = "nvarchar(20)")]
        [MaxLength(20)]
        public string ThresholdMode { get; set; } = "manual";

        [Column("Acknowledged")]
        public bool? Acknowledged { get; set; } = false;

        [Column("AcknowledgedTime", TypeName = "datetime2")]
        public DateTime? AcknowledgedTime { get; set; }

        [Column("Notes", TypeName = "nvarchar(500)")]
        [MaxLength(500)]
        public string Notes { get; set; }
    }
}

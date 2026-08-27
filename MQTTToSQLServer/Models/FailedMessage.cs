using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MQTTToSQLServer.Models
{
    [Table("FailedMessages")]
    public class FailedMessage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [Column("Topic", TypeName = "varchar(500)")]
        [MaxLength(500)]
        public string Topic { get; set; } = string.Empty;

        [Required]
        [Column("Payload", TypeName = "nvarchar(max)")]
        public string Payload { get; set; } = string.Empty;

        [Column("Reason", TypeName = "nvarchar(500)")]
        [MaxLength(500)]
        public string Reason { get; set; }

        [Column("RetryCount")]
        public int RetryCount { get; set; } = 0;

        [Column("IsResolved")]
        public bool IsResolved { get; set; } = false;

        [Column("ReceivedAt", TypeName = "datetime2")]
        public DateTime ReceivedAt { get; set; }

        [Column("ResolvedAt", TypeName = "datetime2")]
        public DateTime? ResolvedAt { get; set; }
    }
}

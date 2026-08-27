using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MQTTToSQLServer.Models
{
    [Table("DeviceRegistry")]
    public class DeviceRegistry
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Column("DeviceKey", TypeName = "varchar(20)")]
        [MaxLength(20)]
        public string DeviceKey { get; set; } = string.Empty;

        [Required]
        [Column("DeviceId", TypeName = "varchar(50)")]
        [MaxLength(50)]
        public string DeviceId { get; set; } = string.Empty;

        [Column("GroupName", TypeName = "varchar(100)")]
        [MaxLength(100)]
        public string GroupName { get; set; }

        [Column("Location", TypeName = "varchar(200)")]
        [MaxLength(200)]
        public string Location { get; set; }

        [Column("FirstSeen", TypeName = "datetime2")]
        public DateTime FirstSeen { get; set; }

        [Column("LastSeen", TypeName = "datetime2")]
        public DateTime LastSeen { get; set; }

        [Column("IsActive")]
        public bool IsActive { get; set; } = true;

        [Column("MessageCount")]
        public long MessageCount { get; set; } = 0;

        [Column("CreatedAt", TypeName = "datetime2")]
        public DateTime CreatedAt { get; set; }

        [Column("UpdatedAt", TypeName = "datetime2")]
        public DateTime UpdatedAt { get; set; }
    }
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MQTTToSQLServer.Models
{
    [Table("AppLog")]
    public class AppLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [Column("LogLevel", TypeName = "varchar(20)")]
        [MaxLength(20)]
        public string LogLevel { get; set; } = string.Empty;

        [Required]
        [Column("Message", TypeName = "nvarchar(max)")]
        public string Message { get; set; } = string.Empty;

        [Column("Topic", TypeName = "varchar(200)")]
        [MaxLength(200)]
        public string Topic { get; set; }

        [Column("DeviceKey", TypeName = "varchar(20)")]
        [MaxLength(20)]
        public string DeviceKey { get; set; }

        [Column("CreatedAt", TypeName = "datetime2")]
        public DateTime CreatedAt { get; set; }
    }
}

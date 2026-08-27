using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MQTTToSQLServer.Models
{
    [Table("AppSettings")]
    public class AppSettingsRecord
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Column("SettingKey", TypeName = "nvarchar(100)")]
        [MaxLength(100)]
        public string SettingKey { get; set; } = string.Empty;

        [Required]
        [Column("SettingValue", TypeName = "nvarchar(500)")]
        [MaxLength(500)]
        public string SettingValue { get; set; } = string.Empty;

        [Column("UpdatedAt", TypeName = "datetime2")]
        public DateTime? UpdatedAt { get; set; }
    }
}

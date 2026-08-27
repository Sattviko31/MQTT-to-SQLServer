using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MQTTToSQLServer.Models
{
    [Table("HourlyEnergy")]
    public class HourlyEnergy
    {
        [Key]
        public long Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string DeviceKey { get; set; } = string.Empty;

        public DateTime Hour { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal EnergyKWh { get; set; }

        public DateTime CalculatedAt { get; set; } = DateTime.Now;
    }
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MQTTToSQLServer.Models
{
    [Table("YearlyEnergy")]
    public class YearlyEnergy
    {
        [Key]
        public long Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string DeviceKey { get; set; } = string.Empty;

        public int Year { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal EnergyKWh { get; set; }

        public DateTime CalculatedAt { get; set; } = DateTime.Now;
    }
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MQTTToSQLServer.Models
{
    [Table("ColumnScaleConfig")]
    public class ColumnScaleConfig
    {
        [Key]
        [Column("ColumnName", TypeName = "varchar(50)")]
        [MaxLength(50)]
        public string ColumnName { get; set; } = string.Empty;

        [Column("ScaleFactor", TypeName = "decimal(18,5)")]
        public decimal ScaleFactor { get; set; }

        [Column("RegisterAddress", TypeName = "varchar(10)")]
        [MaxLength(10)]
        public string RegisterAddress { get; set; }

        [Required]
        [Column("DataType", TypeName = "varchar(20)")]
        [MaxLength(20)]
        public string DataType { get; set; } = "DECIMAL(18,3)";

        [Column("Unit", TypeName = "varchar(50)")]
        [MaxLength(50)]
        public string Unit { get; set; }

        [Column("Category", TypeName = "varchar(50)")]
        [MaxLength(50)]
        public string Category { get; set; }

        [Column("Description", TypeName = "varchar(500)")]
        [MaxLength(500)]
        public string Description { get; set; }

        [Column("IsDynamic")]
        public bool IsDynamic { get; set; } = false;

        [Column("LastUpdated", TypeName = "datetime2")]
        public DateTime LastUpdated { get; set; }
    }
}

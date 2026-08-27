using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MQTTToSQLServer.Models
{
    [Table("ColumnMapping")]
    public class ColumnMapping
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Column("OldColumnName", TypeName = "varchar(50)")]
        [MaxLength(50)]
        public string OldColumnName { get; set; } = string.Empty;

        [Required]
        [Column("NewColumnName", TypeName = "varchar(50)")]
        [MaxLength(50)]
        public string NewColumnName { get; set; } = string.Empty;

        [Column("IsActive")]
        public bool IsActive { get; set; } = true;

        [Column("CreatedAt", TypeName = "datetime2")]
        public DateTime CreatedAt { get; set; }
    }
}

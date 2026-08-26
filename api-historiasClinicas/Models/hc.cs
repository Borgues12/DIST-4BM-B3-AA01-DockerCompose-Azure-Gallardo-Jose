using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace api_historiasClinicas.Models
{
    [Table("tbl_historiales_clinicos")]
    public class HistorialClinico
    {
        [Key]
        [Column("his_id")]
        public int IdHistorialClinico { get; set; }

        [Required]
        [Column("pac_id")]
        public int IdPaciente { get; set; }

        [Required]
        [Column("his_num_historia")]
        [StringLength(50)]
        public string NumHistoria { get; set; } = string.Empty;

        [Required]
        [Column("his_diagnostico")]
        [StringLength(500)]
        public string Diagnostico { get; set; } = string.Empty;

        [Column("his_tratamiento")]
        [StringLength(500)]
        public string? Tratamiento { get; set; }

        [Column("his_fecha")]
        public DateTime Fecha { get; set; } = DateTime.Now;
    }
}
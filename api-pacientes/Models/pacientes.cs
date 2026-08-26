using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace api_pacientes.Models
{
    [Table("tbl_pacientes")]
    public class Paciente
    {
        [Key]
        [Column("pac_id")]
        public int IdPaciente { get; set; }

        [Required]
        [Column("pac_cedula")]
        [StringLength(20)]
        public string Cedula { get; set; } = string.Empty;

        [Required]
        [Column("pac_nombre")]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [Column("pac_apellido")]
        [StringLength(100)]
        public string Apellido { get; set; } = string.Empty;

        [Column("pac_direccion")]
        [StringLength(250)]
        public string? Direccion { get; set; }
    }
}
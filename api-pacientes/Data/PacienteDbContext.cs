using api_pacientes.Models;
using Microsoft.EntityFrameworkCore;

namespace api_pacientes.Data
{
    public class PacienteDbContext : DbContext
    {
        // Constructor para recibir las opciones de configuración (cadena de conexión)
        public PacienteDbContext(DbContextOptions<PacienteDbContext> options)
            : base(options)
        {
        }

        // Mapeo del modelo Paciente con la base de datos
        public DbSet<Paciente> Pacientes { get; set; } = null!;
    }
}
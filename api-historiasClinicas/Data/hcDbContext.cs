using api_historiasClinicas.Models;
using Microsoft.EntityFrameworkCore;

namespace api_historiasClinicas.Data
{
    public class hcDbContext : DbContext
    {
        public hcDbContext(DbContextOptions<hcDbContext> options)
            : base(options)
        {
        }

        // Mapeo del modelo HistorialClinico con la base de datos
        public DbSet<HistorialClinico> Historiales { get; set; } = null!;
    }
}
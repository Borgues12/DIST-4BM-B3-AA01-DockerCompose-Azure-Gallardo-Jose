using api_historiasClinicas.Data;
using api_historiasClinicas.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api_historiasClinicas.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class historialesController : ControllerBase
    {
        private readonly hcDbContext _context;

        public historialesController(hcDbContext context)
        {
            _context = context;
        }

        [Authorize]
        [HttpGet("")]
        public async Task<ActionResult<IEnumerable<HistorialClinico>>> GetHistoriales()
        {
            return await _context.Historiales.AsNoTracking().ToListAsync();
        }

        [Authorize]
        [HttpGet("paciente/{idPaciente}")]
        public async Task<ActionResult<IEnumerable<HistorialClinico>>> GetPorPaciente(int idPaciente)
        {
            return await _context.Historiales
                .AsNoTracking()
                .Where(h => h.IdPaciente == idPaciente)
                .ToListAsync();
        }

        [Authorize(Roles = "Administrador")]
        [HttpPost("crear")]
        public async Task<ActionResult<HistorialClinico>> CrearHistorial(HistorialClinico historial)
        {
            _context.Historiales.Add(historial);
            await _context.SaveChangesAsync();
            return Ok(historial);
        }

        [Authorize(Roles = "Administrador")]
        [HttpPut("actualizar/{id}")]
        public async Task<IActionResult> ActualizarHistorial(int id, HistorialClinico historial)
        {
            if (id != historial.IdHistorialClinico) return BadRequest();

            _context.Entry(historial).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [Authorize(Roles = "Administrador")]
        [HttpDelete("{idEliminar}")]
        public async Task<IActionResult> EliminarHistorial(int idEliminar)
        {
            var historial = await _context.Historiales.FindAsync(idEliminar);
            if (historial == null) return NotFound();

            _context.Historiales.Remove(historial);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
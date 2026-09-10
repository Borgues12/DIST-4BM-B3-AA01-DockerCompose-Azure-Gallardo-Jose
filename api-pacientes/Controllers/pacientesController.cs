using api_pacientes.Data;
using api_pacientes.Models;
using api_pacientes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api_pacientes.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class pacientesController : ControllerBase
    {
        private readonly PacienteDbContext _pacienteDbContext;
        private readonly RabbitMQPublisher _publisher;

        public pacientesController(PacienteDbContext pacienteDbContext,
            RabbitMQPublisher publisher)
        {
            _pacienteDbContext = pacienteDbContext;
            _publisher = publisher;

        }


        // METODO: para listar los pacientes
        [Authorize]
        [HttpGet("")]
        public async Task<ActionResult<IEnumerable<Paciente>>> GetPacientes()
        {
            var pacientes = await _pacienteDbContext.Pacientes
                .AsNoTracking()
                .ToListAsync();

            return Ok(pacientes);
        }

        // METODO: para buscar un paciente por ID
        [Authorize]
        [HttpGet("{idBuscado}")]
        public async Task<ActionResult<Paciente>> GetPaciente(int idBuscado)
        {
            var paciente = await _pacienteDbContext.Pacientes
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.IdPaciente == idBuscado);

            if (paciente == null) return NotFound();

            return Ok(paciente);
        }

        // METODO: para crear un nuevo paciente
        [Authorize(Roles = "Administrador")]
        [HttpPost("crear")]
        public async Task<ActionResult<Paciente>> CreaPaciente(Paciente paciente)
        {
            _pacienteDbContext.Pacientes.Add(paciente);
            await _pacienteDbContext.SaveChangesAsync();
            await _publisher.PublicarPacienteCreadoAsync(paciente);
            return CreatedAtAction(nameof(GetPaciente),
                new { idBuscado = paciente.IdPaciente }, paciente);
        }

        // METODO: actualizar un paciente
        [Authorize(Roles = "Administrador")]
        [HttpPut("actualizar/{id}")]
        public async Task<IActionResult> ActualizarPaciente(int id, Paciente paciente)
        {
            if (id != paciente.IdPaciente) return BadRequest();

            _pacienteDbContext.Entry(paciente).State = EntityState.Modified;
            await _pacienteDbContext.SaveChangesAsync();
            await _publisher.PublicarPacienteActualizadoAsync(paciente);

            return NoContent();
        }

        // METODO: eliminar un paciente
        [Authorize(Roles = "Administrador")]
        [HttpDelete("{idEliminar}")]
        public async Task<IActionResult> EliminarPaciente(int idEliminar)
        {
            var paciente = await _pacienteDbContext.Pacientes.FindAsync(idEliminar);
            if (paciente == null) return NotFound();

            _pacienteDbContext.Pacientes.Remove(paciente);
            await _pacienteDbContext.SaveChangesAsync();

            return NoContent();
        }
    }
}
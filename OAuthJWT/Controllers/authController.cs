using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace OAuthJWT.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public AuthController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // Usuarios hardcodeados - ver README para justificación de esta decisión
        private static readonly Dictionary<string, (string Password, string Rol)> _usuarios = new()
        {
            { "admin",   ("Eduardo.123", "Administrador") },
            { "usuario", ("Jose.123", "Usuario") }
        };

        [HttpPost("login")]
        public IActionResult Login(LoginRequest loginRequest)
        {
            if (!_usuarios.TryGetValue(loginRequest.Usuario, out var datos)
                || datos.Password != loginRequest.Password)
            {
                return Unauthorized("Credenciales inválidas");
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, loginRequest.Usuario),
                new Claim(ClaimTypes.Role, datos.Rol)
            };

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
            var credenciales = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(
                    Convert.ToDouble(_configuration["Jwt:ExpireMinutes"])),
                signingCredentials: credenciales);

            return Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token),
                usuario = loginRequest.Usuario,
                rol = datos.Rol
            });
        }

        public class LoginRequest
        {
            public string Usuario { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }
    }
}
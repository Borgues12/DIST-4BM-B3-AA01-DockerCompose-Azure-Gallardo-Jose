namespace api_historiasClinicas.Events
{
    public class PacienteCreadoEvento
    {
        public int IdPaciente { get; set; }
        public string Cedula { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Apellido { get; set; } = string.Empty;
    }
}

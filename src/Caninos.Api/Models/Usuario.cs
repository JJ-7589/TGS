namespace Caninos.Api.Models;

public class Usuario
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Documento { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public DateTime FechaRegistro { get; set; }

    public ICollection<Mascota> Mascotas { get; set; } = new List<Mascota>();
    public ICollection<Consulta> Consultas { get; set; } = new List<Consulta>();
}

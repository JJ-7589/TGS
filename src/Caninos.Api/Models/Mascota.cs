namespace Caninos.Api.Models;

public class Mascota
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Raza { get; set; } = string.Empty;
    public int Edad { get; set; }
    public decimal PesoKg { get; set; }
    public string Sexo { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool Esterilizado { get; set; }
    public string? Observaciones { get; set; }
    public int UserId { get; set; }
    public DateTime FechaRegistro { get; set; }

    public Usuario Usuario { get; set; } = null!;
    public ICollection<Consulta> Consultas { get; set; } = new List<Consulta>();
}

namespace Caninos.Api.Models;

public class Consulta
{
    public int Id { get; set; }
    public int MascotaId { get; set; }
    public int UserId { get; set; }
    public string TipoServicio { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public DateTime FechaConsulta { get; set; }
    public decimal Costo { get; set; }
    public string Estado { get; set; } = "Programada";
    public string? Observaciones { get; set; }
    public DateTime FechaCreacion { get; set; }

    public Mascota Mascota { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;
}

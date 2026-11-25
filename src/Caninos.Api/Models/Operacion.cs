namespace Caninos.Api.Models;

public class Operacion
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string TipoServicio { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public decimal Costo { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; }

    public ICollection<Consulta> Consultas { get; set; } = new List<Consulta>();
}

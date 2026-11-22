namespace Caninos.Api.Dtos.Consultas;

public sealed class CreateConsultaRequest
{
    public int? UserId { get; set; }
    public int? MascotaId { get; set; }
    public string? TipoServicio { get; set; }
    public string? Descripcion { get; set; }
    public DateTime? FechaConsulta { get; set; }
    public decimal? Costo { get; set; }
    public string? Estado { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class UpdateConsultaRequest
{
    public int? UserId { get; set; }
    public int? MascotaId { get; set; }
    public string? TipoServicio { get; set; }
    public string? Descripcion { get; set; }
    public DateTime? FechaConsulta { get; set; }
    public decimal? Costo { get; set; }
    public string? Estado { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class ConsultaResponse
{
    public int Id { get; set; }
    public int MascotaId { get; set; }
    public int UserId { get; set; }
    public string TipoServicio { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public DateTime FechaConsulta { get; set; }
    public decimal Costo { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
    public string MascotaNombre { get; set; } = string.Empty;
    public string MascotaRaza { get; set; } = string.Empty;
    public string UsuarioNombre { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
}

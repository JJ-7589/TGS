using Caninos.Api.Dtos.Consultas;

namespace Caninos.Api.Dtos.Mascotas;

public sealed class CreateMascotaRequest
{
    public string? Nombre { get; set; }
    public string? Raza { get; set; }
    public int? Edad { get; set; }
    public decimal? PesoKg { get; set; }
    public string? Sexo { get; set; }
    public string? Color { get; set; }
    public bool? Esterilizado { get; set; }
    public string? Observaciones { get; set; }
    public string? FotoUrl { get; set; }
    public int? UserId { get; set; }
}

public sealed class UpdateMascotaRequest
{
    public string? Nombre { get; set; }
    public string? Raza { get; set; }
    public int? Edad { get; set; }
    public decimal? PesoKg { get; set; }
    public string? Sexo { get; set; }
    public string? Color { get; set; }
    public bool? Esterilizado { get; set; }
    public string? Observaciones { get; set; }
    public string? FotoUrl { get; set; }
    public int? UserId { get; set; }
}

public sealed class ConsultaResumenDto
{
    public int Id { get; set; }
    public string TipoServicio { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public DateTime FechaConsulta { get; set; }
    public decimal Costo { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
}

public sealed class MascotaResponse
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
    public string? FotoUrl { get; set; }
    public int UserId { get; set; }
    public string PropietarioNombre { get; set; } = string.Empty;
    public ConsultaResumenDto? ProximaCita { get; set; }
    public int TotalHistorial { get; set; }
    public DateTime FechaRegistro { get; set; }
}

public sealed class MascotaHistorialResponse
{
    public int MascotaId { get; set; }
    public string MascotaNombre { get; set; } = string.Empty;
    public string MascotaRaza { get; set; } = string.Empty;
    public string? FotoUrl { get; set; }
    public ConsultaResumenDto? ProximaCita { get; set; }
    public List<ConsultaResponse> CitasPrevias { get; set; } = new();
    public List<ConsultaResponse> TodasLasConsultas { get; set; } = new();
}

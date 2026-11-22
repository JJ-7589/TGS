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
    public int? UserId { get; set; }
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
    public int UserId { get; set; }
    public string PropietarioNombre { get; set; } = string.Empty;
    public DateTime FechaRegistro { get; set; }
}

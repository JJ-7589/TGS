namespace Caninos.Api.Dtos.Operaciones;

public sealed class CreateOperacionRequest
{
    public string? Nombre { get; set; }
    public string? TipoServicio { get; set; }
    public string? Descripcion { get; set; }
    public decimal? Costo { get; set; }
    public bool? Activo { get; set; }
}

public sealed class UpdateOperacionRequest
{
    public string? Nombre { get; set; }
    public string? TipoServicio { get; set; }
    public string? Descripcion { get; set; }
    public decimal? Costo { get; set; }
    public bool? Activo { get; set; }
}

public sealed class OperacionResponse
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string TipoServicio { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public decimal Costo { get; set; }
    public bool Activo { get; set; }
    public DateTime FechaCreacion { get; set; }
}

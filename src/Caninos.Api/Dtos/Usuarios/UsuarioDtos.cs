namespace Caninos.Api.Dtos.Usuarios;

public sealed class CreateUsuarioRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Documento { get; set; }
    public string? NombreCompleto { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Direccion { get; set; }
}

public sealed class UpdateUsuarioRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Documento { get; set; }
    public string? NombreCompleto { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Direccion { get; set; }
}

public sealed class UsuarioResponse
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Documento { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public DateTime FechaRegistro { get; set; }
}

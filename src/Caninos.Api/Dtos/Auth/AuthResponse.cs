using Caninos.Api.Dtos.Usuarios;

namespace Caninos.Api.Dtos.Auth;

public sealed class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public DateTime ExpiresAt { get; set; }
    public UsuarioResponse Usuario { get; set; } = null!;
}

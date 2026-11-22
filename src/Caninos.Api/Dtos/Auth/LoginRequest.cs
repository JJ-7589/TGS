namespace Caninos.Api.Dtos.Auth;

public sealed class LoginRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

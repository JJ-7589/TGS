using Caninos.Api.Dtos.Auth;
using Caninos.Api.Dtos.Usuarios;
using Caninos.Api.Middleware;
using Caninos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Caninos.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly UsuarioService _usuarioService;

    public AuthController(UsuarioService usuarioService)
    {
        _usuarioService = usuarioService;
    }

    [HttpPost("login")]
    [Consumes("application/json")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest? request)
    {
        if (request is null)
        {
            throw new ApiValidationException("El cuerpo JSON de la petición es obligatorio.");
        }

        var response = await _usuarioService.LoginAsync(request);
        return Ok(response);
    }

    [HttpPost("register")]
    [Consumes("application/json")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] CreateUsuarioRequest? request)
    {
        if (request is null)
        {
            throw new ApiValidationException("El cuerpo JSON de la petición es obligatorio.");
        }

        var response = await _usuarioService.RegisterAsync(request);
        return Created($"/api/usuarios/{response.Usuario.Id}", response);
    }
}

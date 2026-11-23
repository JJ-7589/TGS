using Caninos.Api.Dtos.Usuarios;
using Caninos.Api.Middleware;
using Caninos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Caninos.Api.Controllers;

[ApiController]
[Route("api/perfil")]
[Authorize]
public sealed class PerfilController : ControllerBase
{
    private readonly UsuarioService _usuarioService;

    public PerfilController(UsuarioService usuarioService)
    {
        _usuarioService = usuarioService;
    }

    [HttpGet]
    public async Task<ActionResult<UsuarioResponse>> GetMine()
    {
        var currentUser = HttpContext.GetRequiredCurrentUser();
        var usuario = await _usuarioService.GetByIdAsync(currentUser.UserId);
        return Ok(usuario);
    }

    [HttpPut]
    [Consumes("application/json")]
    public async Task<ActionResult<UsuarioResponse>> UpdateMine([FromBody] UpdatePerfilRequest? request)
    {
        if (request is null)
        {
            throw new ApiValidationException("El cuerpo JSON de la petición es obligatorio.");
        }

        var currentUser = HttpContext.GetRequiredCurrentUser();
        var updated = await _usuarioService.UpdatePerfilAsync(currentUser.UserId, request);
        return Ok(updated);
    }

    [HttpPut("password")]
    [Consumes("application/json")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest? request)
    {
        if (request is null)
        {
            throw new ApiValidationException("El cuerpo JSON de la petición es obligatorio.");
        }

        var currentUser = HttpContext.GetRequiredCurrentUser();
        await _usuarioService.ChangePasswordAsync(currentUser.UserId, request);
        return Ok(new { message = "Contraseña actualizada correctamente." });
    }
}

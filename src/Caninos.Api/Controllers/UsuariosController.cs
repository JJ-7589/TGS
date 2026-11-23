using Caninos.Api.Dtos.Mascotas;
using Caninos.Api.Dtos.Usuarios;
using Caninos.Api.Middleware;
using Caninos.Api.Services;
using Caninos.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Caninos.Api.Controllers;

[ApiController]
[Route("api/usuarios")]
[Authorize]
public sealed class UsuariosController : ControllerBase
{
    private readonly UsuarioService _usuarioService;

    public UsuariosController(UsuarioService usuarioService)
    {
        _usuarioService = usuarioService;
    }

    [HttpGet]
    public async Task<ActionResult<List<UsuarioResponse>>> GetAll([FromQuery] string? search)
    {
        var usuarios = await _usuarioService.GetAllAsync(search);
        return Ok(usuarios);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UsuarioResponse>> GetById(string id)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        var usuario = await _usuarioService.GetByIdAsync(parsedId);
        return Ok(usuario);
    }

    [HttpGet("{id}/mascotas")]
    public async Task<ActionResult<List<MascotaResponse>>> GetMascotasByUsuario(string id)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        var mascotas = await _usuarioService.GetMascotasByUsuarioIdAsync(parsedId);
        return Ok(mascotas);
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<UsuarioResponse>> Create([FromBody] CreateUsuarioRequest? request)
    {
        if (request is null)
        {
            throw new ApiValidationException("El cuerpo JSON de la petición es obligatorio.");
        }

        var created = await _usuarioService.CreateAsync(request);
        return Created($"/api/usuarios/{created.Id}", created);
    }

    [HttpPut("{id}")]
    [Consumes("application/json")]
    public async Task<ActionResult<UsuarioResponse>> Update(string id, [FromBody] UpdateUsuarioRequest? request)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        if (request is null)
        {
            throw new ApiValidationException("El cuerpo JSON de la petición es obligatorio.");
        }

        var currentUser = HttpContext.GetRequiredCurrentUser();
        var updated = await _usuarioService.UpdateAsync(parsedId, request, allowRoleChange: currentUser.IsAdmin);
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        await _usuarioService.DeleteAsync(parsedId);
        return NoContent();
    }
}

using Caninos.Api.Dtos.Mascotas;
using Caninos.Api.Middleware;
using Caninos.Api.Services;
using Caninos.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Caninos.Api.Controllers;

[ApiController]
[Route("api/mascotas")]
[Authorize]
public sealed class MascotasController : ControllerBase
{
    private readonly MascotaService _mascotaService;

    public MascotasController(MascotaService mascotaService)
    {
        _mascotaService = mascotaService;
    }

    [HttpGet]
    public async Task<ActionResult<List<MascotaResponse>>> GetAll([FromQuery] string? userId)
    {
        var errors = new Dictionary<string, List<string>>();
        var parsedUserId = DomainValidation.ParseOptionalPositiveId(userId, "userId", errors);
        DomainValidation.ThrowIfErrors(errors);

        var mascotas = await _mascotaService.GetAllAsync(parsedUserId);
        return Ok(mascotas);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<MascotaResponse>> GetById(string id)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        var mascota = await _mascotaService.GetByIdAsync(parsedId);
        return Ok(mascota);
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<MascotaResponse>> Create([FromBody] CreateMascotaRequest? request)
    {
        if (request is null)
        {
            throw new ApiValidationException("El cuerpo JSON de la petición es obligatorio.");
        }

        var created = await _mascotaService.CreateAsync(request);
        return Created($"/api/mascotas/{created.Id}", created);
    }

    [HttpPut("{id}")]
    [Consumes("application/json")]
    public async Task<ActionResult<MascotaResponse>> Update(string id, [FromBody] UpdateMascotaRequest? request)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        if (request is null)
        {
            throw new ApiValidationException("El cuerpo JSON de la petición es obligatorio.");
        }

        var updated = await _mascotaService.UpdateAsync(parsedId, request);
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        await _mascotaService.DeleteAsync(parsedId);
        return NoContent();
    }
}

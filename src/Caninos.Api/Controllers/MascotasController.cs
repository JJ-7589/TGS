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
        var rawUserId = Request.Query.ContainsKey("userId") ? Request.Query["userId"].ToString() : userId;
        var parsedUserId = DomainValidation.ParseOptionalPositiveId(rawUserId, "userId", errors);
        DomainValidation.ThrowIfErrors(errors);

        var currentUser = HttpContext.GetRequiredCurrentUser();
        var effectiveUserId = currentUser.IsAdmin ? parsedUserId : currentUser.UserId;

        var mascotas = await _mascotaService.GetAllAsync(effectiveUserId);
        return Ok(mascotas);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<MascotaResponse>> GetById(string id)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        var mascota = await _mascotaService.GetByIdAsync(parsedId);
        return Ok(mascota);
    }

    [HttpGet("{id}/historial")]
    public async Task<ActionResult<MascotaHistorialResponse>> GetHistorial(string id)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        var historial = await _mascotaService.GetHistorialAsync(parsedId);
        return Ok(historial);
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<MascotaResponse>> Create([FromBody] CreateMascotaRequest? request)
    {
        if (request is null)
        {
            throw new ApiValidationException("El cuerpo JSON de la petición es obligatorio.");
        }

        var currentUser = HttpContext.GetRequiredCurrentUser();
        if (!currentUser.IsAdmin && request.UserId is null)
        {
            request.UserId = currentUser.UserId;
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

        var currentUser = HttpContext.GetRequiredCurrentUser();
        if (!currentUser.IsAdmin && request.UserId is null)
        {
            request.UserId = currentUser.UserId;
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

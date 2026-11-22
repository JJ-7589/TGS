using Caninos.Api.Dtos.Consultas;
using Caninos.Api.Middleware;
using Caninos.Api.Services;
using Caninos.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Caninos.Api.Controllers;

[ApiController]
[Route("api/consultas")]
[Authorize]
public sealed class ConsultasController : ControllerBase
{
    private readonly ConsultaService _consultaService;

    public ConsultasController(ConsultaService consultaService)
    {
        _consultaService = consultaService;
    }

    [HttpGet]
    public async Task<ActionResult<List<ConsultaResponse>>> GetAll(
        [FromQuery] string? userId,
        [FromQuery] string? mascotaId,
        [FromQuery] string? tipoServicio,
        [FromQuery] string? estado)
    {
        var errors = new Dictionary<string, List<string>>();
        var parsedUserId = DomainValidation.ParseOptionalPositiveId(userId, "userId", errors);
        var parsedMascotaId = DomainValidation.ParseOptionalPositiveId(mascotaId, "mascotaId", errors);

        string? normalizedTipoServicio = null;
        if (tipoServicio is not null)
        {
            normalizedTipoServicio = DomainValidation.ValidateTipoServicio(tipoServicio, errors, "tipoServicio");
        }

        string? normalizedEstado = null;
        if (estado is not null)
        {
            normalizedEstado = DomainValidation.ValidateEstadoConsulta(estado, required: true, errors, "estado");
        }

        DomainValidation.ThrowIfErrors(errors);

        var consultas = await _consultaService.GetAllAsync(
            parsedUserId,
            parsedMascotaId,
            normalizedTipoServicio,
            normalizedEstado);

        return Ok(consultas);
    }

    [HttpGet("usuario/{userId}")]
    public async Task<ActionResult<List<ConsultaResponse>>> GetByUsuario(string userId)
    {
        var parsedUserId = DomainValidation.ParsePositiveId(userId, "userId");
        var consultas = await _consultaService.GetByUsuarioIdAsync(parsedUserId);
        return Ok(consultas);
    }

    [HttpGet("mascota/{mascotaId}")]
    public async Task<ActionResult<List<ConsultaResponse>>> GetByMascota(string mascotaId)
    {
        var parsedMascotaId = DomainValidation.ParsePositiveId(mascotaId, "mascotaId");
        var consultas = await _consultaService.GetByMascotaIdAsync(parsedMascotaId);
        return Ok(consultas);
    }

    [HttpGet("servicio/{tipoServicio}")]
    public async Task<ActionResult<List<ConsultaResponse>>> GetByServicio(string tipoServicio)
    {
        var errors = new Dictionary<string, List<string>>();
        var normalized = DomainValidation.ValidateTipoServicio(tipoServicio, errors, "tipoServicio");
        DomainValidation.ThrowIfErrors(errors);

        var consultas = await _consultaService.GetByTipoServicioAsync(normalized);
        return Ok(consultas);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ConsultaResponse>> GetById(string id)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        var consulta = await _consultaService.GetByIdAsync(parsedId);
        return Ok(consulta);
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<ConsultaResponse>> Create([FromBody] CreateConsultaRequest? request)
    {
        if (request is null)
        {
            throw new ApiValidationException("El cuerpo JSON de la petición es obligatorio.");
        }

        var created = await _consultaService.CreateAsync(request);
        return Created($"/api/consultas/{created.Id}", created);
    }

    [HttpPut("{id}")]
    [Consumes("application/json")]
    public async Task<ActionResult<ConsultaResponse>> Update(string id, [FromBody] UpdateConsultaRequest? request)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        if (request is null)
        {
            throw new ApiValidationException("El cuerpo JSON de la petición es obligatorio.");
        }

        var updated = await _consultaService.UpdateAsync(parsedId, request);
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        await _consultaService.DeleteAsync(parsedId);
        return NoContent();
    }
}

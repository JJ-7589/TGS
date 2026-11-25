using Caninos.Api.Dtos.Operaciones;
using Caninos.Api.Middleware;
using Caninos.Api.Services;
using Caninos.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Caninos.Api.Controllers;

[ApiController]
[Route("api/operaciones")]
[Authorize]
public sealed class OperacionesController : ControllerBase
{
    private readonly OperacionService _operacionService;

    public OperacionesController(OperacionService operacionService)
    {
        _operacionService = operacionService;
    }

    [HttpGet]
    public async Task<ActionResult<List<OperacionResponse>>> GetAll(
        [FromQuery] string? tipoServicio,
        [FromQuery] string? soloActivas)
    {
        var errors = new Dictionary<string, List<string>>();
        var rawTipo = Request.Query.ContainsKey("tipoServicio") ? Request.Query["tipoServicio"].ToString() : tipoServicio;
        var rawActivas = Request.Query.ContainsKey("soloActivas") ? Request.Query["soloActivas"].ToString() : soloActivas;

        string? normalizedTipo = null;
        if (rawTipo is not null)
        {
            normalizedTipo = DomainValidation.ValidateTipoServicio(rawTipo, errors, "tipoServicio");
        }

        bool? parsedSoloActivas = null;
        if (rawActivas is not null)
        {
            if (!bool.TryParse(rawActivas, out var b))
            {
                DomainValidation.AddError(errors, "soloActivas", "El parámetro 'soloActivas' debe ser booleano ('true' o 'false').");
            }
            else
            {
                parsedSoloActivas = b;
            }
        }

        DomainValidation.ThrowIfErrors(errors);

        var list = await _operacionService.GetAllAsync(normalizedTipo, parsedSoloActivas);
        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OperacionResponse>> GetById(string id)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        var op = await _operacionService.GetByIdAsync(parsedId);
        return Ok(op);
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<OperacionResponse>> Create([FromBody] CreateOperacionRequest? request)
    {
        if (request is null)
        {
            throw new ApiValidationException("El cuerpo JSON de la petición es obligatorio.");
        }

        var created = await _operacionService.CreateAsync(request);
        return Created($"/api/operaciones/{created.Id}", created);
    }

    [HttpPut("{id}")]
    [Consumes("application/json")]
    public async Task<ActionResult<OperacionResponse>> Update(string id, [FromBody] UpdateOperacionRequest? request)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        if (request is null)
        {
            throw new ApiValidationException("El cuerpo JSON de la petición es obligatorio.");
        }

        var updated = await _operacionService.UpdateAsync(parsedId, request);
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var parsedId = DomainValidation.ParsePositiveId(id, "id");
        await _operacionService.DeleteAsync(parsedId);
        return NoContent();
    }
}

using Caninos.Api.Data;
using Caninos.Api.Dtos.Operaciones;
using Caninos.Api.Middleware;
using Caninos.Api.Models;
using Caninos.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace Caninos.Api.Services;

public sealed class OperacionService
{
    private readonly AppDbContext _db;

    public OperacionService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<OperacionResponse>> GetAllAsync(string? tipoServicio, bool? soloActivas)
    {
        var query = _db.Operaciones.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(tipoServicio))
        {
            query = query.Where(o => o.TipoServicio == tipoServicio);
        }

        if (soloActivas.HasValue && soloActivas.Value)
        {
            query = query.Where(o => o.Activo);
        }

        return await query
            .OrderBy(o => o.TipoServicio)
            .ThenBy(o => o.Nombre)
            .Select(o => ToResponse(o))
            .ToListAsync();
    }

    public async Task<OperacionResponse> GetByIdAsync(int id)
    {
        var operacion = await _db.Operaciones.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new ApiNotFoundException($"No se encontró ninguna operación con id {id}.");

        return ToResponse(operacion);
    }

    public async Task<OperacionResponse> CreateAsync(CreateOperacionRequest request)
    {
        var errors = new Dictionary<string, List<string>>();
        var nombre = DomainValidation.ValidateRequiredText(request.Nombre, "nombre", "nombre", 2, 120, errors);
        var tipoServicio = DomainValidation.ValidateTipoServicio(request.TipoServicio, errors);
        var descripcion = DomainValidation.ValidateOptionalText(request.Descripcion, "descripcion", "descripcion", 300, errors);
        var costo = DomainValidation.ValidateCosto(request.Costo, errors);
        var activo = request.Activo ?? true;
        DomainValidation.ThrowIfErrors(errors);

        var duplicate = await _db.Operaciones
            .AsNoTracking()
            .AnyAsync(o => o.Nombre.ToLower() == nombre.ToLower());
        if (duplicate)
        {
            throw new ApiConflictException(
                "Ya existe una operación registrada con ese nombre.",
                new Dictionary<string, string[]>
                {
                    ["nombre"] = ["Ya existe una operación registrada con ese nombre."]
                });
        }

        var operacion = new Operacion
        {
            Nombre = nombre,
            TipoServicio = tipoServicio,
            Descripcion = descripcion,
            Costo = costo,
            Activo = activo,
            FechaCreacion = DateTime.UtcNow
        };

        _db.Operaciones.Add(operacion);
        await _db.SaveChangesAsync();

        return ToResponse(operacion);
    }

    public async Task<OperacionResponse> UpdateAsync(int id, UpdateOperacionRequest request)
    {
        var operacion = await _db.Operaciones.FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new ApiNotFoundException($"No se encontró ninguna operación con id {id}.");

        var errors = new Dictionary<string, List<string>>();
        var nombre = DomainValidation.ValidateRequiredText(request.Nombre, "nombre", "nombre", 2, 120, errors);
        var tipoServicio = DomainValidation.ValidateTipoServicio(request.TipoServicio, errors);
        var descripcion = DomainValidation.ValidateOptionalText(request.Descripcion, "descripcion", "descripcion", 300, errors);
        var costo = DomainValidation.ValidateCosto(request.Costo, errors);
        var activo = request.Activo ?? operacion.Activo;
        DomainValidation.ThrowIfErrors(errors);

        var duplicate = await _db.Operaciones
            .AsNoTracking()
            .AnyAsync(o => o.Id != id && o.Nombre.ToLower() == nombre.ToLower());
        if (duplicate)
        {
            throw new ApiConflictException(
                "Ya existe otra operación registrada con ese nombre.",
                new Dictionary<string, string[]>
                {
                    ["nombre"] = ["Ya existe otra operación registrada con ese nombre."]
                });
        }

        operacion.Nombre = nombre;
        operacion.TipoServicio = tipoServicio;
        operacion.Descripcion = descripcion;
        operacion.Costo = costo;
        operacion.Activo = activo;

        await _db.SaveChangesAsync();
        return ToResponse(operacion);
    }

    public async Task DeleteAsync(int id)
    {
        var operacion = await _db.Operaciones.FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new ApiNotFoundException($"No se encontró ninguna operación con id {id}.");

        var usedInHistory = await _db.Consultas.AnyAsync(c => c.OperacionId == id);
        if (usedInHistory)
        {
            throw new ApiConflictException(
                "No se puede eliminar la operación porque ya está asociada al historial de consultas. Puedes desactivarla marcando 'activo' como false.");
        }

        _db.Operaciones.Remove(operacion);
        await _db.SaveChangesAsync();
    }

    public static OperacionResponse ToResponse(Operacion op) => new()
    {
        Id = op.Id,
        Nombre = op.Nombre,
        TipoServicio = op.TipoServicio,
        Descripcion = op.Descripcion,
        Costo = op.Costo,
        Activo = op.Activo,
        FechaCreacion = op.FechaCreacion
    };
}

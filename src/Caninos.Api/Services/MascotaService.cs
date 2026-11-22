using Caninos.Api.Data;
using Caninos.Api.Dtos.Mascotas;
using Caninos.Api.Middleware;
using Caninos.Api.Models;
using Caninos.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace Caninos.Api.Services;

public sealed class MascotaService
{
    private readonly AppDbContext _db;

    public MascotaService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<MascotaResponse>> GetAllAsync(int? userId)
    {
        var query = _db.Mascotas
            .AsNoTracking()
            .Include(m => m.Usuario)
            .AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(m => m.UserId == userId.Value);
        }

        return await query
            .OrderBy(m => m.Id)
            .Select(m => ToResponse(m))
            .ToListAsync();
    }

    public async Task<MascotaResponse> GetByIdAsync(int id)
    {
        var mascota = await _db.Mascotas
            .AsNoTracking()
            .Include(m => m.Usuario)
            .FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new ApiNotFoundException($"No se encontró ninguna mascota con id {id}.");

        return ToResponse(mascota);
    }

    public async Task<MascotaResponse> CreateAsync(CreateMascotaRequest request)
    {
        var errors = new Dictionary<string, List<string>>();
        var nombre = DomainValidation.ValidateRequiredText(request.Nombre, "nombre", "nombre", 1, 80, errors);
        var raza = DomainValidation.ValidateRequiredText(request.Raza, "raza", "raza", 2, 80, errors);
        var edad = DomainValidation.ValidateEdad(request.Edad, errors);
        var pesoKg = DomainValidation.ValidatePesoKg(request.PesoKg, errors);
        var sexo = DomainValidation.ValidateSexo(request.Sexo, errors);
        var color = DomainValidation.ValidateRequiredText(request.Color, "color", "color", 2, 50, errors);
        var esterilizado = DomainValidation.ValidateEsterilizado(request.Esterilizado, errors);
        var observaciones = DomainValidation.ValidateOptionalText(request.Observaciones, "observaciones", "observaciones", 500, errors);
        var userId = DomainValidation.ValidateForeignKeyId(request.UserId, "userId", errors);
        DomainValidation.ThrowIfErrors(errors);

        var propietario = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new ApiNotFoundException($"No existe un usuario propietario con id {userId}.");

        var mascota = new Mascota
        {
            Nombre = nombre,
            Raza = raza,
            Edad = edad,
            PesoKg = pesoKg,
            Sexo = sexo,
            Color = color,
            Esterilizado = esterilizado,
            Observaciones = observaciones,
            UserId = userId,
            FechaRegistro = DateTime.UtcNow
        };

        _db.Mascotas.Add(mascota);
        await _db.SaveChangesAsync();

        mascota.Usuario = propietario;
        return ToResponse(mascota);
    }

    public async Task<MascotaResponse> UpdateAsync(int id, UpdateMascotaRequest request)
    {
        var mascota = await _db.Mascotas.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new ApiNotFoundException($"No se encontró ninguna mascota con id {id}.");

        var errors = new Dictionary<string, List<string>>();
        var nombre = DomainValidation.ValidateRequiredText(request.Nombre, "nombre", "nombre", 1, 80, errors);
        var raza = DomainValidation.ValidateRequiredText(request.Raza, "raza", "raza", 2, 80, errors);
        var edad = DomainValidation.ValidateEdad(request.Edad, errors);
        var pesoKg = DomainValidation.ValidatePesoKg(request.PesoKg, errors);
        var sexo = DomainValidation.ValidateSexo(request.Sexo, errors);
        var color = DomainValidation.ValidateRequiredText(request.Color, "color", "color", 2, 50, errors);
        var esterilizado = DomainValidation.ValidateEsterilizado(request.Esterilizado, errors);
        var observaciones = DomainValidation.ValidateOptionalText(request.Observaciones, "observaciones", "observaciones", 500, errors);
        var userId = DomainValidation.ValidateForeignKeyId(request.UserId, "userId", errors);
        DomainValidation.ThrowIfErrors(errors);

        var propietario = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new ApiNotFoundException($"No existe un usuario propietario con id {userId}.");

        if (mascota.UserId != userId)
        {
            var hasConsultas = await _db.Consultas.AnyAsync(c => c.MascotaId == id);
            if (hasConsultas)
            {
                throw new ApiConflictException(
                    "No se puede cambiar el propietario (userId) de una mascota que ya tiene consultas registradas.");
            }
        }

        mascota.Nombre = nombre;
        mascota.Raza = raza;
        mascota.Edad = edad;
        mascota.PesoKg = pesoKg;
        mascota.Sexo = sexo;
        mascota.Color = color;
        mascota.Esterilizado = esterilizado;
        mascota.Observaciones = observaciones;
        mascota.UserId = userId;

        await _db.SaveChangesAsync();

        mascota.Usuario = propietario;
        return ToResponse(mascota);
    }

    public async Task DeleteAsync(int id)
    {
        var mascota = await _db.Mascotas.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new ApiNotFoundException($"No se encontró ninguna mascota con id {id}.");

        var hasConsultas = await _db.Consultas.AnyAsync(c => c.MascotaId == id);
        if (hasConsultas)
        {
            throw new ApiConflictException(
                "No se puede eliminar la mascota porque tiene consultas registradas.");
        }

        _db.Mascotas.Remove(mascota);
        await _db.SaveChangesAsync();
    }

    public static MascotaResponse ToResponse(Mascota mascota) => new()
    {
        Id = mascota.Id,
        Nombre = mascota.Nombre,
        Raza = mascota.Raza,
        Edad = mascota.Edad,
        PesoKg = mascota.PesoKg,
        Sexo = mascota.Sexo,
        Color = mascota.Color,
        Esterilizado = mascota.Esterilizado,
        Observaciones = mascota.Observaciones,
        UserId = mascota.UserId,
        PropietarioNombre = mascota.Usuario?.NombreCompleto ?? string.Empty,
        FechaRegistro = mascota.FechaRegistro
    };
}

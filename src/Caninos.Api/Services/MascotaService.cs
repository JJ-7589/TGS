using Caninos.Api.Data;
using Caninos.Api.Dtos.Consultas;
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

        var mascotas = await query.OrderBy(m => m.Id).ToListAsync();
        return await EnrichWithConsultasSummaryAsync(mascotas);
    }

    public async Task<MascotaResponse> GetByIdAsync(int id)
    {
        var mascota = await _db.Mascotas
            .AsNoTracking()
            .Include(m => m.Usuario)
            .FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new ApiNotFoundException($"No se encontró ninguna mascota con id {id}.");

        var enriched = await EnrichWithConsultasSummaryAsync([mascota]);
        return enriched[0];
    }

    public async Task<MascotaHistorialResponse> GetHistorialAsync(int mascotaId)
    {
        var mascota = await _db.Mascotas
            .AsNoTracking()
            .Include(m => m.Usuario)
            .FirstOrDefaultAsync(m => m.Id == mascotaId)
            ?? throw new ApiNotFoundException($"No se encontró ninguna mascota con id {mascotaId}.");

        // Consulta indexada por ix_consultas_mascota_id_fecha (mascota_id, fecha_consulta DESC, estado)
        var consultas = await _db.Consultas
            .AsNoTracking()
            .Include(c => c.Mascota)
            .Include(c => c.Usuario)
            .Include(c => c.Operacion)
            .Where(c => c.MascotaId == mascotaId)
            .OrderByDescending(c => c.FechaConsulta)
            .ThenByDescending(c => c.Id)
            .ToListAsync();

        var now = DateTime.UtcNow;

        var proxima = consultas
            .Where(c => c.FechaConsulta >= now && (c.Estado == "Programada" || c.Estado == "EnProceso"))
            .OrderBy(c => c.FechaConsulta)
            .ThenBy(c => c.Id)
            .Select(ToResumen)
            .FirstOrDefault();

        var citasPrevias = consultas
            .Where(c => c.FechaConsulta < now || c.Estado == "Completada" || c.Estado == "Cancelada")
            .Select(ConsultaService.ToResponse)
            .ToList();

        return new MascotaHistorialResponse
        {
            MascotaId = mascota.Id,
            MascotaNombre = mascota.Nombre,
            MascotaRaza = mascota.Raza,
            FotoUrl = mascota.FotoUrl,
            ProximaCita = proxima,
            CitasPrevias = citasPrevias,
            TodasLasConsultas = consultas.Select(ConsultaService.ToResponse).ToList()
        };
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
        var fotoUrl = DomainValidation.ValidateFotoUrl(request.FotoUrl, errors);
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
            FotoUrl = fotoUrl,
            UserId = userId,
            FechaRegistro = DateTime.UtcNow
        };

        _db.Mascotas.Add(mascota);
        await _db.SaveChangesAsync();

        mascota.Usuario = propietario;
        return ToResponse(mascota, proximaCita: null, totalHistorial: 0);
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
        var fotoUrl = DomainValidation.ValidateFotoUrl(request.FotoUrl, errors);
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
        mascota.FotoUrl = fotoUrl;
        mascota.UserId = userId;

        await _db.SaveChangesAsync();

        mascota.Usuario = propietario;
        var enriched = await EnrichWithConsultasSummaryAsync([mascota]);
        return enriched[0];
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

    private async Task<List<MascotaResponse>> EnrichWithConsultasSummaryAsync(List<Mascota> mascotas)
    {
        if (mascotas.Count == 0)
        {
            return new List<MascotaResponse>();
        }

        var ids = mascotas.Select(m => m.Id).ToList();
        var now = DateTime.UtcNow;

        // Aprovecha el índice ix_consultas_mascota_id_fecha evitando consultas N+1
        var consultas = await _db.Consultas
            .AsNoTracking()
            .Include(c => c.Operacion)
            .Where(c => ids.Contains(c.MascotaId))
            .ToListAsync();

        var byMascota = consultas
            .GroupBy(c => c.MascotaId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return mascotas.Select(m =>
        {
            ConsultaResumenDto? proxima = null;
            var totalHistorial = 0;

            if (byMascota.TryGetValue(m.Id, out var list))
            {
                proxima = list
                    .Where(c => c.FechaConsulta >= now && (c.Estado == "Programada" || c.Estado == "EnProceso"))
                    .OrderBy(c => c.FechaConsulta)
                    .ThenBy(c => c.Id)
                    .Select(ToResumen)
                    .FirstOrDefault();

                totalHistorial = list.Count(c =>
                    c.FechaConsulta < now || c.Estado == "Completada" || c.Estado == "Cancelada");
            }

            return ToResponse(m, proxima, totalHistorial);
        }).ToList();
    }

    private static ConsultaResumenDto ToResumen(Consulta c) => new()
    {
        Id = c.Id,
        OperacionId = c.OperacionId,
        OperacionNombre = c.Operacion?.Nombre,
        TipoServicio = c.TipoServicio,
        Descripcion = c.Descripcion,
        FechaConsulta = c.FechaConsulta,
        Costo = c.Costo,
        Estado = c.Estado,
        Observaciones = c.Observaciones
    };

    public static MascotaResponse ToResponse(
        Mascota mascota,
        ConsultaResumenDto? proximaCita = null,
        int totalHistorial = 0) => new()
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
        FotoUrl = mascota.FotoUrl,
        UserId = mascota.UserId,
        PropietarioNombre = mascota.Usuario?.NombreCompleto ?? string.Empty,
        ProximaCita = proximaCita,
        TotalHistorial = totalHistorial,
        FechaRegistro = mascota.FechaRegistro
    };
}

using Caninos.Api.Data;
using Caninos.Api.Dtos.Consultas;
using Caninos.Api.Middleware;
using Caninos.Api.Models;
using Caninos.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace Caninos.Api.Services;

public sealed class ConsultaService
{
    private readonly AppDbContext _db;

    public ConsultaService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<ConsultaResponse>> GetAllAsync(
        int? userId,
        int? mascotaId,
        string? tipoServicio,
        string? estado)
    {
        var query = _db.Consultas
            .AsNoTracking()
            .Include(c => c.Mascota)
            .Include(c => c.Usuario)
            .Include(c => c.Operacion)
            .AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(c => c.UserId == userId.Value);
        }

        if (mascotaId.HasValue)
        {
            query = query.Where(c => c.MascotaId == mascotaId.Value);
        }

        if (!string.IsNullOrWhiteSpace(tipoServicio))
        {
            query = query.Where(c => c.TipoServicio == tipoServicio);
        }

        if (!string.IsNullOrWhiteSpace(estado))
        {
            query = query.Where(c => c.Estado == estado);
        }

        return await query
            .OrderByDescending(c => c.FechaConsulta)
            .ThenByDescending(c => c.Id)
            .Select(c => ToResponse(c))
            .ToListAsync();
    }

    public async Task<ConsultaResponse> GetByIdAsync(int id)
    {
        var consulta = await _db.Consultas
            .AsNoTracking()
            .Include(c => c.Mascota)
            .Include(c => c.Usuario)
            .Include(c => c.Operacion)
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new ApiNotFoundException($"No se encontró ninguna consulta con id {id}.");

        return ToResponse(consulta);
    }

    public async Task<List<ConsultaResponse>> GetByUsuarioIdAsync(int userId)
    {
        var exists = await _db.Usuarios.AsNoTracking().AnyAsync(u => u.Id == userId);
        if (!exists)
        {
            throw new ApiNotFoundException($"No se encontró ningún usuario con id {userId}.");
        }

        return await GetAllAsync(userId, mascotaId: null, tipoServicio: null, estado: null);
    }

    public async Task<List<ConsultaResponse>> GetByMascotaIdAsync(int mascotaId)
    {
        var exists = await _db.Mascotas.AsNoTracking().AnyAsync(m => m.Id == mascotaId);
        if (!exists)
        {
            throw new ApiNotFoundException($"No se encontró ninguna mascota con id {mascotaId}.");
        }

        return await GetAllAsync(userId: null, mascotaId, tipoServicio: null, estado: null);
    }

    public async Task<List<ConsultaResponse>> GetByTipoServicioAsync(string tipoServicioNormalizado, int? scopedUserId = null)
    {
        return await GetAllAsync(userId: scopedUserId, mascotaId: null, tipoServicioNormalizado, estado: null);
    }

    public async Task<ConsultaResponse> CreateAsync(CreateConsultaRequest request, bool isAdmin = true)
    {
        var errors = new Dictionary<string, List<string>>();
        var userId = DomainValidation.ValidateForeignKeyId(request.UserId, "userId", errors);
        var mascotaId = DomainValidation.ValidateForeignKeyId(request.MascotaId, "mascotaId", errors);

        Operacion? operacion = null;
        if (request.OperacionId.HasValue)
        {
            if (request.OperacionId.Value <= 0)
            {
                DomainValidation.AddError(errors, "operacionId", "El campo 'operacionId' debe ser un entero positivo mayor a 0.");
            }
            else
            {
                operacion = await _db.Operaciones
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == request.OperacionId.Value);

                if (operacion is null)
                {
                    throw new ApiNotFoundException($"No existe una operación con id {request.OperacionId.Value}.");
                }

                if (!operacion.Activo)
                {
                    throw new ApiConflictException("La operación seleccionada está inactiva y no admite nuevas citas.");
                }
            }
        }

        string tipoServicio;
        decimal costo;
        string descripcion;
        string estado;

        if (operacion is not null)
        {
            // Al elegir una operación del catálogo, tipoServicio, costo, descripción por defecto y estado se resuelven automáticamente
            tipoServicio = operacion.TipoServicio;

            costo = (!isAdmin || request.Costo is null)
                ? operacion.Costo
                : DomainValidation.ValidateCosto(request.Costo, errors);

            descripcion = string.IsNullOrWhiteSpace(request.Descripcion)
                ? BuildAutoDescripcion(operacion)
                : DomainValidation.ValidateRequiredText(request.Descripcion, "descripcion", "descripcion", 5, 500, errors);

            // Para rol 'usuario' el estado al agendar con procedimiento es siempre automático ('Programada')
            estado = !isAdmin
                ? "Programada"
                : DomainValidation.ValidateEstadoConsulta(request.Estado, required: false, errors);
        }
        else
        {
            tipoServicio = DomainValidation.ValidateTipoServicio(request.TipoServicio, errors);
            descripcion = DomainValidation.ValidateRequiredText(request.Descripcion, "descripcion", "descripcion", 5, 500, errors);
            costo = DomainValidation.ValidateCosto(request.Costo, errors);
            estado = DomainValidation.ValidateEstadoConsulta(request.Estado, required: false, errors);
        }

        var fechaConsulta = DomainValidation.ValidateFechaConsulta(request.FechaConsulta, errors, disallowPast: true);
        var observaciones = DomainValidation.ValidateOptionalText(request.Observaciones, "observaciones", "observaciones", 500, errors);
        DomainValidation.ThrowIfErrors(errors);

        var (usuario, mascota) = await ValidateRelationalOwnershipAsync(userId, mascotaId);

        var consulta = new Consulta
        {
            UserId = userId,
            MascotaId = mascotaId,
            OperacionId = operacion?.Id,
            TipoServicio = tipoServicio,
            Descripcion = descripcion,
            FechaConsulta = fechaConsulta,
            Costo = costo,
            Estado = estado,
            Observaciones = observaciones,
            FechaCreacion = DateTime.UtcNow
        };

        _db.Consultas.Add(consulta);
        await _db.SaveChangesAsync();

        consulta.Usuario = usuario;
        consulta.Mascota = mascota;
        consulta.Operacion = operacion;
        return ToResponse(consulta);
    }

    public async Task<ConsultaResponse> UpdateAsync(int id, UpdateConsultaRequest request, bool isAdmin = true)
    {
        var consulta = await _db.Consultas.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new ApiNotFoundException($"No se encontró ninguna consulta con id {id}.");

        var errors = new Dictionary<string, List<string>>();
        var userId = DomainValidation.ValidateForeignKeyId(request.UserId, "userId", errors);
        var mascotaId = DomainValidation.ValidateForeignKeyId(request.MascotaId, "mascotaId", errors);

        Operacion? operacion = null;
        if (request.OperacionId.HasValue)
        {
            if (request.OperacionId.Value <= 0)
            {
                DomainValidation.AddError(errors, "operacionId", "El campo 'operacionId' debe ser un entero positivo mayor a 0.");
            }
            else
            {
                operacion = await _db.Operaciones
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == request.OperacionId.Value);

                if (operacion is null)
                {
                    throw new ApiNotFoundException($"No existe una operación con id {request.OperacionId.Value}.");
                }

                if (!operacion.Activo && consulta.OperacionId != operacion.Id)
                {
                    throw new ApiConflictException("La operación seleccionada está inactiva y no se puede asignar.");
                }
            }
        }

        string tipoServicio;
        decimal costo;
        string descripcion;
        string estado;

        if (operacion is not null)
        {
            tipoServicio = operacion.TipoServicio;

            costo = (!isAdmin || request.Costo is null)
                ? operacion.Costo
                : DomainValidation.ValidateCosto(request.Costo, errors);

            descripcion = string.IsNullOrWhiteSpace(request.Descripcion)
                ? BuildAutoDescripcion(operacion)
                : DomainValidation.ValidateRequiredText(request.Descripcion, "descripcion", "descripcion", 5, 500, errors);

            if (!isAdmin)
            {
                // Para un usuario estándar el estado se gestiona automáticamente (conserva el actual o permite cancelar)
                if (request.Estado is null)
                {
                    estado = consulta.Estado;
                }
                else
                {
                    var parsedEstado = DomainValidation.ValidateEstadoConsulta(request.Estado, required: true, errors);
                    if (errors.Count == 0 && parsedEstado is not ("Programada" or "Cancelada") && parsedEstado != consulta.Estado)
                    {
                        throw new ApiForbiddenException("Solo un administrador puede marcar una consulta como 'EnProceso' o 'Completada'.");
                    }
                    estado = parsedEstado;
                }
            }
            else
            {
                estado = DomainValidation.ValidateEstadoConsulta(request.Estado, required: true, errors);
            }
        }
        else
        {
            tipoServicio = DomainValidation.ValidateTipoServicio(request.TipoServicio, errors);
            descripcion = DomainValidation.ValidateRequiredText(request.Descripcion, "descripcion", "descripcion", 5, 500, errors);
            costo = DomainValidation.ValidateCosto(request.Costo, errors);
            estado = DomainValidation.ValidateEstadoConsulta(request.Estado, required: true, errors);
        }

        var disallowPast = estado == "Programada";
        var fechaConsulta = DomainValidation.ValidateFechaConsulta(request.FechaConsulta, errors, disallowPast);
        var observaciones = DomainValidation.ValidateOptionalText(request.Observaciones, "observaciones", "observaciones", 500, errors);
        DomainValidation.ThrowIfErrors(errors);

        var (usuario, mascota) = await ValidateRelationalOwnershipAsync(userId, mascotaId);

        consulta.UserId = userId;
        consulta.MascotaId = mascotaId;
        consulta.OperacionId = operacion?.Id;
        consulta.TipoServicio = tipoServicio;
        consulta.Descripcion = descripcion;
        consulta.FechaConsulta = fechaConsulta;
        consulta.Costo = costo;
        consulta.Estado = estado;
        consulta.Observaciones = observaciones;

        await _db.SaveChangesAsync();

        consulta.Usuario = usuario;
        consulta.Mascota = mascota;
        consulta.Operacion = operacion;
        return ToResponse(consulta);
    }

    public async Task DeleteAsync(int id)
    {
        var consulta = await _db.Consultas.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new ApiNotFoundException($"No se encontró ninguna consulta con id {id}.");

        _db.Consultas.Remove(consulta);
        await _db.SaveChangesAsync();
    }

    private static string BuildAutoDescripcion(Operacion op)
    {
        if (string.IsNullOrWhiteSpace(op.Descripcion))
        {
            return op.Nombre.Length >= 5 ? op.Nombre : $"Procedimiento: {op.Nombre}";
        }

        var combined = $"{op.Nombre} — {op.Descripcion}";
        return combined.Length <= 500 ? combined : combined[..500];
    }

    private async Task<(Usuario Usuario, Mascota Mascota)> ValidateRelationalOwnershipAsync(int userId, int mascotaId)
    {
        var usuario = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new ApiNotFoundException($"No existe un usuario con id {userId}.");

        var mascota = await _db.Mascotas.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mascotaId)
            ?? throw new ApiNotFoundException($"No existe una mascota con id {mascotaId}.");

        if (mascota.UserId != userId)
        {
            throw new ApiConflictException(
                $"La mascota con id {mascotaId} no pertenece al usuario con id {userId}.",
                new Dictionary<string, string[]>
                {
                    ["mascotaId"] = [$"La mascota con id {mascotaId} pertenece al usuario con id {mascota.UserId}, no al usuario {userId}."]
                });
        }

        return (usuario, mascota);
    }

    public static ConsultaResponse ToResponse(Consulta consulta) => new()
    {
        Id = consulta.Id,
        MascotaId = consulta.MascotaId,
        UserId = consulta.UserId,
        OperacionId = consulta.OperacionId,
        OperacionNombre = consulta.Operacion?.Nombre,
        TipoServicio = consulta.TipoServicio,
        Descripcion = consulta.Descripcion,
        FechaConsulta = consulta.FechaConsulta,
        Costo = consulta.Costo,
        Estado = consulta.Estado,
        Observaciones = consulta.Observaciones,
        MascotaNombre = consulta.Mascota?.Nombre ?? string.Empty,
        MascotaRaza = consulta.Mascota?.Raza ?? string.Empty,
        UsuarioNombre = consulta.Usuario?.NombreCompleto ?? string.Empty,
        FechaCreacion = consulta.FechaCreacion
    };
}

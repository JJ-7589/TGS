using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Caninos.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Caninos.Api.Middleware;

public sealed record CurrentUserContext(int UserId, string Username, string Rol)
{
    public bool IsAdmin => Rol.Equals("admin", StringComparison.OrdinalIgnoreCase);
}

public static class HttpContextUserExtensions
{
    internal const string CurrentUserItemKey = "Caninos.CurrentUser";

    public static CurrentUserContext GetRequiredCurrentUser(this HttpContext context)
    {
        if (context.Items.TryGetValue(CurrentUserItemKey, out var value) && value is CurrentUserContext user)
        {
            return user;
        }

        throw new ApiUnauthorizedException("No se pudo identificar al usuario autenticado.");
    }
}

/// <summary>
/// Middleware global de permisos basado en roles ('admin' y 'usuario') y propiedad de recursos.
/// Centraliza la autorización de todas las rutas /api/* antes de llegar a los controladores.
/// </summary>
public sealed class PermissionsMiddleware
{
    private readonly RequestDelegate _next;

    public PermissionsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Rutas no-API o públicas (health, auth, swagger, frontend estático, uploads)
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Si el usuario no está autenticado, dejamos que [Authorize] / JWT Challenge responda 401
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var subClaim = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!int.TryParse(subClaim, out var currentUserId) || currentUserId <= 0)
        {
            throw new ApiUnauthorizedException("El token no contiene una identidad de usuario válida.");
        }

        // Leemos el rol actual desde BD para reflejar cambios de rol en tiempo real
        var dbUser = await db.Usuarios
            .AsNoTracking()
            .Where(u => u.Id == currentUserId)
            .Select(u => new { u.Id, u.Username, u.Rol })
            .FirstOrDefaultAsync();

        if (dbUser is null)
        {
            throw new ApiUnauthorizedException("La cuenta asociada a este token ya no existe.");
        }

        var currentUser = new CurrentUserContext(dbUser.Id, dbUser.Username, dbUser.Rol);
        context.Items[HttpContextUserExtensions.CurrentUserItemKey] = currentUser;

        // El rol 'admin' tiene permisos globales sobre todos los módulos
        if (currentUser.IsAdmin)
        {
            await _next(context);
            return;
        }

        // Validaciones globales de permisos para el rol 'usuario'
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // segments[0] == "api", segments[1] == recurso ("perfil", "usuarios", "mascotas", "consultas", "uploads")
        if (segments.Length < 2)
        {
            await _next(context);
            return;
        }

        var resource = segments[1].ToLowerInvariant();
        var method = context.Request.Method;

        switch (resource)
        {
            case "perfil":
            case "uploads":
                // Todo usuario autenticado gestiona su propio perfil y sube fotos de sus mascotas
                break;

            case "usuarios":
                await EnforceUsuarioPermissionsAsync(context, currentUser, segments, method);
                break;

            case "mascotas":
                await EnforceMascotaPermissionsAsync(context, db, currentUser, segments, method);
                break;

            case "consultas":
                await EnforceConsultaPermissionsAsync(context, db, currentUser, segments, method);
                break;
        }

        await _next(context);
    }

    private static async Task EnforceUsuarioPermissionsAsync(
        HttpContext context,
        CurrentUserContext currentUser,
        string[] segments,
        string method)
    {
        // /api/usuarios (colección): solo admin puede listar todos o crear otros usuarios
        if (segments.Length == 2)
        {
            if (HttpMethods.IsGet(method) || HttpMethods.IsPost(method))
            {
                throw new ApiForbiddenException("Solo un administrador puede listar o crear otros usuarios.");
            }
            return;
        }

        // /api/usuarios/{id} o /api/usuarios/{id}/mascotas
        if (!int.TryParse(segments[2], out var targetUserId) || targetUserId <= 0)
        {
            // Dejamos que el controlador devuelva 400 Bad Request por ID inválido
            return;
        }

        if (HttpMethods.IsDelete(method))
        {
            throw new ApiForbiddenException("Solo un administrador puede eliminar cuentas de usuario.");
        }

        if (targetUserId != currentUser.UserId)
        {
            throw new ApiForbiddenException("No tienes permisos para acceder o modificar datos de otro usuario.");
        }

        // Si el usuario intenta editarse por PUT /api/usuarios/{id}, impedir escalación de rol
        if (HttpMethods.IsPut(method))
        {
            var requestedRol = await TryReadJsonStringPropertyAsync(context, "rol");
            if (!string.IsNullOrWhiteSpace(requestedRol) &&
                !requestedRol.Equals(currentUser.Rol, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiForbiddenException("No tienes permisos para modificar tu propio rol.");
            }
        }
    }

    private static async Task EnforceMascotaPermissionsAsync(
        HttpContext context,
        AppDbContext db,
        CurrentUserContext currentUser,
        string[] segments,
        string method)
    {
        // /api/mascotas
        if (segments.Length == 2)
        {
            if (HttpMethods.IsGet(method))
            {
                if (context.Request.Query.TryGetValue("userId", out var rawUserId) &&
                    int.TryParse(rawUserId.ToString(), out var queryUserId) &&
                    queryUserId > 0 &&
                    queryUserId != currentUser.UserId)
                {
                    throw new ApiForbiddenException("No tienes permisos para consultar las mascotas de otro usuario.");
                }
            }
            else if (HttpMethods.IsPost(method))
            {
                var bodyUserId = await TryReadJsonIntPropertyAsync(context, "userId");
                if (bodyUserId.HasValue && bodyUserId.Value > 0 && bodyUserId.Value != currentUser.UserId)
                {
                    throw new ApiForbiddenException("Solo puedes registrar mascotas asociadas a tu propia cuenta.");
                }
            }
            return;
        }

        // /api/mascotas/{id} o /api/mascotas/{id}/historial
        if (!int.TryParse(segments[2], out var mascotaId) || mascotaId <= 0)
        {
            return;
        }

        var ownerId = await db.Mascotas
            .AsNoTracking()
            .Where(m => m.Id == mascotaId)
            .Select(m => (int?)m.UserId)
            .FirstOrDefaultAsync();

        if (ownerId.HasValue && ownerId.Value != currentUser.UserId)
        {
            throw new ApiForbiddenException("No tienes permisos para acceder o modificar una mascota que no te pertenece.");
        }

        if (HttpMethods.IsPut(method))
        {
            var newOwnerId = await TryReadJsonIntPropertyAsync(context, "userId");
            if (newOwnerId.HasValue && newOwnerId.Value > 0 && newOwnerId.Value != currentUser.UserId)
            {
                throw new ApiForbiddenException("No tienes permisos para transferir una mascota a otro usuario.");
            }
        }
    }

    private static async Task EnforceConsultaPermissionsAsync(
        HttpContext context,
        AppDbContext db,
        CurrentUserContext currentUser,
        string[] segments,
        string method)
    {
        // /api/consultas
        if (segments.Length == 2)
        {
            if (HttpMethods.IsGet(method))
            {
                if (context.Request.Query.TryGetValue("userId", out var rawUserId) &&
                    int.TryParse(rawUserId.ToString(), out var queryUserId) &&
                    queryUserId > 0 &&
                    queryUserId != currentUser.UserId)
                {
                    throw new ApiForbiddenException("No tienes permisos para ver consultas de otro usuario.");
                }

                if (context.Request.Query.TryGetValue("mascotaId", out var rawMascotaId) &&
                    int.TryParse(rawMascotaId.ToString(), out var queryMascotaId) &&
                    queryMascotaId > 0)
                {
                    var petOwner = await db.Mascotas
                        .AsNoTracking()
                        .Where(m => m.Id == queryMascotaId)
                        .Select(m => (int?)m.UserId)
                        .FirstOrDefaultAsync();

                    if (petOwner.HasValue && petOwner.Value != currentUser.UserId)
                    {
                        throw new ApiForbiddenException("No tienes permisos para ver consultas de una mascota que no te pertenece.");
                    }
                }
            }
            else if (HttpMethods.IsPost(method))
            {
                var bodyUserId = await TryReadJsonIntPropertyAsync(context, "userId");
                if (bodyUserId.HasValue && bodyUserId.Value > 0 && bodyUserId.Value != currentUser.UserId)
                {
                    throw new ApiForbiddenException("Solo puedes programar consultas para tu propia cuenta.");
                }

                var bodyMascotaId = await TryReadJsonIntPropertyAsync(context, "mascotaId");
                if (bodyMascotaId.HasValue && bodyMascotaId.Value > 0)
                {
                    var petOwner = await db.Mascotas
                        .AsNoTracking()
                        .Where(m => m.Id == bodyMascotaId.Value)
                        .Select(m => (int?)m.UserId)
                        .FirstOrDefaultAsync();

                    if (petOwner.HasValue && petOwner.Value != currentUser.UserId)
                    {
                        throw new ApiForbiddenException("Solo puedes programar consultas para tus propias mascotas.");
                    }
                }
            }
            return;
        }

        // /api/consultas/usuario/{userId}
        if (segments.Length == 4 && segments[2].Equals("usuario", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(segments[3], out var targetUserId) && targetUserId > 0 && targetUserId != currentUser.UserId)
            {
                throw new ApiForbiddenException("No tienes permisos para consultar las citas de otro usuario.");
            }
            return;
        }

        // /api/consultas/mascota/{mascotaId}
        if (segments.Length == 4 && segments[2].Equals("mascota", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(segments[3], out var targetMascotaId) && targetMascotaId > 0)
            {
                var petOwner = await db.Mascotas
                    .AsNoTracking()
                    .Where(m => m.Id == targetMascotaId)
                    .Select(m => (int?)m.UserId)
                    .FirstOrDefaultAsync();

                if (petOwner.HasValue && petOwner.Value != currentUser.UserId)
                {
                    throw new ApiForbiddenException("No tienes permisos para consultar las citas de una mascota que no te pertenece.");
                }
            }
            return;
        }

        // /api/consultas/servicio/{tipoServicio} -> filtrado por el usuario actual en el controlador
        if (segments.Length == 4 && segments[2].Equals("servicio", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // /api/consultas/{id}
        if (segments.Length == 3 && int.TryParse(segments[2], out var consultaId) && consultaId > 0)
        {
            var consultaOwner = await db.Consultas
                .AsNoTracking()
                .Where(c => c.Id == consultaId)
                .Select(c => (int?)c.UserId)
                .FirstOrDefaultAsync();

            if (consultaOwner.HasValue && consultaOwner.Value != currentUser.UserId)
            {
                throw new ApiForbiddenException("No tienes permisos para acceder o modificar una consulta que no te pertenece.");
            }

            if (HttpMethods.IsPut(method))
            {
                var bodyUserId = await TryReadJsonIntPropertyAsync(context, "userId");
                if (bodyUserId.HasValue && bodyUserId.Value > 0 && bodyUserId.Value != currentUser.UserId)
                {
                    throw new ApiForbiddenException("No puedes reasignar una consulta a otro usuario.");
                }
            }
        }
    }

    private static async Task<int?> TryReadJsonIntPropertyAsync(HttpContext context, string propertyName)
    {
        if (context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return null;
        }

        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;

        try
        {
            using var doc = await JsonDocument.ParseAsync(context.Request.Body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.ValueKind == JsonValueKind.Number &&
                        prop.Value.TryGetInt32(out var val))
                    {
                        return val;
                    }
                }
            }
        }
        catch
        {
            // Si el JSON es inválido, el model binder devolverá 400 Bad Request
        }
        finally
        {
            context.Request.Body.Position = 0;
        }

        return null;
    }

    private static async Task<string?> TryReadJsonStringPropertyAsync(HttpContext context, string propertyName)
    {
        if (context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return null;
        }

        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;

        try
        {
            using var doc = await JsonDocument.ParseAsync(context.Request.Body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.ValueKind == JsonValueKind.String)
                    {
                        return prop.Value.GetString();
                    }
                }
            }
        }
        catch
        {
            // Si el JSON es inválido, el model binder devolverá 400 Bad Request
        }
        finally
        {
            context.Request.Body.Position = 0;
        }

        return null;
    }
}

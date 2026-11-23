using System.Text.Json;
using Caninos.Api.Dtos.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Caninos.Api.Middleware;

public sealed class ErrorHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);

            // Uniformiza respuestas de error vacías generadas por el pipeline (404, 405, 415, etc.)
            if (!context.Response.HasStarted &&
                context.Response.StatusCode >= 400 &&
                context.Response.ContentLength is null &&
                string.IsNullOrEmpty(context.Response.ContentType))
            {
                var (errorTitle, message) = context.Response.StatusCode switch
                {
                    StatusCodes.Status401Unauthorized => (
                        "Unauthorized",
                        "No autorizado. Debes proporcionar un token Bearer válido."),
                    StatusCodes.Status403Forbidden => (
                        "Forbidden",
                        "No tienes permisos para realizar esta acción."),
                    StatusCodes.Status404NotFound => (
                        "Not Found",
                        "La ruta o recurso solicitado no existe."),
                    StatusCodes.Status405MethodNotAllowed => (
                        "Method Not Allowed",
                        "El método HTTP utilizado no está permitido para esta ruta."),
                    StatusCodes.Status415UnsupportedMediaType => (
                        "Unsupported Media Type",
                        "El encabezado Content-Type debe ser 'application/json'."),
                    _ => (
                        "Error",
                        "Ocurrió un error al procesar la solicitud.")
                };

                await WriteErrorAsync(context, context.Response.StatusCode, errorTitle, message, new());
            }
        }
        catch (ApiException ex)
        {
            await WriteErrorAsync(context, ex.StatusCode, ex.ErrorTitle, ex.Message, ex.Errors);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqliteEx)
        {
            _logger.LogWarning(ex, "Restricción SQLite infringida (ErrorCode={Code})", sqliteEx.SqliteErrorCode);

            if (sqliteEx.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
            {
                var message = sqliteEx.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                    ? "Ya existe un registro con un valor único duplicado."
                    : sqliteEx.Message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)
                        ? "La operación viola una restricción de llave foránea."
                        : "Los datos incumplen una restricción de integridad en la base de datos.";

                await WriteErrorAsync(context, StatusCodes.Status409Conflict, "Conflict", message, new());
            }
            else
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "Internal Server Error",
                    "Error de base de datos al procesar la solicitud.",
                    new());
            }
        }
        catch (BadHttpRequestException ex)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Bad Request",
                $"Solicitud HTTP inválida: {ex.Message}",
                new());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado procesando {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteErrorAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "Ocurrió un error interno inesperado en el servidor.",
                new());
        }
    }

    public static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string errorTitle,
        string message,
        Dictionary<string, string[]> errors)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new ApiErrorResponse
        {
            Status = statusCode,
            Error = errorTitle,
            Message = message,
            Errors = errors,
            Timestamp = DateTime.UtcNow
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Caninos.Api.Data;
using Caninos.Api.Dtos.Common;
using Caninos.Api.Middleware;
using Caninos.Api.Models;
using Caninos.Api.Services;
using Caninos.Api.Validation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Base de datos SQLite con integridad referencial activa en cada conexión
builder.Services.AddSingleton<SqliteForeignKeysInterceptor>();
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("DefaultConnection")
        ?? "Data Source=storage/caninos.db;Foreign Keys=True";

    options.UseSqlite(connectionString)
        .AddInterceptors(sp.GetRequiredService<SqliteForeignKeysInterceptor>());
});

// Servicios de dominio y seguridad (roles 'usuario' y 'admin')
builder.Services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<MascotaService>();
builder.Services.AddScoped<UsuarioService>();
builder.Services.AddScoped<ConsultaService>();

// Controladores con serialización JSON estricta y formato uniforme de errores 400
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        options.JsonSerializerOptions.Converters.Add(new StrictUtcDateTimeConverter());
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressMapClientErrors = true;
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = new Dictionary<string, string[]>();

        foreach (var (rawKey, entry) in context.ModelState)
        {
            if (entry.Errors.Count == 0)
            {
                continue;
            }

            var key = NormalizeModelStateKey(rawKey);
            var messages = entry.Errors
                .Select(e => NormalizeModelStateMessage(e.ErrorMessage, e.Exception))
                .Distinct()
                .ToArray();

            if (errors.TryGetValue(key, out var existing))
            {
                errors[key] = existing.Concat(messages).Distinct().ToArray();
            }
            else
            {
                errors[key] = messages;
            }
        }

        var response = new ApiErrorResponse
        {
            Status = StatusCodes.Status400BadRequest,
            Error = "Bad Request",
            Message = "Error de validación o tipo de dato en la solicitud JSON.",
            Errors = errors,
            Timestamp = DateTime.UtcNow
        };

        return new BadRequestObjectResult(response);
    };
});

// Autenticación JWT Bearer con soporte de roles ('usuario', 'admin')
var jwtSecret = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("Falta configurar Jwt:SecretKey.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Caninos.Api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "Caninos.Frontend";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = "role"
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                var message = !string.IsNullOrEmpty(context.ErrorDescription)
                    ? $"Token inválido o expirado: {context.ErrorDescription}"
                    : "No autorizado. Debes proporcionar un token Bearer válido en el header 'Authorization'.";

                await ErrorHandlingMiddleware.WriteErrorAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    message,
                    new());
            },
            OnForbidden = async context =>
            {
                await ErrorHandlingMiddleware.WriteErrorAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "No tienes permisos para realizar esta acción.",
                    new());
            }
        };
    });

builder.Services.AddAuthorization();

// CORS abierto para orígenes locales del frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendLocal", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Swagger / OpenAPI con soporte para Bearer JWT
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Sistema de Registro de Caninos API",
        Version = "v1",
        Description = "API REST en .NET + SQLite con roles (usuario/admin), middleware global de permisos, fotos de mascotas, próxima cita e historial indexado."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Pega aquí el token JWT obtenido desde POST /api/auth/login (sin escribir la palabra Bearer)."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var app = builder.Build();

// Inicializar esquema SQLite, migraciones incrementales y usuario admin inicial
await DbInitializer.InitializeAsync(app.Services);

app.UseMiddleware<ErrorHandlingMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Caninos API v1");
    options.RoutePrefix = "swagger";
});

// Carpeta pública de fotos subidas (/uploads/mascotas/...)
var uploadsRoot = Path.Combine(app.Environment.ContentRootPath, "storage", "uploads");
Directory.CreateDirectory(uploadsRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads"
});

// Servir el frontend (frontend-caninos) directamente desde http://localhost:5150/
var frontendCandidates = new[]
{
    Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "frontend-caninos")),
    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "frontend-caninos"))
};
var frontendDir = frontendCandidates.FirstOrDefault(Directory.Exists);
if (frontendDir is not null)
{
    var frontendProvider = new PhysicalFileProvider(frontendDir);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = frontendProvider,
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            ctx.Context.Response.Headers.Pragma = "no-cache";
            ctx.Context.Response.Headers.Expires = "0";
        }
    });
}

app.UseCors("FrontendLocal");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<PermissionsMiddleware>();

app.MapControllers();

// Flujo orgánico del frontend: "/" redirige automáticamente a "/login"
if (frontendDir is not null)
{
    var indexHtmlPath = Path.Combine(frontendDir, "index.html");
    IResult ServeNoCacheIndex(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        ctx.Response.Headers.Pragma = "no-cache";
        ctx.Response.Headers.Expires = "0";
        return Results.File(indexHtmlPath, "text/html; charset=utf-8");
    }

    app.MapGet("/", () => Results.Redirect("/login", permanent: false)).ExcludeFromDescription();
    app.MapGet("/login", ServeNoCacheIndex).ExcludeFromDescription();
    app.MapGet("/registro", ServeNoCacheIndex).ExcludeFromDescription();
    app.MapGet("/dashboard", ServeNoCacheIndex).ExcludeFromDescription();
    app.MapGet("/perfil", ServeNoCacheIndex).ExcludeFromDescription();
}

app.Run();

static string NormalizeModelStateKey(string rawKey)
{
    if (string.IsNullOrWhiteSpace(rawKey) || rawKey == "$" || rawKey == "request")
    {
        return "body";
    }

    var cleaned = rawKey.StartsWith("$.", StringComparison.Ordinal)
        ? rawKey[2..]
        : rawKey;

    if (cleaned.Length == 0)
    {
        return "body";
    }

    return char.ToLowerInvariant(cleaned[0]) + cleaned[1..];
}

static string NormalizeModelStateMessage(string? errorMessage, Exception? exception)
{
    var raw = !string.IsNullOrWhiteSpace(errorMessage)
        ? errorMessage
        : exception?.Message ?? "Valor o tipo de dato inválido.";

    if (raw.Contains("could not be converted", StringComparison.OrdinalIgnoreCase))
    {
        return "El tipo de dato enviado no coincide con el tipo esperado por este campo.";
    }

    if (raw.Contains("is an invalid start of a value", StringComparison.OrdinalIgnoreCase) ||
        raw.Contains("Expected depth", StringComparison.OrdinalIgnoreCase) ||
        raw.Contains("The input does not contain any JSON tokens", StringComparison.OrdinalIgnoreCase) ||
        raw.Contains("A non-empty request body is required", StringComparison.OrdinalIgnoreCase))
    {
        return "El cuerpo JSON está vacío o malformado.";
    }

    return raw;
}

public partial class Program;

using Caninos.Api.Data;
using Caninos.Api.Dtos.Auth;
using Caninos.Api.Dtos.Mascotas;
using Caninos.Api.Dtos.Usuarios;
using Caninos.Api.Middleware;
using Caninos.Api.Models;
using Caninos.Api.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Caninos.Api.Services;

public sealed class UsuarioService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<Usuario> _passwordHasher;
    private readonly JwtTokenService _jwtTokenService;

    public UsuarioService(
        AppDbContext db,
        IPasswordHasher<Usuario> passwordHasher,
        JwtTokenService jwtTokenService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var errors = new Dictionary<string, List<string>>();
        var username = DomainValidation.ValidateUsername(request.Username, errors);
        var password = DomainValidation.ValidatePassword(request.Password, required: true, errors)!;
        DomainValidation.ThrowIfErrors(errors);

        var usuario = await _db.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username);

        if (usuario is null)
        {
            throw new ApiUnauthorizedException("Usuario o contraseña incorrectos.");
        }

        var verification = _passwordHasher.VerifyHashedPassword(usuario, usuario.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw new ApiUnauthorizedException("Usuario o contraseña incorrectos.");
        }

        var (token, expiresAt) = _jwtTokenService.GenerateToken(usuario);
        return new AuthResponse
        {
            Token = token,
            TokenType = "Bearer",
            ExpiresAt = expiresAt,
            Usuario = ToResponse(usuario)
        };
    }

    public async Task<AuthResponse> RegisterAsync(CreateUsuarioRequest request)
    {
        var created = await CreateAsync(request);
        var usuario = await _db.Usuarios.AsNoTracking().FirstAsync(u => u.Id == created.Id);
        var (token, expiresAt) = _jwtTokenService.GenerateToken(usuario);

        return new AuthResponse
        {
            Token = token,
            TokenType = "Bearer",
            ExpiresAt = expiresAt,
            Usuario = created
        };
    }

    public async Task<List<UsuarioResponse>> GetAllAsync(string? search)
    {
        var query = _db.Usuarios.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 100)
            {
                throw ApiValidationException.Single("search", "El parámetro 'search' no puede superar los 100 caracteres.");
            }

            var lowered = term.ToLowerInvariant();
            query = query.Where(u =>
                u.Username.ToLower().Contains(lowered) ||
                u.Documento.ToLower().Contains(lowered) ||
                u.NombreCompleto.ToLower().Contains(lowered) ||
                u.Email.ToLower().Contains(lowered));
        }

        return await query
            .OrderBy(u => u.Id)
            .Select(u => ToResponse(u))
            .ToListAsync();
    }

    public async Task<UsuarioResponse> GetByIdAsync(int id)
    {
        var usuario = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new ApiNotFoundException($"No se encontró ningún usuario con id {id}.");

        return ToResponse(usuario);
    }

    public async Task<List<MascotaResponse>> GetMascotasByUsuarioIdAsync(int id)
    {
        var exists = await _db.Usuarios.AsNoTracking().AnyAsync(u => u.Id == id);
        if (!exists)
        {
            throw new ApiNotFoundException($"No se encontró ningún usuario con id {id}.");
        }

        return await _db.Mascotas
            .AsNoTracking()
            .Include(m => m.Usuario)
            .Where(m => m.UserId == id)
            .OrderBy(m => m.Id)
            .Select(m => MascotaService.ToResponse(m))
            .ToListAsync();
    }

    public async Task<UsuarioResponse> CreateAsync(CreateUsuarioRequest request)
    {
        var errors = new Dictionary<string, List<string>>();
        var username = DomainValidation.ValidateUsername(request.Username, errors);
        var password = DomainValidation.ValidatePassword(request.Password, required: true, errors)!;
        var documento = DomainValidation.ValidateDocumento(request.Documento, errors);
        var nombreCompleto = DomainValidation.ValidateNombreCompleto(request.NombreCompleto, errors);
        var telefono = DomainValidation.ValidateTelefono(request.Telefono, errors);
        var email = DomainValidation.ValidateEmail(request.Email, errors);
        var direccion = DomainValidation.ValidateRequiredText(request.Direccion, "direccion", "direccion", 5, 200, errors);
        DomainValidation.ThrowIfErrors(errors);

        await EnsureUniqueFieldsAsync(username, documento, email, excludeUserId: null);

        var usuario = new Usuario
        {
            Username = username,
            Documento = documento,
            NombreCompleto = nombreCompleto,
            Telefono = telefono,
            Email = email,
            Direccion = direccion,
            FechaRegistro = DateTime.UtcNow
        };
        usuario.PasswordHash = _passwordHasher.HashPassword(usuario, password);

        _db.Usuarios.Add(usuario);
        await _db.SaveChangesAsync();

        return ToResponse(usuario);
    }

    public async Task<UsuarioResponse> UpdateAsync(int id, UpdateUsuarioRequest request)
    {
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new ApiNotFoundException($"No se encontró ningún usuario con id {id}.");

        var errors = new Dictionary<string, List<string>>();
        var username = DomainValidation.ValidateUsername(request.Username, errors);
        var password = DomainValidation.ValidatePassword(request.Password, required: false, errors);
        var documento = DomainValidation.ValidateDocumento(request.Documento, errors);
        var nombreCompleto = DomainValidation.ValidateNombreCompleto(request.NombreCompleto, errors);
        var telefono = DomainValidation.ValidateTelefono(request.Telefono, errors);
        var email = DomainValidation.ValidateEmail(request.Email, errors);
        var direccion = DomainValidation.ValidateRequiredText(request.Direccion, "direccion", "direccion", 5, 200, errors);
        DomainValidation.ThrowIfErrors(errors);

        await EnsureUniqueFieldsAsync(username, documento, email, excludeUserId: id);

        usuario.Username = username;
        usuario.Documento = documento;
        usuario.NombreCompleto = nombreCompleto;
        usuario.Telefono = telefono;
        usuario.Email = email;
        usuario.Direccion = direccion;

        if (password is not null)
        {
            usuario.PasswordHash = _passwordHasher.HashPassword(usuario, password);
        }

        await _db.SaveChangesAsync();
        return ToResponse(usuario);
    }

    public async Task DeleteAsync(int id)
    {
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new ApiNotFoundException($"No se encontró ningún usuario con id {id}.");

        var hasMascotas = await _db.Mascotas.AnyAsync(m => m.UserId == id);
        var hasConsultas = await _db.Consultas.AnyAsync(c => c.UserId == id);

        if (hasMascotas || hasConsultas)
        {
            throw new ApiConflictException(
                "No se puede eliminar el usuario porque tiene mascotas o consultas registradas.");
        }

        _db.Usuarios.Remove(usuario);
        await _db.SaveChangesAsync();
    }

    private async Task EnsureUniqueFieldsAsync(string username, string documento, string email, int? excludeUserId)
    {
        var conflicts = new Dictionary<string, string[]>();

        var usernameExists = await _db.Usuarios
            .AsNoTracking()
            .AnyAsync(u => u.Username.ToLower() == username && (!excludeUserId.HasValue || u.Id != excludeUserId.Value));
        if (usernameExists)
        {
            conflicts["username"] = ["Ya existe un usuario registrado con ese 'username'."];
        }

        var documentoExists = await _db.Usuarios
            .AsNoTracking()
            .AnyAsync(u => u.Documento == documento && (!excludeUserId.HasValue || u.Id != excludeUserId.Value));
        if (documentoExists)
        {
            conflicts["documento"] = ["Ya existe un usuario registrado con ese 'documento'."];
        }

        var emailExists = await _db.Usuarios
            .AsNoTracking()
            .AnyAsync(u => u.Email.ToLower() == email && (!excludeUserId.HasValue || u.Id != excludeUserId.Value));
        if (emailExists)
        {
            conflicts["email"] = ["Ya existe un usuario registrado con ese 'email'."];
        }

        if (conflicts.Count > 0)
        {
            throw new ApiConflictException(
                "Ya existe un registro con uno o más valores únicos duplicados.",
                conflicts);
        }
    }

    public static UsuarioResponse ToResponse(Usuario usuario) => new()
    {
        Id = usuario.Id,
        Username = usuario.Username,
        Documento = usuario.Documento,
        NombreCompleto = usuario.NombreCompleto,
        Telefono = usuario.Telefono,
        Email = usuario.Email,
        Direccion = usuario.Direccion,
        FechaRegistro = usuario.FechaRegistro
    };
}

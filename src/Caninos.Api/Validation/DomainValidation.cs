using System.Globalization;
using System.Text.RegularExpressions;
using Caninos.Api.Middleware;

namespace Caninos.Api.Validation;

public static partial class DomainValidation
{
    public static readonly IReadOnlyList<string> RolesPermitidos = ["usuario", "admin"];
    public static readonly IReadOnlyList<string> TiposServicioPermitidos = ["Salud", "Estetica", "Recreacion"];
    public static readonly IReadOnlyList<string> EstadosConsultaPermitidos = ["Programada", "EnProceso", "Completada", "Cancelada"];
    public static readonly IReadOnlyList<string> SexosPermitidos = ["Macho", "Hembra"];

    [GeneratedRegex(@"^[a-zA-Z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernameRegex();

    [GeneratedRegex(@"^[0-9]{5,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericPasswordRegex();

    [GeneratedRegex(@"^[0-9A-Za-z-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentoRegex();

    [GeneratedRegex(@"^\+?[0-9\s-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TelefonoRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex OnlyDigitsRegex();

    public static void AddError(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var list))
        {
            list = new List<string>();
            errors[field] = list;
        }
        list.Add(message);
    }

    public static void ThrowIfErrors(Dictionary<string, List<string>> errors)
    {
        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }
    }

    public static int ParsePositiveId(string? rawId, string fieldName = "id")
    {
        if (string.IsNullOrWhiteSpace(rawId) ||
            rawId != rawId.Trim() ||
            !int.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
            parsed <= 0)
        {
            throw ApiValidationException.Single(
                fieldName,
                $"El parámetro '{fieldName}' debe ser un número entero positivo mayor a 0.");
        }

        return parsed;
    }

    public static int? ParseOptionalPositiveId(string? rawId, string fieldName, Dictionary<string, List<string>> errors)
    {
        if (rawId is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(rawId) ||
            rawId != rawId.Trim() ||
            !int.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
            parsed <= 0)
        {
            AddError(errors, fieldName, $"El parámetro '{fieldName}' debe ser un número entero positivo mayor a 0.");
            return null;
        }

        return parsed;
    }

    public static string ValidateUsername(string? value, Dictionary<string, List<string>> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, "username", "El campo 'username' es obligatorio.");
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length < 3 || trimmed.Length > 50)
        {
            AddError(errors, "username", "El campo 'username' debe tener entre 3 y 50 caracteres.");
        }

        if (!UsernameRegex().IsMatch(trimmed))
        {
            AddError(errors, "username", "El campo 'username' solo permite letras, números, puntos, guiones y guion bajo (sin espacios).");
        }

        return trimmed.ToLowerInvariant();
    }

    public static string? ValidatePassword(
        string? value,
        bool required,
        Dictionary<string, List<string>> errors,
        string field = "password")
    {
        if (!required && value is null)
        {
            return null;
        }

        if (value is null || value.Length == 0)
        {
            AddError(errors, field, "La contraseña es obligatoria y debe ser numérica de mínimo 5 dígitos.");
            return string.Empty;
        }

        if (!NumericPasswordRegex().IsMatch(value))
        {
            AddError(errors, field, "La contraseña debe ser estrictamente numérica (solo dígitos 0-9, sin espacios ni símbolos) y tener entre 5 y 20 dígitos.");
        }

        return value;
    }

    public static string? ValidateRol(string? value, bool required, Dictionary<string, List<string>> errors)
    {
        if (!required && value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, "rol", "El campo 'rol' solo acepta los valores 'usuario' o 'admin'.");
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is "usuario" or "admin")
        {
            return normalized;
        }

        AddError(errors, "rol", "El campo 'rol' solo acepta los valores 'usuario' o 'admin'.");
        return null;
    }

    public static string ValidateDocumento(string? value, Dictionary<string, List<string>> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, "documento", "El campo 'documento' es obligatorio.");
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length < 5 || trimmed.Length > 20)
        {
            AddError(errors, "documento", "El campo 'documento' debe tener entre 5 y 20 caracteres.");
        }

        if (!DocumentoRegex().IsMatch(trimmed))
        {
            AddError(errors, "documento", "El campo 'documento' solo permite caracteres alfanuméricos y guiones.");
        }

        return trimmed;
    }

    public static string ValidateNombreCompleto(string? value, Dictionary<string, List<string>> errors)
    {
        var trimmed = ValidateRequiredText(value, "nombreCompleto", "nombreCompleto", 2, 120, errors);
        if (trimmed.Length > 0 && OnlyDigitsRegex().IsMatch(trimmed.Replace(" ", string.Empty)))
        {
            AddError(errors, "nombreCompleto", "El campo 'nombreCompleto' no puede estar compuesto únicamente por números.");
        }

        return trimmed;
    }

    public static string ValidateTelefono(string? value, Dictionary<string, List<string>> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, "telefono", "El campo 'telefono' es obligatorio.");
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length < 7 || trimmed.Length > 20)
        {
            AddError(errors, "telefono", "El campo 'telefono' debe tener entre 7 y 20 caracteres.");
        }

        if (!TelefonoRegex().IsMatch(trimmed))
        {
            AddError(errors, "telefono", "El campo 'telefono' solo permite dígitos, espacios, guiones y un '+' inicial opcional.");
        }
        else
        {
            var digitCount = trimmed.Count(char.IsAsciiDigit);
            if (digitCount < 7 || digitCount > 15)
            {
                AddError(errors, "telefono", "El campo 'telefono' debe contener entre 7 y 15 dígitos numéricos.");
            }
        }

        return trimmed;
    }

    public static string ValidateEmail(string? value, Dictionary<string, List<string>> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, "email", "El campo 'email' es obligatorio.");
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 150)
        {
            AddError(errors, "email", "El campo 'email' no puede superar los 150 caracteres.");
        }

        if (!EmailRegex().IsMatch(trimmed))
        {
            AddError(errors, "email", "El campo 'email' no tiene un formato de correo electrónico válido.");
        }

        return trimmed.ToLowerInvariant();
    }

    public static string ValidateRequiredText(
        string? value,
        string field,
        string displayName,
        int minLength,
        int maxLength,
        Dictionary<string, List<string>> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, field, $"El campo '{displayName}' es obligatorio y no puede estar vacío.");
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (ContainsControlChars(trimmed))
        {
            AddError(errors, field, $"El campo '{displayName}' contiene caracteres de control no permitidos.");
        }

        if (trimmed.Length < minLength || trimmed.Length > maxLength)
        {
            AddError(errors, field, $"El campo '{displayName}' debe tener entre {minLength} y {maxLength} caracteres.");
        }

        return trimmed;
    }

    public static string? ValidateOptionalText(
        string? value,
        string field,
        string displayName,
        int maxLength,
        Dictionary<string, List<string>> errors)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (ContainsControlChars(trimmed))
        {
            AddError(errors, field, $"El campo '{displayName}' contiene caracteres de control no permitidos.");
        }

        if (trimmed.Length > maxLength)
        {
            AddError(errors, field, $"El campo '{displayName}' no puede superar los {maxLength} caracteres.");
        }

        return trimmed;
    }

    public static string? ValidateFotoUrl(string? value, Dictionary<string, List<string>> errors)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > 500)
        {
            AddError(errors, "fotoUrl", "El campo 'fotoUrl' no puede superar los 500 caracteres.");
            return null;
        }

        if (ContainsControlChars(trimmed) || trimmed.Any(char.IsWhiteSpace))
        {
            AddError(errors, "fotoUrl", "El campo 'fotoUrl' no puede contener espacios ni caracteres de control.");
            return null;
        }

        if (trimmed.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            if (trimmed.Contains("..", StringComparison.Ordinal))
            {
                AddError(errors, "fotoUrl", "La ruta de la foto no es válida.");
                return null;
            }
            return trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            return trimmed;
        }

        AddError(errors, "fotoUrl", "El campo 'fotoUrl' debe ser una URL válida (http://, https://) o una imagen subida (/uploads/...).");
        return null;
    }

    public static int ValidateEdad(int? value, Dictionary<string, List<string>> errors)
    {
        if (value is null)
        {
            AddError(errors, "edad", "El campo 'edad' es obligatorio y debe ser un entero entre 0 y 30.");
            return 0;
        }

        if (value < 0 || value > 30)
        {
            AddError(errors, "edad", "El campo 'edad' debe estar entre 0 y 30 años.");
        }

        return value.Value;
    }

    public static decimal ValidatePesoKg(decimal? value, Dictionary<string, List<string>> errors)
    {
        if (value is null)
        {
            AddError(errors, "pesoKg", "El campo 'pesoKg' es obligatorio y debe ser un número entre 0.2 y 120.0.");
            return 0m;
        }

        if (value < 0.2m || value > 120.0m)
        {
            AddError(errors, "pesoKg", "El campo 'pesoKg' debe estar entre 0.2 y 120.0 kg.");
        }

        if (decimal.Round(value.Value, 2) != value.Value)
        {
            AddError(errors, "pesoKg", "El campo 'pesoKg' admite como máximo 2 decimales.");
        }

        return value.Value;
    }

    public static string ValidateSexo(string? value, Dictionary<string, List<string>> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, "sexo", "El campo 'sexo' es obligatorio. Valores permitidos: Macho, Hembra.");
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals("Macho", StringComparison.OrdinalIgnoreCase))
        {
            return "Macho";
        }

        if (trimmed.Equals("Hembra", StringComparison.OrdinalIgnoreCase))
        {
            return "Hembra";
        }

        AddError(errors, "sexo", "El campo 'sexo' solo acepta los valores 'Macho' o 'Hembra'.");
        return string.Empty;
    }

    public static bool ValidateEsterilizado(bool? value, Dictionary<string, List<string>> errors)
    {
        if (value is null)
        {
            AddError(errors, "esterilizado", "El campo 'esterilizado' es obligatorio y debe ser booleano (true o false).");
            return false;
        }

        return value.Value;
    }

    public static int ValidateForeignKeyId(int? value, string field, Dictionary<string, List<string>> errors)
    {
        if (value is null)
        {
            AddError(errors, field, $"El campo '{field}' es obligatorio y debe ser un entero positivo mayor a 0.");
            return 0;
        }

        if (value <= 0)
        {
            AddError(errors, field, $"El campo '{field}' debe ser un número entero positivo mayor a 0.");
        }

        return value.Value;
    }

    public static string ValidateTipoServicio(string? value, Dictionary<string, List<string>> errors, string field = "tipoServicio")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, field, "El campo 'tipoServicio' es obligatorio. Valores permitidos: Salud, Estetica, Recreacion.");
            return string.Empty;
        }

        var normalized = NormalizeTipoServicio(value);
        if (normalized is null)
        {
            AddError(errors, field, "El campo 'tipoServicio' solo acepta: 'Salud', 'Estetica' (o 'Estética') y 'Recreacion' (o 'Recreación').");
            return string.Empty;
        }

        return normalized;
    }

    public static string? NormalizeTipoServicio(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "salud" => "Salud",
            "estetica" or "estética" => "Estetica",
            "recreacion" or "recreación" => "Recreacion",
            _ => null
        };
    }

    public static string ValidateEstadoConsulta(string? value, bool required, Dictionary<string, List<string>> errors, string field = "estado")
    {
        if (!required && value is null)
        {
            return "Programada";
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, field, "El campo 'estado' es obligatorio. Valores permitidos: Programada, EnProceso, Completada, Cancelada.");
            return string.Empty;
        }

        var normalized = NormalizeEstadoConsulta(value);
        if (normalized is null)
        {
            AddError(errors, field, "El campo 'estado' solo acepta: 'Programada', 'EnProceso', 'Completada' o 'Cancelada'.");
            return string.Empty;
        }

        return normalized;
    }

    public static string? NormalizeEstadoConsulta(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "programada" => "Programada",
            "enproceso" => "EnProceso",
            "completada" => "Completada",
            "cancelada" => "Cancelada",
            _ => null
        };
    }

    public static DateTime ValidateFechaConsulta(
        DateTime? value,
        Dictionary<string, List<string>> errors,
        bool disallowPast = false)
    {
        if (value is null)
        {
            AddError(errors, "fechaConsulta", "El campo 'fechaConsulta' es obligatorio y debe tener formato ISO-8601 válido.");
            return DateTime.UtcNow;
        }

        var utc = value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };

        if (utc.Year < 2000 || utc.Year > 2100)
        {
            AddError(errors, "fechaConsulta", "El campo 'fechaConsulta' debe estar comprendido entre los años 2000 y 2100.");
            return utc;
        }

        if (disallowPast && utc < DateTime.UtcNow)
        {
            AddError(errors, "fechaConsulta", "No se puede programar una cita antes de la fecha y hora actual.");
        }

        return utc;
    }

    public static decimal ValidateCosto(decimal? value, Dictionary<string, List<string>> errors)
    {
        if (value is null)
        {
            AddError(errors, "costo", "El campo 'costo' es obligatorio y debe ser un número mayor o igual a 0.");
            return 0m;
        }

        if (value < 0m || value > 100_000_000m)
        {
            AddError(errors, "costo", "El campo 'costo' debe estar entre 0 y 100000000.");
        }

        if (decimal.Round(value.Value, 2) != value.Value)
        {
            AddError(errors, "costo", "El campo 'costo' admite como máximo 2 decimales.");
        }

        return value.Value;
    }

    private static bool ContainsControlChars(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsControl(ch) && ch != '\n' && ch != '\r' && ch != '\t')
            {
                return true;
            }
        }
        return false;
    }
}
